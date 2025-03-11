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
        string queryDocumento = "$crossjoin(Invoices,BusinessPartners,Currencies) " + 
            "?$expand=Invoices($select=DocEntry,DocRate,DocCurrency,U_EXX_FE_CDC,U_CDOC,CardCode,U_EST,U_PDE,U_TIM,U_FITE,FolioNumber,DocDate,U_EXX_FE_TipoTran,U_EXX_FE_IndPresencia,PaymentGroupCode,NumberOfInstallments)," + 
            "BusinessPartners($select=CardCode,CardName,FederalTaxID,U_TIPCONT,U_CRSI,U_EXX_FE_TipoOperacion), " +
            "Currencies($select=Code,Name,DocumentsCode) " + 
            "&$filter=Invoices/CardCode eq BusinessPartners/CardCode and " +
            "Invoices/DocCurrency eq Currencies/Code and (Invoices/U_EXX_FE_CDC eq null or Invoices/U_EXX_FE_CDC eq '') and " +
            "Invoices/DocDate eq '20250123'";

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

        // Agrupar las respuestas por DocEntry para consolidar todas las líneas de cada factura
        var facturasAgrupadas = facturasResponse
            .GroupBy(f => f.Invoices.DocEntry)
            .ToDictionary(g => g.Key, g => g.ToList());

        // Crear una lista para almacenar los cardCodes
        var cardCode = facturasResponse.Select(f => f.BusinessPartners.CardCode).Distinct().ToList();

        // Obtener direcciones para todos los socios de negocio
        var direcciones = await GetDireccionesSocioNegocio(cardCode);

        // Obtener todos los países
        var paises = direcciones.Select(d => d.Country).Where(c => !string.IsNullOrEmpty(c)).Distinct().ToList();
        
        // Crear diccionarios para nombres y códigos de países
        var nombresCodigosPaises = new Dictionary<string, (string Nombre, string CodigoReporte)>();

        foreach (var pais in paises)
        {
            var infoCompleta = await GetInformacionPais(pais);
            nombresCodigosPaises[pais] = infoCompleta;
        }

        // Lista final de facturas
        var facturasList = new List<Factura>();

        // Procesar cada factura agrupada con sus líneas
        foreach (var facturaGroup in facturasAgrupadas)
        {
            var docEntry = facturaGroup.Key;
            // Tomar la primera entrada para obtener la información general de la factura
            var primeraEntrada = facturaGroup.Value.First();
            
            if (primeraEntrada.Invoices == null || primeraEntrada.BusinessPartners == null || primeraEntrada.Currencies == null)
            {
                continue;
            }

            // Encontrar la dirección para este socio de negocio
            var direccion = direcciones.FirstOrDefault(d => d.CardCode == primeraEntrada.BusinessPartners.CardCode);

            // Obtener la información del país
            string descripcionPais = "";
            string codigoReportePais = "";
            if (direccion != null && !string.IsNullOrEmpty(direccion.Country) && nombresCodigosPaises.ContainsKey(direccion.Country))
            {
                var infoPais = nombresCodigosPaises[direccion.Country];
                descripcionPais = infoPais.Nombre;
                codigoReportePais = infoPais.CodigoReporte;
            }

            // Crear la factura con los datos generales
            var factura = new Factura
            {
                DocEntry = primeraEntrada.Invoices.DocEntry,
                U_EXX_FE_CDC = primeraEntrada.Invoices.U_EXX_FE_CDC ?? "",
                U_CDOC = primeraEntrada.Invoices.U_CDOC ?? "",
                CardCode = primeraEntrada.Invoices.CardCode ?? "",
                U_EST = primeraEntrada.Invoices.U_EST ?? "",
                U_PDE = primeraEntrada.Invoices.U_PDE ?? "",
                FolioNum = (primeraEntrada.Invoices.FolioNumber ?? "").PadLeft(7, '0'), 
                DocDate = primeraEntrada.Invoices.DocDate,
                U_TIM = primeraEntrada.Invoices.U_TIM,
                U_FITE = primeraEntrada.Invoices.U_FITE,
                iTipTra = primeraEntrada.Invoices.U_EXX_FE_TipoTran,
                iIndPres = primeraEntrada.Invoices.U_EXX_FE_IndPresencia,
                iCondOpe = primeraEntrada.Invoices.PaymentGroupCode,
                iCondCred = primeraEntrada.Invoices.NumberOfInstallments,
                dTiCam = primeraEntrada.Invoices.DocRate,
                BusinessPartner = new BusinessPartner
                {
                    CardCode = primeraEntrada.BusinessPartners.CardCode ?? "",
                    dNomRec = primeraEntrada.BusinessPartners.CardName ?? "",
                    FederalTaxID = primeraEntrada.BusinessPartners.FederalTaxID ?? "00000000",
                    iTiContRec = primeraEntrada.BusinessPartners.U_TIPCONT,
                    iTiOpe = primeraEntrada.BusinessPartners.U_EXX_FE_TipoOperacion,
                    iNatRec = primeraEntrada.BusinessPartners.U_CRSI ?? "",
                    
                    // Mapeo de direcciones desde la consulta separada
                    cPaisRec = codigoReportePais ?? "",
                    dDesPaisRe = descripcionPais,
                },
                Currencies = new Currencies
                {
                    cMoneOpe = primeraEntrada.Currencies.DocumentsCode ?? "",
                    dDesMoneOpe = primeraEntrada.Currencies.Name ?? ""
                },
                iTipEmi = 1,
                dFecha = fechaConHora,
                Items = new List<Item>() // Inicializar lista vacía de ítems
            };

            // Segunda consulta: Obtener las líneas para este DocEntry específico
            string queryLineas = $"Invoices({docEntry})/DocumentLines";
            var responseLineas = await _httpClient.GetAsync(queryLineas);

            if (responseLineas.IsSuccessStatusCode)
            {
                try
                {
                    var jsonResponseLineas = await responseLineas.Content.ReadAsStringAsync();                    
                    List<DocumentLineData> lineasResponse = new List<DocumentLineData>();
                    var responseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponseLineas);

                    lineasResponse = JsonConvert.DeserializeObject<List<DocumentLineData>>(responseObj["DocumentLines"].ToString());

                    foreach (var linea in lineasResponse)
                    {
                        // Manejar posibles valores nulos
                        string itemCode = linea.ItemCode ?? "";
                        string itemDescription = linea.ItemDescription ?? "";
                        decimal quantity = linea.Quantity;
                        decimal priceAfterVAT = linea.PriceAfterVAT;
                        decimal rate = linea.Rate;
                                
                        factura.Items.Add(new Item
                        {
                            dCodInt = itemCode,
                            dDescItem = itemDescription,
                            dCantProSer = quantity,
                            dPUniProSer = priceAfterVAT,
                            dTiCamIt = rate
                        });
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error al procesar las líneas para la factura {docEntry}: {ex.Message}");
                }
            }
            else
            {
                var errorContent = await responseLineas.Content.ReadAsStringAsync();
                Console.WriteLine($"Error al obtener líneas para la factura {docEntry}: {responseLineas.StatusCode}");
            }
            facturasList.Add(factura);
        }

        // Inicializar operación de crédito y obtener cuotas para facturas a crédito
        foreach (var factura in facturasList)
        {
            // Normalizar la condición de operación y condición de crédito según el estándar del servicio
            int condicionOperacion = factura.iCondOpe == -1 ? 1 : 2;
            int condicionCredito = factura.iCondCred == 1 ? 1 : 2;
            
            // Solo inicializar la operación de crédito si la condición de operación es crédito (2)
            if (condicionOperacion == 2)
            {
                // Obtener el plazo de crédito según la condición
                string plazoCredito = null;
                if (condicionCredito == 1)
                {
                    plazoCredito = await ObtenerPlazoCredito(factura.DocEntry);
                }
                
                int? cantidadCuotas = condicionCredito == 2 ? factura.iCondCred : null;
                
                // Inicializar la operación de crédito
                factura.OperacionCredito = new GPagCred(condicionCredito, plazoCredito, cantidadCuotas);
                
                // Si es por cuotas, obtener las cuotas
                if (condicionCredito == 2)
                {
                    try
                    {
                        var cuotasResponse = await GetCuotasFactura(factura.DocEntry);
                        
                        if (cuotasResponse != null && cuotasResponse.Any())
                        {
                            foreach (var cuota in cuotasResponse)
                            {
                                if (DateTime.TryParse(cuota.U_FECHAV, out DateTime fechaVencimiento))
                                {
                                    // Determinar el monto de la cuota (usar TotalFC si está disponible, de lo contrario Total)
                                    decimal montoCuota = cuota.TotalFC > 0 ? cuota.TotalFC : cuota.Total;
                                    
                                    // Crear y agregar la cuota con los datos completos según el manual técnico
                                    var nuevaCuota = new GCuotas(
                                        factura.Currencies.cMoneOpe,         // Moneda de la cuota
                                        factura.Currencies.dDesMoneOpe,      // Descripción de la moneda
                                        montoCuota,                          // Monto de la cuota
                                        fechaVencimiento                     // Fecha de vencimiento
                                    );
                                    
                                    factura.OperacionCredito.Cuotas.Add(nuevaCuota);
                                }
                                else
                                {
                                    Console.WriteLine($"Advertencia: Formato de fecha inválido en cuota: {cuota.U_FECHAV}");
                                }
                            }
                        }
                        else
                        {
                            Console.WriteLine($"No se encontraron cuotas para la factura {factura.DocEntry}");
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error al obtener cuotas para DocEntry {factura.DocEntry}: {ex.Message}");
                        if (ex.InnerException != null)
                        {
                            Console.WriteLine($"Error interno: {ex.InnerException.Message}");
                        }
                    }
                }
            }
        }
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
                        Country = primeraDireccion.Country ?? ""
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

    public async Task<(string Name, string CodeForReports)> GetInformacionPais(string codigoPais)
    {
        try
        {
            string query = $"Countries?$select=Code,Name,CodeForReports&$filter=Code eq '{codigoPais}'";
            var response = await _httpClient.GetAsync(query);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error al obtener información del país {codigoPais}: {response.StatusCode}");
                return ("", "");
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            var responseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
            
            if (responseObj == null || !responseObj.ContainsKey("value"))
            {
                Console.WriteLine($"Formato de respuesta inesperado para el país {codigoPais}");
                return ("", "");
            }

            // Acceder al array de resultados
            var valueArray = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(responseObj["value"].ToString());
            
            if (valueArray == null || valueArray.Count == 0)
            {
                Console.WriteLine($"No se encontró información para el país {codigoPais}");
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
            Console.WriteLine($"Error al obtener información del país {codigoPais}: {ex.Message}");
            return ("", "");
        }
    }

    private async Task<List<CuotaResponse>> GetCuotasFactura(int docEntry)
    {
        try 
        {           
            string query = $"Invoices({docEntry})";
            var response = await _httpClient.GetAsync(query);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error al obtener la factura {docEntry}: {response.StatusCode}");
                Console.WriteLine($"Respuesta: {await response.Content.ReadAsStringAsync()}");
                return new List<CuotaResponse>();
            }
            
            var jsonResponse = await response.Content.ReadAsStringAsync();
            
            // Revisar el contenido de la respuesta para ver si contiene las cuotas
            if (jsonResponse.Contains("\"DocumentInstallments\""))
            {
                // Si la respuesta contiene DocumentInstallments, intentar extraerlas
                var facturaObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
                
                if (facturaObj != null && facturaObj.ContainsKey("DocumentInstallments"))
                {
                    var cuotasJson = facturaObj["DocumentInstallments"].ToString();
                    var cuotasList = JsonConvert.DeserializeObject<List<Dictionary<string, object>>>(cuotasJson);
                    
                    if (cuotasList != null && cuotasList.Any())
                    {
                        return cuotasList.Select(cuota => new CuotaResponse
                        {
                            InstallmentId = cuota.ContainsKey("InstallmentId") ? Convert.ToInt32(cuota["InstallmentId"]) : 0,
                            Total = cuota.ContainsKey("Total") ? Convert.ToDecimal(cuota["Total"]) : 0,
                            TotalFC = cuota.ContainsKey("TotalFC") ? Convert.ToDecimal(cuota["TotalFC"]) : 0,
                            U_FECHAV = cuota.ContainsKey("U_FECHAV") ? cuota["U_FECHAV"]?.ToString() : (cuota.ContainsKey("DueDate") ? cuota["DueDate"]?.ToString() : null)
                        }).ToList();
                    }
                }
            }
            
            // Si llegamos aquí, no pudimos obtener las cuotas
            Console.WriteLine($"No se pudieron encontrar las cuotas para la factura {docEntry}");
            
            return new List<CuotaResponse>();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener cuotas: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Error interno: {ex.InnerException.Message}");
            }
            return new List<CuotaResponse>();
        }
    }

    // Método para obtener la descripción del plazo desde la factura
    private async Task<string> ObtenerPlazoCredito(int docEntry)
    {
        try
        {
            string query = $"Invoices({docEntry})?$select=PaymentGroupCode";
            var response = await _httpClient.GetAsync(query);
            
                
            {
                var jsonResponse = await response.Content.ReadAsStringAsync();
                var bp = JsonConvert.DeserializeObject<dynamic>(jsonResponse);
                
                // Obtener el código de términos de pago
                int payTermCode = bp.PaymentGroupCode;
                
                // Consultar la descripción de los términos de pago
                query = $"PaymentTermsTypes({payTermCode})";
                response = await _httpClient.GetAsync(query);
                
                if (response.IsSuccessStatusCode)
                {
                    jsonResponse = await response.Content.ReadAsStringAsync();
                    var payTerm = JsonConvert.DeserializeObject<dynamic>(jsonResponse);
                    
                    // Formatear el plazo según lo requerido
                    string months = payTerm.PaymentTermsGroupName ?? 0;
                    return months;
                }
            }
            
            // Si no se pudo obtener, retornar nulo y se usará el valor por defecto
            return null;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener plazo de crédito: {ex.Message}");
            return null;
        }
    }

    public async Task<bool> ActualizarCDC(int docEntry, string cdc)
    {
        var requestBody = new { U_EXX_FE_CDC = cdc };
        var content = new StringContent(JsonConvert.SerializeObject(requestBody), Encoding.UTF8, "application/json");
        var response = await _httpClient.PatchAsync($"Invoices({docEntry})", content);

        return response.IsSuccessStatusCode;
    }
}