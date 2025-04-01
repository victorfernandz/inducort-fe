using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Linq;

public class GenerarXML
{
    public static void SerializarDocumentoElectronico(string cdc, int dv, DateTime dFecFirma, string rutaArchivo, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT, DateTime dFeEmiDE,
        string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, 
        string dDesCiuEmi, string dTelEmi, string dEmailE, int iNatRec, int iTiContRec, int iTiOpe, string cPaisRec, string dDesPaisRe, string dNomRec, string dRucReceptor, int dDVReceptor, decimal dTiCam, int iIndPres, int iCondOpe, int iCondCred,
        List<ActividadEconomica> actividades, List<ObligacionAfectada> obligaciones = null, List<GCuotas> cuotas = null, List<Item> items = null, string plazoCredito = null, GTotSub totales = null,
        byte[] certificadoBytes = null, string contraseñaCertificado = null)
    {
        try
        {
            // Usar la primera actividad económica 
            var actividadPrincipal = actividades.First();

            // Crear el documento con la primera actividad
            DocumentoElectronico documento = new DocumentoElectronico(cdc, dv, dFecFirma, 1, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, 
                dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE, actividadPrincipal.Codigo, actividadPrincipal.Descripcion, iNatRec, iTiContRec, iTiOpe, cPaisRec, dDesPaisRe, dNomRec, dRucReceptor,
                dDVReceptor, dTiCam, iIndPres, iCondOpe, iCondCred);
            
            // Si hay más de una actividad, añadir las adicionales
            if (actividades.Count > 1)
            {
                for (int i = 1; i < actividades.Count; i++)
                {
                    documento.DE.CamposGenerales.GrupoCamposEmisor.ActividadesEconomicas.Add(
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

            // Verificar si hay operación de crédito (iCondOpe == 2)
            if (iCondOpe == 2)
            {
                // Verificar qué tipo de crédito es (plazo o cuotas)
                if (iCondCred == 1) // Plazo
                {
                    // Si no se pasó el plazo directamente, intentar obtenerlo de las cuotas
                    string plazoFinal = plazoCredito;
                    
                    // Si no hay plazo, usar valor por defecto o intentar obtenerlo de las cuotas
                    if (string.IsNullOrEmpty(plazoFinal))
                    {
                        plazoFinal = "30 días"; // Valor por defecto
                        
                        // Si hay una cuota para plazo, usar su valor
                        if (cuotas != null && cuotas.Count > 0)
                        {
                            var cuotaPlazo = cuotas[0];
                            if (!string.IsNullOrEmpty(cuotaPlazo.FechaVencimientoCuota))
                            {
                                plazoFinal = cuotaPlazo.FechaVencimientoCuota;
                            }
                        }
                    }
                    
                    documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito = 
                        new GPagCred(1, plazoFinal, null);
                }
                else if (iCondCred == 2) // Cuotas
                {
                    // Para cuotas, cuotas debe contener todas las cuotas
                    int cantidadCuotas = cuotas?.Count ?? 0;
                    
                    // Crear el objeto GPagCred para cuotas
                    documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito = 
                        new GPagCred(2, null, cantidadCuotas);
                    
                    // Agregar cada cuota si hay cuotas disponibles
                    if (cuotas != null && cuotas.Any())
                    {
                        foreach (var cuota in cuotas)
                        {
                            documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito.Cuotas.Add(cuota);
                        }
                    }
                }
            }

            // Procesar ítems adicionales (si se proporcionó más de uno)
            if (items != null && items.Any())
            {
                // Limpiar cualquier ítem que se haya creado por defecto
                documento.DE.CamposEspecificosTipoDocumento.Items.Clear();
            
                // Agregar cada ítem a la lista
                foreach (var item in items)
                {
                    // Calcular totalGs solo si es necesario
                    decimal? totalGs = null;
                    if (item.dTiCamIt > 0)
                    {
                        totalGs = item.dTotBruOpeItem * item.dTiCamIt;
                    }
                    
                    var valorItem = new GValorItem
                    {
                        PrecioUnitario = item.dPUniProSer,
                        TipoCambio = item.dTiCamIt,
                        TotalBrutoItem = item.dTotBruOpeItem,
                        ValorRestaItem = new GValorRestaItem
                        {
                            TotalOperacionItem = item.dTotBruOpeItem,
                            TotalOperacionGs = totalGs
                        }
                    };

                    var camposIVA = new GCamIVA
                    {
                        AfectacionIVA = item.iAfecIVA,
                        DescripcionAfectacionIVA = item.dDesAfecIVA,
                        ProporcionIVA = item.dPropIVA,
                        TasaIVA = (int)item.dTasaIVA,
                        BaseGravadaIVA = item.dBasGravIVA,
                        LiquidacionIVA = item.dLiqIVAItem,
                        BaseExenta = item.dBasExe
                    };
                    
                    documento.DE.CamposEspecificosTipoDocumento.Items.Add(new GCamItem
                    {
                        CodigoItem = item.dCodInt,
                        DescripcionItem = item.dDesProSer,
                        CantidadProducto = item.dCantProSer,
                        ValorItem = valorItem,
                        CamposIVA = camposIVA
                    });
                }
            }

            // Asignar los totales si fueron proporcionados
            if (totales != null)
            {
                documento.DE.CamposTotalesSubtotales = totales;
            }
            else if (items != null && items.Any())
            {
                // Si no se proporcionaron totales pero hay items, calcularlos
                documento.DE.CamposTotalesSubtotales = Totalizador.CalcularTotalesFactura(items, dTiCam, cMoneOpe);
            }
            else
            {
                // Si no hay items ni totales, inicializar un objeto vacío
                documento.DE.CamposTotalesSubtotales = new GTotSub();
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

            // Agregar la firma digital solo si se proporcionaron los datos del certificado
            if (certificadoBytes != null && !string.IsNullOrEmpty(contraseñaCertificado))
            {
                FirmaDigital.FirmarXml(xmlDoc, cdc, certificadoBytes, contraseñaCertificado);

                // Crear nodo gCamFuFD (Grupo J - Campos fuera de la firma digital)
                XmlNamespaceManager nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
                nsManager.AddNamespace("d", "http://www.w3.org/2000/09/xmldsig#");

                XmlNode nodoFirma = xmlDoc.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#")[0];

                // Crear el nodo <gCamFuFD>
                XmlElement nodoGrupoJ = xmlDoc.CreateElement("gCamFuFD", xmlDoc.DocumentElement.NamespaceURI);

                // -------------------------------------------------------------------------
                // Construcción de dCarQR conforme al Manual Técnico v150 de la SET
                // -------------------------------------------------------------------------

                string fechaBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(dFeEmiDE.ToString("yyyy-MM-ddTHH:mm:ss")));
                string rucRec = dRucReceptor;
                string totalGral = documento.DE.CamposTotalesSubtotales.TotalGravada10.ToString("F8").Replace(",", ".");
                string totalIVA = documento.DE.CamposTotalesSubtotales.TotalGravadaIVA.ToString("F8").Replace(",", ".");
                string cantidadItems = documento.DE.CamposEspecificosTipoDocumento.Items.Count.ToString();
                string idCSC = "1"; // ID del Código Secreto Compartido

                string digestValue = xmlDoc.GetElementsByTagName("DigestValue", "http://www.w3.org/2000/09/xmldsig#")[0]?.InnerText ?? "";

                string cadenaQR = 
                    $"nVersion=150" +
                    $"&Id={cdc}" +
                    $"&dFeEmiDE={fechaBase64}" +
                    $"&dRucRec={rucRec}" +
                    $"&dTotGralOpe={totalGral}" +
                    $"&dTotIVA={totalIVA}" +
                    $"&cItems={cantidadItems}" +
                    $"&DigestValue={digestValue}" +
                    $"&IdCSC={idCSC}";

                string cHashQR;
                using (var sha256 = System.Security.Cryptography.SHA256.Create())
                {
                    var bytesQR = Encoding.UTF8.GetBytes(cadenaQR);
                    var hash = sha256.ComputeHash(bytesQR);
                    cHashQR = BitConverter.ToString(hash).Replace("-", "").ToLower();
                }

                string urlQR = $"https://ekuatia.set.gov.py/consultas/qr?{cadenaQR}&cHashQR={cHashQR}";

                XmlElement dCarQR = xmlDoc.CreateElement("dCarQR", xmlDoc.DocumentElement.NamespaceURI);
                dCarQR.InnerText = urlQR;
                nodoGrupoJ.AppendChild(dCarQR);

                // (Opcional) Si querés agregar dInfAdic y NO vas a enviar a SIFEN, descomentá:
                /*
                XmlElement dInfAdic = xmlDoc.CreateElement("dInfAdic", xmlDoc.DocumentElement.NamespaceURI);
                dInfAdic.InnerText = "Gracias por su preferencia.";
                nodoGrupoJ.AppendChild(dInfAdic);
                */

                // Insertar <gCamFuFD> después del nodo <Signature>
                xmlDoc.DocumentElement.InsertAfter(nodoGrupoJ, nodoFirma);
            }
            else
            {
                Console.WriteLine("Advertencia: No se ha proporcionado certificado para firmar el documento.");
            }

            // Configurar los ajustes de escritura 
            var settings = new XmlWriterSettings
            {
                Indent = true,
                Encoding = Encoding.UTF8,
                OmitXmlDeclaration = false
            };

            // Crear el directorio si no existe
            string directorio = Path.GetDirectoryName(rutaArchivo);
            if (!string.IsNullOrEmpty(directorio) && !Directory.Exists(directorio))
            {
                Directory.CreateDirectory(directorio);
            }

            // Guardar el documento
            using (var writer = XmlWriter.Create(rutaArchivo, settings))
            {
                xmlDoc.Save(writer);
            }

            Console.WriteLine($"Documento XML generado exitosamente en: {rutaArchivo}");
        }
        catch (Exception ex)
        {
            throw new Exception($"Error al generar el XML: {ex.Message}", ex);
        }
    }
}