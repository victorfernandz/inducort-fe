// Archivo: MultiBaseSAPCDCService.cs
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

public class MultiBaseSAPCDCService : BackgroundService
{
    private readonly ILogger<MultiBaseSAPCDCService> _logger;
    private readonly Config _config;
    private readonly IServiceProvider _serviceProvider;

    public MultiBaseSAPCDCService(ILogger<MultiBaseSAPCDCService> logger, Config config, IServiceProvider serviceProvider)
    {
        _logger = logger;
        _config = config;
        _serviceProvider = serviceProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("MultiBaseSAPCDCService iniciado...");

        while (!stoppingToken.IsCancellationRequested)
        {
            foreach (var sapConfig in _config.SapServiceLayerList)
            {
                try
                {
                    _logger.LogInformation($"Procesando base de datos: {sapConfig.CompanyDB}");

                    using var scope = _serviceProvider.CreateScope();
                    var scopedServices = scope.ServiceProvider;

                    var sapService = new SAPServiceLayer(sapConfig);
                    var empresaService = new EmpresaService(sapService, scopedServices.GetRequiredService<ILogger<EmpresaService>>());
                    var facturaService = new FacturaService(sapService, scopedServices.GetRequiredService<ILogger<FacturaService>>());
                    var notaCreditoService = new NotaCreditoService(sapService, scopedServices.GetRequiredService<ILogger<NotaCreditoService>>());
                    var loggerSifen = scopedServices.GetRequiredService<LoggerSifenService>();
                    var envioService = new EnvioSifenService(sapConfig.Sifen.Url, loggerSifen, new Config { SapServiceLayerList = new List<SapServiceLayerConfig> { sapConfig }, HanaDatabase = _config.HanaDatabase },
                        scopedServices.GetRequiredService<ILogger<EnvioSifenService>>(), sapService
                    );

                    var servicio = new SAPCDCService(scopedServices.GetRequiredService<ILogger<SAPCDCService>>(), sapService, facturaService, notaCreditoService, empresaService, envioService, loggerSifen,
                        new Config { SapServiceLayerList = new List<SapServiceLayerConfig> { sapConfig }, HanaDatabase = _config.HanaDatabase }
                    );

                    await servicio.ProcesarTodoAsync(stoppingToken);

                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, $"Error procesando base {sapConfig.CompanyDB}");
                }
            }

            _logger.LogInformation("Esperando 10 minutos para el siguiente ciclo...");
            await Task.Delay(TimeSpan.FromMinutes(10), stoppingToken);
        }
    }
} 
