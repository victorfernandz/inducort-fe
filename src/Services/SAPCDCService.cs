using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using Newtonsoft.Json;
using System.Globalization;
using Microsoft.Extensions.FileSystemGlobbing.Internal.PathSegments;

public class SAPCDCService : BackgroundService
{
    private readonly ILogger<SAPCDCService> _logger;
    private readonly SAPServiceLayer _sapServiceLayer;
    private readonly FacturaService _facturaService;
    private readonly NotaCreditoService _notaCreditoService;
    private readonly EmpresaService _empresaService;
    private readonly EnvioSifenService _envioService;
    private readonly EventoService _eventoInutilizacion;
    private readonly EventoServiceCancelacion _eventoCancelacion;
    private EmpresaInfo _empresaInfo;
    private readonly LoggerSifenService _loggerSifen;
    private readonly Config _config;
    private readonly HttpClient _httpClient;

    public SAPCDCService(ILogger<SAPCDCService> logger, SAPServiceLayer sapServiceLayer, FacturaService facturaService, NotaCreditoService notaCreditoService, EnvioSifenService envioService, EventoServiceCancelacion eventoCancelacion, EmpresaService empresaService, LoggerSifenService loggerSifen, EventoService eventoInutilizacion, Config config)
    {
        _logger = logger;
        _sapServiceLayer = sapServiceLayer;
        _facturaService = facturaService;
        _notaCreditoService = notaCreditoService;
        _empresaService = empresaService;
        _envioService = envioService;
        _loggerSifen = loggerSifen;
        _eventoInutilizacion = eventoInutilizacion;
        _config = config;
        _eventoCancelacion = eventoCancelacion;
    }

    private SapServiceLayerConfig ActiveSapConfig => _config.SapServiceLayerList[0];

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.CompletedTask;
    }
    public async Task ProcesarTodoAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation($"Iniciando procesamiento para: {_config.SapServiceLayerList[0].CompanyDB}");

        bool loggedIn = await _sapServiceLayer.Login();
        if (!loggedIn)
        {
            _logger.LogError("No se pudo iniciar sesión en SAP.");
            return;
        }

        try
        {
            _empresaInfo = await _empresaService.GetEmpresaInfo();
            if (_empresaInfo == null)
            {
                _logger.LogError("No se pudo obtener la información de la empresa.");
                return;
            }

            _empresaInfo.ActividadesEconomicas = await _empresaService.GetActividadesEconomicas();
            if (_empresaInfo.ActividadesEconomicas.Count == 0)
            {
                _logger.LogWarning("No se obtuvieron actividades económicas. Se usará un valor predeterminado.");
                _empresaInfo.ActividadesEconomicas.Add(new ActividadEconomica
                {
                    Codigo = "0",
                    Descripcion = "Actividad no especificada"
                });
            }

            _empresaInfo.ObligacionesAfectadas = await _empresaService.GetObligacionesAfectadas();

            await ProcesarFacturasSinCDC(cancellationToken);
            await ProcesarFacturaCancelada(cancellationToken);
            await ProcesarFacturasPendientes(cancellationToken);
            await ProcesarNotaCreditoSinCDC(cancellationToken);
            await ProcesarNotaCreditoPendiente(cancellationToken);
            await ProcesarEventoInutilizacion(cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en procesamiento general de {_config.SapServiceLayerList[0].CompanyDB}: {ex.Message}");
        }
        finally
        {
            await _sapServiceLayer.Logout();
        }
    }

    private async Task ProcesarFacturaCancelada(CancellationToken stoppingToken)
    {
        int tipoDoc = 1;
        try
        {
            _logger.LogInformation("Procesando Evento de Cancelación de Facturas");
            var eventoCancelacion = await _eventoCancelacion.GetEventoFacturaCancelada();
            _logger.LogInformation($"Se ecnontraron {eventoCancelacion.Count} documentos para cancelar.");

            // Obtener el certificado activo
            var (certificadoBytes, contraseñaCerntificado) = await ObtenerCertificadoActivo();
            var loteDocumentos = new List<(int docEntry, string Id, string xmlFirmado)>();
            string tipoDocumentoLote;
        
            foreach (var docFacturaCancelada in eventoCancelacion)
            {
                int docEntry = docFacturaCancelada.DocEntry;
                string idEvento = docFacturaCancelada.DocEntry.ToString();
                string cdc = docFacturaCancelada.CDC;
                string motivoEvento = docFacturaCancelada.Motivo;
                DateTime dFecFirma = DateTime.Now;

                // Generar XML
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayerList[0].CompanyDB);
                Directory.CreateDirectory(xmlDir);
                string rutaXml = Path.Combine(xmlDir, $"Documento_cancelado_{cdc}.xml");

                GenerarXML.SerializarDocumentoCancelacion(ActiveSapConfig.Sifen, idEvento, cdc, dFecFirma, rutaXml, motivoEvento, certificadoBytes, contraseñaCerntificado);
                
                try
                {
                    // Leer el XML firmado y enviar a SIFEN
                    string xmlFirmadoFinal = File.ReadAllText(rutaXml);
                    loteDocumentos.Add((docEntry, cdc, xmlFirmadoFinal));

                    if (loteDocumentos.Count == 1)
                    {
                        await _envioService.EnviarEventosAsync(loteDocumentos, tipoDoc);
                        _logger.LogInformation($"Evento de cancelación enviado para CDC {cdc}");
                        loteDocumentos.Clear();
                    }

                    _logger.LogInformation($"Documento_cancelado_{cdc} enviado a SIFEN correctamente.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error al preparar/enviar cancelación CDC {cdc}: {ex.Message}");
                    _logger.LogError($"StackTrace: {ex.StackTrace}");
                }
            }
        } 
        catch (Exception ex)
        {
            _logger.LogError($"Error {ex}.");
        }
    }
    
    private async Task ProcesarEventoInutilizacion(CancellationToken stoppingToen)
    {
        int tipoDoc = 2;
        try
        {
            _logger.LogInformation("Procesando Evento de Inutilización");
            var eventoInutilizacion = await _eventoInutilizacion.GetEventoInutilizacion();
            _logger.LogInformation($"Se encontraron {eventoInutilizacion.Count} documentos para inutilizar.");

            // Obtener el certificado digital activo
            var (certificadoBytes, contraseñaCertificado) = await ObtenerCertificadoActivo();
            var loteDocumentos = new List<(int docEntry, string Id, string xmlFirmado)>();
            string tipoDocumentoLote;

            foreach (var docInutilizacion in eventoInutilizacion)
            {
                int docEntry = docInutilizacion.DocEntry;
                int timbrado = docInutilizacion.dNumTim;
                string establecimiento = docInutilizacion.dEst;
                string puntoEmision = docInutilizacion.dPunExp;
                string numeroInicio = docInutilizacion.dNumIn.PadLeft(7,'0');
                string numeroFin = docInutilizacion.dNumFin.PadLeft(7, '0');
                int tipoDocumento = docInutilizacion.iTiDE;
                string motivoEvento = docInutilizacion.mOtEve;
                DateTime dFecFirma = DateTime.Now;
                string cdc = docInutilizacion.dEst + docInutilizacion.dNumIn.PadLeft(7,'0');

                // Generar XML
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayerList[0].CompanyDB);
                Directory.CreateDirectory(xmlDir);
                string rutaXml = Path.Combine(xmlDir, $"Documento_{establecimiento}{numeroInicio}.xml");
                GenerarXML.SerializarDocumentoInutilizacion(ActiveSapConfig.Sifen, cdc, dFecFirma, rutaXml, tipoDocumento, timbrado, establecimiento, puntoEmision, numeroInicio, numeroFin, motivoEvento, certificadoBytes, contraseñaCertificado);

                try
                {
                    string rutaXmlFirmado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayerList[0].CompanyDB, $"Documento_{establecimiento}{numeroInicio}.xml");
                    string xmlFirmadoFinal = File.ReadAllText(rutaXmlFirmado);
                    tipoDocumentoLote = tipoDocumento.ToString();
                    loteDocumentos.Add((docInutilizacion.DocEntry, cdc, xmlFirmadoFinal));

                    if (loteDocumentos.Count == 1)
                    {
                        await _envioService.EnviarEventosAsync(loteDocumentos, tipoDoc);
                        _logger.LogInformation("Documento de intulización enviado.");
                            loteDocumentos.Clear();
                    }
                    _logger.LogInformation($"Documento_{establecimiento}{numeroInicio} enviado a SIFEN correctamente.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error al preparar/enviar documento: {ex.Message}");
                    _logger.LogError($"StackTrace: {ex.StackTrace}");

                    string errorPath = "Errors";
                    Directory.CreateDirectory(errorPath);
                    File.WriteAllText(Path.Combine(errorPath, $"error_Documento_{establecimiento}{numeroInicio}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                        $"CDC: {establecimiento}{numeroInicio}\nError: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error {ex}.");
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

            var loteDocumentos = new List<(int docEntry, string cdc, string xmlFirmadoFinal)>();
            string tipoDocumentoLote = null;

            //Consulta las facturas sin CDC
            foreach (var factura in facturas)
            {
                string docType = factura.DocType;
                decimal dPorcDesIt = factura.dPorcDesIt ?? 0;
                string resumido = factura.Resumido;
                string rucCompleto = factura.BusinessPartner.FederalTaxID;
                string[] rucPartes = rucCompleto.Split('-');
                int U_CRSI = factura.BusinessPartner.iNatRec == "CONTRIBUYENTE" ? 1 : 2;
                int U_TIPCONT = factura.BusinessPartner.iTiContRec;
                int U_EXX_FE_TipoOperacion = factura.BusinessPartner.iTiOpe ?? 0;
                string? dRucReceptor = "";
                int? dDVReceptor = null;
                string? iTipIDRec = null;
                string? dNumIDRec = null;

                if (U_CRSI == 1)
                {
                    dRucReceptor = rucPartes.Length > 0 ? rucPartes[0] : "";//.PadLeft(8, '0') : "00000000";
                    dDVReceptor = rucPartes.Length > 1 ? int.Parse(rucPartes[1]) : 0;
                }
                else
                {
                    iTipIDRec = factura.BusinessPartner.iTipIDRec == "CEDULA" ? "1" : "2";
                    dNumIDRec = rucPartes.Length > 0 ? rucPartes[0] : "";
                }

                string cMoneOpe = factura.Currencies.cMoneOpe;
                string dDesMoneOpe = factura.Currencies.dDesMoneOpe;
                decimal dTiCam = factura.dTiCam;
                string CardName = factura.BusinessPartner.dNomRec;
                string Country = factura.BusinessPartner.cPaisRec;
                string DescPais = factura.BusinessPartner.dDesPaisRe;
                string dDirRec = factura.BusinessPartner.dDirRec;
                int? dNumCasRec = factura.BusinessPartner.dNumCasRec ?? 0;
                string? dTelRec = factura.BusinessPartner.dTelRec;
                string? dCelRec = factura.BusinessPartner.dCelRec;
                string? dEmailRec = factura.BusinessPartner.dEmailRec;
                string iTiDE = factura.U_CDOC;
                string dEst = factura.U_EST;
                string dPunExp = factura.U_PDE;
                string dNumDoc = factura.FolioNum.PadLeft(7, '0');
                //        string dFecha = factura.DocDate.Replace("-", ""); // Fecha del documento para usar en el CDC
                string iTipTra = factura.iTipTra;
                int iIndPres = factura.iIndPres ?? 0;
                int iCondOpe = factura.iCondOpe == -1 ? 1 : 2;
                int iCondCred = factura.iCondCred == 1 ? 1 : 2;
                DateTime dFeIniT = DateTime.ParseExact(factura.U_FITE, "yyyy-MM-dd", null);
                string? dSerieNum = factura.dSerieNum;
                int dNumTim = factura.U_TIM;
                int iTipEmi = 1; // Siempre fijo en 1

                DateTime fecha = DateTime.ParseExact(factura.DocDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                TimeSpan hora = TimeSpan.Zero;

                if (factura.DocTime > 0)
                {
                    int horaInt = factura.DocTime;

                    int horas = horaInt / 10000;
                    int minutos = (horaInt % 10000) / 100;
                    int segundos = horaInt % 100;

                    hora = new TimeSpan(horas, minutos, segundos);
                }

                DateTime dFeEmiDE = fecha.Date.Add(hora);

                string fechaFormatoCDC = dFeEmiDE.ToString("yyyyMMdd");
                DateTime dFecFirma = DateTime.Now.AddMinutes(-2);

                //Agregamos las cuotas para las facturas a plazos
                List<GCuotas> cuotasList = new List<GCuotas>();
                if (factura.OperacionCredito != null && factura.OperacionCredito.Cuotas != null)
                {
                    cuotasList = factura.OperacionCredito.Cuotas;
                }

                // Obtener el plazo de crédito si existe y es a plazo
                string? plazoCredito = "";
                if (iCondOpe == 2 && iCondCred == 1 && factura.OperacionCredito != null)
                {
                    plazoCredito = (factura.OperacionCredito.PlazoCredito ?? "").PadRight(15).Substring(0, 15);
                }

                // Procesamiento de líneas de items
                List<Item> itemsList = new List<Item>();
                if (factura.Items != null && factura.Items.Any())
                {
                    foreach (var item in factura.Items)
                    {
                        decimal totalBruto = item.dCantProSer * item.dPUniProSer;
                        decimal descuentoItemUnitario = item.dDescItem;
                        decimal descuentoGlobalUnitario = item.dDescGloItem;
                        decimal anticipoItemUnitario = item.dAntPreUniIt;
                        decimal anticipoGlobalUnitario = item.dAntGloPreUniIt;

                        decimal totalNeto = (item.dPUniProSer - descuentoItemUnitario - descuentoGlobalUnitario - anticipoItemUnitario - anticipoGlobalUnitario) 
                            * item.dCantProSer;
                        int tasaIVA = 0;

                        if (item.dTasaIVA == 5 || item.dTasaIVA == 1.5m)
                        {
                            tasaIVA = 5;
                        }
                        else if (item.dTasaIVA == 10)
                        {
                            tasaIVA = 10;
                        }

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
                            // baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                            baseGravadaIVA = Math.Round((100 * (totalNeto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                        }
                        else if ((tasaIVA == 5 || item.dTasaIVA == 1.5m) && (afectacionIVA == 1 || afectacionIVA == 4))
                        {
                            //    baseGravadaIVA = Math.Round((totalBruto * (proporcionIVA / 100)) / 1.05m,8);
                            baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
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
                            baseExenta = Math.Round((100 * totalBruto * (100 - proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                        }

                        itemsList.Add(new Item
                        {
                            dCodInt = factura.DocType == "S" ? "1" : item.dCodInt,
                            dDesProSer = item.dDesProSer,
                            dCantProSer = factura.DocType == "S" ? 1 : item.dCantProSer,
                            dPUniProSer = item.dPUniProSer,
                            cUniMed = item.cUniMed,
                            dDesUniMed = item.dDesUniMed,
                            dTiCamIt = item.dTiCamIt,

                            dDescItem = item.dDescItem,
                            dPorcDesIt = item.dPorcDesIt,
                            dDescGloItem = item.dDescGloItem,
                            dAntPreUniIt = item.dAntPreUniIt,
                            dAntGloPreUniIt = item.dAntGloPreUniIt,

                            dTotBruOpeItem = totalBruto,
                            dTotOpeItem = totalNeto,

                            iAfecIVA = afectacionIVA,
                            dDesAfecIVA = descAfectacionIVA,
                            dPropIVA = proporcionIVA,
                            dTasaIVA = tasaIVA,
                            dBasGravIVA = baseGravadaIVA,
                            dLiqIVAItem = liquidacionIVA,
                            dBasExe = baseExenta
                        });
                    }
                }

                // Calcular subtotales y totales usando el helper
                var totalesFactura = Totalizador.CalcularTotalesFactura(itemsList, factura.dTiCam, factura.Currencies.cMoneOpe);

                var pagoContado = await _facturaService.GetPagoContado(factura.DocEntry);
                int iTiPago = pagoContado?.TipoPago ?? 99;
                decimal dMonTiPag = pagoContado?.MontoTipoPago ?? 0;
                string cMoneTiPag = pagoContado?.MonedaTipoPago ?? "PYG";
                string dDMoneTiPag = pagoContado?.DescripcionMonedaTipoPago ?? "Guarani";
                decimal? dTiCamTiPag = pagoContado?.TipoCambioPago ?? 0;

                // Se genera el Código de Control (CDC)     
                string dCodSeg = GenerarCodigoSeguridad();
                
                string cdc = GenerarCDC.GenerarCodigoCDC(iTiDE, _empresaInfo.Ruc, _empresaInfo.Dv.ToString(), dEst, dPunExp, dNumDoc, _empresaInfo.TipoContribuyente.ToString(), fechaFormatoCDC, iTipEmi.ToString(), dCodSeg);
                // Se extraer el Dígito Verificador (dv)
                int dv = int.Parse(cdc.Substring(cdc.Length - 1)); // Último carácter del CDC
                string xmlTiDE = Convert.ToInt32(factura.U_CDOC).ToString();

                _logger.LogInformation($"CDC generado y actualizado: {cdc}");

                // Generar XML
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
            //    string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayer.CompanyDB);
                string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayerList[0].CompanyDB);

                Directory.CreateDirectory(xmlDir);

                string rutaXml = Path.Combine(xmlDir, $"Documento_{cdc}.xml");

                GenerarXML.SerializarDocumentoElectronico(ActiveSapConfig.Sifen, cdc, dv, dFecFirma, rutaXml, dCodSeg, xmlTiDE, dNumTim, dEst, dPunExp, dNumDoc, dSerieNum, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, _empresaInfo.Ruc,
                    _empresaInfo.Dv, _empresaInfo.TipoContribuyente, _empresaInfo.NombreEmpresa, _empresaInfo.DireccionEmisor, _empresaInfo.NumeroCasaEmisor, _empresaInfo.CodDepartamento, _empresaInfo.DescDepartamento,
                    _empresaInfo.CodDistrito, _empresaInfo.DescDistrito, _empresaInfo.CodLocalidad, _empresaInfo.DescLocalidad, _empresaInfo.TelefEmisor, _empresaInfo.EmailEmisor, U_CRSI, U_TIPCONT, dDirRec, dNumCasRec,
                    U_EXX_FE_TipoOperacion, Country, DescPais, CardName, dRucReceptor, dDVReceptor, dTelRec, dCelRec, dEmailRec,
                    dTiCam, iIndPres, iCondOpe, iCondCred, iTiPago, dMonTiPag, cMoneTiPag, dDMoneTiPag, dTiCamTiPag, iTipIDRec, dNumIDRec,
                    _empresaInfo.ActividadesEconomicas, _empresaInfo.ObligacionesAfectadas, cuotasList, itemsList, plazoCredito, totalesFactura, certificadoBytes, contraseñaCertificado);

                try
                {
                //    string rutaXmlFirmado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayer.CompanyDB, $"Documento_{cdc}.xml");
                    string rutaXmlFirmado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayerList[0].CompanyDB, $"Documento_{cdc}.xml");

                    string xmlFirmadoFinal = File.ReadAllText(rutaXmlFirmado);

                    tipoDocumentoLote ??= xmlTiDE;

                //    if (_config.Sifen.Url.ToLower().Contains("test"))
                    if (ActiveSapConfig.Sifen.Url.ToLower().Contains("test"))
                    {
                        loteDocumentos.Add((factura.DocEntry, cdc, xmlFirmadoFinal));

                        await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, xmlTiDE);
                        _logger.LogInformation("Lote de 3 documentos enviado.");
                        loteDocumentos.Clear();
                        tipoDocumentoLote = "";
                    }
                    else
                    {
                        loteDocumentos.Add((factura.DocEntry, cdc, xmlFirmadoFinal));

                        if (loteDocumentos.Count == 1)
                        {
                            await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, xmlTiDE);
                            _logger.LogInformation("Lote de 3 documentos enviado.");
                            loteDocumentos.Clear();
                            tipoDocumentoLote = null;
                        }
                    }

                    _logger.LogInformation($"Documento con CDC {cdc} enviado a SIFEN correctamente.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error al preparar/enviar documento (factura) con CDC {cdc}: {ex.Message}");
                    _logger.LogError($"StackTrace: {ex.StackTrace}");

                    string errorPath = "Errors";
                    Directory.CreateDirectory(errorPath);
                    File.WriteAllText(Path.Combine(errorPath, $"error_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                        $"CDC: {cdc}\nError: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
            }
/*
            // Si quedó un lote incompleto
            if (loteDocumentos.Count > 0)
            {
                await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, facturas.First().U_CDOC);
                _logger.LogInformation($"Lote final de {loteDocumentos.Count} documento(s) enviado.");
            }*/
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en ProcesarFacturasSinCDC: {ex.Message}");
            _logger.LogError($"StackTrace: {ex.StackTrace}");
        }
    }

    private async Task ProcesarFacturasPendientes(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Procesando facturas con CDC en estado pendiente...");

            var facturasPendientes = await _facturaService.GetFacturasSinAutorizar();
            if (facturasPendientes.Count == 0)
            {
                _logger.LogWarning("No se encontraron facturas pendientes por reconsulta.");
                return;
            }

            string basePath = AppDomain.CurrentDomain.BaseDirectory;
        //    string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayer.CompanyDB);
            string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayerList[0].CompanyDB);

            foreach (var factura in facturasPendientes)
            {
                try
                {
                    string cdc = factura.U_EXX_FE_CDC;
                    string resumido = factura.Resumido;
                    string archivoXml = $"Documento_{cdc}.xml";
                    string rutaXmlFirmado = Path.Combine(xmlDir, archivoXml);

                    if (factura.U_EXX_FE_Estado == "NAU")
                    {
                        try
                        {
                            var (certBytes, certPassword) = await ObtenerCertificadoActivo();
                            string dCodSeg = cdc.Substring(34, 9);

                            await RegenerarXmlFactura(factura, cdc, dCodSeg, certBytes, certPassword);
                            _logger.LogInformation($"XML regenerado para documento rechazado con CDC {cdc}.");
                        }
                        catch (Exception exReg)
                        {
                            _logger.LogError($"Error al regenerar XML para CDC {cdc}: {exReg.Message}");
                            continue;
                        }

                        if (!File.Exists(rutaXmlFirmado))
                        {
                            _logger.LogWarning($"No se encontró el archivo XML firmado para CDC {cdc} en {rutaXmlFirmado}");
                            continue;
                        }

                        string xmlFirmado = File.ReadAllText(rutaXmlFirmado);

                        await _envioService.EnviarDocumentoAsincronico(
                            new List<(int, string, string)> { (factura.DocEntry, cdc, xmlFirmado) },
                            factura.U_CDOC,
                            factura.U_CDOC
                        );

                        _logger.LogInformation($"Documento con CDC {cdc} reenviado a SIFEN.");
                    }
                    else if (factura.U_EXX_FE_Estado == "ENV" || factura.U_EXX_FE_Estado == "OFF")
                    {
                        if (!File.Exists(rutaXmlFirmado))
                        {
                            _logger.LogWarning($"No se encontró el archivo XML firmado para CDC {cdc} en {rutaXmlFirmado}");
                            continue;
                        }

                        string xmlFirmado = File.ReadAllText(rutaXmlFirmado);

                        var baseAddr = _httpClient?.BaseAddress?.ToString() ?? "";                        
                        bool esTest = baseAddr?.Contains("test", StringComparison.OrdinalIgnoreCase) == true;
                        
                        string dId = null;
                        string lote = null;

                        if (!esTest)
                        {
                            (dId, lote) = _loggerSifen.ObtenerLotePorCDC(cdc);   
                        }
                        
                        if (string.IsNullOrEmpty(dId) || string.IsNullOrEmpty(lote))
                        {
                            _logger.LogWarning($"No se encontró lote o dId para CDC {cdc}. Omitiendo.");
                            continue;
                        }

                        try
                        {
                            var (certBytes, certPassword) = await ObtenerCertificadoActivo();
                            _envioService.ConfigurarCertificadoCliente(certBytes, certPassword);
                        }
                        catch (Exception certEx)
                        {
                            _logger.LogError($"No se pudo configurar el certificado TLS: {certEx.Message}");
                            continue;
                        }

                        await _envioService.ConsultarEstadoLoteAsync(
                            dId, lote,
                            new List<(int, string, string)> { (factura.DocEntry, cdc, xmlFirmado) },
                            factura.U_CDOC,
                            DateTime.Now, DateTime.Now
                        );
                    }
                }
                catch (Exception exDoc)
                {
                    _logger.LogError($"Error al procesar factura pendiente CDC {factura.U_EXX_FE_CDC}: {exDoc.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error general en ProcesarFacturasPendientes: {ex.Message}");
        }
    }

    private async Task ProcesarNotaCreditoSinCDC(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Procesando Notas de crédito sin CDC...");
            var notasCredito = await _notaCreditoService.GetNotaCreditoSinCDC();
            _logger.LogInformation($"Se encontraron {notasCredito.Count} Notas de crédito sin CDC.");

            // Obtener el certificado digital activo
            var (certificadoBytes, contraseñaCertificado) = await ObtenerCertificadoActivo();

            var loteDocumentos = new List<(int docEntry, string cdc, string xmlFirmadoFinal)>();
            string tipoDocumentoLote = null;

            //Consulta las Notas de crédito sin CDC
            foreach (var notaCredito in notasCredito)
            {
                string docType = notaCredito.DocType;
                string rucCompleto = notaCredito.BusinessPartner.FederalTaxID;
                string[] rucPartes = rucCompleto.Split('-');
                int U_CRSI = notaCredito.BusinessPartner.iNatRec == "CONTRIBUYENTE" ? 1 : 2;
                int U_TIPCONT = notaCredito.BusinessPartner.iTiContRec;
                int U_EXX_FE_TipoOperacion = notaCredito.BusinessPartner.iTiOpe ?? 0;
                string? dRucReceptor = "";
                int? dDVReceptor = null;
                string? dTelRec = notaCredito.BusinessPartner.dTelRec;
                string? dCelRec = notaCredito.BusinessPartner.dCelRec;
                string? dEmailRec = notaCredito.BusinessPartner.dEmailRec;
                string? iTipIDRec = null;
                string? dNumIDRec = null;

                if (U_CRSI == 1)
                {
                    dRucReceptor = rucPartes.Length > 0 ? rucPartes[0] : "";//.PadLeft(8, '0') : "00000000";
                    dDVReceptor = rucPartes.Length > 1 ? int.Parse(rucPartes[1]) : 0;
                }
                else
                {
                    iTipIDRec = notaCredito.BusinessPartner.iTipIDRec == "CEDULA" ? "1" : "2";
                    dNumIDRec = rucPartes.Length > 0 ? rucPartes[0] : "";
                }

                string cMoneOpe = notaCredito.Currencies.cMoneOpe;
                string dDesMoneOpe = notaCredito.Currencies.dDesMoneOpe;
                decimal dTiCam = notaCredito.dTiCam;
                string CardName = notaCredito.BusinessPartner.dNomRec;
                string Country = notaCredito.BusinessPartner.cPaisRec;
                string DescPais = notaCredito.BusinessPartner.dDesPaisRe;
                string dDirRec = notaCredito.BusinessPartner.dDirRec;
                int? dNumCasRec = notaCredito.BusinessPartner.dNumCasRec;
                string iTiDE = notaCredito.U_CDOC == "03" ? "05" : notaCredito.U_CDOC;
                string dEst = notaCredito.U_EST;
                string dPunExp = notaCredito.U_PDE;
                string dNumDoc = notaCredito.FolioNum.PadLeft(7, '0');
                DateTime dFeIniT = DateTime.ParseExact(notaCredito.U_FITE, "yyyy-MM-dd", null);
                string? dSerieNum = notaCredito.dSerieNum;
                int dNumTim = notaCredito.U_TIM;
                int iTipEmi = 1; // Siempre fijo en 1

                int? iMotEmi = notaCredito.iMotEmi;
                int iTipDocAso = notaCredito.iTipDocAso;
                string? dEstDocAso = null;
                string? dPExpDocAso = null;
                string? dNumDocAso = null;
                int? iTipoDocAso = null;
                string? dCdCDERef = null;
                int? dNTimDI = null;
                DateTime? dFecEmiDI = null;
                int? U_TIM = notaCredito.timbradoSAP;

                string notacreditoReferencia = notaCredito.U_NUMFC;

                if (!string.IsNullOrWhiteSpace(notacreditoReferencia))
                {
                    string[] partesNotaCredito = notacreditoReferencia.Split('-');

                    if (iTipDocAso == 1)
                    {
                        string? EST = partesNotaCredito.Length > 0 ? partesNotaCredito[0] : null;
                        string? PDE = partesNotaCredito.Length > 1 ? partesNotaCredito[1] : null;
                        string? Folio = partesNotaCredito.Length > 2 ? partesNotaCredito[2] : null;

                        var datos = await _notaCreditoService.ObtenerCDCFactura(EST, PDE, Folio, rucCompleto, U_TIM);

                        dCdCDERef = datos.dCdCDERef;

                    }
                    else
                    {
                        dEstDocAso = partesNotaCredito.Length > 0 ? partesNotaCredito[0] : null;
                        dPExpDocAso = partesNotaCredito.Length > 1 ? partesNotaCredito[1] : null;
                        dNumDocAso = partesNotaCredito.Length > 2 ? partesNotaCredito[2] : null;
                        iTipoDocAso = 1;

                        var datos = await _notaCreditoService.ObtenerCDCFactura(dEstDocAso, dPExpDocAso, dNumDocAso, rucCompleto, U_TIM);

                        dNTimDI = datos.dNTimDI;
                        dFecEmiDI = datos.dFecEmiDI;
                        dCdCDERef = null;
                    }
                }

                DateTime fecha = DateTime.ParseExact(notaCredito.DocDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
                TimeSpan hora = TimeSpan.Zero;

                if (notaCredito.DocTime > 0)
                {
                    int horaInt = notaCredito.DocTime;

                    int horas = horaInt / 10000;
                    int minutos = (horaInt % 10000) / 100;
                    int segundos = horaInt % 100;

                    hora = new TimeSpan(horas, minutos, segundos);
                }

                DateTime dFeEmiDE = fecha.Date.Add(hora);

                string fechaFormatoCDC = dFeEmiDE.ToString("yyyyMMdd");
                DateTime dFecFirma = DateTime.Now.AddMinutes(-2);

                // Procesamiento de líneas de items
                List<Item> itemsList = new List<Item>();
                if (notaCredito.Items != null && notaCredito.Items.Any())
                {
                    foreach (var item in notaCredito.Items)
                    {
                        decimal totalBruto = item.dCantProSer * item.dPUniProSer;
                        decimal descuentoItemUnitario = item.dDescItem;
                        decimal descuentoGlobalUnitario = item.dDescGloItem;
                        decimal anticipoItemUnitario = item.dAntPreUniIt;
                        decimal anticipoGlobalUnitario = item.dAntGloPreUniIt;

                        decimal totalNeto = (item.dPUniProSer - descuentoItemUnitario - descuentoGlobalUnitario - anticipoItemUnitario - anticipoGlobalUnitario) 
                            * item.dCantProSer;
                        int tasaIVA = 0;

                        if (item.dTasaIVA == 5 || item.dTasaIVA == 1.5m)
                        {
                            tasaIVA = 5;
                        }
                        else if (item.dTasaIVA == 10)
                        {
                            tasaIVA = 10;
                        }

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
                        //    baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                              baseGravadaIVA = Math.Round((100 * (totalNeto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                        }
                        else if ((tasaIVA == 5 || item.dTasaIVA == 1.5m) && (afectacionIVA == 1 || afectacionIVA == 4))
                        {
                            baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
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
                            baseExenta = Math.Round((100 * totalBruto * (100 - proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                        }

                        itemsList.Add(new Item
                        {
                            dCodInt = notaCredito.DocType == "S" ? "1" : item.dCodInt,
                            dDesProSer = item.dDesProSer,
                            dCantProSer = notaCredito.DocType == "S" ? 1 : item.dCantProSer,
                            dPUniProSer = item.dPUniProSer,
                            cUniMed = item.cUniMed,
                            dDesUniMed = item.dDesUniMed,
                            dTiCamIt = item.dTiCamIt,

                            dDescItem = item.dDescItem,
                            dPorcDesIt = item.dPorcDesIt,
                            dDescGloItem = item.dDescGloItem,
                            dAntPreUniIt = item.dAntPreUniIt,
                            dAntGloPreUniIt = item.dAntGloPreUniIt,

                            dTotBruOpeItem = totalBruto,
                            dTotOpeItem = totalNeto,

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

                int? iIndPres = null;
                int? iCondOpe = null;
                int? iCondCred = null;
                string? iTipTra = null;
                int? iTiPago = null;
                int? dMonTiPag = null;
                string? cMoneTiPag = null;
                string? dDMoneTiPag = null;
                int? dTiCamTiPag = null;

                List<GCuotas>? cuotasList = null;
                string? plazoCredito = "";

                // Calcular subtotales y totales usando el helper
                var totalesFactura = Totalizador.CalcularTotalesFactura(itemsList, notaCredito.dTiCam, notaCredito.Currencies.cMoneOpe);

                // Se genera el Código de Control (CDC)     
                string dCodSeg = GenerarCodigoSeguridad();

                string cdc = GenerarCDC.GenerarCodigoCDC(iTiDE, _empresaInfo.Ruc, _empresaInfo.Dv.ToString(), dEst, dPunExp, dNumDoc, _empresaInfo.TipoContribuyente.ToString(), fechaFormatoCDC, iTipEmi.ToString(), dCodSeg);
                // Se extraer el Dígito Verificador (dv)
                int dv = int.Parse(cdc.Substring(cdc.Length - 1)); // Último carácter del CDC
                string xmlTiDE = Convert.ToInt32(iTiDE).ToString();

                // Generar XML
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
            //    string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayer.CompanyDB);
                string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayerList[0].CompanyDB);
                Directory.CreateDirectory(xmlDir);

                string rutaXml = Path.Combine(xmlDir, $"Documento_{cdc}.xml");

                GenerarXML.SerializarDocumentoElectronico(ActiveSapConfig.Sifen, cdc, dv, dFecFirma, rutaXml, dCodSeg, xmlTiDE, dNumTim, dEst, dPunExp, dNumDoc, dSerieNum, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, _empresaInfo.Ruc,
                    _empresaInfo.Dv, _empresaInfo.TipoContribuyente, _empresaInfo.NombreEmpresa, _empresaInfo.DireccionEmisor, _empresaInfo.NumeroCasaEmisor, _empresaInfo.CodDepartamento, _empresaInfo.DescDepartamento,
                    _empresaInfo.CodDistrito, _empresaInfo.DescDistrito, _empresaInfo.CodLocalidad, _empresaInfo.DescLocalidad, _empresaInfo.TelefEmisor, _empresaInfo.EmailEmisor, U_CRSI, U_TIPCONT, dDirRec, dNumCasRec,
                    U_EXX_FE_TipoOperacion, Country, DescPais, CardName, dRucReceptor, dDVReceptor, dTelRec, dCelRec, dEmailRec,
                    dTiCam, iIndPres, iCondOpe, iCondCred, iTiPago, dMonTiPag, cMoneTiPag, dDMoneTiPag, dTiCamTiPag, iTipIDRec, dNumIDRec,
                    _empresaInfo.ActividadesEconomicas, _empresaInfo.ObligacionesAfectadas, cuotasList, itemsList, plazoCredito, totalesFactura, certificadoBytes, contraseñaCertificado,
                    iMotEmi, dCdCDERef, dFecEmiDI, dNTimDI, dEstDocAso, dPExpDocAso, dNumDocAso, iTipDocAso, iTipoDocAso);

                try
                {
                //    string rutaXmlFirmado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayer.CompanyDB, $"Documento_{cdc}.xml");
                    string rutaXmlFirmado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayerList[0].CompanyDB, $"Documento_{cdc}.xml");
                    string xmlFirmadoFinal = File.ReadAllText(rutaXmlFirmado);

                    tipoDocumentoLote ??= xmlTiDE;

                //    if (_config.Sifen.Url.ToLower().Contains("test"))
                    if (ActiveSapConfig.Sifen.Url.ToLower().Contains("test"))
                    {
                        loteDocumentos.Add((notaCredito.DocEntry, cdc, xmlFirmadoFinal));

                        await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, xmlTiDE);
                        _logger.LogInformation("Lote de 3 documentos enviado.");
                        loteDocumentos.Clear();
                        tipoDocumentoLote = "";
                    }
                    else
                    {
                        loteDocumentos.Add((notaCredito.DocEntry, cdc, xmlFirmadoFinal));

                        if (loteDocumentos.Count == 3)
                        {
                            await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, xmlTiDE);
                            _logger.LogInformation("Lote de 3 documentos enviado.");
                            loteDocumentos.Clear();
                            tipoDocumentoLote = null;
                        }
                    }

                    _logger.LogInformation($"Documento con CDC {cdc} enviado a SIFEN correctamente.");
                }
                catch (Exception ex)
                {
                    _logger.LogError($"Error al preparar/enviar documento (nota de crédito) con CDC {cdc}: {ex.Message}");
                    _logger.LogError($"StackTrace: {ex.StackTrace}");

                    string errorPath = "Errors";
                    Directory.CreateDirectory(errorPath);
                    File.WriteAllText(Path.Combine(errorPath, $"error_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                        $"CDC: {cdc}\nError: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
            }

            // Si quedó un lote incompleto
            if (loteDocumentos.Count > 0)
            {
                await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, notasCredito.First().U_CDOC);
                _logger.LogInformation($"Lote final de {loteDocumentos.Count} documento(s) enviado.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error en ProcesarNotaCreditoSinCDC: {ex.Message}");
            _logger.LogError($"StackTrace: {ex.StackTrace}");
        }
    }

    private async Task ProcesarNotaCreditoPendiente(CancellationToken stoppingToken)
    {
        try
        {
            _logger.LogInformation("Procesando Notas de crédito con CDC en estado pendiente...");

            var notaCreditoPendiente = await _notaCreditoService.GetNotaCreditoSinAutorizar();
            _logger.LogInformation($"Se econtraron {notaCreditoPendiente.Count} Notas de crédito con CDC en estado pendiente...");

            if (notaCreditoPendiente.Count == 0)
            {
                _logger.LogWarning("No se encontraron Notas de crédito pendientes por reconsulta.");
                return;
            }

            string basePath = AppDomain.CurrentDomain.BaseDirectory;
        //    string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayer.CompanyDB);
            string xmlDir = Path.Combine(basePath, "XML", _config.SapServiceLayerList[0].CompanyDB);

            foreach (var notaCredito in notaCreditoPendiente)
            {
                try
                {
                    string cdc = notaCredito.U_EXX_FE_CDC;
                    string archivoXml = $"Documento_{cdc}.xml";
                    string rutaXmlFirmado = Path.Combine(xmlDir, archivoXml);

                    if (notaCredito.U_EXX_FE_Estado == "NAU")
                    {
                        try
                        {
                            var (certBytes, certPassword) = await ObtenerCertificadoActivo();
                            string dCodSeg = cdc.Substring(34, 9);

                            await RegenerarXmlNotaCredito(notaCredito, cdc, dCodSeg, certBytes, certPassword);
                            _logger.LogInformation($"XML regenerado para documento rechazado con CDC {cdc}.");
                        }
                        catch (Exception exReg)
                        {
                            _logger.LogError($"Error al regenerar XML para CDC {cdc}: {exReg.Message}");
                            continue;
                        }

                        if (!File.Exists(rutaXmlFirmado))
                        {
                            _logger.LogWarning($"No se encontró el archivo XML firmado para CDC {cdc} en {rutaXmlFirmado}");
                            continue;
                        }

                        string xmlFirmado = File.ReadAllText(rutaXmlFirmado);

                        await _envioService.EnviarDocumentoAsincronico(
                            new List<(int, string, string)> { (notaCredito.DocEntry, cdc, xmlFirmado) },
                            notaCredito.U_CDOC,
                            notaCredito.U_CDOC
                        );

                        _logger.LogInformation($"Documento con CDC {cdc} reenviado a SIFEN.");
                    }
                    else if (notaCredito.U_EXX_FE_Estado == "ENV" || notaCredito.U_EXX_FE_CODERR == "0361" || notaCredito.U_EXX_FE_CODERR == "0301")
                    {
                        if (!File.Exists(rutaXmlFirmado))
                        {
                            _logger.LogWarning($"No se encontró el archivo XML firmado para CDC {cdc} en {rutaXmlFirmado}");
                            continue;
                        }

                        string xmlFirmado = File.ReadAllText(rutaXmlFirmado);

                        var (dId, lote) = _loggerSifen.ObtenerLotePorCDC(cdc);
                        if (string.IsNullOrEmpty(dId) || string.IsNullOrEmpty(lote))
                        {
                            _logger.LogWarning($"No se encontró lote o dId para CDC {cdc}. Omitiendo.");
                            continue;
                        }

                        try
                        {
                            var (certBytes, certPassword) = await ObtenerCertificadoActivo();
                            _envioService.ConfigurarCertificadoCliente(certBytes, certPassword);
                        }
                        catch (Exception certEx)
                        {
                            _logger.LogError($"No se pudo configurar el certificado TLS: {certEx.Message}");
                            continue;
                        }

                        await _envioService.ConsultarEstadoLoteAsync(
                            dId, lote,
                            new List<(int, string, string)> { (notaCredito.DocEntry, cdc, xmlFirmado) },
                            notaCredito.U_CDOC,
                            DateTime.Now, DateTime.Now
                        );
                    }
                }
                catch (Exception exDoc)
                {
                    _logger.LogError($"Error al procesar Nota de crédito pendiente CDC {notaCredito.U_EXX_FE_CDC}: {exDoc.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error general en ProcesarNotaCreditoPendiente: {ex.Message}");
        }
    }

    // Para la generación del código de Seguridad
    private static readonly Random random = new Random();
    private string GenerarCodigoSeguridad()
    {
        return random.Next(1, 1000000000).ToString("D9");
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

            // Obtener los datos del certificado y contraseña
            string? certificadoBase64 = certificado["U_ARCHIVO"].ToString();
            string? contraseñaBase64 = certificado["U_PWD"].ToString();
            byte[] certificadoBytes = Convert.FromBase64String(certificadoBase64);
            string contraseña = Encoding.UTF8.GetString(Convert.FromBase64String(contraseñaBase64));

            //    _logger.LogInformation($"Certificado obtenido correctamente: {certificado["Name"]}");
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

    private async Task RegenerarXmlFactura(Factura factura, string cdc, string dCodSeg, byte[] certificadoBytes, string contraseñaCertificado)
    {
        //    string xmlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayer.CompanyDB);
        string xmlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayerList[0].CompanyDB);
        Directory.CreateDirectory(xmlDir);

        string rutaXml = Path.Combine(xmlDir, $"Documento_{cdc}.xml");

        if (File.Exists(rutaXml))
        {
            string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackupXml");
            Directory.CreateDirectory(backupDir);

            string backupPath = Path.Combine(backupDir, $"Documento_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml");
            File.Copy(rutaXml, backupPath, true);
            File.Delete(rutaXml);
            _logger.LogInformation($"Se guardó respaldo del XML anterior en {backupPath} y se eliminó el archivo original para regeneración.");
        }

        // Obtener la info de empresa actual
        if (_empresaInfo == null)
            _empresaInfo = await _empresaService.GetEmpresaInfo();

        // Obtener ítems y totales actualizados
        List<Item> itemsList = new List<Item>();

        if (factura.Items != null && factura.Items.Any())
        {
            foreach (var item in factura.Items)
            {
                decimal totalBruto = item.dCantProSer * item.dPUniProSer;

                decimal descuentoItemUnitario = item.dDescItem;
                decimal descuentoGlobalUnitario = item.dDescGloItem;
                decimal anticipoItemUnitario = item.dAntPreUniIt;
                decimal anticipoGlobalUnitario = item.dAntGloPreUniIt;

                decimal totalNeto = (item.dPUniProSer - descuentoItemUnitario - descuentoGlobalUnitario - anticipoItemUnitario - anticipoGlobalUnitario) 
                    * item.dCantProSer;
                int tasaIVA = 0;

                if (item.dTasaIVA == 5 || item.dTasaIVA == 1.5m)
                {
                    tasaIVA = 5;
                }
                else if (item.dTasaIVA == 10)
                {
                    tasaIVA = 10;
                }

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
                else if (item.taxCode != null && item.taxCode.Equals("IVA_EXO", StringComparison.OrdinalIgnoreCase))
                {
                    afectacionIVA = 2;
                    descAfectacionIVA = "Exonerado (Art. 100 - Ley 6380/2019)";
                    proporcionIVA = 0;
                }

                decimal baseGravadaIVA = 0;

                if (tasaIVA == 10 && (afectacionIVA == 1 || afectacionIVA == 4))
                {
                    //    baseGravadaIVA = Math.Round((totalBruto * (proporcionIVA / 100)) / 1.1m,8);
                    // baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                    baseGravadaIVA = Math.Round((100 * (totalNeto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                }
                else if ((tasaIVA == 5 || item.dTasaIVA == 1.5m) && (afectacionIVA == 1 || afectacionIVA == 4))
                {
                    //    baseGravadaIVA = Math.Round((totalBruto * (proporcionIVA / 100)) / 1.05m,8);
                    baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
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
                    baseExenta = Math.Round((100 * totalBruto * (100 - proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                }

                itemsList.Add(new Item
                {
                    dCodInt = factura.DocType == "S" ? "1" : item.dCodInt,
                    dDesProSer = item.dDesProSer,
                    dCantProSer = factura.DocType == "S" ? 1 : item.dCantProSer,
                    dPUniProSer = item.dPUniProSer,
                    cUniMed = item.cUniMed,
                    dDesUniMed = item.dDesUniMed,
                    dTiCamIt = item.dTiCamIt,

                    dDescItem = item.dDescItem,
                    dPorcDesIt = item.dPorcDesIt,
                    dDescGloItem = item.dDescGloItem,
                    dAntPreUniIt = item.dAntPreUniIt,
                    dAntGloPreUniIt = item.dAntGloPreUniIt,

                    dTotBruOpeItem = totalBruto,
                    dTotOpeItem = totalNeto,

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

        // Fecha de emisión y firma
        DateTime dFecFirma = DateTime.Now.AddMinutes(-2);
        DateTime fecha = DateTime.ParseExact(factura.DocDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        TimeSpan hora = TimeSpan.Zero;
        if (factura.DocTime > 0)
        {
            int horaInt = factura.DocTime;
            int horas = horaInt / 10000;
            int minutos = (horaInt % 10000) / 100;
            int segundos = horaInt % 100;
            hora = new TimeSpan(horas, minutos, segundos);
        }
        DateTime dFeEmiDE = fecha.Date.Add(hora);
        string iTiDE = int.Parse(factura.U_CDOC).ToString(); // Sacamos el cero

        string dDirRec = factura.BusinessPartner.dDirRec;
        int? dNumCasRec = factura.BusinessPartner.dNumCasRec;

        GenerarXML.SerializarDocumentoElectronico(ActiveSapConfig.Sifen,
            cdc: cdc,
            dv: int.Parse(cdc[^1..]),
            dFecFirma: dFecFirma,
            rutaArchivo: rutaXml,
            dCodSeg: dCodSeg,
            iTiDE: iTiDE,
            dNumTim: factura.U_TIM,
            dEst: factura.U_EST,
            dPunExp: factura.U_PDE,
            dNumDoc: factura.FolioNum.PadLeft(7, '0'),
            dSerieNum: factura.dSerieNum,
            dFeIniT: DateTime.ParseExact(factura.U_FITE, "yyyy-MM-dd", null),
            dFeEmiDE: dFeEmiDE,
            iTipTra: factura.iTipTra,
            cMoneOpe: factura.Currencies.cMoneOpe,
            dDesMoneOpe: factura.Currencies.dDesMoneOpe,
            dRucEm: _empresaInfo.Ruc,
            dDVEmi: _empresaInfo.Dv,
            iTipCont: _empresaInfo.TipoContribuyente,
            dNomEmi: _empresaInfo.NombreEmpresa,
            dDirEmi: _empresaInfo.DireccionEmisor,
            dNumCas: _empresaInfo.NumeroCasaEmisor,
            cDepEmi: _empresaInfo.CodDepartamento,
            dDesDepEmi: _empresaInfo.DescDepartamento,
            cDisEmi: _empresaInfo.CodDistrito,
            dDesDisEmi: _empresaInfo.DescDistrito,
            cCiuEmi: _empresaInfo.CodLocalidad,
            dDesCiuEmi: _empresaInfo.DescLocalidad,
            dTelEmi: _empresaInfo.TelefEmisor,
            dEmailE: _empresaInfo.EmailEmisor,
            iNatRec: factura.BusinessPartner.iNatRec == "CONTRIBUYENTE" ? 1 : 2,
            iTiContRec: factura.BusinessPartner.iTiContRec,
            dDirRec: dDirRec,
            dNumCasRec: dNumCasRec,
            iTiOpe: factura.BusinessPartner.iTiOpe ?? 0,
            cPaisRec: factura.BusinessPartner.cPaisRec,
            dDesPaisRe: factura.BusinessPartner.dDesPaisRe,
            dNomRec: factura.BusinessPartner.dNomRec,
            dRucReceptor: factura.BusinessPartner.FederalTaxID.Split('-')[0],
            dDVReceptor: factura.BusinessPartner.FederalTaxID.Split('-').Length > 1 ? int.Parse(factura.BusinessPartner.FederalTaxID.Split('-')[1]) : 0,
            dTelRec: factura.BusinessPartner.dTelRec,
            dCelRec: factura.BusinessPartner.dCelRec,
            dEmailRec: factura.BusinessPartner.dEmailRec,
            dTiCam: factura.dTiCam,
            iIndPres: factura.iIndPres,
            iCondOpe: factura.iCondOpe == -1 ? 1 : 2,
            iCondCred: factura.iCondCred == 1 ? 1 : 2,
            iTiPago: factura.PagoContado?.TipoPago,
            dMonTiPag: factura.PagoContado?.MontoTipoPago,
            cMoneTiPag: factura.PagoContado?.MonedaTipoPago,
            dDMoneTiPag: factura.PagoContado?.DescripcionMonedaTipoPago,
            dTiCamTiPag: factura.PagoContado?.TipoCambioPago,
            iTipIDRec: factura.BusinessPartner.iTipIDRec == "CEDULA" ? "1" : "2",
            dNumIDRec: factura.BusinessPartner.FederalTaxID.Split('-')[0],
            actividades: _empresaInfo.ActividadesEconomicas,
            obligaciones: _empresaInfo.ObligacionesAfectadas,
            cuotas: factura.OperacionCredito?.Cuotas,
            items: itemsList,
            plazoCredito: (factura.OperacionCredito?.PlazoCredito ?? "").Substring(0, Math.Min(15, factura.OperacionCredito?.PlazoCredito?.Length ?? 0)),
            totales: totalesFactura,
            certificadoBytes: certificadoBytes,
            contraseñaCertificado: contraseñaCertificado);
    }

    private async Task RegenerarXmlNotaCredito(NotaCredito notaCredito, string cdc, string dCodSeg, byte[] certificadoBytes, string contraseñaCertificado)
    {
    //    string xmlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayer.CompanyDB);
        string xmlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", _config.SapServiceLayerList[0].CompanyDB);
        Directory.CreateDirectory(xmlDir);

        string rutaXml = Path.Combine(xmlDir, $"Documento_{cdc}.xml");

        if (File.Exists(rutaXml))
        {
            string backupDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BackupXml");
            Directory.CreateDirectory(backupDir);

            string backupPath = Path.Combine(backupDir, $"Documento_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml");
            File.Copy(rutaXml, backupPath, true);
            File.Delete(rutaXml);
            _logger.LogInformation($"Se guardó respaldo del XML anterior en {backupPath} y se eliminó el archivo original para regeneración.");
        }

        // Obtener ítems y totales actualizados
        List<Item> itemsList = new List<Item>();

        if (notaCredito.Items != null && notaCredito.Items.Any())
        {
            foreach (var item in notaCredito.Items)
            {
                decimal totalBruto = item.dCantProSer * item.dPUniProSer;

                decimal descuentoItemUnitario = item.dDescItem;
                decimal descuentoGlobalUnitario = item.dDescGloItem;
                decimal anticipoItemUnitario = item.dAntPreUniIt;
                decimal anticipoGlobalUnitario = item.dAntGloPreUniIt;

                decimal totalNeto = (item.dPUniProSer - descuentoItemUnitario - descuentoGlobalUnitario - anticipoItemUnitario - anticipoGlobalUnitario) 
                    * item.dCantProSer;
                int tasaIVA = 0;

                if (item.dTasaIVA == 5 || item.dTasaIVA == 1.5m)
                {
                    tasaIVA = 5;
                }
                else if (item.dTasaIVA == 10)
                {
                    tasaIVA = 10;
                }

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
                    //baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                    baseGravadaIVA = Math.Round((100 * (totalNeto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                }
                else if ((tasaIVA == 5 || item.dTasaIVA == 1.5m) && (afectacionIVA == 1 || afectacionIVA == 4))
                {
                    //    baseGravadaIVA = Math.Round((totalBruto * (proporcionIVA / 100)) / 1.05m,8);
                    baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
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
                    baseExenta = Math.Round((100 * totalBruto * (100 - proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
                }

                itemsList.Add(new Item
                {
                    dCodInt = notaCredito.DocType == "S" ? "1" : item.dCodInt,
                    dDesProSer = item.dDesProSer,
                    dCantProSer = item.dCantProSer,
                    dPUniProSer = item.dPUniProSer,
                    cUniMed = item.cUniMed,
                    dDesUniMed = item.dDesUniMed,
                    dTiCamIt = item.dTiCamIt,
                    
                    dTotBruOpeItem = totalBruto,
                    dTotOpeItem = totalNeto,
                    
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
        var totalesFactura = Totalizador.CalcularTotalesFactura(itemsList, notaCredito.dTiCam, notaCredito.Currencies.cMoneOpe);

        // Fecha de emisión y firma
        DateTime dFecFirma = DateTime.Now.AddMinutes(-2);
        DateTime fecha = DateTime.ParseExact(notaCredito.DocDate, "yyyy-MM-dd", CultureInfo.InvariantCulture);
        TimeSpan hora = TimeSpan.Zero;
        if (notaCredito.DocTime > 0)
        {
            int horaInt = notaCredito.DocTime;
            int horas = horaInt / 10000;
            int minutos = (horaInt % 10000) / 100;
            int segundos = horaInt % 100;
            hora = new TimeSpan(horas, minutos, segundos);
        }
        DateTime dFeEmiDE = fecha.Date.Add(hora);
        //int.Parse(notaCredito.U_CDOC).ToString();
        string iTiDE = "";

        if (notaCredito.U_CDOC == "3" || notaCredito.U_CDOC == "03")
        {
            iTiDE = "5";
        }

        int? iMotEmi = notaCredito.iMotEmi;
        int iTipDocAso = notaCredito.iTipDocAso;
        string? dEstDocAso = null;
        string? dPExpDocAso = null;
        string? dNumDocAso = null;
        int? iTipoDocAso = null;
        string? dCdCDERef = null;
        int? dNTimDI = null;
        DateTime? dFecEmiDI = null;
        string notacreditoReferencia = notaCredito.U_NUMFC;
        int? U_TIM = notaCredito.timbradoSAP;

        if (!string.IsNullOrWhiteSpace(notacreditoReferencia))
        {
            string[] partesNotaCredito = notacreditoReferencia.Split('-');

            if (iTipDocAso == 1)
            {
                string? EST = partesNotaCredito.Length > 0 ? partesNotaCredito[0] : null;
                string? PDE = partesNotaCredito.Length > 1 ? partesNotaCredito[1] : null;
                string? Folio = partesNotaCredito.Length > 2 ? partesNotaCredito[2] : null;

                var datos = await _notaCreditoService.ObtenerCDCFactura(EST, PDE, Folio, notaCredito.BusinessPartner.FederalTaxID, U_TIM);

                dCdCDERef = datos.dCdCDERef;

            }
            else
            {
                dEstDocAso = partesNotaCredito.Length > 0 ? partesNotaCredito[0] : null;
                dPExpDocAso = partesNotaCredito.Length > 1 ? partesNotaCredito[1] : null;
                dNumDocAso = partesNotaCredito.Length > 2 ? partesNotaCredito[2] : null;
                iTipoDocAso = 1;

                var datos = await _notaCreditoService.ObtenerCDCFactura(dEstDocAso, dPExpDocAso, dNumDocAso, notaCredito.BusinessPartner.FederalTaxID, U_TIM);

                dNTimDI = datos.dNTimDI;
                dFecEmiDI = datos.dFecEmiDI;
                dCdCDERef = null;
            }
        }

        string dDirRec = notaCredito.BusinessPartner.dDirRec;
        int? dNumCasRec = notaCredito.BusinessPartner.dNumCasRec;

        GenerarXML.SerializarDocumentoElectronico( ActiveSapConfig.Sifen,
            cdc: cdc,
            dv: int.Parse(cdc[^1..]),
            dFecFirma: dFecFirma,
            rutaArchivo: rutaXml,
            dCodSeg: dCodSeg,
            iTiDE: iTiDE,
            dNumTim: notaCredito.U_TIM,
            dEst: notaCredito.U_EST,
            dPunExp: notaCredito.U_PDE,
            dNumDoc: notaCredito.FolioNum.PadLeft(7, '0'),
            dSerieNum : notaCredito.dSerieNum,
            dFeIniT: DateTime.ParseExact(notaCredito.U_FITE, "yyyy-MM-dd", null),
            dFeEmiDE: dFeEmiDE,
            iTipTra: null,
            cMoneOpe: notaCredito.Currencies.cMoneOpe,
            dDesMoneOpe: notaCredito.Currencies.dDesMoneOpe,
            dRucEm: _empresaInfo.Ruc,
            dDVEmi: _empresaInfo.Dv,
            iTipCont: _empresaInfo.TipoContribuyente,
            dNomEmi: _empresaInfo.NombreEmpresa,
            dDirEmi: _empresaInfo.DireccionEmisor,
            dNumCas: _empresaInfo.NumeroCasaEmisor,
            cDepEmi: _empresaInfo.CodDepartamento,
            dDesDepEmi: _empresaInfo.DescDepartamento,
            cDisEmi: _empresaInfo.CodDistrito,
            dDesDisEmi: _empresaInfo.DescDistrito,
            cCiuEmi: _empresaInfo.CodLocalidad,
            dDesCiuEmi: _empresaInfo.DescLocalidad,
            dTelEmi: _empresaInfo.TelefEmisor,
            dEmailE: _empresaInfo.EmailEmisor,
            iNatRec: notaCredito.BusinessPartner.iNatRec == "CONTRIBUYENTE" ? 1 : 2,
            iTiContRec: notaCredito.BusinessPartner.iTiContRec,
            dDirRec: dDirRec,
            dNumCasRec: dNumCasRec,
            iTiOpe: notaCredito.BusinessPartner.iTiOpe ?? 0,
            cPaisRec: notaCredito.BusinessPartner.cPaisRec,
            dDesPaisRe: notaCredito.BusinessPartner.dDesPaisRe,
            dNomRec: notaCredito.BusinessPartner.dNomRec,
            dRucReceptor: notaCredito.BusinessPartner.FederalTaxID.Split('-')[0],
            dDVReceptor: notaCredito.BusinessPartner.FederalTaxID.Split('-').Length > 1 ? int.Parse(notaCredito.BusinessPartner.FederalTaxID.Split('-')[1]) : 0,
            dTelRec: notaCredito.BusinessPartner.dTelRec,
            dCelRec: notaCredito.BusinessPartner.dCelRec,
            dEmailRec: notaCredito.BusinessPartner.dEmailRec,
            dTiCam: notaCredito.dTiCam,
            iIndPres: null,
            iCondOpe: null,
            iCondCred: null,
            iTiPago: null,
            dMonTiPag: null,
            cMoneTiPag: null,
            dDMoneTiPag: null,
            dTiCamTiPag: null,
            iTipIDRec: notaCredito.BusinessPartner.iTipIDRec == "CEDULA" ? "1" : "2",
            dNumIDRec: notaCredito.BusinessPartner.FederalTaxID.Split('-')[0],
            actividades: _empresaInfo.ActividadesEconomicas,
            obligaciones: _empresaInfo.ObligacionesAfectadas,
            cuotas: null,
            items: itemsList,
            plazoCredito: null,
            totales: totalesFactura,
            certificadoBytes: certificadoBytes,
            contraseñaCertificado: contraseñaCertificado,
            //-----------
            iMotEmi: iMotEmi,
            dCdCDERef: dCdCDERef,
            dFecEmiDI: dFecEmiDI,
            dNTimDI: dNTimDI,
            dEstDocAso: dEstDocAso,
            dPExpDocAso: dPExpDocAso,
            dNumDocAso: dNumDocAso,
            iTipDocAso: iTipDocAso,
            iTipoDocAso: iTipoDocAso);
    }

}    