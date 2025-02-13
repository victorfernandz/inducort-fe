using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

public class SAPCDCService : BackgroundService
{
    private readonly ILogger<SAPCDCService> _logger;
    private readonly SAPServiceLayer _sapServiceLayer;

    public SAPCDCService(ILogger<SAPCDCService> logger, SAPServiceLayer sapServiceLayer)
    {
        _logger = logger;
        _sapServiceLayer = sapServiceLayer;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Servicio SAPCDC iniciado...");

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                _logger.LogInformation("Buscando facturas sin CDC en SAP...");
                
                //Login
                bool loggedIn = await _sapServiceLayer.Login();
                if (!loggedIn)
                {
                    _logger.LogError("No se pudo iniciar sesión en SAP.");
                    await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
                    continue;
                }

                //Consulta las facturas sin CDC
                var facturas = await _sapServiceLayer.GetFacturasSinCDC();
                foreach (var factura in facturas)
                {
                    string rucCompleto = factura.BusinessPartner.FederalTaxID;
                    string[] rucPartes = rucCompleto.Split('-');
                    string dRucEm = rucPartes.Length > 0 ? rucPartes[0].PadLeft(8, '0') : "00000000";
                    string dDVEmi = rucPartes.Length > 1 ? rucPartes[1] : "0";

                    string iTiDE = factura.U_CDOC.PadLeft(2, '0');
                    string dEst = factura.U_EST;
                    string dPunExp = factura.U_PDE;
                    string dNumDoc = factura.FolioNum.PadLeft(7, '0');
                    string dTipCont = factura.BusinessPartner.U_TIPCONT;
                    string dFecha = factura.DocDate.Replace("-", "");
                    string iTipEmi = "1"; // Siempre fijo en 1

                    string dCodSeg = GenerarCodigoSeguridad();
                    string cdc = GenerarCDC.GenerarCodigoCDC(
                        iTiDE, dRucEm, dDVEmi, dEst, dPunExp, dNumDoc, dTipCont, dFecha, iTipEmi, dCodSeg);

                    bool actualizado = await _sapServiceLayer.ActualizarCDC(factura.DocEntry, cdc);

                    if (actualizado)
                        _logger.LogInformation($"CDC generado y actualizado: {cdc}");
                    else
                        _logger.LogWarning($"No se pudo actualizar el CDC para la factura {factura.DocEntry}");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error en SAPCDCService: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken);
        }
    }

    private string GenerarCodigoSeguridad()
    {
        Random random = new Random();
        return random.Next(1, 999999999).ToString("D9");
    }
}
