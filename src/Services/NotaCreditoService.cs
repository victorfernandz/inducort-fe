using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

public class NotaCreditoService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<NotaCreditoService> _logger;

    public NotaCreditoService(SAPServiceLayer sapServiceLayer, ILogger<NotaCreditoService> logger)
    {
        _httpClient = sapServiceLayer.GetHttpClient();
        _logger = logger;
    }

    public async Task<List<NotaCredito>> GetNotaCreditoSinCDC()
    {
        string queryDocumento = "$crossjoin(CreditNotes,BusinessPartners,Currencies) " + 
            "?$expand=CreditNotes($select=DocEntry,DocRate,DocCurrency,U_EXX_FE_CDC,U_CDOC,CardCode,U_EST,U_PDE,U_TIM,U_FITE,FolioNumber,DocDate,U_EXX_FE_TipoTran,U_EXX_FE_IndPresencia,PaymentGroupCode,NumberOfInstallments,U_NUMFC,U_TIMFC)," + 
            "BusinessPartners($select=CardCode,CardName,FederalTaxID,U_TIPCONT,U_CRSI,U_EXX_FE_TipoOperacion), " +
            "Currencies($select=Code,Name,DocumentsCode) " + 
            "&$filter=CreditNotes/CardCode eq BusinessPartners/CardCode and " +
            "CreditNotes/DocCurrency eq Currencies/Code and (CreditNotes/U_EXX_FE_CDC eq null or CreditNotes/U_EXX_FE_CDC eq '') and " +
            "CreditNotes/DocDate eq '20250130'";

        var jsonResponse = await HttpHelper.GetStringAsync(_httpClient, queryDocumento, _logger, "Error en la consulta a SAP");
        if (string.IsNullOrEmpty(jsonResponse))
        {
            _logger.LogWarning("No se encontraron datos en la respuesta de SAP.");
            return new List<NotaCredito>();
        }

        var rawJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
        if (rawJson == null || !rawJson.ContainsKey("value"))
        {
            _logger.LogWarning("No se encontraron datos en la respuesta de SAP.");
            return new List<NotaCredito>();
        }

        // Obtener la lista de Notas de crédito y deserializar
        var notaCreditoJson = rawJson["value"].ToString();
        var notaCreditoResponse = JsonConvert.DeserializeObject<List<NotaCreditoResponse>>(notaCreditoJson);

        if (notaCreditoResponse == null)
        {
            _logger.LogWarning("No se pudieron deserializar las notas de crédito.");
            return new List<NotaCredito>();
        }

        // Agrupar las respuestas por DocEntry para consolidar todas las líneas de cada nota de crédito
        var notaCreditoAgrupadas = notaCreditoResponse
            .GroupBy(n => n.CreditNotes.DocEntry)
            .ToDictionary(g => g.Key, g => g.ToList());

        var cardCode = notaCreditoResponse.Select(f => f.BusinessPartners.CardCode).Distinct().ToList();
        var direcciones = await GetDireccionesSocioNegocio(cardCode);
        var paises = direcciones.Select(d => d.Country).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();

        // Crear diccionarios para nombres y códigos de países
        var nombresCodigosPaises = new Dictionary<string, (string Nombre, string CodigoReporte)>();

        foreach (var pais in paises)
        {
            var infoCompleta = await GetInformacionPais(pais);
            nombresCodigosPaises[pais] = infoCompleta;
        }

        // Lista final de facturas
        var notaCreditoList = new List<NotaCredito>();

        
    }

    public async Task<List<BusinessPartnerData.BPAddressInfo>> GetDireccionesSocioNegocio(List<string> cardCodes)
    {
        var direcciones = new List<BusinessPartnerData.BPAddressInfo>();

        foreach (var cardCode in cardCodes)
        {
            try
            {
                // Consultamos a la colección de direcciones
                string queryDirecciones = $"BusinessPartners('{cardCode}')/BPAddresses";

                // Usando el método auxiliar para obtener la respuesta
                var jsonResponse = await HttpHelper.GetStringAsync(_httpClient, queryDirecciones, _logger, $"Error al obtener direcciones para {cardCode}");

                if (string.IsNullOrEmpty(jsonResponse))
                {
                    continue;
                }
                
                // Deserializar como un objeto JSON dinámico para luego acceder al array BPAddresses
                var responseObj = JsonConvert.DeserializeObject<BPAddressesWrapper>(jsonResponse);
                
                if (responseObj == null || responseObj.BPAddresses == null || !responseObj.BPAddresses.Any())
                {
                    _logger.LogWarning($"No se encontraron direcciones para {cardCode}.");
                    continue;
                }

                // Tomar la primera dirección
                var primeraDireccion = responseObj.BPAddresses.FirstOrDefault();
                
                if (primeraDireccion != null)
                {
                    direcciones.Add(new BusinessPartnerData.BPAddressInfo
                    {
                        CardCode = cardCode,
                        Country = primeraDireccion.Country ?? ""
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Error al procesar direcciones para {cardCode}: {ex.Message}");
                
                if (ex.InnerException != null)
                {
                    _logger.LogError($"Error interno: {ex.InnerException.Message}");
                }
            }
        }

        return direcciones;
    }

    public async Task<(string Name, string CodeForReports)> GetInformacionPais(string codigoPais)
    {
        try
        {
            string query = $"Countries?$select=Code,Name,CodeForReports&$filter=Code eq '{codigoPais}'";
            var jsonResponse = await HttpHelper.GetStringAsync(_httpClient, query, _logger, $"Error al obtener información del país {codigoPais}");

            if (string.IsNullOrEmpty(jsonResponse))
            {
                return ("", "");
            }
            
            var responseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
            
            if (responseObj == null || !responseObj.ContainsKey("value"))
            {
                _logger.LogWarning($"Formato de respuesta inesperado para el país {codigoPais}");
                return ("", "");
            }

            // Acceder al array de resultados
            var valueArray = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(responseObj["value"].ToString());
            
            if (valueArray == null || valueArray.Count == 0)
            {
                _logger.LogWarning($"No se encontró información para el país {codigoPais}");
                return ("", "");
            }

            // Obtener la información del país
            var paisInfo = valueArray[0];
            string name = paisInfo.ContainsKey("Name") ? paisInfo["Name"].ToString() : "";
            string codeForReports = paisInfo.ContainsKey("CodeForReports") ? paisInfo["CodeForReports"].ToString() : "";
            
            return (name, codeForReports);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error al obtener información del país {codigoPais}: {ex.Message}");
            return ("", "");
        }
    }

    public async Task<bool> ActualizarCDC(int docEntry, string cdc)
    {
        var requestBody = new { U_EXX_FE_CDC = cdc };
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync($"CreditNotes({docEntry})", content);

        return response.IsSuccessStatusCode;
    }
    
}