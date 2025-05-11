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

        // Establecer atributo Id 
        XmlAttribute idAttr = xmlDoc.CreateAttribute("Id");
        idAttr.Value = cdc;
        deElement.Attributes.Append(idAttr);

        if (deElement.HasAttribute("xmlns"))
            deElement.RemoveAttribute("xmlns");

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

        // Insertar la firma
        xmlDoc.DocumentElement.AppendChild(xmlDoc.ImportNode(xmlDigitalSignature, true));

        // Obtener el DigestValue generado
        string digestValueReal = xmlDigitalSignature.GetElementsByTagName("DigestValue")[0]?.InnerText;
        InsertarQRCode(xmlDoc, cdc, dRucReceptor, digestValueReal);

        Console.WriteLine("Firma aplicada correctamente con QR");
    }

    private static void InsertarQRCode(XmlDocument xmlDoc, string cdc, string dRucRec, string digestBase64)
    {
        try
        {
            string fechaStr = xmlDoc.GetElementsByTagName("dFeEmiDE")[0].InnerText;
            byte[] fechaBytes = Encoding.UTF8.GetBytes(fechaStr);
            string fechaHex = BitConverter.ToString(fechaBytes).Replace("-", "").ToLower();

            string totalGral = xmlDoc.GetElementsByTagName("dTotGralOpe")[0].InnerText;
            string totalIVA = xmlDoc.GetElementsByTagName("dTotIVA")[0].InnerText;
            string cItems = xmlDoc.GetElementsByTagName("gCamItem").Count.ToString();

            byte[] digestAsciiBytes = Encoding.UTF8.GetBytes(digestBase64);
            string digestHex = BitConverter.ToString(digestAsciiBytes).Replace("-", "").ToLower();

            var config = Config.LoadConfig();
            string idCSC = config.Sifen.IdCSC;
            string csc = config.Sifen.CSC;

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

            string cadenaParaHash = cadenaQR + csc;
            string cHashQR;

            using (SHA256 sha256 = SHA256.Create())
            {
                byte[] hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(cadenaParaHash));
                cHashQR = BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
            }

            string urlQR = "https://ekuatia.set.gov.py/consultas/qr?" + cadenaQR + "&cHashQR=" + cHashQR;

            XmlElement gCamFuFD = xmlDoc.CreateElement("gCamFuFD", xmlDoc.DocumentElement.NamespaceURI);
            XmlElement dCarQR = xmlDoc.CreateElement("dCarQR", xmlDoc.DocumentElement.NamespaceURI);
            dCarQR.InnerText = urlQR;
            gCamFuFD.AppendChild(dCarQR);

            var existentes = xmlDoc.GetElementsByTagName("gCamFuFD");
            if (existentes.Count > 0)
            {
                existentes[0].ParentNode.RemoveChild(existentes[0]);
            }

            xmlDoc.DocumentElement.AppendChild(gCamFuFD);
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
            XmlElement idElem = base.GetIdElement(document, idValue);
            if (idElem != null) return idElem;

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