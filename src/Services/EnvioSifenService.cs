using System.Text;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http.Headers;
using System.Security.Authentication;
using Newtonsoft.Json;
using System.Xml.Schema;
using System.Xml;
using System.Text.RegularExpressions;

public class EnvioSifenService 
{
    private HttpClient _httpClient;
    private readonly LoggerSifenService _logger;
    private readonly string _baseDatos;
    private readonly ILogger _log;
    private readonly SAPServiceLayer _sapServiceLayer;
    
    public EnvioSifenService(string baseUrl, LoggerSifenService logger, Config config, ILogger log, SAPServiceLayer sapServiceLayer = null)
    {
        _logger = logger;
        _baseDatos = config.SapServiceLayer.CompanyDB;
        _log = log;
        _sapServiceLayer = sapServiceLayer;
        
        var handler = new HttpClientHandler
        {
            SslProtocols = SslProtocols.Tls12,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };
        
        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(baseUrl),
            Timeout = TimeSpan.FromMinutes(2)
        };
        
        _log.LogInformation($"EnvioSifenService inicializado con URL base: {baseUrl}");
    }

    public static string NormalizarXmlFirmado(string xmlFirmado, bool quitarDeclaracionXml = true)
{
        if (string.IsNullOrWhiteSpace(xmlFirmado))
            return xmlFirmado;

        if (quitarDeclaracionXml && xmlFirmado.TrimStart().StartsWith("<?xml"))
        {
            int endDeclaration = xmlFirmado.IndexOf("?>");
            if (endDeclaration > 0)
                xmlFirmado = xmlFirmado.Substring(endDeclaration + 2).TrimStart();
        }

        XmlDocument xmlDoc = new XmlDocument();
        xmlDoc.PreserveWhitespace = true;
        xmlDoc.LoadXml(xmlFirmado);

        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            OmitXmlDeclaration = quitarDeclaracionXml,
            NewLineHandling = NewLineHandling.None,
            CheckCharacters = false
        };

        using (var ms = new MemoryStream())
        using (var writer = XmlWriter.Create(ms, settings))
        {
            xmlDoc.Save(writer);
            writer.Flush();
            ms.Position = 0;
            return Encoding.UTF8.GetString(ms.ToArray()).TrimStart('\r', '\n');
        }
    }
    
    public async Task<string> EnviarDocumentoAsincronico(List<(string cdc, string xmlFirmado)> documentosFirmados, string tipoDocumento)
    {
        if (documentosFirmados.Count < 1 || documentosFirmados.Count > 50)
            throw new Exception("El lote debe contener entre 1 y 50 documentos.");

        var fechaCreacion = DateTime.Now;
        var fechaEnvio = DateTime.Now;
        string dId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
        string debugDir = "debug_xml";
        Directory.CreateDirectory(debugDir);
        string numeroLote = "";
        string codigoRespuesta = "";

        try
        {
            if (_sapServiceLayer != null)
            {
                try
                {
                    var (certificadoBytes, password) = await ObtenerCertificadoActivo();
                    ConfigurarCertificadoCliente(certificadoBytes, password);
                }
                catch (Exception certEx)
                {
                    _log.LogWarning($"No se pudo obtener el certificado TLS: {certEx.Message}");
                }
            }

            // Crear XML del lote
            XmlDocument loteDoc = new XmlDocument();
            loteDoc.PreserveWhitespace = true;
            XmlDeclaration xmlDecl = loteDoc.CreateXmlDeclaration("1.0", "utf-8", null);
            loteDoc.AppendChild(xmlDecl);

            XmlElement rootElement = loteDoc.CreateElement("rLoteDE", "http://ekuatia.set.gov.py/sifen/xsd");
            rootElement.SetAttribute("xmlns:xsi", "http://www.w3.org/2001/XMLSchema-instance");
            rootElement.SetAttribute("xsi:schemaLocation", "http://ekuatia.set.gov.py/sifen/xsd siRecepLoteDE_v150.xsd");
            loteDoc.AppendChild(rootElement);


            foreach (var (cdc, xmlFirmado) in documentosFirmados)
            {
                try
                {
                    string rutaArchivoFirmado = $"XML/Documento_{cdc}.xml";
                    File.Copy(rutaArchivoFirmado, Path.Combine(debugDir, $"rDE_completo_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), true);

                    string xmlNormalizado = NormalizarXmlFirmado(xmlFirmado);
                    XmlDocument docXml = new XmlDocument();
                    docXml.PreserveWhitespace = true;
                    docXml.LoadXml(xmlNormalizado);

                    XmlNode rdeImportado = loteDoc.ImportNode(docXml.DocumentElement, true);
                    rootElement.AppendChild(rdeImportado);
                }
                catch (Exception ex)
                {
                    _log.LogError($"Error al procesar XML para CDC {cdc}: {ex.Message}");
                    throw;
                }
            }

            // Guardar el lote en memoria
            MemoryStream loteStream = new MemoryStream();
            XmlWriterSettings settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Encoding = new UTF8Encoding(false)
            };
            using (XmlWriter writer = XmlWriter.Create(loteStream, settings))
            {
                loteDoc.Save(writer);
            }
            loteStream.Position = 0;

            string loteXmlDebug = Encoding.UTF8.GetString(loteStream.ToArray());
            File.WriteAllText(Path.Combine(debugDir, $"rLoteDE_zip_contenido_{dId}.xml"), loteXmlDebug);

            // Comprimir ZIP
            string zipPath = Path.Combine(debugDir, $"rLoteDE_lote_{dId}.zip");
            if (File.Exists(zipPath))
                File.Delete(zipPath);

            using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
            {
                var entry = zip.CreateEntry("rLoteDE.xml", CompressionLevel.Optimal);
                using (var entryStream = entry.Open())
                {
                    loteStream.CopyTo(entryStream);
                }
            }

            // Codificar en base64
            byte[] zipBytes = File.ReadAllBytes(zipPath);
            string base64Zip = Convert.ToBase64String(zipBytes);
            File.WriteAllText(Path.Combine(debugDir, $"lote_base64_{dId}.txt"), base64Zip);

            // Construir SOAP
            StringBuilder sb = new StringBuilder();
            sb.Append("<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\">");
            sb.Append("<soap:Header/>"); // evitar problemas con autenticación
            sb.Append("<soap:Body>");
            sb.Append("<rEnvioLote xmlns=\"http://ekuatia.set.gov.py/sifen/xsd\">");
            sb.Append($"<dId>{dId}</dId>");
            sb.Append($"<xDE>{base64Zip}</xDE>");
            sb.Append("</rEnvioLote>");
            sb.Append("</soap:Body>");
            sb.Append("</soap:Envelope>");

            string soapEnvelope = sb.ToString();
            File.WriteAllText(Path.Combine(debugDir, $"soap_request_lote_{dId}.xml"), soapEnvelope);

            var soapContent = new StringContent(soapEnvelope, new UTF8Encoding(false), "application/soap+xml");
            soapContent.Headers.ContentType.Parameters.Clear();
            soapContent.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("charset", "UTF-8"));
            soapContent.Headers.Remove("SOAPAction");
            soapContent.Headers.Add("SOAPAction", "\"http://ekuatia.set.gov.py/sifen/xsd/siRecepLoteDE\"");

            string fullUrl = "de/ws/async/recibe-lote";

            var response = await _httpClient.PostAsync(fullUrl, soapContent);
            string mensajeRespuesta = await response.Content.ReadAsStringAsync();
            File.WriteAllText(Path.Combine(debugDir, $"soap_response_lote_{dId}_{DateTime.Now:yyyyMMddHHmmss}.xml"), mensajeRespuesta);

            string estado = response.IsSuccessStatusCode ? "Enviado" : "Error";
            DateTime? fechaRespuesta = DateTime.Now;

            try
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(mensajeRespuesta);
                var ns = new XmlNamespaceManager(xmlDoc.NameTable);
                ns.AddNamespace("ns2", "http://ekuatia.set.gov.py/sifen/xsd");

                string codRes = xmlDoc.SelectSingleNode("//ns2:dCodRes", ns)?.InnerText;
                string nroLote = xmlDoc.SelectSingleNode("//ns2:dProtConsLote", ns)?.InnerText;

                if (!string.IsNullOrEmpty(codRes)) codigoRespuesta = codRes;
                if (!string.IsNullOrEmpty(nroLote)) numeroLote = nroLote;

                _log.LogInformation($"Código respuesta: {codigoRespuesta}, Número lote: {numeroLote}");
            }
            catch (Exception ex)
            {
                _log.LogWarning($"Error al analizar respuesta XML: {ex.Message}");
            }

            foreach (var (cdc, xmlFirmado) in documentosFirmados)
            {
                try
                {
                    _logger.RegistrarDocumento(_baseDatos, cdc, xmlFirmado, estado, tipoDocumento, "siRecepLoteDE", fechaCreacion, fechaEnvio, fechaRespuesta, mensajeRespuesta, codigoRespuesta);
                }
                catch (Exception dbEx)
                {
                    _log.LogError($"Error al registrar CDC {cdc}: {dbEx.Message}");
                }
            }

            return numeroLote;
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al enviar lote: {ex.Message}");
            if (ex.InnerException != null)
            {
                _log.LogError($"Error interno: {ex.InnerException.Message}");
            }
            throw;
        }
    }

    public async Task<bool> ConsultarEstadoLoteAsync(string dId, List<string> cdcs = null, int intentos = 0)
    {
        try
        {
            _log.LogInformation($"Consultando estado del lote: {dId} (Intento {intentos + 1})");

        var soapRequest = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"">
  <soap:Header/>
  <soap:Body>
    <xConsLoteDE xmlns=""http://ekuatia.set.gov.py/sifen/xsd"">
      <dProtConsLote>{dId}</dProtConsLote>
    </xConsLoteDE>
  </soap:Body>
</soap:Envelope>";

            var content = new StringContent(soapRequest, Encoding.UTF8, "application/soap+xml");
            content.Headers.Add("SOAPAction", "\"http://ekuatia.set.gov.py/sifen/xsd/siConsLoteDE\"");

            string endpoint = "de/ws/async/consulta-lote";
            var response = await _httpClient.PostAsync(endpoint, content);
            var resultXml = await response.Content.ReadAsStringAsync();

            string debugDir = "debug_xml";
            File.WriteAllText(Path.Combine(debugDir, $"soap_consulta_lote_{dId}_{DateTime.Now:yyyyMMddHHmmss}.xml"), resultXml);

            if (response.IsSuccessStatusCode)
            {
                XmlDocument xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(resultXml);

                var ns = new XmlNamespaceManager(xmlDoc.NameTable);
                ns.AddNamespace("env", "http://www.w3.org/2003/05/soap-envelope");
                ns.AddNamespace("ns2", "http://ekuatia.set.gov.py/sifen/xsd");

                string estado = xmlDoc.SelectSingleNode("//ns2:dEstRes", ns)?.InnerText;
                string codigo = xmlDoc.SelectSingleNode("//ns2:dCodRes", ns)?.InnerText;
                string mensaje = xmlDoc.SelectSingleNode("//ns2:dMsgRes", ns)?.InnerText;

                _log.LogInformation($"Resultado consulta lote - Estado: {estado}, Código: {codigo}, Mensaje: {mensaje}");

                // Volvemos a consultar después de un tiempo
                if (estado == "Procesamiento" && intentos < 5)
                {
                    _log.LogInformation($"Lote en procesamiento, esperando antes de volver a consultar...");
                    await Task.Delay(5000);
                    return await ConsultarEstadoLoteAsync(dId, cdcs, intentos + 1);
                }

                // Procesar los resultados individuales de cada documento
                if (cdcs != null && cdcs.Count > 0 && (estado == "Aprobado" || estado == "Aprobado con Observaciones" || estado == "Rechazado"))
                {
                    Dictionary<string, (string estado, string mensaje)> resultadosPorCdc = new Dictionary<string, (string, string)>();

                    // Buscar nodos de resultados individuales
                    var docNodes = xmlDoc.SelectNodes("//ns2:gResProc", ns);
                    if (docNodes != null)
                    {
                        foreach (XmlNode docNode in docNodes)
                        {
                            string cdc = docNode.SelectSingleNode("./ns2:dCDC", ns)?.InnerText;
                            string estadoDoc = docNode.SelectSingleNode("./ns2:dEstRes", ns)?.InnerText;
                            string mensajeDoc = docNode.SelectSingleNode("./ns2:dMsgRes", ns)?.InnerText;

                            if (!string.IsNullOrEmpty(cdc) && cdcs.Contains(cdc))
                            {
                                resultadosPorCdc[cdc] = (estadoDoc, mensajeDoc);
                                
                                // Actualizar el estado del documento en la base de datos
                                try
                                {
                                //    _logger.ActualizarEstadoDocumento(_baseDatos, cdc, estadoDoc, mensajeDoc);
                                    _log.LogInformation($"CDC {cdc} actualizado con estado: {estadoDoc}");
                                }
                                catch (Exception ex)
                                {
                                    _log.LogError($"Error al actualizar estado del CDC {cdc}: {ex.Message}");
                                }
                            }
                        }
                    }

                    // Verificar si todos los documentos del lote fueron procesados
                    bool todosProcesados = cdcs.All(cdc => resultadosPorCdc.ContainsKey(cdc));
                    _log.LogInformation($"Documentos procesados: {resultadosPorCdc.Count}/{cdcs.Count}");
                    
                    return todosProcesados;
                }
                
                return estado != "Procesamiento";
            }
            else
            {
                _log.LogWarning($"Error HTTP en consulta de lote: {response.StatusCode}");
                _log.LogWarning(resultXml);
                return false;
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al consultar estado del lote: {ex.Message}");
            return false;
        }
    }
    
    private async Task<(byte[] certificadoBytes, string contraseña)> ObtenerCertificadoActivo()
    {
        try
        {
            if (_sapServiceLayer == null)
            {
                throw new InvalidOperationException("SAPServiceLayer no está disponible para obtener el certificado");
            }
            
            // Consultar el certificado activo
            string query = "U_CERTIFICADOS?$filter=U_ACTIVO eq 'Y'";
            
            var response = await _sapServiceLayer.GetHttpClient().GetAsync(query);
            
            if (!response.IsSuccessStatusCode)
            {
                _log.LogError($"Error al consultar certificados: {response.StatusCode}");
                throw new Exception($"Error al consultar certificados: {response.StatusCode}");
            }
            
            var jsonResponse = await response.Content.ReadAsStringAsync();
            
            if (string.IsNullOrEmpty(jsonResponse))
            {
                throw new Exception("No se pudo obtener respuesta del servicio de certificados");
            }
            
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
            string certificadoBase64 = certificado["U_ARCHIVO"].ToString();
            string contraseñaBase64 = certificado["U_PWD"].ToString();
            
            byte[] certificadoBytes = Convert.FromBase64String(certificadoBase64);
            string contraseña = Encoding.UTF8.GetString(Convert.FromBase64String(contraseñaBase64));
            
            _log.LogInformation($"Certificado obtenido correctamente: {certificado["Name"]}");
            return (certificadoBytes, contraseña);
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al obtener certificado: {ex.Message}");
            if (ex.InnerException != null)
            {
                _log.LogError($"Error interno: {ex.InnerException.Message}");
            }
            throw new Exception("Error al obtener certificado digital", ex);
        }
    }
    
    private void ConfigurarCertificadoCliente(byte[] certificadoBytes, string contraseña)
    {
        try
        {
            // Cargar el certificado
            var certificado = new X509Certificate2(certificadoBytes, contraseña, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            
            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                ClientCertificates = { certificado }
            };
            
            var nuevoHttpClient = new HttpClient(handler)
            {
                BaseAddress = _httpClient.BaseAddress,
                Timeout = _httpClient.Timeout
            };
            
            foreach (var header in _httpClient.DefaultRequestHeaders)
            {
                nuevoHttpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
            
            var clienteAnterior = _httpClient;
            _httpClient = nuevoHttpClient;
            
            clienteAnterior.Dispose();
            
            _log.LogInformation($"Certificado cliente configurado: {certificado.Subject}, válido desde {certificado.NotBefore} hasta {certificado.NotAfter}");
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al configurar certificado cliente: {ex.Message}");
            throw;
        }
    }
}