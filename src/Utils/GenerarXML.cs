using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;

public class GenerarXML
{
    public static void SerializarDocumentoElectronico(string cdc, int dv, string rutaArchivo, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, 
        string dNumDoc, DateTime dFeIniT, DateTime dFeEmiDE, string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, 
        string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE, 
        List<ActividadEconomica> actividades, List<ObligacionAfectada> obligaciones = null)
    {
        try
        {
            // Usar la primera actividad económica para inicializar el documento (si existe)
            var actividadPrincipal = actividades.FirstOrDefault() ?? new ActividadEconomica { Codigo = "0", Descripcion = "No especificada" };

             // Crear el documento con la primera actividad
            DocumentoElectronico documento = new DocumentoElectronico(cdc, dv, 1, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe,
                dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE, actividadPrincipal.Codigo, 
                actividadPrincipal.Descripcion);
            
            // Si hay más de una actividad, añadir las adicionales
            if (actividades.Count > 1)
            {
                for (int i = 1; i < actividades.Count; i++)
                {
                    documento.DE.CamposGenerales.ActividadesEconomicas.Add(
                        new GActEco(actividades[i].Codigo, actividades[i].Descripcion));
                }
            }

            // Agregar obligaciones afectadas si hay
            if (obligaciones != null && obligaciones.Any())
            {
                foreach (var obligacion in obligaciones)
                {
                    documento.DE.CamposGenerales.OperacionComercial.ObligacionesAfectadas.Add(
                        new GOblAfe(obligacion.Codigo, obligacion.Descripcion));
                }
            }

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