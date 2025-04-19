using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

public class FirmaDigitalBC
{
    public static void FirmarXml(XmlDocument xmlDoc, string cdc, byte[] certificadoBytes, string contraseñaCertificado)
    {
        try
        {
            Console.WriteLine($"Iniciando proceso de firma para CDC: {cdc}");

            // Cargar el certificado
            var cert = new X509Certificate2(certificadoBytes, contraseñaCertificado, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
            Console.WriteLine($"Certificado cargado: {cert.Subject}");

            // Obtener la clave privada RSA
            RSA privateKey = cert.GetRSAPrivateKey();
            if (privateKey == null)
            {
                throw new Exception("No se pudo extraer la clave RSA privada del certificado");
            }

            // Buscar el elemento DE para obtener su ID
            XmlElement deElement = xmlDoc.GetElementsByTagName("DE")[0] as XmlElement;
            if (deElement == null)
                throw new Exception("No se encontró el elemento DE en el documento XML");

            // Establecer el atributo Id en el elemento DE
            deElement.SetAttribute("Id", cdc);
            Console.WriteLine($"Atributo Id establecido: {cdc}");

            // Eliminar cualquier firma existente
            var signatures = xmlDoc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
            for (int i = signatures.Count - 1; i >= 0; i--)
            {
                XmlNode node = signatures[i];
                node.ParentNode.RemoveChild(node);
            }

            // Calcular el digest del elemento DE
            using (SHA256 sha256 = SHA256.Create())
            {
                // Clonar el nodo DE asegurando que el atributo Id esté presente al momento de firmar
                XmlDocument tempDoc = new XmlDocument();
                tempDoc.PreserveWhitespace = true;

                // Crear el nodo raíz DE con mismo namespace
                XmlElement rootDE = tempDoc.CreateElement("DE", deElement.NamespaceURI);

                // Copiar atributos incluyendo el Id
                foreach (XmlAttribute attr in deElement.Attributes)
                {
                    XmlAttribute newAttr = tempDoc.CreateAttribute(attr.Prefix, attr.LocalName, attr.NamespaceURI);
                    newAttr.Value = attr.Value;
                    rootDE.Attributes.Append(newAttr);
                }

                // Importar el contenido de DE
                foreach (XmlNode child in deElement.ChildNodes)
                {
                    XmlNode imported = tempDoc.ImportNode(child, true);
                    rootDE.AppendChild(imported);
                }

                // Agregar al documento temporal
                tempDoc.AppendChild(rootDE);
                
                // Serializar para obtener bytes
                using (MemoryStream ms = new MemoryStream())
                {
                    using (XmlTextWriter writer = new XmlTextWriter(ms, new UTF8Encoding(false)))
                    {
                        tempDoc.WriteTo(writer);
                    }
                    
                    byte[] dataToDigest = ms.ToArray();
                    byte[] digestValue = sha256.ComputeHash(dataToDigest);
                    string base64Digest = Convert.ToBase64String(digestValue);
                    Console.WriteLine($"Digest calculado: {base64Digest}");
                    
                    // Crear el elemento Signature
                    XmlElement signatureElement = CreateXmlSignature(xmlDoc, base64Digest, cdc, cert, privateKey);
                    
                    // Agregar la firma como hija directa de <rDE>, no dentro de <DE>
                    XmlNode rdeNode = deElement.ParentNode;
                    rdeNode.AppendChild(signatureElement);
                    
                    Console.WriteLine("Firma XML insertada correctamente después del elemento DE");
                }
            }

            // Guardar para verificación
            string debugPath = "debug";
            Directory.CreateDirectory(debugPath);
            xmlDoc.Save(Path.Combine(debugPath, $"xml_firmado_final_{cdc}.xml"));
            Console.WriteLine("Documento firmado guardado para verificación");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR en firma: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"Detalle: {ex.InnerException.Message}");
            throw new Exception($"Error al firmar XML: {ex.Message}", ex);
        }
    }

    private static XmlElement CreateXmlSignature(XmlDocument xmlDoc, string digestValue, 
        string cdc, X509Certificate2 cert, RSA privateKey)
    {
        // Crear el elemento Signature con el namespace correcto
        XmlElement signatureElement = xmlDoc.CreateElement("Signature", "http://www.w3.org/2000/09/xmldsig#");
        
        // Crear el elemento SignedInfo
        XmlElement signedInfoElement = xmlDoc.CreateElement("SignedInfo", "http://www.w3.org/2000/09/xmldsig#");
        
        // Agregar CanonicalizationMethod
        XmlElement canonMethodElement = xmlDoc.CreateElement("CanonicalizationMethod", "http://www.w3.org/2000/09/xmldsig#");
        canonMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#");
    //    canonMethodElement.SetAttribute("Algorithm", "http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
        signedInfoElement.AppendChild(canonMethodElement);
        
        // Agregar SignatureMethod
        XmlElement sigMethodElement = xmlDoc.CreateElement("SignatureMethod", "http://www.w3.org/2000/09/xmldsig#");
        sigMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
        signedInfoElement.AppendChild(sigMethodElement);
        
        // Agregar Reference
        XmlElement referenceElement = xmlDoc.CreateElement("Reference", "http://www.w3.org/2000/09/xmldsig#");
        referenceElement.SetAttribute("URI", "#" + cdc);
        
        // Agregar Transforms
        XmlElement transformsElement = xmlDoc.CreateElement("Transforms", "http://www.w3.org/2000/09/xmldsig#");
        
        // Transform 1: Enveloped Signature
        XmlElement transform1Element = xmlDoc.CreateElement("Transform", "http://www.w3.org/2000/09/xmldsig#");
        transform1Element.SetAttribute("Algorithm", "http://www.w3.org/2000/09/xmldsig#enveloped-signature");
        transformsElement.AppendChild(transform1Element);
        
        // Transform 2: Exclusive Canonicalization
        XmlElement transform2Element = xmlDoc.CreateElement("Transform", "http://www.w3.org/2000/09/xmldsig#");
        transform2Element.SetAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#");
    //    transform2Element.SetAttribute("Algorithm", "http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
        transformsElement.AppendChild(transform2Element);
        
        referenceElement.AppendChild(transformsElement);
        
        // Agregar DigestMethod
        XmlElement digestMethodElement = xmlDoc.CreateElement("DigestMethod", "http://www.w3.org/2000/09/xmldsig#");
        digestMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256");
        referenceElement.AppendChild(digestMethodElement);
        
        // Agregar DigestValue
        XmlElement digestValueElement = xmlDoc.CreateElement("DigestValue", "http://www.w3.org/2000/09/xmldsig#");
        digestValueElement.InnerText = digestValue;
        referenceElement.AppendChild(digestValueElement);
        
        signedInfoElement.AppendChild(referenceElement);
        signatureElement.AppendChild(signedInfoElement);
        
        // Serializar el SignedInfo para firmarlo
        StringBuilder signedInfoXml = new StringBuilder();
        using (StringWriter sw = new StringWriter(signedInfoXml))
        {
            using (XmlTextWriter xw = new XmlTextWriter(sw))
            {
                signedInfoElement.WriteTo(xw);
            }
        }
        
        // Calcular la firma del SignedInfo
        byte[] dataToSign = Encoding.UTF8.GetBytes(signedInfoXml.ToString());
        
        // Firmar usando RSA
        byte[] signatureBytes = privateKey.SignData(dataToSign, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string signatureValue = Convert.ToBase64String(signatureBytes);
        
        // Agregar SignatureValue
        XmlElement signatureValueElement = xmlDoc.CreateElement("SignatureValue", "http://www.w3.org/2000/09/xmldsig#");
        signatureValueElement.InnerText = signatureValue;
        signatureElement.AppendChild(signatureValueElement);
        
        // Agregar KeyInfo con el certificado
        XmlElement keyInfoElement = xmlDoc.CreateElement("KeyInfo", "http://www.w3.org/2000/09/xmldsig#");
        XmlElement x509DataElement = xmlDoc.CreateElement("X509Data", "http://www.w3.org/2000/09/xmldsig#");
        XmlElement x509CertElement = xmlDoc.CreateElement("X509Certificate", "http://www.w3.org/2000/09/xmldsig#");
        x509CertElement.InnerText = Convert.ToBase64String(cert.GetRawCertData());
        x509DataElement.AppendChild(x509CertElement);
        keyInfoElement.AppendChild(x509DataElement);
        signatureElement.AppendChild(keyInfoElement);
        
        return signatureElement;
    }
}