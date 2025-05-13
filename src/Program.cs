using System;
using System.IO;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            // Cargar la configuración desde config.json
            Config config = Config.LoadConfig();

            // Permitir certificados SSL no válidos (ambiente de test)
            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            // Crear y ejecutar el host como servicio de Windows
            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices(services =>
                {
                    // Registrar la configuración
                    services.AddSingleton(config);

                    // Registrar servicios estándar
                    services.AddSingleton<SAPServiceLayer>();
                    services.AddSingleton<EmpresaService>();
                    services.AddSingleton<FacturaService>();

                    // Registrar el servicio de logger para SAP_SIFEN
                    services.AddSingleton<LoggerSifenService>(sp =>
                    {
                        var cfg = sp.GetRequiredService<Config>();
                        var logger = sp.GetRequiredService<ILogger<LoggerSifenService>>();
                        
                        // Usar el método de la clase Config para obtener la cadena de conexión
                        string connectionString = cfg.GetHanaConnectionString();
                        
                        return new LoggerSifenService(connectionString, logger);
                    });

                    // Registrar servicio de envío a SIFEN
                    services.AddSingleton<EnvioSifenService>(sp =>
                    {
                        var cfg = sp.GetRequiredService<Config>();
                        var logger = sp.GetRequiredService<ILogger<EnvioSifenService>>();
                        var loggerSifen = sp.GetRequiredService<LoggerSifenService>();
                        var sapService = sp.GetRequiredService<SAPServiceLayer>();
                                            
                        // Usa la URL de SIFEN desde la configuración y pasa el SAPServiceLayer
                        return new EnvioSifenService(cfg.Sifen.Url, loggerSifen, cfg, logger, sapService);
                    });

                    services.AddSingleton<CancelarDocumento>(sp =>
                    {
                        var logger = sp.GetRequiredService<ILogger<CancelarDocumento>>();
                        var loggerSifen = sp.GetRequiredService<LoggerSifenService>();
                        var config = sp.GetRequiredService<Config>();
                        var sapService = sp.GetRequiredService<SAPServiceLayer>();
                        return new CancelarDocumento(logger, loggerSifen, config, sapService);
                    });

                    // Registrar servicio principal que arranca la lógica del sistema
                    services.AddHostedService<SAPCDCService>();

                    // Configurar logging
                    services.AddLogging(configure => 
                    {
                        configure.AddConsole();
                        
                        // Configurar archivo de log
                        string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "logs");
                        Directory.CreateDirectory(logDir);
                        
                        configure.AddFile(Path.Combine(logDir, "sifen-service-{Date}.log"));
                    });
                })
                .Build();

            host.Run();
        }
        catch (Exception ex)
        {
            File.WriteAllText("error.log", ex.ToString());
            Console.WriteLine($"Error al iniciar el servicio: {ex.Message}");
        }
    }
} 