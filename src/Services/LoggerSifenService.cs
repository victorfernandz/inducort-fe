using System;
using System.Data.Odbc;
using System.IO;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;

public class LoggerSifenService
{
    private readonly string _connectionString;
    private readonly ILogger<LoggerSifenService> _logger;
    private readonly string _respuestasPath = "Respuestas";

    public LoggerSifenService(string connectionString, ILogger<LoggerSifenService> logger = null, string respuestasPath = null)
    {
        _connectionString = connectionString;
        _logger = logger;

        if (!string.IsNullOrEmpty(respuestasPath))
            _respuestasPath = respuestasPath;

        Directory.CreateDirectory(_respuestasPath);
    }

    public void RegistrarDocumento(string baseDatos, string cdc, string dId, string lote, string xmlFirmado, string estado, string tipoDocumento, string servicio, DateTime fechaCreacion, DateTime fechaEnvio, DateTime? fechaRespuesta, string mensajeRespuesta, string codigoRespuesta = "")
    {
        try
        {
            string nombreArchivo = string.Empty;
            string rutaCompleta = string.Empty;
            string dCodRes = codigoRespuesta;
            string dEstRes = estado;
            string dMsgRes = string.Empty;

            if (!string.IsNullOrEmpty(mensajeRespuesta))
            {
                // Guardar respuesta en archivo
                nombreArchivo = $"Respuesta_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.txt";
                rutaCompleta = Path.Combine(_respuestasPath, nombreArchivo);
                File.WriteAllText(rutaCompleta, mensajeRespuesta, Encoding.UTF8);
                _logger?.LogInformation($"Respuesta SIFEN para CDC {cdc} guardada en archivo: {rutaCompleta}");

                try
                {
                    XmlDocument doc = new XmlDocument();
                    doc.LoadXml(mensajeRespuesta);

                    XmlNamespaceManager ns = new XmlNamespaceManager(doc.NameTable);
                    ns.AddNamespace("ns2", "http://ekuatia.set.gov.py/sifen/xsd");

                    var nodeEstRes = doc.SelectSingleNode("//ns2:dEstRes", ns);
                    var nodeCodRes = doc.SelectSingleNode("//ns2:dCodRes", ns);
                    var nodeMsgRes = doc.SelectSingleNode("//ns2:dMsgRes", ns);

                    if (nodeEstRes != null) dEstRes = nodeEstRes.InnerText.Trim();
                    if (nodeCodRes != null) dCodRes = nodeCodRes.InnerText.Trim();
                    if (nodeMsgRes != null) dMsgRes = nodeMsgRes.InnerText.Trim();
                }
                catch (Exception exXml)
                {
                    try
                    {
                        int start = mensajeRespuesta.IndexOf("<title>", StringComparison.OrdinalIgnoreCase);
                        int end = mensajeRespuesta.IndexOf("</title>", StringComparison.OrdinalIgnoreCase);

                        if (start >= 0 && end > start)
                        {
                            dMsgRes = mensajeRespuesta.Substring(start + 7, end - (start + 7)).Trim();
                            dEstRes = "Error";
                            dCodRes = "HTML";
                        }
                    }
                    catch (Exception exHtml)
                    {
                        _logger?.LogWarning($"No se pudo analizar HTML: {exHtml.Message}");
                    }

                    _logger?.LogWarning($"No se pudo analizar XML: {exXml.Message}");
                }

                if (string.IsNullOrEmpty(dCodRes) && mensajeRespuesta.Contains("|Codigo:"))
                {
                    int startCodigo = mensajeRespuesta.IndexOf("|Codigo:") + 8;
                    int endCodigo = mensajeRespuesta.IndexOf("|", startCodigo);
                    if (endCodigo > startCodigo)
                    {
                        dCodRes = mensajeRespuesta.Substring(startCodigo, endCodigo - startCodigo);
                    }
                }

                if (string.IsNullOrEmpty(dMsgRes) && mensajeRespuesta.Contains("|Mensaje:"))
                {
                    int startMsg = mensajeRespuesta.IndexOf("|Mensaje:") + 9;
                    int endMsg = (mensajeRespuesta.IndexOf("|", startMsg) > 0)
                        ? mensajeRespuesta.IndexOf("|", startMsg)
                        : mensajeRespuesta.Length;
                    if (endMsg > startMsg)
                    {
                        dMsgRes = mensajeRespuesta.Substring(startMsg, endMsg - startMsg);
                    }
                }
            }

            if (!string.IsNullOrEmpty(codigoRespuesta))
            {
                dCodRes = codigoRespuesta;
            }

            // Insertar en base de datos
            using (var connection = new OdbcConnection(_connectionString))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO SAP_SIFEN.DOCUMENTOS_SIFEN
                        (BASE_DATOS, DID, LOTE, CDC, XML, ESTADO, DCODRES, TIPO_DOCUMENTO, SERVICIO, FECHA_CREACION, FECHA_ENVIO, FECHA_RESPUESTA, MENSAJE_RESPUESTA, RESPUESTA_BRUTA)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

                    command.Parameters.AddWithValue("@BASE_DATOS", baseDatos);
                    command.Parameters.AddWithValue("@DID", dId);
                    command.Parameters.AddWithValue("@LOTE", lote);
                    command.Parameters.AddWithValue("@CDC", cdc);
                    command.Parameters.AddWithValue("@XML", xmlFirmado);
                    command.Parameters.AddWithValue("@ESTADO", dEstRes);
                    command.Parameters.AddWithValue("@DCODRES", dCodRes);
                    command.Parameters.AddWithValue("@TIPO_DOCUMENTO", tipoDocumento);
                    command.Parameters.AddWithValue("@SERVICIO", servicio);
                    command.Parameters.AddWithValue("@FECHA_CREACION", fechaCreacion);
                    command.Parameters.AddWithValue("@FECHA_ENVIO", fechaEnvio);
                    command.Parameters.AddWithValue("@FECHA_RESPUESTA", fechaRespuesta.HasValue ? (object)fechaRespuesta.Value : DBNull.Value);
                    command.Parameters.AddWithValue("@MENSAJE_RESPUESTA", string.IsNullOrEmpty(dMsgRes) ? $"Archivo: {nombreArchivo}" : dMsgRes);
                    command.Parameters.AddWithValue("@RESPUESTA_BRUTA", mensajeRespuesta ?? string.Empty);

                    command.ExecuteNonQuery();
                    _logger?.LogInformation($"Documento con CDC {cdc} registrado correctamente en la base de datos.");
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                if (!string.IsNullOrEmpty(mensajeRespuesta) && !string.IsNullOrEmpty(cdc))
                {
                    string errorPath = Path.Combine(_respuestasPath, "Errors");
                    Directory.CreateDirectory(errorPath);

                    string errorFile = Path.Combine(errorPath, $"error_respuesta_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.txt");
                    File.WriteAllText(errorFile, mensajeRespuesta, Encoding.UTF8);

                    File.WriteAllText(
                        Path.Combine(errorPath, $"error_info_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                        $"Error: {ex.Message}\r\nStackTrace: {ex.StackTrace}"
                    );

                    _logger?.LogWarning($"Respuesta SIFEN guardada como error en archivo: {errorFile}");
                }
            }
            catch (Exception exInner)
            {
                _logger?.LogError($"Error al guardar respaldo de error: {exInner.Message}");
            }

            _logger?.LogError($"Error al registrar documento con CDC {cdc}: {ex.Message}");
            throw;
        }
    }
    
    public (string dId, string lote) ObtenerLotePorCDC(string cdc)
    {
        using var conn = new OdbcConnection(_connectionString);
        conn.Open();

        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT DID, LOTE FROM SAP_SIFEN.DOCUMENTOS_SIFEN WHERE CDC = ? ORDER BY FECHA_CREACION DESC";
        cmd.Parameters.AddWithValue("@CDC", cdc);

        using var reader = cmd.ExecuteReader();
        if (reader.Read())
        {
            string dId = reader["DID"]?.ToString();
            string lote = reader["LOTE"]?.ToString();
            return (dId, lote);
        }

        return (null, null);
    }

}