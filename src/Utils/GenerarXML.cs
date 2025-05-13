using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Globalization;

public class GenerarXML
{
    public static void SerializarDocumentoElectronico(string cdc, int dv, DateTime dFecFirma, string rutaArchivo, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT, DateTime dFeEmiDE,
        string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, 
        string dEmailE, int iNatRec, int iTiContRec, int iTiOpe, string cPaisRec, string dDesPaisRe, string dNomRec, string dRucReceptor, int dDVReceptor, decimal dTiCam, int iIndPres, int iCondOpe, int iCondCred, int iTiPago, decimal dMonTiPag, 
        string cMoneTiPag, string dDMoneTiPag, decimal? dTiCamTiPag,
        List<ActividadEconomica> actividades, List<ObligacionAfectada> obligaciones = null, List<GCuotas> cuotas = null, List<Item> items = null, string plazoCredito = null, GTotSub totales = null,
        byte[] certificadoBytes = null, string contraseñaCertificado = null)
    {
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var actividadPrincipal = actividades.First();

            DocumentoElectronico documento = new DocumentoElectronico(cdc, dv, dFecFirma, 1, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, 
                dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE, actividadPrincipal.Codigo, actividadPrincipal.Descripcion, iNatRec, iTiContRec, iTiOpe, cPaisRec, dDesPaisRe, dNomRec, dRucReceptor,
                dDVReceptor, dTiCam, iIndPres, iCondOpe, iCondCred, iTiPago, dMonTiPag, cMoneTiPag, dDMoneTiPag, dTiCamTiPag);

            // Agregar actividades económicas adicionales
            if (actividades.Count > 1)
            {
                for (int i = 1; i < actividades.Count; i++)
                {
                    documento.DE.CamposGenerales.GrupoCamposEmisor.ActividadesEconomicas.Add(new GActEco(actividades[i].Codigo, actividades[i].Descripcion));
                }
            }

            // Agregar obligaciones
            if (obligaciones != null && obligaciones.Any())
            {
                foreach (var obligacion in obligaciones)
                {
                    documento.DE.CamposGenerales.OperacionComercial.ObligacionesAfectadas.Add(new GOblAfe(obligacion.Codigo, obligacion.Descripcion));
                }
            }

            // Configurar condiciones de operación y crédito
            if (iCondOpe == 2)
            {
                if (iCondCred == 1)
                {
                    string plazoFinal = string.IsNullOrEmpty(plazoCredito) ? 
                        (cuotas?.FirstOrDefault()?.FechaVencimientoCuota ?? "30 días") : 
                        plazoCredito;
                    
                    documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito = 
                        new GPagCred(1, plazoFinal, null);
                }
                else if (iCondCred == 2)
                {
                    int cantidadCuotas = cuotas?.Count ?? 0;
                    documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito = 
                        new GPagCred(2, null, cantidadCuotas);
                    
                    if (cuotas != null && cuotas.Any())
                    {
                        foreach (var cuota in cuotas)
                        {
                            documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito.Cuotas.Add(cuota);
                        }
                    }
                }
            }

            // Agregar items
            if (items != null && items.Any())
            {
                documento.DE.CamposEspecificosTipoDocumento.Items.Clear();
                foreach (var item in items)
                {
                    int condicionTipoCambio = documento.DE.CamposGenerales.OperacionComercial.CondicionTipoCambio;
                    var valorItem = new GValorItem
                    {
                        PrecioUnitario = item.dPUniProSer,
                        TipoCambioIt = item.dTiCamIt,
                        TotalBrutoItem = item.dTotBruOpeItem,
                        ValorRestaItem = new GValorRestaItem { TotalOperacionItem = item.dTotBruOpeItem },
                        MonedaOperacion = cMoneOpe,
                        EsTipoCambioGlobal = condicionTipoCambio == 1
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
                        UnidadMedida = item.cUniMed > 0 ? item.cUniMed : 77,
                        DescripcionUnidadMedida = !string.IsNullOrWhiteSpace(item.dDesUniMed) ? item.dDesUniMed : "UNI",
                        CantidadProducto = item.dCantProSer,
                        ValorItem = valorItem,
                        CamposIVA = camposIVA
                    });
                }
            }

            // Calcular totales
            documento.DE.CamposTotalesSubtotales = totales ?? (items != null && items.Any() ? Totalizador.CalcularTotalesFactura(items, dTiCam, cMoneOpe) : new GTotSub());

            // Serializar usando MemoryStream
            var serializer = new XmlSerializer(typeof(DocumentoElectronico));
            var xmlDoc = new XmlDocument { PreserveWhitespace = true };

            using (var ms = new MemoryStream())
            using (var writer = XmlWriter.Create(ms, new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(false),
                Indent = false,
                OmitXmlDeclaration = true,
                NewLineHandling = NewLineHandling.None
            }))
            {
                serializer.Serialize(writer, documento);
                writer.Flush();

                // Validar XML generado como string antes de cargar al XmlDocument
                string xmlStringGenerado = Encoding.UTF8.GetString(ms.ToArray());
                ms.Position = 0;
                xmlDoc.Load(ms);
            }

            // Preparar namespace y atributos de schema
            var root = xmlDoc.DocumentElement;
            root.Attributes.RemoveNamedItem("xmlns");
            root.Attributes.RemoveNamedItem("xmlns:xsi");
            root.Attributes.RemoveNamedItem("xmlns:xsd");
            root.Attributes.RemoveNamedItem("xsi:schemaLocation");

            root.SetAttribute("xmlns", "http://ekuatia.set.gov.py/sifen/xsd");

            XmlAttribute xmlnsXsi = xmlDoc.CreateAttribute("xmlns", "xsi", "http://www.w3.org/2000/xmlns/");
            xmlnsXsi.Value = "http://www.w3.org/2001/XMLSchema-instance";
            root.Attributes.Append(xmlnsXsi);

            XmlAttribute schemaLocation = xmlDoc.CreateAttribute("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance");
            schemaLocation.Value = "http://ekuatia.set.gov.py/sifen/xsd siRecepDE_v150.xsd";
            root.Attributes.Append(schemaLocation);

            // Firmar el XML
            if (certificadoBytes != null && !string.IsNullOrEmpty(contraseñaCertificado))
            {
                string rutaDebug = Path.Combine(Path.GetDirectoryName(rutaArchivo), "debug_pre_firma.xml");
                using (var fs = new FileStream(rutaDebug, FileMode.Create, FileAccess.Write))
                using (var writer = XmlWriter.Create(fs, new XmlWriterSettings {
                    Encoding = new UTF8Encoding(false),
                    Indent = false,
                    OmitXmlDeclaration = true,
                    NewLineHandling = NewLineHandling.None
                }))
                {
                    xmlDoc.Save(writer);
                    writer.Flush();
                }
                Console.WriteLine("XML guardado antes de firmar: " + rutaDebug);

                SifenSigner.FirmarXml(xmlDoc, cdc, dRucReceptor, certificadoBytes, contraseñaCertificado);
            }

            XmlWriterSettings settings = new XmlWriterSettings
            {
                OmitXmlDeclaration = true,
                Encoding = new UTF8Encoding(false),
                Indent = false,
                NewLineHandling = NewLineHandling.None,
                CheckCharacters = false
            };

            using (XmlWriter writer = XmlWriter.Create(rutaArchivo, settings))
            {
                xmlDoc.Save(writer);
            }

            Console.WriteLine($"XML generado exitosamente: {rutaArchivo}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error en GenerarXML.SerializarDocumentoElectronico: {ex.Message}");
            if (ex.InnerException != null)
            {
                Console.WriteLine($"Error interno: {ex.InnerException.Message}");
            }
            throw new Exception($"Error al generar el XML: {ex.Message}", ex);
        }
    }
/*
    public static string FirmarDesdePreGenerado(string dRucReceptor, byte[] certificadoBytes, string contraseñaCertificado)
    {
        try
        {
            string carpetaDebug = "XML";
            Directory.CreateDirectory(carpetaDebug);

            string rutaPreFirma = Path.Combine(carpetaDebug, "debug_pre_firma.xml");

            if (!File.Exists(rutaPreFirma))
                throw new FileNotFoundException("No se encontró el archivo debug_pre_firma.xml", rutaPreFirma);

            XmlDocument xmlDoc = new XmlDocument { PreserveWhitespace = true };
            xmlDoc.Load(rutaPreFirma);

            var nsManager = new XmlNamespaceManager(xmlDoc.NameTable);
            nsManager.AddNamespace("s", "http://ekuatia.set.gov.py/sifen/xsd");

            var deNode = xmlDoc.SelectSingleNode("//s:DE", nsManager) as XmlElement;
            if (deNode == null)
                throw new Exception("No se encontró el nodo <DE>");

            string cdc = deNode.GetAttribute("Id");
            var root = xmlDoc.DocumentElement;
            root.Attributes.RemoveNamedItem("xmlns");
            root.Attributes.RemoveNamedItem("xmlns:xsi");
            root.Attributes.RemoveNamedItem("xmlns:xsd");
            root.Attributes.RemoveNamedItem("xsi:schemaLocation");

            root.SetAttribute("xmlns", "http://ekuatia.set.gov.py/sifen/xsd");

            XmlAttribute xmlnsXsi = xmlDoc.CreateAttribute("xmlns", "xsi", "http://www.w3.org/2000/xmlns/");
            xmlnsXsi.Value = "http://www.w3.org/2001/XMLSchema-instance";
            root.Attributes.Append(xmlnsXsi);

            XmlAttribute schemaLocation = xmlDoc.CreateAttribute("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance");
            schemaLocation.Value = "http://ekuatia.set.gov.py/sifen/xsd siRecepDE_v150.xsd";
            root.Attributes.Append(schemaLocation);

            // Aplicar firma
            SifenSigner.FirmarXml(xmlDoc, cdc, dRucReceptor, certificadoBytes, contraseñaCertificado);

            // Guardar como Documento_{cdc}.xml con normalización
            string rutaFirmado = Path.Combine(carpetaDebug, $"Documento_{cdc}.xml");
            string xmlNormalizado = EnvioSifenService.NormalizarXmlFirmado(xmlDoc.OuterXml, quitarDeclaracionXml: true);
            File.WriteAllText(rutaFirmado, xmlNormalizado, new UTF8Encoding(false));

            return cdc;
        }
        catch (Exception ex)
        {
            throw new Exception("Error en FirmarDesdePreGenerado", ex);
        }
    } */
}