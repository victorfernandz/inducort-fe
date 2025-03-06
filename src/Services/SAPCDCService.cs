using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class SAPCDCService : BackgroundService
{
    private readonly ILogger<SAPCDCService> _logger;
    private readonly SAPServiceLayer _sapServiceLayer;
    private readonly FacturaService _facturaService;
    private readonly EmpresaService _empresaService;
    private EmpresaInfo _empresaInfo;

    public SAPCDCService(ILogger<SAPCDCService> logger, SAPServiceLayer sapServiceLayer, FacturaService facturaService, EmpresaService empresaService)
    {
        _logger = logger;
        _sapServiceLayer = sapServiceLayer;
        _facturaService = facturaService;
        _empresaService = empresaService;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Servicio SAPCDC iniciado...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Buscando facturas sin CDC en SAP...");
                
                //Login
                bool loggedIn = await _sapServiceLayer.Login();
                if (!loggedIn)
                {
                    _logger.LogError("No se pudo iniciar sesión en SAP.");
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    continue;
                }

                // Obtener información de la empresa
                _empresaInfo = await _empresaService.GetEmpresaInfo();
                if (_empresaInfo == null)
                {
                    _logger.LogError("No se pudo obtener la información de la empresa.");
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    continue;
                }

                // Obtener actividades económicas
                _empresaInfo.ActividadesEconomicas = await _empresaService.GetActividadesEconomicas();

                // Si no hay actividades económicas, usamos un valor predeterminado
                if (_empresaInfo.ActividadesEconomicas.Count == 0)
                {
                    _logger.LogWarning("No se obtuvieron actividades económicas. Se usará un valor predeterminado.");
                    _empresaInfo.ActividadesEconomicas.Add(new ActividadEconomica 
                    { 
                        Codigo = "0",
                        Descripcion = "Actividad no especificada"
                    });
                }

                // Obtener obligaciones afectadas
                _empresaInfo.ObligacionesAfectadas = await _empresaService.GetObligacionesAfectadas();
                if (_empresaInfo.ObligacionesAfectadas.Count == 0)
                {
                    _logger.LogWarning("No se obtuvieron obligaciones afectadas.");
                }
                else
                {
                    _logger.LogInformation($"Se obtuvieron {_empresaInfo.ObligacionesAfectadas.Count} obligaciones afectadas.");
                }

                // Procesar Facturas sin CDC
                await ProcesarFacturasSinCDC(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en SAPCDCService: {ex.Message}");
                _logger.LogError($"StackTrace: {ex.StackTrace}");
            }
            finally
            {
                await _sapServiceLayer.Logout(); // Cerrar sesión después de cada ciclo
            }
            
            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }

    private async Task ProcesarFacturasSinCDC(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Procesando facturas sin CDC...");
            var facturas = await _facturaService.GetFacturasSinCDC();
            _logger.LogInformation($"Se encontraron {facturas.Count} facturas sin CDC.");

                //Consulta las facturas sin CDC
                foreach (var factura in facturas)
                {
                    string rucCompleto = factura.BusinessPartner.FederalTaxID;
                    string cMoneOpe = factura.Currencies.cMoneOpe;
                    string dDesMoneOpe = factura.Currencies.dDesMoneOpe;
                    string CardName = factura.BusinessPartner.dNomRec;
                    string[] rucPartes = rucCompleto.Split('-');
                    string dRucReceptor = rucPartes.Length > 0 ? rucPartes[0].PadLeft(8, '0') : "00000000";
                    int dDVReceptor = rucPartes.Length > 1 ? int.Parse(rucPartes[1]) : 0;
                    int U_CRSI = factura.BusinessPartner.iNatRec == "CONTRIBUYENTE" ? 1 : 2;
                    int U_TIPCONT = factura.BusinessPartner.iTiContRec;
                    int U_EXX_FE_TipoOperacion = factura.BusinessPartner.iTiOpe;
                    string Country = factura.BusinessPartner.cPaisRec;
                    string DescPais = factura.BusinessPartner.dDesPaisRe;
                    string iTiDE = factura.U_CDOC.PadLeft(2, '0');
                    string dEst = factura.U_EST;
                    string dPunExp = factura.U_PDE;
                    string dNumDoc = factura.FolioNum.PadLeft(7, '0');
                    string dFecha = factura.DocDate.Replace("-", "");
                    string iTipTra = factura.iTipTra;
                    int iIndPres = factura.iIndPres;
                    int iCondOpe = factura.iCondOpe == -1 ? 1 : 2;
                    int iCondCred = factura.iCondCred == 1 ? 1 : 2;
                    DateTime dFeIniT = DateTime.ParseExact(factura.U_FITE, "yyyy-MM-dd", null);
                    int dNumTim = factura.U_TIM;
                    int iTipEmi = 1; // Siempre fijo en 1
                    DateTime dFeEmiDE = DateTime.Now;

                    //Agregamos las cuotas para las facturas a plazos
                    List<GCuotas> cuotasList = new List<GCuotas>();
                    if (factura.OperacionCredito != null && factura.OperacionCredito.Cuotas != null)
                    {
                        cuotasList = factura.OperacionCredito.Cuotas;
                    }

                    // Se genera el Código de Control (CDC)     
                    string dCodSeg = GenerarCodigoSeguridad();
                    string cdc = GenerarCDC.GenerarCodigoCDC(iTiDE, _empresaInfo.Ruc, _empresaInfo.Dv.ToString(), dEst, dPunExp, dNumDoc, 
                        _empresaInfo.TipoContribuyente.ToString(), dFecha, iTipEmi.ToString(), dCodSeg);

                    // Se extraer el Dígito Verificador (dv)
                    int dv = int.Parse(cdc.Substring(cdc.Length - 1)); // Último carácter del CDC

                //    bool actualizado = await _facturaService.ActualizarCDC(factura.DocEntry, cdc);

                /*    if (actualizado)
                    {
                        _logger.LogInformation($"CDC generado y actualizado: {cdc}");   
*/
                        // Generar XML
                        string rutaXml = $"XML/Documento_{cdc}.xml"; 
                        
                        // Usar un solo método para generar el XML
                        GenerarXML.SerializarDocumentoElectronico(cdc, dv, rutaXml, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, _empresaInfo.Ruc,  
                            _empresaInfo.Dv, _empresaInfo.TipoContribuyente, _empresaInfo.NombreEmpresa, _empresaInfo.DireccionEmisor, _empresaInfo.NumeroCasaEmisor, _empresaInfo.CodDepartamento, _empresaInfo.DescDepartamento, 
                            _empresaInfo.CodDistrito, _empresaInfo.DescDistrito, _empresaInfo.CodLocalidad, _empresaInfo.DescLocalidad, _empresaInfo.TelefEmisor, _empresaInfo.EmailEmisor, U_CRSI, U_TIPCONT, 
                            U_EXX_FE_TipoOperacion, Country, DescPais, CardName, dRucReceptor, dDVReceptor, iIndPres, iCondOpe, iCondCred, _empresaInfo.ActividadesEconomicas, _empresaInfo.ObligacionesAfectadas, cuotasList);
                /*    }
                    else
                    {
                        _logger.LogWarning($"No se pudo actualizar el CDC para la factura {factura.DocEntry}");
                    } */
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en SAPCDCService: {ex.Message}");
            }
            finally
            {
                await _sapServiceLayer.Logout(); // Cerrar sesión después de cada ciclo
            }

        await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
    }

    private string GenerarCodigoSeguridad()
    {
        Random random = new Random();
        return random.Next(1, 999999999).ToString("D9");
    }
}
