using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Text;
using Newtonsoft.Json;
using System.Globalization;
using Org.BouncyCastle.Asn1;

public class SAPCDCService : BackgroundService
{
    private readonly ILogger<SAPCDCService> _logger;
    private readonly SAPServiceLayer _sapServiceLayer;
    private readonly FacturaService _facturaService;
    private readonly NotaCreditoService _notaCreditoService;
    private readonly EmpresaService _empresaService;
    private readonly EnvioSifenService _envioService;
    private EmpresaInfo _empresaInfo;
    private readonly LoggerSifenService _loggerSifen;
    private readonly Config _config;

    public SAPCDCService(ILogger<SAPCDCService> logger, SAPServiceLayer sapServiceLayer, FacturaService facturaService, NotaCreditoService notaCreditoService, EmpresaService empresaService, EnvioSifenService envioService, LoggerSifenService loggerSifen, Config config)
    {
        _logger = logger;
        _sapServiceLayer = sapServiceLayer;
        _facturaService = facturaService;
        _notaCreditoService = notaCreditoService;
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
                _logger.LogInformation("Buscando documentos sin CDC en SAP...");

                //Login
                bool loggedIn = await _sapServiceLayer.Login();
                if (!loggedIn)
                {
                    _logger.LogError("No se pudo iniciar sesión en SAP.");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
                    continue;
                }

                // Obtener información de la empresa
                _empresaInfo = await _empresaService.GetEmpresaInfo();
                if (_empresaInfo == null)
                {
                    _logger.LogError("No se pudo obtener la información de la empresa.");
                    await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
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
                    //    _logger.LogInformation($"Se obtuvieron {_empresaInfo.ObligacionesAfectadas.Count} obligaciones afectadas.");
                }

                // Procesar Facturas sin CDC
                await ProcesarFacturasSinCDC(stoppingToken);

                // Procesar facturas NO Autorizadas
                await ProcesarFacturasPendientes(stoppingToken);

                // Procesar Notas de crédito sin CDC
                await ProcesarNotaCreditoSinCDC(stoppingToken);

                //    await ReconsultarNCPendientes(stoppingToken);                

            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en SAPCDCService: {ex.Message}");
                _logger.LogError($"StackTrace: {ex.StackTrace}");
            }
            finally
            {
                await _sapServiceLayer.Logout();
            }

            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
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
                string rucCompleto = factura.BusinessPartner.FederalTaxID;
                string[] rucPartes = rucCompleto.Split('-');
                int U_CRSI = factura.BusinessPartner.iNatRec == "CONTRIBUYENTE" ? 1 : 2;
                int U_TIPCONT = factura.BusinessPartner.iTiContRec;
                int U_EXX_FE_TipoOperacion = factura.BusinessPartner.iTiOpe;
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
                string iTiDE = factura.U_CDOC;
                string dEst = factura.U_EST;
                string dPunExp = factura.U_PDE;
                string dNumDoc = factura.FolioNum.PadLeft(7, '0');
                //        string dFecha = factura.DocDate.Replace("-", ""); // Fecha del documento para usar en el CDC
                string iTipTra = factura.iTipTra;
                int iIndPres = factura.iIndPres;
                int iCondOpe = factura.iCondOpe == -1 ? 1 : 2;
                int iCondCred = factura.iCondCred == 1 ? 1 : 2;
                DateTime dFeIniT = DateTime.ParseExact(factura.U_FITE, "yyyy-MM-dd", null);
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
                DateTime dFecFirma = DateTime.Now;

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
                    plazoCredito = factura.OperacionCredito.PlazoCredito;
                }

                // Procesamiento de líneas de items
                List<Item> itemsList = new List<Item>();
                if (factura.Items != null && factura.Items.Any())
                {
                    foreach (var item in factura.Items)
                    {
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
                            baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
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
                            dCantProSer = item.dCantProSer,
                            dPUniProSer = item.dPUniProSer,
                            cUniMed = item.cUniMed,
                            dDesUniMed = item.dDesUniMed,
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

                var pagoContado = await _facturaService.GetPagoContado(factura.DocEntry);
                int iTiPago = pagoContado?.TipoPago ?? 99;
                decimal dMonTiPag = pagoContado?.MontoTipoPago ?? 0;
                string cMoneTiPag = pagoContado?.MonedaTipoPago ?? "PYG";
                string dDMoneTiPag = pagoContado?.DescripcionMonedaTipoPago ?? "Guaraní";
                decimal? dTiCamTiPag = pagoContado?.TipoCambioPago;

                // Se genera el Código de Control (CDC)     
                string dCodSeg = GenerarCodigoSeguridad();
                Console.WriteLine(dCodSeg);

                string cdc = GenerarCDC.GenerarCodigoCDC(iTiDE, _empresaInfo.Ruc, _empresaInfo.Dv.ToString(), dEst, dPunExp, dNumDoc, _empresaInfo.TipoContribuyente.ToString(), fechaFormatoCDC, iTipEmi.ToString(), dCodSeg);
                Console.WriteLine(cdc);
                // Se extraer el Dígito Verificador (dv)
                int dv = int.Parse(cdc.Substring(cdc.Length - 1)); // Último carácter del CDC
                Console.WriteLine(dv);
                string xmlTiDE = Convert.ToInt32(factura.U_CDOC).ToString();

                _logger.LogInformation($"CDC generado y actualizado: {cdc}");

                // Generar XML
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string xmlDir = Path.Combine(basePath, "XML");
                Directory.CreateDirectory(xmlDir);

                string rutaXml = Path.Combine(xmlDir, $"Documento_{cdc}.xml");

                GenerarXML.SerializarDocumentoElectronico(cdc, dv, dFecFirma, rutaXml, dCodSeg, xmlTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, _empresaInfo.Ruc,
                    _empresaInfo.Dv, _empresaInfo.TipoContribuyente, _empresaInfo.NombreEmpresa, _empresaInfo.DireccionEmisor, _empresaInfo.NumeroCasaEmisor, _empresaInfo.CodDepartamento, _empresaInfo.DescDepartamento,
                    _empresaInfo.CodDistrito, _empresaInfo.DescDistrito, _empresaInfo.CodLocalidad, _empresaInfo.DescLocalidad, _empresaInfo.TelefEmisor, _empresaInfo.EmailEmisor, U_CRSI, U_TIPCONT,
                    U_EXX_FE_TipoOperacion, Country, DescPais, CardName, dRucReceptor, dDVReceptor, dTiCam, iIndPres, iCondOpe, iCondCred, iTiPago, dMonTiPag, cMoneTiPag, dDMoneTiPag, dTiCamTiPag, iTipIDRec, dNumIDRec,
                    _empresaInfo.ActividadesEconomicas, _empresaInfo.ObligacionesAfectadas, cuotasList, itemsList, plazoCredito, totalesFactura, certificadoBytes, contraseñaCertificado);

                try
                {
                    string rutaXmlFirmado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", $"Documento_{cdc}.xml");
                    string xmlFirmadoFinal = File.ReadAllText(rutaXmlFirmado);

                    tipoDocumentoLote ??= xmlTiDE;

                    if (_config.Sifen.Url.ToLower().Contains("test"))
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
                    _logger.LogError($"Error al preparar/enviar documento con CDC {cdc}: {ex.Message}");
                    _logger.LogError($"StackTrace: {ex.StackTrace}");

                    string errorPath = "Errors";
                    Directory.CreateDirectory(errorPath);
                    File.WriteAllText(Path.Combine(errorPath, $"error_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                        $"CDC: {cdc}\nError: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
            }

            // Si quedó un lote incompleto
            if (loteDocumentos.Count > 0)// && !_config.Sifen.Url.ToLower().Contains("test"))
            {
                await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, facturas.First().U_CDOC);
                _logger.LogInformation($"Lote final de {loteDocumentos.Count} documento(s) enviado.");
            }
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
            string xmlDir = Path.Combine(basePath, "XML");

            foreach (var factura in facturasPendientes)
            {
                try
                {
                    string cdc = factura.U_EXX_FE_CDC;
                    string archivoXml = $"Documento_{cdc}.xml";
                    string rutaXmlFirmado = Path.Combine(xmlDir, archivoXml);

                    // Regenerar XML si fue rechazado
                    if (factura.U_EXX_FE_Estado == "NAU")
                    {
                        try
                        {
                            var (certBytes, certPassword) = await ObtenerCertificadoActivo();
                            string dCodSeg = cdc.Substring(36, 8); 

                            await RegenerarXmlFirmado(factura, cdc, dCodSeg, certBytes, certPassword);
                            _logger.LogInformation($"XML regenerado para documento rechazado con CDC {cdc}.");
                        }
                        catch (Exception exReg)
                        {
                            _logger.LogError($"Error al regenerar XML para CDC {cdc}: {exReg.Message}");
                            continue; 
                        }
                    }

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

                    _logger.LogWarning($"(CDC={cdc}) dId recuperado: {dId}, Lote recuperado: {lote}");

                    // Configurar certificado TLS
                    try
                    {
                        var (certBytes, certPassword) = await ObtenerCertificadoActivo();
                        _envioService.ConfigurarCertificadoCliente(certBytes, certPassword);
                        _logger.LogInformation("Certificado TLS configurado para consulta de estado del lote.");
                    }
                    catch (Exception certEx)
                    {
                        _logger.LogError($"No se pudo configurar el certificado TLS: {certEx.Message}");
                        continue;
                    }

                    // Consultar estado del lote
                    await _envioService.ConsultarEstadoLoteAsync(dId, lote, new List<(int, string, string)> { (factura.DocEntry, cdc, xmlFirmado) }, factura.U_CDOC,
                    //    "", // mensajeRespuesta
                        DateTime.Now, DateTime.Now
                    );
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
                int U_EXX_FE_TipoOperacion = notaCredito.BusinessPartner.iTiOpe;
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
                    iTipIDRec = notaCredito.BusinessPartner.iTipIDRec;
                    dNumIDRec = rucPartes.Length > 0 ? rucPartes[0] : "";
                }

                string cMoneOpe = notaCredito.Currencies.cMoneOpe;
                string dDesMoneOpe = notaCredito.Currencies.dDesMoneOpe;
                decimal dTiCam = notaCredito.dTiCam;
                string CardName = notaCredito.BusinessPartner.dNomRec;
                string Country = notaCredito.BusinessPartner.cPaisRec;
                string DescPais = notaCredito.BusinessPartner.dDesPaisRe;
                string iTiDE = notaCredito.U_CDOC == "03" ? "05" : notaCredito.U_CDOC;
                string dEst = notaCredito.U_EST;
                string dPunExp = notaCredito.U_PDE;
                string dNumDoc = notaCredito.FolioNum.PadLeft(7, '0');
                DateTime dFeIniT = DateTime.ParseExact(notaCredito.U_FITE, "yyyy-MM-dd", null);
                int dNumTim = notaCredito.U_TIM;
                int iTipEmi = 1; // Siempre fijo en 1

                int iMotEmi = notaCredito.iMotEmi;
                int iTipDocAso = notaCredito.iTipDocAso;
                string? dEstDocAso = null;
                string? dPExpDocAso = null;
                string? dNumDocAso = null;
                int? iTipoDocAso = null;
                string? dCdCDERef = null;
                int? dNTimDI = null;
                DateTime? dFecEmiDI = null;

                string notacreditoReferencia = notaCredito.U_NUMFC;

                if (!string.IsNullOrWhiteSpace(notacreditoReferencia))
                {
                    string[] partesNotaCredito = notacreditoReferencia.Split('-');

                    if (iTipDocAso == 1)
                    {
                        string? EST = partesNotaCredito.Length > 0 ? partesNotaCredito[0] : null;
                        string? PDE = partesNotaCredito.Length > 1 ? partesNotaCredito[1] : null;
                        string? Folio = partesNotaCredito.Length > 2 ? partesNotaCredito[2] : null;

                        var datos = await _notaCreditoService.ObtenerCDCFactura(EST, PDE, Folio, rucCompleto);

                        dCdCDERef = datos.dCdCDERef;

                    }
                    else
                    {
                        dEstDocAso = partesNotaCredito.Length > 0 ? partesNotaCredito[0] : null;
                        dPExpDocAso = partesNotaCredito.Length > 1 ? partesNotaCredito[1] : null;
                        dNumDocAso = partesNotaCredito.Length > 2 ? partesNotaCredito[2] : null;
                        iTipoDocAso = 1;

                        var datos = await _notaCreditoService.ObtenerCDCFactura(dEstDocAso, dPExpDocAso, dNumDocAso, rucCompleto);

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
                DateTime dFecFirma = DateTime.Now;

                // Procesamiento de líneas de items
                List<Item> itemsList = new List<Item>();
                if (notaCredito.Items != null && notaCredito.Items.Any())
                {
                    foreach (var item in notaCredito.Items)
                    {
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
                            baseGravadaIVA = Math.Round((100 * (totalBruto * proporcionIVA)) / (10000 + (tasaIVA * proporcionIVA)), 8);
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
                            dCantProSer = item.dCantProSer,
                            dPUniProSer = item.dPUniProSer,
                            cUniMed = item.cUniMed,
                            dDesUniMed = item.dDesUniMed,
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


                // Actualizamos en SAP el cdc del documento
                //            bool actualizado = await _notaCreditoService.ActualizarCDC(notaCredito.DocEntry, cdc);

                /*            if (actualizado)
                            {
                                _logger.LogInformation($"CDC generado y actualizado: {cdc}");   
               */
                // Generar XML
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string xmlDir = Path.Combine(basePath, "XML");
                Directory.CreateDirectory(xmlDir);

                string rutaXml = Path.Combine(xmlDir, $"Documento_{cdc}.xml");

                GenerarXML.SerializarDocumentoElectronico(cdc, dv, dFecFirma, rutaXml, dCodSeg, xmlTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, _empresaInfo.Ruc,
                    _empresaInfo.Dv, _empresaInfo.TipoContribuyente, _empresaInfo.NombreEmpresa, _empresaInfo.DireccionEmisor, _empresaInfo.NumeroCasaEmisor, _empresaInfo.CodDepartamento, _empresaInfo.DescDepartamento,
                    _empresaInfo.CodDistrito, _empresaInfo.DescDistrito, _empresaInfo.CodLocalidad, _empresaInfo.DescLocalidad, _empresaInfo.TelefEmisor, _empresaInfo.EmailEmisor, U_CRSI, U_TIPCONT,
                    U_EXX_FE_TipoOperacion, Country, DescPais, CardName, dRucReceptor, dDVReceptor, dTiCam, iIndPres, iCondOpe, iCondCred, iTiPago, dMonTiPag, cMoneTiPag, dDMoneTiPag, dTiCamTiPag, iTipIDRec, dNumIDRec,
                    _empresaInfo.ActividadesEconomicas, _empresaInfo.ObligacionesAfectadas, cuotasList, itemsList, plazoCredito, totalesFactura, certificadoBytes, contraseñaCertificado,
                    iMotEmi, dCdCDERef, dFecEmiDI, dNTimDI, dEstDocAso, dPExpDocAso, dNumDocAso, iTipDocAso, iTipoDocAso);

                try
                {
                    string rutaXmlFirmado = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML", $"Documento_{cdc}.xml");
                    string xmlFirmadoFinal = File.ReadAllText(rutaXmlFirmado);

                    tipoDocumentoLote ??= xmlTiDE;

                    if (_config.Sifen.Url.ToLower().Contains("test"))
                    {
                        string respuesta = await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, xmlTiDE);
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
                    _logger.LogError($"Error al preparar/enviar documento con CDC {cdc}: {ex.Message}");
                    _logger.LogError($"StackTrace: {ex.StackTrace}");

                    string errorPath = "Errors";
                    Directory.CreateDirectory(errorPath);
                    File.WriteAllText(Path.Combine(errorPath, $"error_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                        $"CDC: {cdc}\nError: {ex.Message}\nStackTrace: {ex.StackTrace}");
                }
            }

            // Si quedó un lote incompleto
            if (loteDocumentos.Count > 0 && !_config.Sifen.Url.ToLower().Contains("test"))
            {
                await _envioService.EnviarDocumentoAsincronico(loteDocumentos, tipoDocumentoLote, notasCredito.First().U_CDOC);
                _logger.LogInformation($"Lote final de {loteDocumentos.Count} documento(s) enviado.");
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
    
    private async Task RegenerarXmlFirmado(Factura factura, string cdc, string dCodSeg, byte[] certificadoBytes, string contraseñaCertificado)
    {
        string xmlDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XML");
        Directory.CreateDirectory(xmlDir);

        string rutaXml = Path.Combine(xmlDir, $"Documento_{cdc}.xml");

        // Obtener la info de empresa actual
        if (_empresaInfo == null)
            _empresaInfo = await _empresaService.GetEmpresaInfo();

        // Obtener ítems y totales actualizados
        var totalesFactura = Totalizador.CalcularTotalesFactura(factura.Items, factura.dTiCam, factura.Currencies.cMoneOpe);
        // Obtener el código de seguridad actual del documento generado
        //string dCodSeg = cdc.Substring(36, 8); 

        // Fecha de emisión y firma
        DateTime dFecFirma = DateTime.Now;
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
        string iTiDE = factura.U_CDOC;

        GenerarXML.SerializarDocumentoElectronico(
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
            iTiOpe: factura.BusinessPartner.iTiOpe,
            cPaisRec: factura.BusinessPartner.cPaisRec,
            dDesPaisRe: factura.BusinessPartner.dDesPaisRe,
            dNomRec: factura.BusinessPartner.dNomRec,
            dRucReceptor: factura.BusinessPartner.FederalTaxID.Split('-')[0],
            dDVReceptor: factura.BusinessPartner.FederalTaxID.Split('-').Length > 1 ? int.Parse(factura.BusinessPartner.FederalTaxID.Split('-')[1]) : 0,
            dTiCam: factura.dTiCam,
            iIndPres: factura.iIndPres,
            iCondOpe: factura.iCondOpe,
            iCondCred: factura.iCondCred,
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
            items: factura.Items,
            plazoCredito: factura.OperacionCredito?.PlazoCredito,
            totales: totalesFactura,
            certificadoBytes: certificadoBytes,
            contraseñaCertificado: contraseñaCertificado
        );
    }

}    