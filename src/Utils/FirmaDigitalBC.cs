using System;
using System.IO;
using System.Xml;
using System.Text;
using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography;

public class FirmaDigitalBC
{
    public static void FirmarXml(XmlDocument xmlDoc, string cdc, DateTime dFeEmiDE, string dRucReceptor, byte[] certificadoBytes, string contraseñaCertificado)
    {
        try
        {
            Console.WriteLine($"Iniciando proceso de firma para CDC: {cdc}");

            // Cargar certificado
            var cert = new X509Certificate2(certificadoBytes, contraseñaCertificado, X509KeyStorageFlags.Exportable | X509KeyStorageFlags.PersistKeySet);
        //    Console.WriteLine($"Certificado cargado: {cert.Subject}");

            // Clave privada
            RSA privateKey = cert.GetRSAPrivateKey();
            if (privateKey == null)
                throw new Exception("No se pudo extraer la clave RSA privada del certificado");

            // Buscar nodo <DE>
            XmlElement deElement = xmlDoc.GetElementsByTagName("DE")[0] as XmlElement;
            if (deElement == null)
                throw new Exception("No se encontró el elemento DE en el documento XML");

            // Establecer Id
            deElement.SetAttribute("Id", cdc);
            Console.WriteLine($"Atributo Id establecido: {cdc}");

            // Eliminar firmas previas
            var signatures = xmlDoc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");
            for (int i = signatures.Count - 1; i >= 0; i--)
                signatures[i].ParentNode.RemoveChild(signatures[i]);

            // Digest
            string digestBase64;
            using (SHA256 sha256 = SHA256.Create())
            {
                XmlDocument tempDoc = new XmlDocument();
                tempDoc.PreserveWhitespace = true;
                XmlElement rootDE = tempDoc.CreateElement("DE", deElement.NamespaceURI);

                foreach (XmlAttribute attr in deElement.Attributes)
                {
                    XmlAttribute newAttr = tempDoc.CreateAttribute(attr.Prefix, attr.LocalName, attr.NamespaceURI);
                    newAttr.Value = attr.Value;
                    rootDE.Attributes.Append(newAttr);
                }

                foreach (XmlNode child in deElement.ChildNodes)
                    rootDE.AppendChild(tempDoc.ImportNode(child, true));

                tempDoc.AppendChild(rootDE);

                using (MemoryStream ms = new MemoryStream())
                {
                    using (XmlTextWriter writer = new XmlTextWriter(ms, new UTF8Encoding(false)))
                        tempDoc.WriteTo(writer);

                    byte[] dataToDigest = ms.ToArray();
                    byte[] digestValue = sha256.ComputeHash(dataToDigest);
                    digestBase64 = Convert.ToBase64String(digestValue);
                //    Console.WriteLine($"Digest calculado: {digestBase64}");
                }
            }

            // Crear firma
            XmlElement signatureElement = CreateXmlSignature(xmlDoc, digestBase64, cdc, cert, privateKey);

            // Insertar firma en <rDE>
            XmlNode rdeNode = deElement.ParentNode;
            rdeNode.AppendChild(signatureElement);
    //        Console.WriteLine("Firma XML insertada correctamente después del elemento DE");

            // Insertar gCamFuFD después de la firma
            AgregarGcamFuFD(xmlDoc, cdc, dFeEmiDE, dRucReceptor, digestBase64);

            // Guardar para debug
            string debugPath = "debug";
            Directory.CreateDirectory(debugPath);
            xmlDoc.Save(Path.Combine(debugPath, $"xml_firmado_final_{cdc}.xml"));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"ERROR en firma: {ex.Message}");
            if (ex.InnerException != null)
                Console.WriteLine($"Detalle: {ex.InnerException.Message}");
            throw new Exception($"Error al firmar XML: {ex.Message}", ex);
        }
    }

    private static XmlElement CreateXmlSignature(XmlDocument xmlDoc, string digestValue, string cdc, X509Certificate2 cert, RSA privateKey)
    {
        XmlElement signatureElement = xmlDoc.CreateElement("Signature", "http://www.w3.org/2000/09/xmldsig#");
        XmlElement signedInfoElement = xmlDoc.CreateElement("SignedInfo", "http://www.w3.org/2000/09/xmldsig#");

        XmlElement canonMethodElement = xmlDoc.CreateElement("CanonicalizationMethod", "http://www.w3.org/2000/09/xmldsig#");
    //    canonMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#");
        canonMethodElement.SetAttribute("Algorithm", "http://www.w3.org/TR/2001/REC-xml-c14n-20010315");
        signedInfoElement.AppendChild(canonMethodElement);

        XmlElement sigMethodElement = xmlDoc.CreateElement("SignatureMethod", "http://www.w3.org/2000/09/xmldsig#");
        sigMethodElement.SetAttribute("Algorithm", "http://www.w3.org/2001/04/xmldsig-more#rsa-sha256");
        signedInfoElement.AppendChild(sigMethodElement);

        XmlElement referenceElement = xmlDoc.CreateElement("Reference", "http://www.w3.org/2000/09/xmldsig#");
        referenceElement.SetAttribute("URI", "#" + cdc);

        XmlElement transformsElement = xmlDoc.CreateElement("Transforms", "http://www.w3.org/2000/09/xmldsig#");

        XmlElement transform1 = xmlDoc.CreateElement("Transform", "http://www.w3.org/2000/09/xmldsig#");
        transform1.SetAttribute("Algorithm", "http://www.w3.org/2000/09/xmldsig#enveloped-signature");
        transformsElement.AppendChild(transform1);

        XmlElement transform2 = xmlDoc.CreateElement("Transform", "http://www.w3.org/2000/09/xmldsig#");
        transform2.SetAttribute("Algorithm", "http://www.w3.org/2001/10/xml-exc-c14n#");
        transformsElement.AppendChild(transform2);

        referenceElement.AppendChild(transformsElement);

        XmlElement digestMethod = xmlDoc.CreateElement("DigestMethod", "http://www.w3.org/2000/09/xmldsig#");
        digestMethod.SetAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256");
        referenceElement.AppendChild(digestMethod);

        XmlElement digestValueElement = xmlDoc.CreateElement("DigestValue", "http://www.w3.org/2000/09/xmldsig#");
        digestValueElement.InnerText = digestValue;
        referenceElement.AppendChild(digestValueElement);

        signedInfoElement.AppendChild(referenceElement);
        signatureElement.AppendChild(signedInfoElement);

        // Firmar SignedInfo
        string signedXml;
        using (var sw = new StringWriter())
        using (var xw = new XmlTextWriter(sw))
        {
            signedInfoElement.WriteTo(xw);
            signedXml = sw.ToString();
        }

        byte[] signedBytes = Encoding.UTF8.GetBytes(signedXml);
        byte[] signatureBytes = privateKey.SignData(signedBytes, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        string signatureBase64 = Convert.ToBase64String(signatureBytes);

        XmlElement sigValue = xmlDoc.CreateElement("SignatureValue", "http://www.w3.org/2000/09/xmldsig#");
        sigValue.InnerText = signatureBase64;
        signatureElement.AppendChild(sigValue);

        XmlElement keyInfo = xmlDoc.CreateElement("KeyInfo", "http://www.w3.org/2000/09/xmldsig#");
        XmlElement x509Data = xmlDoc.CreateElement("X509Data", "http://www.w3.org/2000/09/xmldsig#");
        XmlElement x509Cert = xmlDoc.CreateElement("X509Certificate", "http://www.w3.org/2000/09/xmldsig#");
        x509Cert.InnerText = Convert.ToBase64String(cert.GetRawCertData());
        x509Data.AppendChild(x509Cert);
        keyInfo.AppendChild(x509Data);
        signatureElement.AppendChild(keyInfo);

        return signatureElement;
    }

    public static void AgregarGcamFuFD(XmlDocument xmlDoc, string cdc, DateTime dFeEmiDE, string dRucReceptor, string digestBase64)
    {
        try
        {
            XmlNode nodoDE = xmlDoc.GetElementsByTagName("DE")[0];
            XmlNode nodoPadreDE = nodoDE?.ParentNode;

            var config = Config.LoadConfig();
            string idCSC = config.Sifen.IdCSC;
            string csc = config.Sifen.CSC;

            //string fechaISO = dFeEmiDE.ToString("yyyy-MM-ddTHH:mm:ss");
            string fechaISO = xmlDoc.GetElementsByTagName("dFeEmiDE")[0].InnerText;
            string fechaHex = BitConverter.ToString(Encoding.UTF8.GetBytes(fechaISO)).Replace("-", "").ToLower();
            string totalGral = xmlDoc.GetElementsByTagName("dTotGralOpe")[0].InnerText;
            string totalIVA = xmlDoc.GetElementsByTagName("dTotIVA")[0].InnerText;
            string cantidadItems = xmlDoc.GetElementsByTagName("gCamItem").Count.ToString();
            string digestHex = Base64ToHex(digestBase64);

            string cadenaVisibleQR =
                $"nVersion=150" +
                $"&Id={cdc}" +
                $"&dFeEmiDE={fechaHex}" +
                $"&dRucRec={dRucReceptor}" +
                $"&dTotGralOpe={totalGral}" +
                $"&dTotIVA={totalIVA}" +
                $"&cItems={cantidadItems}" +
                $"&DigestValue={digestHex}" +
                $"&IdCSC={idCSC}";
            
            Console.WriteLine(cadenaVisibleQR);

            string cadenaParaHash = cadenaVisibleQR + csc;
            string cHashQR;
            using (var sha256 = SHA256.Create())
            {
                var bytesQR = Encoding.UTF8.GetBytes(cadenaParaHash);
                var hash = sha256.ComputeHash(bytesQR);
                cHashQR = BitConverter.ToString(hash).Replace("-", "").ToLower();
            }

            string urlQR = $"https://ekuatia.set.gov.py/consultas-test/qr?{cadenaVisibleQR}&cHashQR={cHashQR}";

            XmlElement nodoGrupoJ = xmlDoc.CreateElement("gCamFuFD", xmlDoc.DocumentElement.NamespaceURI);
            XmlElement dCarQR = xmlDoc.CreateElement("dCarQR", xmlDoc.DocumentElement.NamespaceURI);
            dCarQR.InnerText = urlQR;
            //dCarQR.InnerText = urlQR.Replace("&", "&amp;");
            nodoGrupoJ.AppendChild(dCarQR);

            nodoPadreDE?.AppendChild(nodoGrupoJ);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Error al agregar gCamFuFD: " + ex.Message);
        }
    }
    private static string Base64ToHex(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }

/*
    private static string Base64ToHex(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }
*/
}
