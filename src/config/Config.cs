using System;
using System.IO;
using Newtonsoft.Json;

public class Config
{
    public SapServiceLayerConfig SapServiceLayer { get; set; }
    public HanaDatabaseConfig HanaDatabase { get; set; }
    public SifenConfig Sifen { get; set; }

    public static Config LoadConfig()
    {
        try
        {
            string basePath = AppDomain.CurrentDomain.BaseDirectory;
            string configPath = Path.Combine(basePath, "config", "config.json");

            if (!File.Exists(configPath))
            {
                throw new FileNotFoundException($"Archivo de configuración no encontrado: {configPath}");
            }

            string json = File.ReadAllText(configPath);
            var config = JsonConvert.DeserializeObject<Config>(json);
            
            // Validar configuración mínima requerida
            if (config.SapServiceLayer == null)
            {
                throw new InvalidOperationException("Configuración SAP Service Layer no encontrada");
            }

            if (config.HanaDatabase == null)
            {
                config.HanaDatabase = new HanaDatabaseConfig
                {
                    ServerNode = "192.168.0.5:30015",
                    UserName = "SYSTEM",
                    Password = "V1nsoc4!",
                    Schema = "SAP_SIFEN"
                };
            }

            if (config.Sifen == null ||
                string.IsNullOrWhiteSpace(config.Sifen.Url) ||
                string.IsNullOrWhiteSpace(config.Sifen.IdCSC) ||
                string.IsNullOrWhiteSpace(config.Sifen.CSC))
            {
                throw new InvalidOperationException("Configuración SIFEN no encontrada o incompleta (Url, IdCSC, CSC)");
            }

            return config;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al cargar la configuración: {ex.Message}");
            throw;
        }
    }

    // Genera una cadena de conexión ODBC para HANA
    public string GetHanaConnectionString()
    {
        if (HanaDatabase == null)
        {
            throw new InvalidOperationException("La configuración de la base de datos HANA no está disponible");
        }

        return $"Driver=HDBODBC;ServerNode={HanaDatabase.ServerNode};UID={HanaDatabase.UserName};PWD={HanaDatabase.Password};";
    }
}

public class SapServiceLayerConfig
{
    public string Url { get; set; }
    public string CompanyDB { get; set; }
    public string UserName { get; set; }
    public string Password { get; set; }
}

public class HanaDatabaseConfig
{
    public string ServerNode { get; set; }
    public string UserName { get; set; }
    public string Password { get; set; }
    public string Schema { get; set; }
}

public class SifenConfig
{
    public string Url { get; set; }
    public string IdCSC { get; set; }
    public string CSC { get; set; }
}
