using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

public class GenerarXML
{
    public static void SerializarDocumentoElectronico(string cdc, int dv, string rutaArchivo, string dCodSeg, string iTiDE,
        int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT, DateTime dFeEmiDE)
    {
        try
        {
            // Crear el documento
            DocumentoElectronico documento = new DocumentoElectronico(cdc, dv, 1, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE);
            
            // Serializar primero a un StringWriter
            var stringWriter = new StringWriter();
            var serializer = new XmlSerializer(typeof(DocumentoElectronico));
            
            // Usar namespaces vacíos para la serialización inicial
            var emptyNs = new XmlSerializerNamespaces();
            emptyNs.Add("", "");
            
            serializer.Serialize(stringWriter, documento, emptyNs);

            // Cargar el XML en un XmlDocument para manipularlo
            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(stringWriter.ToString());

            // Obtener el nodo raíz
            var root = xmlDoc.DocumentElement;

            // Limpiar los atributos existentes
            root.RemoveAllAttributes();

            // Agregar los atributos en el orden correcto
            var xmlns = xmlDoc.CreateAttribute("xmlns");
            xmlns.Value = "http://ekuatia.set.gov.py/sifen/xsd";
            root.Attributes.Append(xmlns);

            var xmlnsXsi = xmlDoc.CreateAttribute("xmlns", "xsi", "http://www.w3.org/2000/xmlns/");
            xmlnsXsi.Value = "http://www.w3.org/2001/XMLSchema-instance";
            root.Attributes.Append(xmlnsXsi);

            var schemaLocation = xmlDoc.CreateAttribute("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance");
            schemaLocation.Value = "https://ekuatia.set.gov.py/sifen/xsd siRecepDE_v150.xsd";
            root.Attributes.Append(schemaLocation);

            // Configurar los ajustes de escritura 
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            // Guardar el documento
            using (var writer = XmlWriter.Create(rutaArchivo, settings))
            {
                xmlDoc.Save(writer);
            }
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al generar el XML: {ex.Message}", ex);
        }
    }
}