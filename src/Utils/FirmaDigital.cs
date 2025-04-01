using System;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.IO;
using System.Text;

public class FirmaDigital
{
    public static void FirmarXml(XmlDocument xmlDoc, string cdc, byte[] certificadoBytes, string contraseñaCertificado)
    {
        try
        {
            Console.WriteLine($"Iniciando proceso de firma para CDC: {cdc}");

            // Guardar el XML original para diagnóstico con codificación UTF-8
            string tempFileName = "xml_antes_firma.xml";
            XmlWriterSettings debugSettings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false), // UTF-8 sin BOM
                Indent = true
            };
            using (var writer = XmlWriter.Create(tempFileName, debugSettings))
            {
                xmlDoc.Save(writer);
            }

            // Cargar el certificado
            Console.WriteLine("Cargando certificado...");
            var cert = new X509Certificate2(certificadoBytes, contraseñaCertificado,
                X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);

            Console.WriteLine($"Certificado cargado: {cert.Subject}");

            // Verificar y eliminar firmas existentes
            XmlNodeList existingSignatures = xmlDoc.GetElementsByTagName("Signature", SignedXml.XmlDsigNamespaceUrl);
            if (existingSignatures.Count > 0)
            {
                for (int i = existingSignatures.Count - 1; i >= 0; i--)
                {
                    XmlNode node = existingSignatures[i];
                    node.ParentNode?.RemoveChild(node);
                }
            }

            // Enfoque manual para construir la firma
            // Obtener nodo DE y asegurarse que tenga el atributo Id
            XmlNamespaceManager nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
            nsManager.AddNamespace("rde", "http://ekuatia.set.gov.py/sifen/xsd");
            
            // Buscar el nodo DE con namespace
            XmlElement deElement = null;
            XmlNodeList deNodesWithNs = xmlDoc.SelectNodes("//rde:DE", nsManager);
            
            if (deNodesWithNs != null && deNodesWithNs.Count > 0)
            {
                deElement = (XmlElement)deNodesWithNs[0];
            }
            else
            {
                // Intentar sin namespace
                XmlNodeList deNodes = xmlDoc.GetElementsByTagName("DE");
                if (deNodes.Count > 0)
                {
                    deElement = (XmlElement)deNodes[0];
                }
            }
            
            if (deElement == null)
            {
                throw new Exception("No se encontró el elemento DE para calcular el digest");
            }

            Console.WriteLine($"Nodo DE encontrado con nombre: {deElement.Name}");

            // Asegurarse de que el elemento DE tenga el atributo Id
            if (!deElement.HasAttribute("Id"))
            {
                deElement.SetAttribute("Id", cdc);
                Console.WriteLine($"Atributo Id añadido con valor: {cdc}");
            }
            else 
            {
                string currentId = deElement.GetAttribute("Id");
                Console.WriteLine($"Atributo Id existente: {currentId}");
                if (currentId != cdc)
                {
                    Console.WriteLine($"Actualizando Id de {currentId} a {cdc}");
                    deElement.SetAttribute("Id", cdc);
                }
            }

            // El problema puede estar en la forma en que el elemento ID se establece y luego se encuentra
            // Vamos a crear la firma manualmente
            XmlElement signatureElement = xmlDoc.CreateElement("Signature", SignedXml.XmlDsigNamespaceUrl);

            // SignedInfo
            XmlElement signedInfoElement = xmlDoc.CreateElement("SignedInfo", SignedXml.XmlDsigNamespaceUrl);
            
            // CanonicalizationMethod
            XmlElement canonMethodElement = xmlDoc.CreateElement("CanonicalizationMethod", SignedXml.XmlDsigNamespaceUrl);
            canonMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#");
            signedInfoElement.AppendChild(canonMethodElement);
            
            // SignatureMethod
            XmlElement sigMethodElement = xmlDoc.CreateElement("SignatureMethod", SignedXml.XmlDsigNamespaceUrl);
            sigMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
            signedInfoElement.AppendChild(sigMethodElement);
            
            // Reference
            XmlElement referenceElement = xmlDoc.CreateElement("Reference", SignedXml.XmlDsigNamespaceUrl);
            referenceElement.SetAttribute("URI", "#" + cdc);
            
            // Transforms
            XmlElement transformsElement = xmlDoc.CreateElement("Transforms", SignedXml.XmlDsigNamespaceUrl);
            
            // Transform - Enveloped Signature
            XmlElement transformEnvelopedElement = xmlDoc.CreateElement("Transform", SignedXml.XmlDsigNamespaceUrl);
            transformEnvelopedElement.SetAttribute("Algorithm", "http://www.w3.org/2000/09/xmldsig#enveloped-signature");
            transformsElement.AppendChild(transformEnvelopedElement);
            
            // Transform - Exclusive C14N
            XmlElement transformExcC14NElement = xmlDoc.CreateElement("Transform", SignedXml.XmlDsigNamespaceUrl);
            transformExcC14NElement.SetAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#");
            transformsElement.AppendChild(transformExcC14NElement);
            
            // Agregar transformaciones a la referencia
            referenceElement.AppendChild(transformsElement);
            
            // DigestMethod
            XmlElement digestMethodElement = xmlDoc.CreateElement("DigestMethod", SignedXml.XmlDsigNamespaceUrl);
            digestMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256");
            referenceElement.AppendChild(digestMethodElement);
            
            // Calcular el DigestValue
            // Clonar el nodo para evitar alteraciones
            XmlDocument tempDoc = new XmlDocument();
            tempDoc.PreserveWhitespace = true;
            XmlNode importedDE = tempDoc.ImportNode(deElement, true);
            tempDoc.AppendChild(importedDE);
            
            // Aplicar canonicalización exclusiva
            XmlDsigExcC14NTransform c14n = new XmlDsigExcC14NTransform();
            c14n.LoadInput(tempDoc);
            Stream canonicalStream = (Stream)c14n.GetOutput(typeof(Stream));
            
            byte[] hash;
            using (var sha256 = System.Security.Cryptography.SHA256.Create())
            {
                hash = sha256.ComputeHash(canonicalStream);
            }
            
            string digestValue = Convert.ToBase64String(hash);
            
            // DigestValue
            XmlElement digestValueElement = xmlDoc.CreateElement("DigestValue", SignedXml.XmlDsigNamespaceUrl);
            digestValueElement.InnerText = digestValue;
            referenceElement.AppendChild(digestValueElement);
            
            // Agregar referencia a SignedInfo
            signedInfoElement.AppendChild(referenceElement);
            
            // Agregar SignedInfo a Signature
            signatureElement.AppendChild(signedInfoElement);
            
            // Calcular SignatureValue
            XmlDocument signedInfoDoc = new XmlDocument();
            signedInfoDoc.PreserveWhitespace = true;
            XmlNode importedSignedInfo = signedInfoDoc.ImportNode(signedInfoElement, true);
            signedInfoDoc.AppendChild(importedSignedInfo);
            
            XmlDsigExcC14NTransform c14nSignedInfo = new XmlDsigExcC14NTransform();
            c14nSignedInfo.LoadInput(signedInfoDoc);
            Stream canonicalSignedInfoStream = (Stream)c14nSignedInfo.GetOutput(typeof(Stream));
            
            byte[] canonicalSignedInfoBytes;
            using (MemoryStream ms = new MemoryStream())
            {
                canonicalSignedInfoStream.CopyTo(ms);
                canonicalSignedInfoBytes = ms.ToArray();
            }
            
            var rsa = cert.GetRSAPrivateKey();
            byte[] signatureBytes = rsa.SignData(canonicalSignedInfoBytes,
                System.Security.Cryptography.HashAlgorithmName.SHA256,
                System.Security.Cryptography.RSASignaturePadding.Pkcs1);
            
            // SignatureValue
            XmlElement signatureValueElement = xmlDoc.CreateElement("SignatureValue", SignedXml.XmlDsigNamespaceUrl);
            signatureValueElement.InnerText = Convert.ToBase64String(signatureBytes);
            signatureElement.AppendChild(signatureValueElement);
            
            // KeyInfo
            XmlElement keyInfoElement = xmlDoc.CreateElement("KeyInfo", SignedXml.XmlDsigNamespaceUrl);
            XmlElement x509DataElement = xmlDoc.CreateElement("X509Data", SignedXml.XmlDsigNamespaceUrl);
            XmlElement x509CertificateElement = xmlDoc.CreateElement("X509Certificate", SignedXml.XmlDsigNamespaceUrl);
            x509CertificateElement.InnerText = Convert.ToBase64String(cert.RawData);
            x509DataElement.AppendChild(x509CertificateElement);
            keyInfoElement.AppendChild(x509DataElement);
            signatureElement.AppendChild(keyInfoElement);
            
            // Agregar Signature al documento
            xmlDoc.DocumentElement.AppendChild(signatureElement);
            Console.WriteLine("Firma XML añadida al documento correctamente");

            // Guardar el XML firmado con codificación UTF-8 sin BOM
            string signedFileName = "xml_firmado.xml";
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false), // UTF-8 sin BOM
                Indent = true
            };
            using (XmlWriter writer = XmlWriter.Create(signedFileName, settings))
            {
                xmlDoc.Save(writer);
            }
            Console.WriteLine($"XML firmado guardado como: {signedFileName}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR CRÍTICO: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Error interno: {ex.InnerException.Message}");
            }
            throw new Exception($"Error al firmar el documento XML: {ex.Message}", ex);
        }
    }
}