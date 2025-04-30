using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using System.IO.Compression;
using System.Security.Cryptography.X509Certificates;
using System.Net.Http.Headers;
using System.Security.Authentication;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.Xml;

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
    
    public async Task EnviarDocumentoAsincronico(string cdc, string xmlFirmado, string tipoDocumento)
    {
        try
        {
            var fechaCreacion = DateTime.Now;
            var fechaEnvio = DateTime.Now;

            // Cargar certificado desde SAP si está disponible
            try
            {
                if (_sapServiceLayer != null)
                {
                    var (certificadoBytes, password) = await ObtenerCertificadoActivo();
                    ConfigurarCertificadoCliente(certificadoBytes, password);
                }
            }
            catch (Exception certEx)
            {
                _log.LogWarning($"No se pudo obtener el certificado para autenticación TLS: {certEx.Message}");
            }

            // ID único para el lote
            string dId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            string xmlDocumento = xmlFirmado;

            string debugDir = "debug_xml";
            Directory.CreateDirectory(debugDir);
            File.WriteAllText(Path.Combine(debugDir, $"rDE_completo_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), xmlDocumento);

            XmlDocument xmlDoc = new XmlDocument();
            xmlDoc.PreserveWhitespace = true;
            xmlDoc.LoadXml(xmlFirmado);

            XmlElement deNode = xmlDoc.GetElementsByTagName("DE")[0] as XmlElement;
            if (deNode == null)
                throw new Exception("No se encontró el elemento <DE> en el XML");

            bool tieneGCamFuFD = xmlDoc.GetElementsByTagName("gCamFuFD").Count > 0;
            _log.LogInformation($"El documento {(tieneGCamFuFD ? "ya tiene" : "no tiene")} un nodo gCamFuFD");

            string deXml;
            using (var sw = new StringWriter())
            {
                var settings = new XmlWriterSettings
                {
                    OmitXmlDeclaration = true,
                    Indent = false,
                    NewLineHandling = NewLineHandling.None,
                    Encoding = new UTF8Encoding(false)
                };

                using (var writer = XmlWriter.Create(sw, settings))
                {
                    deNode.WriteTo(writer); 
                }

                deXml = sw.ToString();
            }

            File.WriteAllText(Path.Combine(debugDir, $"DE_solo_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), deXml);

            byte[] deBytes = Encoding.UTF8.GetBytes(deXml);
            byte[] compressedData;

            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionLevel.Optimal))
                {
                    gzipStream.Write(deBytes, 0, deBytes.Length);
                    gzipStream.Flush();
                }
                compressedData = memoryStream.ToArray();
            }

            string base64CompressedData = Convert.ToBase64String(compressedData);

            // Guardar versiones intermedias para depuración
            File.WriteAllBytes(Path.Combine(debugDir, $"debug_lote_comprimido_{cdc}.gz"), compressedData);
            File.WriteAllText(Path.Combine(debugDir, $"debug_lote_base64_{cdc}.txt"), base64CompressedData);

            // Construcción del sobre SOAP para siRecepLote
            string soapEnvelope = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
<soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"">
  <soap:Header/>
  <soap:Body>
    <rEnvioLote xmlns=""http://ekuatia.set.gov.py/sifen/xsd"">
      <dId>{dId}</dId>
      <xDE>{base64CompressedData}</xDE>
    </rEnvioLote>
  </soap:Body>
</soap:Envelope>";

            File.WriteAllText(Path.Combine(debugDir, $"soap_request_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), soapEnvelope);

            // Validación de Base64
            bool EsBase64Valido(string base64)
            {
                Span<byte> buffer = new Span<byte>(new byte[base64.Length]);
                return Convert.TryFromBase64String(base64, buffer, out _);
            }

            if (!EsBase64Valido(base64CompressedData))
            {
                _log.LogError("El contenido codificado en Base64 del XML comprimido no es válido.");
                throw new FormatException("Base64 inválido en el campo <xDE>.");
            }   
                        
            // Enviar solicitud
            var content = new StringContent(soapEnvelope, Encoding.UTF8, "application/soap+xml");
            content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("charset", "UTF-8"));
            content.Headers.Add("SOAPAction", "\"http://ekuatia.set.gov.py/sifen/xsd/siRecepLoteDE\"");

            string fullUrl = "de/ws/async/recibe-lote";
            
            var response = await _httpClient.PostAsync(fullUrl, content);
            // Procesar respuesta
            string estado = "Error";
            string mensajeRespuesta = "";
            string codigoRespuesta = "";
            DateTime? fechaRespuesta = null;

            if (response.IsSuccessStatusCode)
            {
                var respuestaXml = await response.Content.ReadAsStringAsync();
                mensajeRespuesta = respuestaXml;
                estado = "Enviado";
                fechaRespuesta = DateTime.Now;

                File.WriteAllText(Path.Combine(debugDir, $"soap_response_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), respuestaXml);
                _log.LogInformation($"Documento enviado correctamente. StatusCode: {response.StatusCode}");
                
                // Analizar respuesta para obtener el número de lote
                try 
                {
                    // Verificar si contiene "dProtConsLote" mediante búsqueda simple
                    int startIdx = respuestaXml.IndexOf("<dProtConsLote>");
                    int endIdx = respuestaXml.IndexOf("</dProtConsLote>");
                    
                    if (startIdx > 0 && endIdx > startIdx)
                    {
                        startIdx += "<dProtConsLote>".Length;
                        string numeroLote = respuestaXml.Substring(startIdx, endIdx - startIdx);
                        _log.LogInformation($"Número de lote obtenido: {numeroLote}");

                        mensajeRespuesta += $"|NumeroLote:{numeroLote}";
                        codigoRespuesta = "0300"; 

                        // Llamada inmediata a consulta del estado
                        if (!string.IsNullOrEmpty(numeroLote) && estado == "Enviado" && codigoRespuesta == "0300")
                        {
                            await ConsultarEstadoLoteAsync(numeroLote);
                        }
                    }
                    else
                    {
                        // También buscar dCodRes (código de resultado)
                        startIdx = respuestaXml.IndexOf("<ns2:dCodRes>");
                        endIdx = respuestaXml.IndexOf("</ns2:dCodRes>");
                        
                        if (startIdx > 0 && endIdx > startIdx)
                        {
                            startIdx += "<ns2:dCodRes>".Length;
                            codigoRespuesta = respuestaXml.Substring(startIdx, endIdx - startIdx);
                            _log.LogInformation($"Código de resultado: {codigoRespuesta}");
                            
                            // Buscar mensaje asociado
                            int msgStartIdx = respuestaXml.IndexOf("<ns2:dMsgRes>");
                            int msgEndIdx = respuestaXml.IndexOf("</ns2:dMsgRes>");
                            
                            if (msgStartIdx > 0 && msgEndIdx > msgStartIdx)
                            {
                                msgStartIdx += "<ns2:dMsgRes>".Length;
                                string mensajeResultado = respuestaXml.Substring(msgStartIdx, msgEndIdx - msgStartIdx);
                                _log.LogInformation($"Mensaje de resultado: {mensajeResultado}");
                                
                                mensajeRespuesta += $"|Codigo:{codigoRespuesta}|Mensaje:{mensajeResultado}";
                            }
                        }
                    }
                }
                catch (Exception parseEx)
                {
                    _log.LogWarning($"No se pudo extraer información de la respuesta: {parseEx.Message}");
                }
            }
            else
            {
                mensajeRespuesta = $"Error HTTP: {response.StatusCode}";
                _log.LogError($"Error en respuesta SIFEN: {mensajeRespuesta}");
                
                // Intentar leer el cuerpo de la respuesta para más detalles
                if (response.Content != null)
                {
                    string errorBody = await response.Content.ReadAsStringAsync();
                    if (!string.IsNullOrEmpty(errorBody))
                    {
                        mensajeRespuesta += $" | Detalle: {errorBody}";
                        _log.LogError($"Detalle del error: {errorBody}");
                        
                        // Guardar respuesta de error para debugging
                        File.WriteAllText(
                            Path.Combine(debugDir, $"soap_error_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), errorBody
                        );
                        
                        // Extraer información del error del XML de rechazo
                        try
                        {
                            XmlDocument xmlError = new XmlDocument();
                            xmlError.LoadXml(errorBody);
                            
                            // Configurar namespace manager para la búsqueda XPath
                            XmlNamespaceManager nsManager = new XmlNamespaceManager(xmlError.NameTable);
                            nsManager.AddNamespace("env", "http://www.w3.org/2003/05/soap-envelope");
                            nsManager.AddNamespace("ns2", "http://ekuatia.set.gov.py/sifen/xsd");
                            
                            // Buscar estado del resultado
                            XmlNode estadoNode = xmlError.SelectSingleNode("//ns2:dEstRes", nsManager);
                            if (estadoNode != null)
                            {
                                estado = estadoNode.InnerText; // Actualizar el estado
                            }
                            
                            // Buscar código de resultado
                            XmlNode codigoNode = xmlError.SelectSingleNode("//ns2:dCodRes", nsManager);
                            if (codigoNode != null)
                            {
                                codigoRespuesta = codigoNode.InnerText; // Guardar código
                            }
                            
                            // Buscar mensaje de resultado
                            XmlNode mensajeNode = xmlError.SelectSingleNode("//ns2:dMsgRes", nsManager);
                            if (mensajeNode != null)
                            {
                                string mensajeError = mensajeNode.InnerText;
                                _log.LogWarning($"Error SIFEN: {estado} - Código: {codigoRespuesta} - Mensaje: {mensajeError}");
                            }
                        }
                        catch (Exception xmlEx)
                        {
                            _log.LogWarning($"No se pudo analizar el XML de error: {xmlEx.Message}");
                        }
                    }
                }
            }
            
            try
            {
                _logger.RegistrarDocumento(_baseDatos, cdc, xmlFirmado, estado, tipoDocumento, "siRecepLoteDE", 
                    fechaCreacion, fechaEnvio, fechaRespuesta, mensajeRespuesta, codigoRespuesta);
            }
            catch (Exception dbEx)
            {
                _log.LogError($"Error al registrar en la base de datos: {dbEx.Message}");
                if (dbEx.InnerException != null)
                {
                    _log.LogError($"Error interno: {dbEx.InnerException.Message}");
                }
                
                // Continuamos el proceso sin interrumpirlo por errores de BD
                _log.LogWarning($"Continuando el proceso a pesar del error de BD para documento con CDC: {cdc}");
            }
            
            _log.LogInformation($"Documento {cdc} procesado. Estado: {estado}");
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al enviar documento SIFEN: {ex.Message}");
            _log.LogError($"StackTrace: {ex.StackTrace}");
            
            // Guardar el error en archivo de respaldo
            try
            {
                string errorPath = "sifen_errors";
                Directory.CreateDirectory(errorPath);
                File.WriteAllText(
                    Path.Combine(errorPath, $"error_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                    $"CDC: {cdc}\nError: {ex.Message}\nStackTrace: {ex.StackTrace}\n\n" +
                    $"XML:\n{xmlFirmado}"
                );
            }
            catch
            {
                
            }
        }
    }

    public async Task ConsultarEstadoLoteAsync(string dId)
    {
        try
        {
            _log.LogInformation($"Consultando estado del lote: {dId}");

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
            //content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("action", "siConsLoteDE"));
            content.Headers.Add("SOAPAction", "\"siConsLoteDE\"");

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
            }
            else
            {
                _log.LogWarning($"Error HTTP en consulta de lote: {response.StatusCode}");
                _log.LogWarning(resultXml);
            }
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al consultar estado del lote: {ex.Message}");
        }
    }
    
    private async Task<(byte[] certificadoBytes, string contraseña)> ObtenerCertificadoActivo()
    {
        try
        {
            // Verificar que hay SAPServiceLayer disponible
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
            var certificado = new X509Certificate2(
                certificadoBytes, 
                contraseña, 
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet
            );
            
            // Crear un nuevo handler con el certificado
            var handler = new HttpClientHandler
            {
                SslProtocols = SslProtocols.Tls12,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
                ClientCertificates = { certificado }
            };
            
            // Crear un nuevo HttpClient con el handler configurado
            var nuevoHttpClient = new HttpClient(handler)
            {
                BaseAddress = _httpClient.BaseAddress,
                Timeout = _httpClient.Timeout
            };
            
            // Transferir headers del cliente anterior
            foreach (var header in _httpClient.DefaultRequestHeaders)
            {
                nuevoHttpClient.DefaultRequestHeaders.Add(header.Key, header.Value);
            }
            
            // Reemplazar el cliente actual
            var clienteAnterior = _httpClient;
            _httpClient = nuevoHttpClient;
            
            // Disponer el cliente anterior
            clienteAnterior.Dispose();
            
            _log.LogInformation($"Certificado cliente configurado: {certificado.Subject}, " +
                               $"válido desde {certificado.NotBefore} hasta {certificado.NotAfter}");
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al configurar certificado cliente: {ex.Message}");
            throw;
        }
    }
}