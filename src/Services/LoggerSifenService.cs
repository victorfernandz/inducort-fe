using System;
using System.Data.Odbc;
using System.IO;
using System.Text;
using Microsoft.Extensions.Logging;

public class LoggerSifenService
{
    private readonly string _connectionString;
    private readonly ILogger<LoggerSifenService> _logger;
    private readonly string _respuestasPath = "Respuestas"; // Carpeta por defecto

    public LoggerSifenService(string connectionString, ILogger<LoggerSifenService> logger = null, string respuestasPath = null)
    {
        _connectionString = connectionString;
        _logger = logger;
        
        // Si se proporciona una ruta personalizada, usarla
        if (!string.IsNullOrEmpty(respuestasPath))
        {
            _respuestasPath = respuestasPath;
        }
        
        // Asegurar que el directorio existe
        Directory.CreateDirectory(_respuestasPath);
    }

    public void RegistrarDocumento(string baseDatos, string cdc, string qr, string xmlFirmado, string estado, string tipoDocumento, string servicio, DateTime fechaCreacion, DateTime fechaEnvio, DateTime? fechaRespuesta, string mensajeRespuesta)
    {
        try
        {
            // Guardar el mensaje de respuesta SIFEN en un archivo separado
            string nombreArchivo = string.Empty;
            string rutaCompleta = string.Empty;
            
            if (!string.IsNullOrEmpty(mensajeRespuesta))
            {
                // Simplemente guardar con extensión .txt para evitar problemas
                nombreArchivo = $"Respuesta_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.txt";
                rutaCompleta = Path.Combine(_respuestasPath, nombreArchivo);
                
                // Guardar la respuesta sin procesar
                File.WriteAllText(rutaCompleta, mensajeRespuesta, Encoding.UTF8);
                
                _logger?.LogInformation($"Respuesta SIFEN para CDC {cdc} guardada en archivo: {rutaCompleta}");
            }

            using (var connection = new OdbcConnection(_connectionString))
            {
                connection.Open();

                using (var command = connection.CreateCommand())
                {
                    command.CommandText = @"
                        INSERT INTO SAP_SIFEN.DOCUMENTOS_SIFEN
                        (BASE_DATOS, CDC, QR, XML, ESTADO, TIPO_DOCUMENTO, SERVICIO, FECHA_CREACION, FECHA_ENVIO, FECHA_RESPUESTA, MENSAJE_RESPUESTA)
                        VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)";

                    command.Parameters.AddWithValue("@BASE_DATOS", baseDatos);
                    command.Parameters.AddWithValue("@CDC", cdc);
                    command.Parameters.AddWithValue("@QR", qr ?? string.Empty);
                    command.Parameters.AddWithValue("@XML", xmlFirmado);
                    command.Parameters.AddWithValue("@ESTADO", estado);
                    command.Parameters.AddWithValue("@TIPO_DOCUMENTO", tipoDocumento);
                    command.Parameters.AddWithValue("@SERVICIO", servicio);
                    command.Parameters.AddWithValue("@FECHA_CREACION", fechaCreacion);
                    command.Parameters.AddWithValue("@FECHA_ENVIO", fechaEnvio);
                    
                    if (fechaRespuesta.HasValue)
                        command.Parameters.AddWithValue("@FECHA_RESPUESTA", fechaRespuesta.Value);
                    else
                        command.Parameters.AddWithValue("@FECHA_RESPUESTA", DBNull.Value);
                    
                    // Almacenar la referencia al archivo en lugar del contenido completo
                    command.Parameters.AddWithValue("@MENSAJE_RESPUESTA", string.IsNullOrEmpty(nombreArchivo) ? string.Empty : $"Archivo: {nombreArchivo}");

                    command.ExecuteNonQuery();
                    
                    _logger?.LogInformation($"Documento con CDC {cdc} registrado correctamente en la base de datos.");
                }
            }
        }
        catch (Exception ex)
        {
            // En caso de error, intentar guardar el mensaje de respuesta en un archivo de error
            try
            {
                if (!string.IsNullOrEmpty(mensajeRespuesta) && !string.IsNullOrEmpty(cdc))
                {
                    string errorPath = Path.Combine(_respuestasPath, "Errors");
                    Directory.CreateDirectory(errorPath);
                    
                    string errorFile = Path.Combine(errorPath, $"error_respuesta_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.txt");
                    File.WriteAllText(errorFile, mensajeRespuesta, Encoding.UTF8);
                    
                    // También guardar información del error
                    File.WriteAllText(
                        Path.Combine(errorPath, $"error_info_{cdc}_{DateTime.Now:yyyyMMddHHmmss}.log"),
                        $"Error: {ex.Message}\r\nStackTrace: {ex.StackTrace}"
                    );
                    
                    _logger?.LogWarning($"Respuesta SIFEN guardada en archivo de error: {errorFile}");
                }
            }
            catch (Exception exInner)
            {
                _logger?.LogError($"Error al guardar respuesta en archivo de error: {exInner.Message}");
            }
            
            _logger?.LogError($"Error al registrar documento con CDC {cdc}: {ex.Message}");
            throw;
        }
    }
}