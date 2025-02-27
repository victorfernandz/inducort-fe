using System;
using System.Xml.Serialization;
using System.Xml;
using System.Runtime.Serialization;
using System.Text.Json.Serialization;
using System.Security;
using System.Diagnostics.Contracts;
using System.Net.NetworkInformation;

[XmlRoot("rDE", Namespace = "http://ekuatia.set.gov.py/sifen/xsd")]
public class DocumentoElectronico // (AA001-AA009)
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

    public DocumentoElectronico(string cdc, int dv, int dSisFact, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT, 
        DateTime dFeEmiDE, string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi,
        string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE, string cActEco, string dDesActEco)        
    {
        DE = new DEContent(cdc, dv, dSisFact, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, dRucEm, dDVEmi, iTipCont,
        dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE, cActEco, dDesActEco);
    }
}

public class DEContent // (A001-A099) - Nodo padre AA001
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

    [XmlElement("gDatGralOpe")]

    public GDatGralOpe CamposGenerales { get; set; }

    public DEContent() {}

    public DEContent(string cdc, int dv, int dSisFact, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT, DateTime dFeEmiDE,
        string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi,
        int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE, string cActEco, string dDesActEco)
    {
        Id = cdc;
        DigitoVerificador = dv;
        SistemaFacturacion = dSisFact;
        GrupoOperacion = new GOpeDE(dCodSeg);
        GrupoTimbrado = new GTimb(iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT);
        CamposGenerales = new GDatGralOpe(dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi,
            cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE, cActEco, dDesActEco);
    }
}

public class GOpeDE // (B001-B099) - Nodo padre A001
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

public class GTimb // (C001-C099) - Nodo padre A001
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

public class GDatGralOpe //(D001-D299) - Nodo padre A001
{
    [XmlIgnore] // Evita serializar el DateTime directamente
    public DateTime FechaHoraEmision { get; set; }

    [XmlElement("dFeEmiDE")]
    public string FechaHoraEmisionString
    {
        get => FechaHoraEmision.ToString("yyyy-MM-ddTHH:mm:ss");
        set => FechaHoraEmision = DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss", null);
    }

    [XmlElement("gOpeCom")]
    public GOpeCom OperacionComercial { get; set; }

    [XmlElement("gEmis")]
    public GEmis GrupoCamposEmisor { get; set; }

    // Cambiamos de una única actividad a una lista
    [XmlElement("gActEco")]
    public List<GActEco> ActividadesEconomicas { get; set; } = new List<GActEco>();

    public GDatGralOpe(){}

    // Constructor para una sola actividad económica
    public GDatGralOpe(DateTime dFeEmiDE, string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi,
        int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE, string cActEco, 
        string dDesActEco, List<ObligacionAfectada> obligaciones = null)
    {
        FechaHoraEmision = dFeEmiDE;
        OperacionComercial = new GOpeCom(iTipTra, cMoneOpe, dDesMoneOpe, obligaciones);
        GrupoCamposEmisor = new GEmis(dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE);
        // Añadir la única actividad económica
        ActividadesEconomicas.Add(new GActEco(cActEco, dDesActEco));
    }
    
    // Constructor para múltiples actividades económicas
    public GDatGralOpe(DateTime dFeEmiDE, string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi,
        int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE, 
        List<ActividadEconomica> actividades, List<ObligacionAfectada> obligaciones = null)
    {
        FechaHoraEmision = dFeEmiDE;
        OperacionComercial = new GOpeCom(iTipTra, cMoneOpe, dDesMoneOpe, obligaciones);
        GrupoCamposEmisor = new GEmis(dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE);
        
        // Añadir todas las actividades económicas
        foreach (var actividad in actividades)
        {
            ActividadesEconomicas.Add(new GActEco(actividad.Codigo, actividad.Descripcion));
        }
    }
}

public class GOpeCom // (D010-D099) - Nodo padre D001
{
    [XmlElement("iTipTra", Order = 1)]
    public string TipoTransaccion { get; set; }

    [XmlElement("dDesTipTra", Order = 2)]
    public string DescripcionTipoTransaccion { get; set; }

    [XmlElement("iTImp", Order = 3)]
    public int TipoImpuesto = 5;

    [XmlElement("dDesTImp", Order = 4)]
    public string DescripTipoImp = "IVA - Renta";

    [XmlElement("cMoneOpe", Order = 5)]
    public string MonedaOperacion { get; set; }

    [XmlElement("dDesMoneOpe", Order = 6)]
    public string DescripcionMoneda { get; set; }

    [XmlElement("dCondTiCam", Order = 7)]
    public int CondicionTipoCambio { get; set; } = 1;

    [XmlElement("gOblAfe", Order = 8)]
    public List<GOblAfe> ObligacionesAfectadas { get; set; } = new List<GOblAfe>();

    public GOpeCom(){}

    public GOpeCom(string iTipTra, string cMoneOpe, string dDesMoneOpe, List<ObligacionAfectada> obligaciones = null)
    {
        TipoTransaccion = iTipTra;
        DescripcionTipoTransaccion = ObtenerDescripTipoTran(iTipTra);
    //    MonedaOperacion = cMoneOpe;
        MonedaOperacion = cMoneOpe;
        DescripcionMoneda = dDesMoneOpe;

        // Agregar obligaciones afectadas solo si la lista no es nula y tiene elementos
        if (obligaciones != null && obligaciones.Any())
        {
            foreach (var obligacion in obligaciones)
            {
                ObligacionesAfectadas.Add(new GOblAfe(obligacion.Codigo, obligacion.Descripcion));
            }
        }
    }

    public bool ShouldSerializeCondicionTipoCambio()
    {
        return MonedaOperacion != "PYG";
    }

    private string ObtenerDescripTipoTran(string iTipTra)
    {
        return iTipTra switch
        {
            "1" => "Venta de mercadería",
            "2" => "Prestación de servicios",
            "3" => "Mixto (Venta de mercadería y servicios)",
            "4" => "Venta de activo fijo",
            "5" => "Venta de divisas",
            "6" => "Compra de divisas",
            "7" => "Promoción o entrega de muestras",
            "8" => "Donación",
            "9" => "Anticipo",
            "10" => "Compra de productos",
            "11" => "Compra de servicios",
            "12" => "Venta de crédito fiscal"
        };
    }
};

public class GEmis //(D100-D129) - Nodo padre D001
{
    [XmlElement("dRucEm", Order = 1)]
    public string RucEmisor { get; set; }

    [XmlElement("dDVEmi", Order = 2)]
    public int DigVeriEmisor { get; set; }

    [XmlElement("iTipCont", Order = 3)]
    public int TipoContri { get; set; }

    [XmlElement("dNomEmi", Order = 4)]
    public string NombreEmisor { get; set; }

    [XmlElement("dDirEmi", Order = 5)]
    public string DireccionEmisor { get; set; }

    [XmlElement("dNumCas", Order = 6)]
    public int NumeroCasaE { get; set; }

    [XmlElement("cDepEmi", Order = 7)]
    public int CodDepartEmisor { get; set; }

    [XmlElement("dDesDepEmi", Order = 8)]
    public string DesDepEmisor { get; set; }

    [XmlElement("cDisEmi", Order = 9)]
    public int CodDistEmisor { get; set; }

    [XmlElement("dDesDisEmi", Order = 10)]
    public string DesDistEmisor { get; set;} 

    [XmlElement("cCiuEmi", Order = 11)]
    public int CodCiudEmisor { get; set; }

    [XmlElement("dDesCiuEmi", Order = 12)]
    public string DesCiuEmisor { get; set; }

    [XmlElement("dTelEmi", Order = 13)]
    public string TelefEmisor { get; set; }

    [XmlElement("dEmailE", Order = 14)]
    public string EmailEmisor { get; set; }

    public GEmis(){}

    public GEmis(string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi,
        int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE)
    {
        RucEmisor = dRucEm;
        DigVeriEmisor = dDVEmi;
        TipoContri = iTipCont;
        NombreEmisor = dNomEmi;
        DireccionEmisor = dDirEmi;
        NumeroCasaE = dNumCas;
        CodDepartEmisor = cDepEmi;
        DesDepEmisor = dDesDepEmi;
        CodDistEmisor = cDisEmi;
        DesDistEmisor = dDesDisEmi;
        CodCiudEmisor = cCiuEmi;
        DesCiuEmisor = dDesCiuEmi;
        TelefEmisor = dTelEmi;
        EmailEmisor = dEmailE;
    }
}

public class GActEco // (D130-D139) - Nodo padre D100
{
    [XmlElement("cActEco")]
    public string CodActiEconomica { get; set; }

    [XmlElement("dDesActEco")]
    public string DescActiEconomica { get; set;}

    public GActEco (){}

    public GActEco (string cActEco, string dDesActEco)
    {
        CodActiEconomica = cActEco;
        DescActiEconomica = dDesActEco;
    }
}

public class GOblAfe // (D030-D040) - Nodo padre D010
{
    [XmlElement("cOblAfe")]
    public int CodObligacionAfectada { get; set; }

    [XmlElement("dDesOblAfe")]
    public string DescObligacionAfectada { get; set;}

    public GOblAfe() {}

    public GOblAfe(int cOblAfe, string dDesOblAfe)
    {
        CodObligacionAfectada = cOblAfe;
        DescObligacionAfectada = dDesOblAfe;
    }
}