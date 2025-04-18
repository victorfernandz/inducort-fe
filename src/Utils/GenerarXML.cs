using System;
using System.IO;
using System.Text;
using System.Xml;
using System.Xml.Serialization;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Globalization;

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
            CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
            CultureInfo.CurrentUICulture = CultureInfo.InvariantCulture;

            var actividadPrincipal = actividades.First();
            DocumentoElectronico documento = new DocumentoElectronico(cdc, dv, dFecFirma, 1, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, 
                dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE, actividadPrincipal.Codigo, actividadPrincipal.Descripcion, iNatRec, iTiContRec, iTiOpe, cPaisRec, dDesPaisRe, dNomRec, dRucReceptor,
                dDVReceptor, dTiCam, iIndPres, iCondOpe, iCondCred);

            if (actividades.Count > 1)
            {
                for (int i = 1; i < actividades.Count; i++)
                {
                    documento.DE.CamposGenerales.GrupoCamposEmisor.ActividadesEconomicas.Add(
                        new GActEco(actividades[i].Codigo, actividades[i].Descripcion));
                }
            }

            if (obligaciones != null && obligaciones.Any())
            {
                foreach (var obligacion in obligaciones)
                {
                    documento.DE.CamposGenerales.OperacionComercial.ObligacionesAfectadas.Add(
                        new GOblAfe(obligacion.Codigo, obligacion.Descripcion));
                }
            }

            if (iCondOpe == 2)
            {
                if (iCondCred == 1)
                {
                    string plazoFinal = plazoCredito;
                    if (string.IsNullOrEmpty(plazoFinal))
                    {
                        plazoFinal = "30 días";
                        if (cuotas != null && cuotas.Count > 0 && !string.IsNullOrEmpty(cuotas[0].FechaVencimientoCuota))
                        {
                            plazoFinal = cuotas[0].FechaVencimientoCuota;
                        }
                    }
                    documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito = new GPagCred(1, plazoFinal, null);
                }
                else if (iCondCred == 2)
                {
                    int cantidadCuotas = cuotas?.Count ?? 0;
                    documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito = new GPagCred(2, null, cantidadCuotas);
                    if (cuotas != null && cuotas.Any())
                    {
                        foreach (var cuota in cuotas)
                        {
                            documento.DE.CamposEspecificosTipoDocumento.CondicionOperacion.OperacionCredito.Cuotas.Add(cuota);
                        }
                    }
                }
            }

            if (items != null && items.Any())
            {
                documento.DE.CamposEspecificosTipoDocumento.Items.Clear();
                foreach (var item in items)
                {
                    decimal? totalGs = item.dTiCamIt > 0 ? item.dTotBruOpeItem * item.dTiCamIt : null;
                    var valorItem = new GValorItem
                    {
                        PrecioUnitario = item.dPUniProSer,
                        TipoCambioIt = item.dTiCamIt,
                        TotalBrutoItem = item.dTotBruOpeItem,
                        ValorRestaItem = new GValorRestaItem
                        {
                            TotalOperacionItem = item.dTotBruOpeItem,
                            TotalOperacionGs = totalGs
                        },
                        MonedaOperacion = cMoneOpe
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

            documento.DE.CamposTotalesSubtotales = totales ?? (items != null && items.Any()
                ? Totalizador.CalcularTotalesFactura(items, dTiCam, cMoneOpe)
                : new GTotSub());

            var stringWriter = new StringWriter();
            var serializer = new XmlSerializer(typeof(DocumentoElectronico));
            var emptyNs = new XmlSerializerNamespaces();
            emptyNs.Add("", "");
            serializer.Serialize(stringWriter, documento, emptyNs);

            var xmlDoc = new XmlDocument();
            xmlDoc.LoadXml(stringWriter.ToString());

            if (certificadoBytes != null && !string.IsNullOrEmpty(contraseñaCertificado))
            {
                FirmaDigitalBC.FirmarXml(xmlDoc, cdc, certificadoBytes, contraseñaCertificado);

                try
                {
                    XmlNode nodoDE = xmlDoc.GetElementsByTagName("DE")[0];
                    XmlNode nodoPadreDE = nodoDE?.ParentNode;

                    XmlElement nodoGrupoJ = xmlDoc.CreateElement("gCamFuFD", xmlDoc.DocumentElement.NamespaceURI);
                    var config = Config.LoadConfig();
                    string idCSC = config.Sifen.IdCSC;
                    string csc = config.Sifen.CSC;

                    string fechaISO = dFeEmiDE.ToString("yyyy-MM-ddTHH:mm:ss");
                    string fechaHex = BitConverter.ToString(Encoding.UTF8.GetBytes(fechaISO)).Replace("-", "").ToLower();
                    string totalGral = xmlDoc.GetElementsByTagName("dTotGralOpe")[0].InnerText;
                    string totalIVA = xmlDoc.GetElementsByTagName("dTotIVA")[0].InnerText;
                    string cantidadItems = documento.DE.CamposEspecificosTipoDocumento.Items.Count.ToString();

                    string digestValue = "";
                    var digestNodes = xmlDoc.GetElementsByTagName("DigestValue", "http://www.w3.org/2000/09/xmldsig#");
                    if (digestNodes.Count > 0)
                    {
                        digestValue = digestNodes[0]?.InnerText ?? "";
                    }

                    string digestHex = Base64ToHex(digestValue);

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

                    string cadenaParaHash = cadenaVisibleQR + csc;

                    string cHashQR;
                    using (var sha256 = SHA256.Create())
                    {
                        var bytesQR = Encoding.UTF8.GetBytes(cadenaParaHash);
                        var hash = sha256.ComputeHash(bytesQR);
                        cHashQR = BitConverter.ToString(hash).Replace("-", "").ToLower();
                    }

                    string urlQR = $"https://ekuatia.set.gov.py/consultas/qr?{cadenaVisibleQR}&cHashQR={cHashQR}";

                    XmlElement dCarQR = xmlDoc.CreateElement("dCarQR", xmlDoc.DocumentElement.NamespaceURI);
                    dCarQR.InnerText = urlQR;
                    nodoGrupoJ.AppendChild(dCarQR);

                    if (nodoPadreDE != null)
                    {
                        nodoPadreDE.AppendChild(nodoGrupoJ);
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("Error al construir nodo gCamFuFD: " + ex.Message);
                }
            }

            var root = xmlDoc.DocumentElement;
            root.RemoveAllAttributes();

            var xmlns = xmlDoc.CreateAttribute("xmlns");
            xmlns.Value = "http://ekuatia.set.gov.py/sifen/xsd";
            root.Attributes.Append(xmlns);

            var xmlnsXsi = xmlDoc.CreateAttribute("xmlns", "xsi", "http://www.w3.org/2000/xmlns/");
            xmlnsXsi.Value = "http://www.w3.org/2001/XMLSchema-instance";
            root.Attributes.Append(xmlnsXsi);

            var schemaLocation = xmlDoc.CreateAttribute("xsi", "schemaLocation", "http://www.w3.org/2001/XMLSchema-instance");
            schemaLocation.Value = "http://ekuatia.set.gov.py/sifen/xsd siRecepDE_v150.xsd";
            root.Attributes.Append(schemaLocation);

            Directory.CreateDirectory(Path.GetDirectoryName(rutaArchivo));
            XmlWriterSettings settings = new XmlWriterSettings
            {
                Encoding = new UTF8Encoding(true),
                Indent = true
            };

            // Guardar directamente el XmlDocument con el nodo raíz "rDE"
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

    public static string Base64ToHex(string base64)
    {
        byte[] bytes = Convert.FromBase64String(base64);
        return BitConverter.ToString(bytes).Replace("-", "").ToLower();
    }
}
