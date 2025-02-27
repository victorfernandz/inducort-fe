using System;
using System.IO;
using Newtonsoft.Json;

public class Config
{
    public SapServiceLayerConfig SapServiceLayer { get; set; }

    public static Config LoadConfig()
    {
        string basePath = AppDomain.CurrentDomain.BaseDirectory;
        string configPath = Path.Combine(basePath, "config", "config.json");

        if (!File.Exists(configPath))
        {
            throw new FileNotFoundException($"Archivo de configuración no encontrado: {configPath}");
        }

        string json = File.ReadAllText(configPath);
        return JsonConvert.DeserializeObject<Config>(json);
    }
}

public class SapServiceLayerConfig
{
    public string Url { get; set; }
    public string CompanyDB { get; set; }
    public string UserName { get; set; }
    public string Password { get; set; }
}
