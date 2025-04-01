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
    
    public async Task EnviarDocumentoAsincronico(string cdc, string qr, string xmlFirmado, string tipoDocumento)
    {
        try
        {
            var fechaCreacion = DateTime.Now;
            var fechaEnvio = DateTime.Now;
            
            // Obtener certificado activo si es necesario
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
                _log.LogWarning("Continuando sin certificado para autenticación mutua");
            }
            
            // Crear identificador único para el lote
            string dId = DateTime.Now.ToString("yyyyMMddHHmmssfff");
            
            // Preparar el XML del documento para el lote (eliminar declaración XML si existe)
            string xmlDocumento = xmlFirmado.TrimStart();
            if (xmlDocumento.StartsWith("<?xml", StringComparison.OrdinalIgnoreCase))
            {
                int endOfDeclaration = xmlDocumento.IndexOf("?>");
                if (endOfDeclaration > 0)
                {
                    xmlDocumento = xmlDocumento.Substring(endOfDeclaration + 2).TrimStart();
                }
            }

            // Verificar si el documento resultante empieza correctamente
            if (!xmlDocumento.StartsWith("<rDE"))
            {
                _log.LogWarning($"El documento XML no comienza con <rDE> después de quitar la declaración XML");
            }

            // SOLUCIÓN DEFINITIVA: Reemplazar el esquema https por http
            // Esta es la corrección crítica que faltaba
            xmlDocumento = xmlDocumento.Replace(
                "schemaLocation=\"https://ekuatia.set.gov.py/sifen/xsd", 
                "schemaLocation=\"http://ekuatia.set.gov.py/sifen/xsd");

            // No envolver en rLoteDE, enviar directamente
            string loteXml = $"<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n{xmlDocumento}";

            // Guardar una copia del XML exacto que enviaremos
            string debugDir = "debug_xml";
            Directory.CreateDirectory(debugDir);
            File.WriteAllText(
                Path.Combine(debugDir, $"xml_a_enviar_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), 
                loteXml
            );

            // Validación del XML antes de enviarlo
            try {
                var xmlDoc = new XmlDocument();
                xmlDoc.LoadXml(loteXml);
                _log.LogInformation("XML validado correctamente");
                // Registro adicional para confirmar la corrección del esquema
                _log.LogInformation($"Schema location: {xmlDoc.DocumentElement.GetAttribute("schemaLocation", "http://www.w3.org/2001/XMLSchema-instance")}");
            } catch (XmlException xmlEx) {
                _log.LogError($"XML mal formado: {xmlEx.Message}");
                throw new Exception("El documento XML no está bien formado", xmlEx);
            }

            // Comprimir el XML del lote
            byte[] compressedData;
            using (var memoryStream = new MemoryStream())
            {
                using (var gzipStream = new GZipStream(memoryStream, CompressionMode.Compress, true))
                {
                    byte[] loteBytes = Encoding.UTF8.GetBytes(loteXml);
                    gzipStream.Write(loteBytes, 0, loteBytes.Length);
                }
                compressedData = memoryStream.ToArray();
            }

            // Convertir a Base64
            string base64CompressedData = Convert.ToBase64String(compressedData);

            // Crear el sobre SOAP con la estructura correcta para siRecepLoteDE
            var soapBuilder = new StringBuilder();
            soapBuilder.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            soapBuilder.AppendLine("<soap:Envelope xmlns:soap=\"http://www.w3.org/2003/05/soap-envelope\">");
            soapBuilder.AppendLine("<soap:Header/>");
            soapBuilder.AppendLine("<soap:Body>");
            soapBuilder.AppendLine($"<rEnvioLote xmlns=\"http://ekuatia.set.gov.py/sifen/xsd\">");
            soapBuilder.AppendLine($"<dId>{dId}</dId>");
            soapBuilder.AppendLine($"<xDE>{base64CompressedData}</xDE>");
            soapBuilder.AppendLine("</rEnvioLote>");
            soapBuilder.AppendLine("</soap:Body>");
            soapBuilder.AppendLine("</soap:Envelope>");
            string soapEnvelope = soapBuilder.ToString();

            // Enviar la solicitud SOAP
            var content = new StringContent(soapEnvelope, Encoding.UTF8, "application/soap+xml");
            
            // La URL correcta según el manual técnico sección 7.10 (pág. 41)
            string fullUrl = "de/ws/async/recibe-lote.wsdl";
            _log.LogInformation($"Enviando documento a SIFEN: {_httpClient.BaseAddress}{fullUrl}");
            
            // Configurar encabezados y opciones adecuadas para el servicio SOAP
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");
            content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("charset", "UTF-8"));
            content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("action", "http://ekuatia.set.gov.py/sifen/xsd/siRecepLoteDE"));
            
            // Guardar request SOAP para debugging
            File.WriteAllText(
                Path.Combine(debugDir, $"soap_request_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), 
                soapEnvelope
            );
            
            var response = await _httpClient.PostAsync(fullUrl, content);
            
            string estado = "Error";
            string mensajeRespuesta = "";
            DateTime? fechaRespuesta = null;
            
            // Guardar la respuesta HTTP completa para depuración
            _log.LogDebug($"Respuesta HTTP: {response.StatusCode}");
            foreach (var header in response.Headers)
            {
                _log.LogDebug($"Header {header.Key}: {string.Join(", ", header.Value)}");
            }
            
            if (response.IsSuccessStatusCode)
            {
                var respuestaXml = await response.Content.ReadAsStringAsync();
                mensajeRespuesta = respuestaXml;
                estado = "Enviado";
                fechaRespuesta = DateTime.Now;
                
                // Guardar respuesta XML para debugging
                File.WriteAllText(
                    Path.Combine(debugDir, $"soap_response_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.xml"), 
                    respuestaXml
                );
                
                _log.LogInformation($"Respuesta de SIFEN: Éxito con status code {response.StatusCode}");
                
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
                        
                        // Aquí podrías almacenar el número de lote para futuras consultas
                        mensajeRespuesta += $"|NumeroLote:{numeroLote}";
                    }
                    else
                    {
                        // También buscar dCodRes (código de resultado)
                        startIdx = respuestaXml.IndexOf("<dCodRes>");
                        endIdx = respuestaXml.IndexOf("</dCodRes>");
                        
                        if (startIdx > 0 && endIdx > startIdx)
                        {
                            startIdx += "<dCodRes>".Length;
                            string codigoResultado = respuestaXml.Substring(startIdx, endIdx - startIdx);
                            _log.LogInformation($"Código de resultado: {codigoResultado}");
                            
                            // Buscar mensaje asociado
                            int msgStartIdx = respuestaXml.IndexOf("<dMsgRes>");
                            int msgEndIdx = respuestaXml.IndexOf("</dMsgRes>");
                            
                            if (msgStartIdx > 0 && msgEndIdx > msgStartIdx)
                            {
                                msgStartIdx += "<dMsgRes>".Length;
                                string mensajeResultado = respuestaXml.Substring(msgStartIdx, msgEndIdx - msgStartIdx);
                                _log.LogInformation($"Mensaje de resultado: {mensajeResultado}");
                                
                                mensajeRespuesta += $"|Codigo:{codigoResultado}|Mensaje:{mensajeResultado}";
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
                    }
                }
            }
            
            try
            {
                _logger.RegistrarDocumento(_baseDatos, cdc, qr, xmlFirmado, estado, tipoDocumento, "siRecepLoteDE", fechaCreacion, fechaEnvio, fechaRespuesta, mensajeRespuesta);
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
                // Si ni siquiera podemos escribir el archivo, continuamos
            }
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