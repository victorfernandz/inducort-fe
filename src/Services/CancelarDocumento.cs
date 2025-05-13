using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Xml;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography.X509Certificates;

public class CancelarDocumento
{
    private readonly ILogger _log;
    private readonly LoggerSifenService _logger;
    private readonly Config _config;
    private readonly SAPServiceLayer _sapServiceLayer;

    public CancelarDocumento(ILogger log, LoggerSifenService logger, Config config, SAPServiceLayer sapServiceLayer)
    {
        _log = log;
        _logger = logger;
        _config = config;
        _sapServiceLayer = sapServiceLayer;
    }

    public async Task<bool> CancelarDocumentoAsync(string cdc)
    {
        // Usar un ID simple como se muestra en el ejemplo
        string dId = new Random().Next(1, 1000).ToString();
        string baseDatos = _config.SapServiceLayer.CompanyDB;

        var (certBytes, password) = await ObtenerCertificadoActivo();
        var certificado = new X509Certificate2(certBytes, password, X509KeyStorageFlags.Exportable);

        XmlDocument xml = GenerarXmlCancelacion(cdc, dId);
        
        // El ID ya incluye el # en GenerarXmlCancelacion
        XmlDocument xmlFirmado = SifenSigner.FirmarEvento(xml, dId, certificado);
        
        // Preservar la declaración XML
        string xmlNormalizado = EnvioSifenService.NormalizarXmlFirmado(xmlFirmado.OuterXml, false);

        string debugDir = "debug_xml";
        Directory.CreateDirectory(debugDir);
        string xmlPath = Path.Combine(debugDir, $"evento_cancelacion_{dId}.xml");
        File.WriteAllText(xmlPath, xmlNormalizado);

        string zipPath = Path.Combine(debugDir, $"evento_cancelacion_{dId}.zip");
        if (File.Exists(zipPath)) File.Delete(zipPath);
        
        using (var zip = ZipFile.Open(zipPath, ZipArchiveMode.Create))
        {
            zip.CreateEntryFromFile(xmlPath, "Evento.xml", CompressionLevel.Optimal);
        }

        string base64Zip = Convert.ToBase64String(File.ReadAllBytes(zipPath));

        // SOAP para eventos
        string soapEnvelope = $@"<?xml version=""1.0"" encoding=""UTF-8""?>
    <soap:Envelope xmlns:soap=""http://www.w3.org/2003/05/soap-envelope"" xmlns:xsd=""http://ekuatia.set.gov.py/sifen/xsd"">
        <soap:Header/>
        <soap:Body>
            <xsd:rEnvioEventoDe>
                <xsd:dId>{dId}</xsd:dId>
                <xsd:xDE>{base64Zip}</xsd:xDE>
            </xsd:rEnvioEventoDe>
        </soap:Body>
    </soap:Envelope>";

        File.WriteAllText(Path.Combine(debugDir, $"soap_envio_evento_{dId}.xml"), soapEnvelope);

        var handler = new HttpClientHandler
        {
            SslProtocols = System.Security.Authentication.SslProtocols.Tls12,
            ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true,
            ClientCertificates = { certificado }
        };
        
        var httpClient = new HttpClient(handler)
        {
            BaseAddress = new Uri(_config.Sifen.Url),
            Timeout = TimeSpan.FromMinutes(2)
        };

        var soapContent = new StringContent(soapEnvelope, new UTF8Encoding(false), "application/soap+xml");
        soapContent.Headers.ContentType.Parameters.Clear();
        soapContent.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("charset", "UTF-8"));

        string endpoint = "de/ws/eventos/evento.wsdl";
        var response = await httpClient.PostAsync(endpoint, soapContent);
        string respuestaXml = await response.Content.ReadAsStringAsync();

        File.WriteAllText(Path.Combine(debugDir, $"respuesta_evento_{dId}_{DateTime.Now:yyyyMMddHHmmss}.xml"), respuestaXml);

        try
        {
            _logger.RegistrarDocumento(baseDatos, cdc, xmlNormalizado, response.IsSuccessStatusCode ? "Enviado" : "Error", 
                "11", "siRecepEvento", DateTime.Now, DateTime.Now, DateTime.Now, respuestaXml, "");
        }
        catch (Exception ex)
        {
            _log.LogError($"Error al registrar evento: {ex.Message}");
        }

        return response.IsSuccessStatusCode;
    }

    private XmlDocument GenerarXmlCancelacion(string cdc, string dId)
{
    var fechaFirma = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss");
    
    // ID simple, sin prefijo #
    string xmlString = $@"<?xml version=""1.0"" encoding=""UTF-8"" standalone=""no""?>
<gGroupGesEve xmlns=""http://ekuatia.set.gov.py/sifen/xsd"" xmlns:xsi=""http://www.w3.org/2001/XMLSchema-instance""
    xsi:schemaLocation=""http://ekuatia.set.gov.py/sifen/xsd siRecepEvento_v150.xsd"">
    <rGesEve xsi:schemaLocation=""http://ekuatia.set.gov.py/sifen/xsd siRecepEvento_v150.xsd"">
        <rEve Id=""{dId}"">
            <dFecFirma>{fechaFirma}</dFecFirma>
            <dVerFor>150</dVerFor>
            <gGroupTiEvt>
                <rGeVeCan>
                    <Id>{cdc}</Id>
                    <mOtEve>Cancelación solicitada por el emisor</mOtEve>
                </rGeVeCan>
            </gGroupTiEvt>
        </rEve>
    </rGesEve>
</gGroupGesEve>";

    XmlDocument doc = new XmlDocument();
    doc.PreserveWhitespace = true;
    doc.LoadXml(xmlString);
    return doc;
}
    private async Task<(byte[] certBytes, string password)> ObtenerCertificadoActivo()
    {
        var envioService = new EnvioSifenService(_config.Sifen.Url, _logger, _config, _log, _sapServiceLayer);
        return await envioService.ObtenerCertificadoActivo();
    }
}