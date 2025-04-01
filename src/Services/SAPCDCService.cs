using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Linq;

public class SAPCDCService : BackgroundService
{
    private readonly ILogger<SAPCDCService> _logger;
    private readonly SAPServiceLayer _sapServiceLayer;
    private readonly FacturaService _facturaService;
    private readonly EmpresaService _empresaService;
    private readonly EnvioSifenService _envioService;
    private readonly LoggerSifenService _loggerSifen;
    private readonly Config _config;

    private EmpresaInfo _empresaInfo;

    public SAPCDCService(ILogger<SAPCDCService> logger, SAPServiceLayer sapServiceLayer, FacturaService facturaService, EmpresaService empresaService, EnvioSifenService envioService, LoggerSifenService loggerSifen, Config config)
    {
        _logger = logger;
        _sapServiceLayer = sapServiceLayer;
        _facturaService = facturaService;
        _empresaService = empresaService;
        _envioService = envioService;
        _loggerSifen = loggerSifen;
        _config = config;
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

            // Obtener el certificado digital activo
            var (certificadoBytes, contraseñaCertificado) = await ObtenerCertificadoActivo();

            //Consulta las facturas sin CDC
            foreach (var factura in facturas)
            {
                string rucCompleto = factura.BusinessPartner.FederalTaxID;
                string cMoneOpe = factura.Currencies.cMoneOpe;
                string dDesMoneOpe = factura.Currencies.dDesMoneOpe;
                decimal dTiCam = factura.dTiCam;
                string CardName = factura.BusinessPartner.dNomRec;
                string[] rucPartes = rucCompleto.Split('-');
                string dRucReceptor = rucPartes.Length > 0 ? rucPartes[0] : "";//.PadLeft(8, '0') : "00000000";
                int dDVReceptor = rucPartes.Length > 1 ? int.Parse(rucPartes[1]) : 0;
                int U_CRSI = factura.BusinessPartner.iNatRec == "CONTRIBUYENTE" ? 1 : 2;
                int U_TIPCONT = factura.BusinessPartner.iTiContRec;
                int U_EXX_FE_TipoOperacion = factura.BusinessPartner.iTiOpe;
                string Country = factura.BusinessPartner.cPaisRec;
                string DescPais = factura.BusinessPartner.dDesPaisRe;
                string iTiDE = factura.U_CDOC;
                string dEst = factura.U_EST;
                string dPunExp = factura.U_PDE;
                string dNumDoc = factura.FolioNum.PadLeft(7, '0');
                string dFecha = factura.DocDate.Replace("-", ""); // Fecha del documento para usar en el CDC
                string iTipTra = factura.iTipTra;
                int iIndPres = factura.iIndPres;
                int iCondOpe = factura.iCondOpe == -1 ? 1 : 2;
                int iCondCred = factura.iCondCred == 1 ? 1 : 2;
                DateTime dFeIniT = DateTime.ParseExact(factura.U_FITE, "yyyy-MM-dd", null);
                int dNumTim = factura.U_TIM;
                int iTipEmi = 1; // Siempre fijo en 1
                DateTime dFeEmiDE = DateTime.Now;
                DateTime dFecFirma = DateTime.Now;

                //Agregamos las cuotas para las facturas a plazos
                List<GCuotas> cuotasList = new List<GCuotas>();
                if (factura.OperacionCredito != null && factura.OperacionCredito.Cuotas != null)
                {
                    cuotasList = factura.OperacionCredito.Cuotas;
                }

                // Obtener el plazo de crédito si existe y es a plazo
                string plazoCredito = null;
                if (iCondOpe == 2 && iCondCred == 1 && factura.OperacionCredito != null)
                {
                    plazoCredito = factura.OperacionCredito.PlazoCredito;
                }

                // Procesamiento de líneas de items
                List<Item> itemsList = new List<Item>();
                if (factura.Items != null && factura.Items.Any())
                {
                    foreach (var item in factura.Items)
                    {                           
                        // Calculate the total for this line
                        decimal totalBruto = item.dCantProSer * item.dPUniProSer;
                        int tasaIVA = 0;

                        if (item.dTasaIVA == 5 || item.dTasaIVA == 1.5m)
                        {
                            tasaIVA = 5;
                        }
                        else if (item.dTasaIVA == 10)
                        {
                            tasaIVA = 10;
                        }
                        
                        // Default values for IVA related fields
                        string descAfectacionIVA = "Gravado IVA";
                        int afectacionIVA = 1;
                        int proporcionIVA = 100;
                        
                        if (item.taxCode != null && item.taxCode.Equals("IVA_EXE", StringComparison.OrdinalIgnoreCase))
                        {
                            afectacionIVA = 3;
                            descAfectacionIVA = "Exento";
                            proporcionIVA = 0;
                        }
                        else if (item.taxCode != null && item.taxCode.Equals("IVA_IMB", StringComparison.OrdinalIgnoreCase))
                        {
                            afectacionIVA = 4;
                            descAfectacionIVA = "Gravado parcial (Grav- Exento)";
                            proporcionIVA = 30;
                        }
                        else if (item.taxCode?.Contains("IVA_5", StringComparison.OrdinalIgnoreCase) == true ||
                            (item.taxCode != null && item.taxCode.Equals("IVA_10", StringComparison.OrdinalIgnoreCase)))                                
                        {
                            afectacionIVA = 1;
                            descAfectacionIVA = "Gravado IVA";
                            proporcionIVA = 100;
                        }

                        decimal baseGravadaIVA = 0;

                        if (tasaIVA == 10 && (afectacionIVA == 1 || afectacionIVA == 4)) 
                        {
                        //    baseGravadaIVA = Math.Round((totalBruto * (proporcionIVA / 100)) / 1.1m,8);
                            baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)),8);
                        }
                        else if ((tasaIVA == 5 || item.dTasaIVA == 1.5m) && (afectacionIVA == 1 || afectacionIVA == 4)) 
                        {
                        //    baseGravadaIVA = Math.Round((totalBruto * (proporcionIVA / 100)) / 1.05m,8);
                            baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)),8);
                        }
                        else if (tasaIVA == 0 && (afectacionIVA == 2 || afectacionIVA == 3)) 
                        {
                            baseGravadaIVA = 0;
                        }

                        decimal liquidacionIVA = 0;

                        if (afectacionIVA != 2 && afectacionIVA != 3)
                        {
                            decimal tasaDecimal = tasaIVA / 100m;
                            liquidacionIVA = Math.Round(baseGravadaIVA * tasaDecimal, 8);
                        }

                        decimal baseExenta = 0;

                        if (afectacionIVA == 4)
                        {
                            baseExenta = Math.Round((100 * totalBruto * (100 - proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)),8);
                        }

                        // Create transformed item
                        itemsList.Add(new Item
                        {
                            dCodInt = item.dCodInt,
                            dDesProSer = item.dDesProSer,
                            dCantProSer = item.dCantProSer,
                            dPUniProSer = item.dPUniProSer,
                            dTiCamIt = item.dTiCamIt,
                            dTotBruOpeItem = totalBruto,
                            iAfecIVA = afectacionIVA,
                            dDesAfecIVA = descAfectacionIVA,
                            dPropIVA = proporcionIVA,
                            dTasaIVA = tasaIVA,
                            dBasGravIVA = baseGravadaIVA,
                            dLiqIVAItem = liquidacionIVA,
                            dBasExe = baseExenta,
                        });
                    }
                }

                // Calcular subtotales y totales usando el helper
                var totalesFactura = Totalizador.CalcularTotalesFactura(itemsList, factura.dTiCam, factura.Currencies.cMoneOpe);

                // Se genera el Código de Control (CDC)     
                string dCodSeg = GenerarCodigoSeguridad();
                string cdc = GenerarCDC.GenerarCodigoCDC(iTiDE, _empresaInfo.Ruc, _empresaInfo.Dv.ToString(), dEst, dPunExp, dNumDoc, 
                    _empresaInfo.TipoContribuyente.ToString(), dFecha, iTipEmi.ToString(), dCodSeg);

                // Se extraer el Dígito Verificador (dv)
                int dv = int.Parse(cdc.Substring(cdc.Length - 1)); // Último carácter del CDC

                // Convertir el tipo de documento a entero y luego a string para eliminar los ceros iniciales
                string xmlTiDE = Convert.ToInt32(factura.U_CDOC).ToString();

            //    bool actualizado = await _facturaService.ActualizarCDC(factura.DocEntry, cdc);

            /*    if (actualizado)
                {
                    _logger.LogInformation($"CDC generado y actualizado: {cdc}");   
*/
                    // Generar XML
                    string rutaXml = $"XML/Documento_{cdc}.xml"; 
                    
                    // Usar un solo método para generar el XML
                    GenerarXML.SerializarDocumentoElectronico(cdc, dv, dFecFirma, rutaXml, dCodSeg, xmlTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, _empresaInfo.Ruc,  
                        _empresaInfo.Dv, _empresaInfo.TipoContribuyente, _empresaInfo.NombreEmpresa, _empresaInfo.DireccionEmisor, _empresaInfo.NumeroCasaEmisor, _empresaInfo.CodDepartamento, _empresaInfo.DescDepartamento, 
                        _empresaInfo.CodDistrito, _empresaInfo.DescDistrito, _empresaInfo.CodLocalidad, _empresaInfo.DescLocalidad, _empresaInfo.TelefEmisor, _empresaInfo.EmailEmisor, U_CRSI, U_TIPCONT, 
                        U_EXX_FE_TipoOperacion, Country, DescPais, CardName, dRucReceptor, dDVReceptor, dTiCam, iIndPres, iCondOpe, iCondCred, _empresaInfo.ActividadesEconomicas, _empresaInfo.ObligacionesAfectadas, cuotasList
                        , itemsList, plazoCredito, totalesFactura, certificadoBytes, contraseñaCertificado);
            /*    }
                else
                {
                    _logger.LogWarning($"No se pudo actualizar el CDC para la factura {factura.DocEntry}");
                } */
                try
                    {
                        // Leer contenido del XML generado
                        string xmlFirmado = File.ReadAllText(rutaXml);
                        
                        // Enviar el documento a SIFEN
                        await _envioService.EnviarDocumentoAsincronico(cdc, null, xmlFirmado, xmlTiDE);
                        
                        _logger.LogInformation($"Documento con CDC {cdc} enviado a SIFEN correctamente");
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError($"Error al enviar documento a SIFEN: {ex.Message}");
                        
                        // Guardar información del error para diagnóstico
                        string errorPath = "Errors";
                        Directory.CreateDirectory(errorPath);
                        File.WriteAllText(
                            Path.Combine(errorPath, $"error_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                            $"CDC: {cdc}\nError: {ex.Message}\nStackTrace: {ex.StackTrace}"
                        );
                    }
                }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en ProcesarFacturasSinCDC: {ex.Message}");
            _logger.LogError($"StackTrace: {ex.StackTrace}");
        }
    }

    private string GenerarCodigoSeguridad()
    {
        Random random = new Random();
        return random.Next(1, 999999999).ToString("D9");
    }
    
    private async Task<(byte[] certificadoBytes, string contraseña)> ObtenerCertificadoActivo()
    {
        try
        {
            // Consultar el certificado activo
            string query = "U_CERTIFICADOS?$filter=U_ACTIVO eq 'Y'";
            
            var response = await _sapServiceLayer.GetHttpClient().GetAsync(query);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogError($"Error al consultar certificados: {response.StatusCode}");
                throw new Exception($"Error al consultar certificados: {response.StatusCode}");
            }
            
            var jsonResponse = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrEmpty(jsonResponse))
            {
                throw new Exception("No se pudo obtener respuesta del servicio de certificados");
            }
            
            // Deserializar la respuesta JSON
            var responseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
            
            if (responseObj == null || !responseObj.ContainsKey("value"))
            {
                throw new Exception("Formato de respuesta inválido al obtener certificado");
            }
            
            var certificadosArray = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(responseObj["value"].ToString());
            
            if (certificadosArray == null || certificadosArray.Count == 0)
            {
                throw new Exception("No se encontró un certificado activo");
            }
            
            // Tomar el primer certificado activo
            var certificado = certificadosArray[0];
            
            // Obtener los datos del certificado y contraseña (que están en Base64)
            string certificadoBase64 = certificado["U_ARCHIVO"].ToString();
            string contraseñaBase64 = certificado["U_PWD"].ToString();
            
            // Decodificar el certificado y la contraseña desde Base64
            byte[] certificadoBytes = Convert.FromBase64String(certificadoBase64);
            string contraseña = Encoding.UTF8.GetString(Convert.FromBase64String(contraseñaBase64));
            
            _logger.LogInformation($"Certificado obtenido correctamente: {certificado["Name"]}");
            return (certificadoBytes, contraseña);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener certificado: {ex.Message}");
            if (ex.InnerException != null)
            {
                _logger.LogError($"Error interno: {ex.InnerException.Message}");
            }
            throw new Exception("Error al obtener certificado digital", ex);
        }
    }
}    