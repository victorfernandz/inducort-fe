using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class FacturaService
{
    private readonly HttpClient _httpClient;

    public FacturaService(SAPServiceLayer sapServiceLayer)
    {
        _httpClient = sapServiceLayer.GetHttpClient();
    }

    public async Task<List<Factura>> GetFacturasSinCDC()
    {
        string queryDocumento = "$crossjoin(Invoices,BusinessPartners,Currencies)?" +
                    "$expand=Invoices($select=DocEntry,DocDate,CardCode,FolioNumber,DocCurrency," + 
                    "U_CDOC,U_EST,U_PDE,U_TIM,U_FITE,U_EXX_FE_CDC,U_EXX_FE_TipoTran)," +
                    "BusinessPartners($select=CardCode,CardName,FederalTaxID,U_TIPCONT,U_CRSI,U_EXX_FE_TipoOperacion),Currencies($select=Code,Name,DocumentsCode)" +
                    "&$filter=Invoices/CardCode eq BusinessPartners/CardCode and Invoices/DocCurrency eq Currencies/Code and" +
                    "(Invoices/U_EXX_FE_CDC eq null or Invoices/U_EXX_FE_CDC eq '') and Invoices/DocDate eq '20250123'";

        var response = await _httpClient.GetAsync(queryDocumento);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error en la consulta a SAP: {response.StatusCode}");
            return new List<Factura>();
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        var rawJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
        if (rawJson == null || !rawJson.ContainsKey("value"))
        {
            Console.WriteLine("No se encontraron datos en la respuesta de SAP.");
            return new List<Factura>();
        }

        // Obtener la lista de facturas y deserializar
        var facturasJson = rawJson["value"].ToString();
        var facturasResponse = JsonConvert.DeserializeObject<List<FacturaResponse>>(facturasJson);

        if (facturasResponse == null)
        {
            Console.WriteLine("No se pudieron deserializar las facturas.");
            return new List<Factura>();
        }

        // Asignamos la hora de generación del xml para enviar como fecha y hora del documento
        string fechaConHora = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");

        // Crear una lista para almacenar los cardCodes
        var cardCode = facturasResponse.Select(f => f.BusinessPartners.CardCode).Distinct().ToList();

        // Obtener direcciones para todos los socios de negocio en una sola consulta
        var direcciones = await GetDireccionesSocioNegocio(cardCode);

        // Convertir FacturaResponse a Factura con validaciones de nulos
        var facturasList = facturasResponse
            .Where(f => f.Invoices != null && f.BusinessPartners != null && f.Currencies != null)
            .Select(f => 
            {
                // Encontrar la dirección para este socio de negocio
                var direccion = direcciones.FirstOrDefault(d => d.CardCode == f.BusinessPartners.CardCode);

                return new Factura
                {
                    DocEntry = f.Invoices.DocEntry,
                    U_CDOC = f.Invoices.U_CDOC ?? "",
                    CardCode = f.Invoices.CardCode ?? "",
                    U_EST = f.Invoices.U_EST ?? "",
                    U_PDE = f.Invoices.U_PDE ?? "",
                    FolioNum = (f.Invoices.FolioNumber ?? "").PadLeft(7, '0'), 
                    DocDate = f.Invoices.DocDate,
                    U_TIM = f.Invoices.U_TIM,
                    U_FITE = f.Invoices.U_FITE,
                    iTipTra = f.Invoices.U_EXX_FE_TipoTran,
                    BusinessPartner = new BusinessPartner
                    {
                        CardCode = f.BusinessPartners.CardCode ?? "",
                        dNomRec = f.BusinessPartners.CardName ?? "",
                        FederalTaxID = f.BusinessPartners.FederalTaxID ?? "00000000",
                        iTiContRec = f.BusinessPartners.U_TIPCONT,
                        iTiOpe = f.BusinessPartners.U_EXX_FE_TipoOperacion,
                        iNatRec = f.BusinessPartners.U_CRSI ?? "",
                        
                        // Mapeo de direcciones desde la consulta separada
                        cPaisRec = direccion?.Country ?? "",
                        dDirRec = direccion?.Street ?? "",
                        dNumCasRec = direccion?.StreetNo ?? 0,
                        cDepRec = direccion?.U_EXX_FE_DEPT ?? 0
                    },
                    Currencies = new Currencies
                    {
                        cMoneOpe = f.Currencies.DocumentsCode ?? "",
                        dDesMoneOpe = f.Currencies.Name ?? ""
                    },
                    iTipEmi = 1,
                    dFecha = fechaConHora
                };
            }).ToList();

        return facturasList;
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

                var response = await _httpClient.GetAsync(queryDirecciones);

                if (!response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"Error al obtener direcciones para {cardCode}: {response.StatusCode}");
                    Console.WriteLine($"Detalles del error: {await response.Content.ReadAsStringAsync()}");
                    continue;
                }

                var jsonResponse = await response.Content.ReadAsStringAsync();
                
                // Console.WriteLine($"Respuesta para {cardCode}: {jsonResponse}");
                
                // Deserializar como un objeto JSON dinámico para luego acceder al array BPAddresses
                var responseObj = JsonConvert.DeserializeObject<BPAddressesWrapper>(jsonResponse);
                
                if (responseObj == null || responseObj.BPAddresses == null || !responseObj.BPAddresses.Any())
                {
                    Console.WriteLine($"No se encontraron direcciones para {cardCode}.");
                    continue;
                }

                // Tomar la primera dirección
                var primeraDireccion = responseObj.BPAddresses.FirstOrDefault();
                
                if (primeraDireccion != null)
                {
                    direcciones.Add(new BusinessPartnerData.BPAddressInfo
                    {
                        CardCode = cardCode,
                        Country = primeraDireccion.Country ?? "",
                        Street = primeraDireccion.Street ?? "",
                        StreetNo = primeraDireccion.StreetNo ?? 0,
                        U_EXX_FE_DEPT = primeraDireccion.U_EXX_FE_DEPT ?? 0
                    });
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error al procesar direcciones para {cardCode}: {ex.Message}");
                if (ex.InnerException != null)
                {
                    Console.WriteLine($"Error interno: {ex.InnerException.Message}");
                }
            }
        }

        return direcciones;
    }

    public async Task<bool> ActualizarCDC(int docEntry, string cdc)
    {
        var requestBody = new { U_EXX_FE_CDC = cdc };
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync($"Invoices({docEntry})", content);

        return response.IsSuccessStatusCode;
    }
}