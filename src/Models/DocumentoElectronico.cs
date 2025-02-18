using System;
using System.Xml.Serialization;
using System.Xml;

[XmlRoot("rDE", Namespace = "http://ekuatia.set.gov.py/sifen/xsd")]
public class DocumentoElectronico
{
    public DocumentoElectronico()
    {
        Xmlns = new XmlSerializerNamespaces();
        Xmlns.Add("", "http://ekuatia.set.gov.py/sifen/xsd");
        Xmlns.Add("xsi", "http://www.w3.org/2001/XMLSchema-instance");
    }

    [XmlNamespaceDeclarations]
    public XmlSerializerNamespaces Xmlns;

    [XmlElement("dVerFor")]
    public string VersionFormato { get; set; } = "150";

    [XmlElement("DE")]
    public DEContent DE { get; set; }

    public DocumentoElectronico(string cdc, int dv, int dSisFact, string dCodSeg, string iTiDE, 
        int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT)
    {
        DE = new DEContent(cdc, dv, dSisFact, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT);
    }
}

public class DEContent
{
    [XmlAttribute("Id")]
    public string Id { get; set; } // El Id es el CDC según el manual técnico

    [XmlElement("dDVId")]
    public int DigitoVerificador { get; set; }

    [XmlElement("dSisFact")]
    public int SistemaFacturacion { get; set; } 

    [XmlElement("gOpeDE")]
    public GOpeDE GrupoOperacion { get; set; }

    [XmlElement("gTimb")]
    public GTimb GrupoTimbrado { get; set; }

    public DEContent() {}

    public DEContent(string cdc, int dv, int dSisFact, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, 
        string dNumDoc, DateTime dFeIniT)
    {
        Id = cdc;
        DigitoVerificador = dv;
        SistemaFacturacion = dSisFact;
        GrupoOperacion = new GOpeDE(dCodSeg);
        GrupoTimbrado = new GTimb(iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT);
    }
}

public class GOpeDE
{
    [XmlElement("iTipEmi")]
    public int TipoEmision { get; set; } = 1;

    [XmlElement("dDesTipEmi")]
    public string DescripcionTipoEmision { get; set; } = "Normal";

    [XmlElement("dCodSeg")]
    public string CodigoSeguridad { get; set; }

    public GOpeDE() {}

    public GOpeDE(string dCodSeg)
    {
        CodigoSeguridad = dCodSeg;
    }
}

public class GTimb
{
    [XmlElement("iTiDE")]
    public string TipoDocumento { get; set; }

    [XmlElement("dDesTiDE")]
    public string DescripcionTipoDocumento { get; set; }

    [XmlElement("dNumTim")]
    public int NumeroTimbrado { get; set; }

    [XmlElement("dEst")]
    public string Establecimiento { get; set; }

    [XmlElement("dPunExp")]
    public string PuntoDeExpedicion { get; set; }

    [XmlElement("dNumDoc")]
    public string NumeroDocumento { get; set; }

    [XmlIgnore] // Evita serializar el DateTime directamente
    public DateTime FechaInicioTimbrado { get; set; }

    [XmlElement("dFeIniT")]
    public string FechaInicioTimbradoString
    {
        get => FechaInicioTimbrado.ToString("yyyy-MM-dd"); // Formato correcto para XML
        set => FechaInicioTimbrado = DateTime.ParseExact(value, "yyyy-MM-dd", null);
    }

    public GTimb() {}

    public GTimb(string iTiDE, int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT)
    {
        TipoDocumento = iTiDE;
        DescripcionTipoDocumento = ObtenerDescripcionTipoDocumento(iTiDE);
        NumeroTimbrado = dNumTim;
        Establecimiento = dEst;
        PuntoDeExpedicion = dPunExp;
        NumeroDocumento = dNumDoc;
        FechaInicioTimbrado = dFeIniT;
    }

    private string ObtenerDescripcionTipoDocumento(string iTiDE)
    {
        return iTiDE switch
        {
            "01" => "Factura electrónica",
            "02" => "Factura electrónica de exportación",
            "03" => "Factura electrónica de importación",
            "04" => "Autofactura electrónica",
            "05" => "Nota de crédito electrónica",
            "06" => "Nota de débito electrónica",
            "07" => "Nota de remisión electrónica",
            "08" => "Comprobante de retención electrónica"
        };
    }
}