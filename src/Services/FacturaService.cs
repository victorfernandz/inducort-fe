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
        string queryDocumento = "$crossjoin(Invoices,BusinessPartners,Currencies)?$expand=Invoices($select=DocEntry,DocDate,CardCode,FolioNumber,DocCurrency,U_CDOC,U_EST,U_PDE,U_TIM,U_FITE,U_EXX_FE_CDC,U_EXX_FE_TipoTran," +
                    "U_EXX_FE_IndPresencia,PaymentGroupCode,NumberOfInstallments)," +
                    "BusinessPartners($select=CardCode,CardName,FederalTaxID,U_TIPCONT,U_CRSI,U_EXX_FE_TipoOperacion),Currencies($select=Code,Name,DocumentsCode)" +
                    "&$filter=Invoices/CardCode eq BusinessPartners/CardCode and Invoices/DocCurrency eq Currencies/Code and" +
                    "(Invoices/U_EXX_FE_CDC eq null or Invoices/U_EXX_FE_CDC eq '') and Invoices/DocDate eq '20250127' and Cancelled eq 'tNO' ";

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

        // Convertir FacturaResponse a Factura con validaciones de nulos
        var facturasList = facturasResponse
            .Where(f => f.Invoices != null && f.BusinessPartners != null && f.Currencies != null)
            .Select(f => 
            {
                // Encontrar la dirección para este socio de negocio
                var direccion = direcciones.FirstOrDefault(d => d.CardCode == f.BusinessPartners.CardCode);

                /// Obtener la información del país
                string descripcionPais = "";
                string codigoReportePais = "";
                if (direccion != null && !string.IsNullOrEmpty(direccion.Country) && nombresCodigosPaises.ContainsKey(direccion.Country))
                {
                    var infoPais = nombresCodigosPaises[direccion.Country];
                    descripcionPais = infoPais.Nombre;
                    codigoReportePais = infoPais.CodigoReporte;
                }

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
                    iIndPres = f.Invoices.U_EXX_FE_IndPresencia,
                    iCondOpe = f.Invoices.PaymentGroupCode,
                    iCondCred = f.Invoices.NumberOfInstallments,
                    BusinessPartner = new BusinessPartner
                    {
                        CardCode = f.BusinessPartners.CardCode ?? "",
                        dNomRec = f.BusinessPartners.CardName ?? "",
                        FederalTaxID = f.BusinessPartners.FederalTaxID ?? "00000000",
                        iTiContRec = f.BusinessPartners.U_TIPCONT,
                        iTiOpe = f.BusinessPartners.U_EXX_FE_TipoOperacion,
                        iNatRec = f.BusinessPartners.U_CRSI ?? "",
                        
                        // Mapeo de direcciones desde la consulta separada
                        cPaisRec = codigoReportePais ?? "",
                        dDesPaisRe = descripcionPais,
                    /*    dDirRec = direccion?.Street ?? "",
                        dNumCasRec = direccion?.StreetNo ?? 0,
                        cDepRec = direccion?.U_EXX_FE_DEPT ?? 0 */
                    },
                    Currencies = new Currencies
                    {
                        cMoneOpe = f.Currencies.DocumentsCode ?? "",
                        dDesMoneOpe = f.Currencies.Name ?? ""
                    },
                    iTipEmi = 1,
                    dFecha = fechaConHora,
                };
            }).ToList();

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
                        // Aquí deberías obtener el plazo real 
                        plazoCredito = await ObtenerPlazoCredito(factura.DocEntry);
                        if (string.IsNullOrEmpty(plazoCredito))
                        {
                            plazoCredito = "30 días"; // Valor por defecto
                        }
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
                                        Console.WriteLine($"Agregada cuota: Moneda={nuevaCuota.MonedaCuota}, " + $"Monto={nuevaCuota.MontoCuota}, " + $"Vencimiento={nuevaCuota.FechaVencimientoCuota}");
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
                    /*    Street = primeraDireccion.Street ?? "",
                        StreetNo = primeraDireccion.StreetNo ?? 0,
                        U_EXX_FE_DEPT = primeraDireccion.U_EXX_FE_DEPT ?? 0 */
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
            // Primero, intentemos un enfoque diferente: buscar directamente el endpoint para cuotas
   /*         string queryInstallments = $"Invoices({docEntry})/DocumentInstallments";
            
            var responseInstallments = await _httpClient.GetAsync(queryInstallments);
            
            // Si esta consulta tiene éxito, procesar las cuotas directamente
            if (responseInstallments.IsSuccessStatusCode)
            {
                var jsonResponseInstallments = await responseInstallments.Content.ReadAsStringAsync();
                var responseObj = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponseInstallments);
                
                if (responseObj != null && responseObj.ContainsKey("value"))
                {
                    var cuotasJson = responseObj["value"].ToString();
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
            } */
            
            // Si el enfoque anterior falló, vamos a consultar la factura completa
            string query = $"Invoices({docEntry})";
            
            var response = await _httpClient.GetAsync(query);
            
            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error al obtener la factura {docEntry}: {response.StatusCode}");
                Console.WriteLine($"Respuesta: {await response.Content.ReadAsStringAsync()}");
                return new List<CuotaResponse>();
            }
            
            var jsonResponse = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Respuesta de SAP para la factura {docEntry}: {jsonResponse.Substring(0, Math.Min(500, jsonResponse.Length))}...");
            
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