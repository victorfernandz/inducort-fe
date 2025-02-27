using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json;

public class EmpresaService
{
    private readonly SAPServiceLayer _sapServiceLayer;
    private readonly HttpClient _httpClient;
    
    public EmpresaService(SAPServiceLayer sapServiceLayer)
    {
        _sapServiceLayer = sapServiceLayer;
        _httpClient = sapServiceLayer.GetHttpClient();
    }

    public async Task<EmpresaInfo> GetEmpresaInfo()
    {
        string query = "EPY_PLPY?$select=Code,EPY_DEMPCollection";
        var response = await _httpClient.GetAsync(query);

        if (!response.IsSuccessStatusCode)
        {
            Console.WriteLine($"Error en la consulta a SAP EPY_PLPY: {response.StatusCode}");
            return null;
        }

        var jsonResponse = await response.Content.ReadAsStringAsync();
        Console.WriteLine($"Respuesta JSON: {jsonResponse}");
        
        var rawJson = JsonConvert.DeserializeObject<Dictionary<string, object>>(jsonResponse);
        if (rawJson == null || !rawJson.ContainsKey("value"))
        {
            Console.WriteLine("No se encontraron datos en la respuesta de EPY_PLPY.");
            return null;
        }

        // Deserializar la respuesta
        var plpyResponse = JsonConvert.DeserializeObject<PlpyResponse>(jsonResponse);
        
        if (plpyResponse?.value == null || !plpyResponse.value.Any())
        {
            Console.WriteLine("No se encontraron registros en EPY_PLPY.");
            return null;
        }

        // Obtenemos el primer registro
        var primerRegistro = plpyResponse.value.First();
        
        // Buscamos los datos en EPY_DEMP
        if (primerRegistro.EPY_DEMPCollection == null || !primerRegistro.EPY_DEMPCollection.Any())
        {
            Console.WriteLine("No se encontraron datos en EPY_DEMP.");
            return null;
        }

        var datosEmpresa = primerRegistro.EPY_DEMPCollection.First();
        
        // Se crea la instancia de EmpresaInfo
        var empresaInfo = new EmpresaInfo
        {
            NombreEmpresa = datosEmpresa.U_NEMP,
            Ruc = datosEmpresa.U_RUCE,
            Dv = datosEmpresa.U_DVEMI,
            TipoContribuyente = datosEmpresa.U_TIPCONT,
            DireccionEmisor = datosEmpresa.U_DSUC,
            NumeroCasaEmisor = datosEmpresa.U_NUMCASA,
            CodDepartamento = datosEmpresa.U_DEPT,
            CodDistrito = datosEmpresa.U_DIST,
            CodLocalidad = datosEmpresa.U_BALO,
            TelefEmisor = datosEmpresa.U_PHONE,
            EmailEmisor = datosEmpresa.U_EMAIL,
        };

        // Obtener descripción del departamento
        string queryDpto = $"EPY_DPTO?$select=Code,U_NDEP&$filter=Code eq '{datosEmpresa.U_DEPT}'";
        var responseDpto = await _httpClient.GetAsync(queryDpto);
        if (responseDpto.IsSuccessStatusCode)
        {
            var dptoJsonResponse = await responseDpto.Content.ReadAsStringAsync();
            var dptoResponse = JsonConvert.DeserializeObject<DptoResponse>(dptoJsonResponse);
            if (dptoResponse?.value != null && dptoResponse.value.Any())
            {
                empresaInfo.DescDepartamento = dptoResponse.value.First().U_NDEP;
            }
        }

        // Obtener descripción del distrito
        string queryDist = $"EPY_DIST?$select=Code,U_NCIU&$filter=Code eq '{datosEmpresa.U_DIST}'";
        var responseDist = await _httpClient.GetAsync(queryDist);
        if (responseDist.IsSuccessStatusCode)
        {
            var distJsonResponse = await responseDist.Content.ReadAsStringAsync();
            var distResponse = JsonConvert.DeserializeObject<DistResponse>(distJsonResponse);
            if (distResponse?.value != null && distResponse.value.Any())
            {
                empresaInfo.DescDistrito = distResponse.value.First().U_NCIU;
            }
        }

        // Obtener descripción de la localidad
        string queryBalo = $"EPY_BALO?$select=Code,U_NLOC&$filter=Code eq '{datosEmpresa.U_BALO}'";
        var responseBalo = await _httpClient.GetAsync(queryBalo);
        if (responseBalo.IsSuccessStatusCode)
        {
            var baloJsonResponse = await responseBalo.Content.ReadAsStringAsync();
            var baloResponse = JsonConvert.DeserializeObject<BaloResponse>(baloJsonResponse);
            if (baloResponse?.value != null && baloResponse.value.Any())
            {
                empresaInfo.DescLocalidad = baloResponse.value.First().U_NLOC;
            }
        }

        return empresaInfo;
    }

    public async Task<List<ActividadEconomica>> GetActividadesEconomicas()
    {
        try
        {
            string query = "EPY_ACG?$select=Code,EPY_ACEGRACollection";
            var response = await _httpClient.GetAsync(query);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error en la consulta a SAP EPY_ACG: {response.StatusCode}");
                return new List<ActividadEconomica>();
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Respuesta JSON de actividades económicas: {jsonResponse}");
            
            // Deserializar a un objeto dinámico para mayor flexibilidad
            dynamic responseObj = JsonConvert.DeserializeObject(jsonResponse);
            
            var actividades = new List<ActividadEconomica>();
            
            if (responseObj?.value == null || responseObj.value.Count == 0)
            {
                Console.WriteLine("No se encontraron registros de actividades económicas.");
                return actividades;
            }

            // Procesar todas las actividades económicas de todos los registros
            foreach (var registro in responseObj.value)
            {
                if (registro.EPY_ACEGRACollection != null && registro.EPY_ACEGRACollection.Count > 0)
                {
                    foreach (var actividad in registro.EPY_ACEGRACollection)
                    {
                        actividades.Add(new ActividadEconomica
                        {
                            Codigo = (string)actividad.U_CACT,
                            Descripcion = (string)actividad.U_NACTECO
                        });
                    }
                }
            }

            return actividades;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener actividades económicas: {ex.Message}");
            return new List<ActividadEconomica>();
        }
    }

    public async Task<List<ObligacionAfectada>> GetObligacionesAfectadas()
    {
        try
        {
            string query = "EPY_OCG?$select=Code,EPY_OBLICollection";
            var response = await _httpClient.GetAsync(query);

            if (!response.IsSuccessStatusCode)
            {
                Console.WriteLine($"Error en la consulta a SAP EPY_OCG: {response.StatusCode}");
                return new List<ObligacionAfectada>();
            }

            var jsonResponse = await response.Content.ReadAsStringAsync();
            Console.WriteLine($"Respuesta JSON de obligaciones afectadas: {jsonResponse}");
            
            // Deserializar la respuesta
            var obligacionesResponse = JsonConvert.DeserializeObject<ObligacionesResponse>(jsonResponse);
            
            var obligaciones = new List<ObligacionAfectada>();
            
            if (obligacionesResponse?.value == null || !obligacionesResponse.value.Any())
            {
                Console.WriteLine("No se encontraron registros de obligaciones afectadas.");
                return obligaciones;
            }

            // Procesar todas las obligaciones
            foreach (var registro in obligacionesResponse.value)
            {
                if (registro.EPY_OBLICollection != null && registro.EPY_OBLICollection.Any())
                {
                    // Agregar cada obligación a la lista
                    foreach (var obligacion in registro.EPY_OBLICollection)
                    {
                        obligaciones.Add(new ObligacionAfectada 
                        { 
                            Codigo = obligacion.U_COBLI,
                            Descripcion = obligacion.U_NOBLI
                        });
                    }
                }
            }

            return obligaciones;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al obtener obligaciones afectadas: {ex.Message}");
            return new List<ObligacionAfectada>();
        }
    }
}