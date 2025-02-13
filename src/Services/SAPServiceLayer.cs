using System;
using System.Net;
using System.Security.Cryptography.X509Certificates;
using System.Net.Security;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;

public class SAPServiceLayer
{
    private readonly HttpClient _httpClient;
    private readonly string _baseUrl;
    private readonly string _companyDB;
    private readonly string _username;
    private readonly string _password;
    private string _sessionId;
    private string _routeId;

    public SAPServiceLayer(Config config)
    {
        _baseUrl = config.SapServiceLayer.Url.TrimEnd('/') + "/";
        _companyDB = config.SapServiceLayer.CompanyDB;
        _username = config.SapServiceLayer.UserName;
        _password = config.SapServiceLayer.Password;

        // Para ignorar SSL
        ServicePointManager.ServerCertificateValidationCallback += (sender, cert, chain, sslPolicyErrors) => true;
        ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12 | SecurityProtocolType.Tls13;

        HttpClientHandler handler = new HttpClientHandler
        {
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
        };

        _httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_baseUrl),
            Timeout = TimeSpan.FromSeconds(30)
        };

        _httpClient.DefaultRequestHeaders.Add("Prefer", "odata.maxpagesize=100");
    }

    public async Task<bool> Login()
    {
        var requestBody = new
        {
            CompanyDB = _companyDB,
            UserName = _username,
            Password = _password
        };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PostAsync("Login", content);

        if (response.IsSuccessStatusCode)
        {
            var jsonResponse = await response.Content.ReadAsStringAsync();
            dynamic loginData = JsonConvert.DeserializeObject(jsonResponse);
            _sessionId = loginData.SessionId;
            _routeId = loginData.RouteId;

            _httpClient.DefaultRequestHeaders.Add("B1SESSION", _sessionId);
            _httpClient.DefaultRequestHeaders.Add("RouteID", _routeId);

            return true;
        }
        return false;
    }

    public async Task<List<Factura>> GetFacturasSinCDC()
    {
        string query = "$crossjoin(Invoices,BusinessPartners)?" +
                    "$expand=Invoices($select=DocEntry,U_EXX_FE_CDC,U_CDOC,CardCode,U_EST,U_PDE,FolioNumber,DocDate)," +
                    "BusinessPartners($select=CardCode,FederalTaxID,U_TIPCONT)" +
                    "&$filter=Invoices/CardCode eq BusinessPartners/CardCode and Invoices/U_EXX_FE_CDC eq null and Invoices/DocDate eq '20250130'";

        var response = await _httpClient.GetAsync(query);

        if (response.IsSuccessStatusCode)
        {
            var jsonResponse = await response.Content.ReadAsStringAsync();

            // Deserializar como un diccionario para manejar claves desconocidas
            var rawJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);

            // Verificar si la clave "value" está presente antes de deserializar
            if (rawJson != null && rawJson.ContainsKey("value"))
            {
                var facturasJson = rawJson["value"].ToString();
                var facturasResponse = JsonConvert.DeserializeObject<List<FacturaResponse>>(facturasJson);

                if (facturasResponse != null)
                {
                    var facturasList = facturasResponse.Select(f => new Factura
                    {
                        DocEntry = f.Invoices.DocEntry,
                        U_EXX_FE_CDC = f.Invoices.U_EXX_FE_CDC,
                        U_CDOC = f.Invoices.U_CDOC,
                        CardCode = f.Invoices.CardCode,
                        U_EST = f.Invoices.U_EST,
                        U_PDE = f.Invoices.U_PDE,
                        FolioNum = f.Invoices.FolioNumber.PadLeft(7, '0'),
                        DocDate = f.Invoices.DocDate,
                        BusinessPartner = new BusinessPartner
                        {
                            CardCode = f.BusinessPartners.CardCode,
                            FederalTaxID = f.BusinessPartners.FederalTaxID,
                            U_TIPCONT = f.BusinessPartners.U_TIPCONT
                        },
                        dFecha = f.Invoices.DocDate,
                        iTipEmi = "1"
                    }).ToList();

                    return facturasList;
                }
            }
        }

        return new List<Factura>();
    }

    public async Task<bool> ActualizarCDC(int docEntry, string cdc)
    {
        var requestBody = new { U_EXX_FE_CDC = cdc };

        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync($"Invoices({docEntry})", content);

        return response.IsSuccessStatusCode;
    }
}
