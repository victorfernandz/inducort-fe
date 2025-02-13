using System;
using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

class Program
{
    static void Main(string[] args)
    {
        try
        {
            // Cargar la configuración desde config.json
            Config config = Config.LoadConfig();

            ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
            ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

            // Crea el servicio
            var host = Host.CreateDefaultBuilder(args)
                .UseWindowsService()
                .ConfigureServices(services =>
                {
                    services.AddSingleton(config);
                    services.AddSingleton<SAPServiceLayer>(new SAPServiceLayer(config));
                    services.AddHostedService<SAPCDCService>();
                    services.AddLogging(configure => configure.AddConsole());
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
