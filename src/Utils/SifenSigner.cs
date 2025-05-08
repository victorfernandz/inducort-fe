using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;

public class SifenSigner
{
    public static void FirmarXml(XmlDocument xmlDoc, string cdc, string dRucReceptor, byte[] certificadoBytes, string contraseñaCertificado)
    {
        var cert = new X509Certificate2(certificadoBytes, contraseñaCertificado, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        var privateKey = cert.GetRSAPrivateKey();
        if (privateKey == null) throw new Exception("Clave privada RSA no disponible en el certificado.");

        var deElement = xmlDoc.GetElementsByTagName("DE")[0] as XmlElement;
        if (deElement == null) throw new Exception("Elemento <DE> no encontrado en el XML.");

        // Establecer atributo Id correctamente como parte del DOM (para evitar "Malformed reference element")
        XmlAttribute idAttr = xmlDoc.CreateAttribute("Id");
        idAttr.Value = cdc;
        deElement.Attributes.Append(idAttr);

        // Quitar xmlns si es redundante
        if (deElement.HasAttribute("xmlns"))
            deElement.RemoveAttribute("xmlns");

        // Eliminar firmas previas
        var firmas = xmlDoc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
        for (int i = firmas.Count - 1; i >= 0; i--)
            firmas[i].ParentNode.RemoveChild(firmas[i]);

        // Calcular DigestValue con canonicalización exclusiva
        string digestBase64;
        using (SHA256 sha256 = SHA256.Create())
        {
            var c14n = new XmlDsigExcC14NTransform();
            XmlNodeList nodeList = xmlDoc.GetElementsByTagName("DE");
            c14n.LoadInput(nodeList);
            using var stream = (Stream)c14n.GetOutput(typeof(Stream));
            using (MemoryStream ms = new MemoryStream())
            {
                stream.CopyTo(ms);
                byte[] canonicalBytes = ms.ToArray();
                byte[] digest = sha256.ComputeHash(canonicalBytes);
                digestBase64 = Convert.ToBase64String(digest);
            }
        }

        // Crear y configurar la firma
        SignedXml signedXml = new SignedXmlWithId(xmlDoc);
        signedXml.SigningKey = privateKey;

        Reference reference = new Reference("#" + cdc);
        reference.AddTransform(new XmlDsigEnvelopedSignatureTransform());
        reference.AddTransform(new XmlDsigExcC14NTransform());
        signedXml.AddReference(reference);

        signedXml.SignedInfo.CanonicalizationMethod = "http://www.w3.org/2001/10/xml-exc-c14n#";
        signedXml.SignedInfo.SignatureMethod = "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256";

        KeyInfo keyInfo = new KeyInfo();
        keyInfo.AddClause(new KeyInfoX509Data(cert));
        signedXml.KeyInfo = keyInfo;

        signedXml.ComputeSignature();
        XmlElement xmlDigitalSignature = signedXml.GetXml();

        // Insertar la firma como hijo de <rDE>
        xmlDoc.DocumentElement.AppendChild(xmlDoc.ImportNode(xmlDigitalSignature, true));

        // Agregar código QR después de la firma
        InsertarQRCode(xmlDoc, cdc, dRucReceptor, digestBase64);

        Console.WriteLine("Firma aplicada correctamente con QR");
    }

    private static void InsertarQRCode(XmlDocument xmlDoc, string cdc, string dRucRec, string _)
    {
        try
        {
            // 1. Obtener la fecha de emisión
            string fechaStr = xmlDoc.GetElementsByTagName("dFeEmiDE")[0].InnerText;

            // 2. Convertir fecha a hexadecimal
            byte[] fechaBytes = Encoding.UTF8.GetBytes(fechaStr);
            string fechaHex = BitConverter.ToString(fechaBytes).Replace("-", "").ToLower();

            // 3. Obtener valores del XML firmado
            string totalGral = xmlDoc.GetElementsByTagName("dTotGralOpe")[0].InnerText;
            string totalIVA = xmlDoc.GetElementsByTagName("dTotIVA")[0].InnerText;
            string cItems = xmlDoc.GetElementsByTagName("gCamItem").Count.ToString();

            // 4. Leer DigestValue real desde el XML ya firmado
            string digestBase64 = xmlDoc.GetElementsByTagName("DigestValue")[0].InnerText;
            byte[] digestBytes = Convert.FromBase64String(digestBase64);
            string digestHex = BitConverter.ToString(digestBytes).Replace("-", "").ToLower();

            // 5. Configuración CSC
            var config = Config.LoadConfig();
            string idCSC = config.Sifen.IdCSC;
            string csc = config.Sifen.CSC;

            // 6. Construir la cadena QR
            string cadenaQR =
                $"nVersion=150" +
                $"&Id={cdc}" +
                $"&dFeEmiDE={fechaHex}" +
                $"&dRucRec={dRucRec}" +
                $"&dTotGralOpe={totalGral}" +
                $"&dTotIVA={totalIVA}" +
                $"&cItems={cItems}" +
                $"&DigestValue={digestHex}" +
                $"&IdCSC={idCSC}";

            // 7. Calcular cHashQR
            string cadenaParaHash = cadenaQR + csc;
            string cHashQR;
            
            Console.WriteLine("========== DEBUG QR ==========");
            Console.WriteLine("fechaHex: " + fechaHex);
            Console.WriteLine("totalGral: " + totalGral);
            Console.WriteLine("totalIVA: " + totalIVA);
            Console.WriteLine("cItems: " + cItems);
            Console.WriteLine("digestHex: " + digestHex);
            Console.WriteLine("cadenaQR: " + cadenaQR);
            Console.WriteLine("cadenaParaHash (final): " + cadenaParaHash);
            Console.WriteLine("==============================");

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(cadenaParaHash));
                cHashQR = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }

            // 8. Armar la URL final
            string baseUrl = "https://ekuatia.set.gov.py/consultas/qr?";
            string urlQR = baseUrl + cadenaQR + "&cHashQR=" + cHashQR;

            // 9. Crear los nodos XML
            XmlElement gCamFuFD = xmlDoc.CreateElement("gCamFuFD", xmlDoc.DocumentElement.NamespaceURI);
            XmlElement dCarQR = xmlDoc.CreateElement("dCarQR", xmlDoc.DocumentElement.NamespaceURI);
            dCarQR.InnerText = urlQR;
            gCamFuFD.AppendChild(dCarQR);

            // 10. Eliminar anterior si existe
            var existentes = xmlDoc.GetElementsByTagName("gCamFuFD");
            if (existentes.Count > 0)
            {
                existentes[0].ParentNode.RemoveChild(existentes[0]);
            }

            // 11. ✅ Insertar dentro del nodo <rDE> (no dentro de <DE>)
            xmlDoc.DocumentElement.AppendChild(gCamFuFD);

            Console.WriteLine("Código QR generado correctamente");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error al generar código QR: {ex.Message}");
            throw;
        }
    }

    public class SignedXmlWithId : SignedXml
    {
        public SignedXmlWithId(XmlDocument doc) : base(doc) { }

        public override XmlElement GetIdElement(XmlDocument document, string idValue)
        {
            // Recorre todo el XML buscando nodos con atributo "Id"
            XmlElement idElem = base.GetIdElement(document, idValue);
            if (idElem != null) return idElem;

            // Buscar manualmente cualquier nodo con atributo Id
            XmlNodeList elems = document.GetElementsByTagName("*");
            foreach (XmlElement elem in elems)
            {
                if (elem.HasAttribute("Id") && elem.GetAttribute("Id") == idValue)
                    return elem;
            }

            return null;
        }
    }
}
