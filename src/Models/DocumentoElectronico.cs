using System;
using System.Xml.Serialization;
using System.Xml;
using System.Globalization;

[XmlRoot("rDE", Namespace = "http://ekuatia.set.gov.py/sifen/xsd")]

// Campos que identifican el formato electrónico XML (AA001-AA009)
public class DocumentoElectronico // Nodo Padre AA001
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

    [XmlElement("gCamFuFD")]
    public gCamFuFD gCamFuFD { get; set; }

    public DocumentoElectronico(string cdc, int dv, DateTime dFecFirma, int dSisFact, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT, DateTime dFeEmiDE, string iTipTra, string cMoneOpe,
        string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, 
        string dEmailE, string cActEco, string dDesActEco, int iNatRec, int iTiContRec, int iTiOpe, string cPaisRec, string dDesPaisRe, string dNomRec, string dRucRec, int dDVRec, decimal dTiCam, int iIndPres, int iCondOpe, int iCondCred) 
   
    {
        DE = new DEContent(cdc, dv, dFecFirma, dSisFact, dCodSeg, iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT, dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, 
            cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE, cActEco, dDesActEco, iNatRec, iTiContRec, iTiOpe, cPaisRec, dDesPaisRe, dNomRec, dRucRec, dDVRec, dTiCam, iIndPres, iCondOpe, iCondCred);

    }
}

// Campos firmados del Documento Electrónico (A001-A099)
public class DEContent // Nodo padre AA001
{
    [XmlAttribute("Id")]
    public string Id { get; set; } // El Id es el CDC según el manual técnico

    [XmlElement("dDVId")]
    public int DigitoVerificador { get; set; }

    [XmlIgnore]
    public DateTime FechaFirma { get; set; }

    [XmlElement("dFecFirma")]
    public string FechaFirmaString
    {
        get => FechaFirma.ToString("yyyy-MM-ddTHH:mm:ss"); // Formato correcto para XML
        set => FechaFirma = DateTime.ParseExact(value, "yyyy-MM-ddTHH:mm:ss", null);
    }

    [XmlElement("dSisFact")]
    public int SistemaFacturacion { get; set; } 

    [XmlElement("gOpeDE")]
    public GOpeDE GrupoOperacion { get; set; }

    [XmlElement("gTimb")]
    public GTimb GrupoTimbrado { get; set; }

    [XmlElement("gDatGralOpe")]

    public GDatGralOpe CamposGenerales { get; set; }

    [XmlElement("gDtipDE")]
    public GDtipDE CamposEspecificosTipoDocumento { get; set; }

    [XmlElement("gTotSub")]
    public GTotSub CamposTotalesSubtotales { get; set; }

    public DEContent() {}

    public DEContent(string cdc, int dv, DateTime dFecFirma, int dSisFact, string dCodSeg, string iTiDE, int dNumTim, string dEst, string dPunExp, string dNumDoc, DateTime dFeIniT, DateTime dFeEmiDE, string iTipTra, string cMoneOpe, string dDesMoneOpe,
        string dRucEm,int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi, string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE,
        string cActEco, string dDesActEco, int iNatRec, int iTiContRec, int iTiOpe, string cPaisRec, string dDesPaisRe, string dNomRec, string dRucRec, int dDVRec, decimal dTiCam, int iIndPres, int iCondOpe, int iCondCred)

    {
        Id = cdc;
        DigitoVerificador = dv;
        FechaFirma = dFecFirma;
        SistemaFacturacion = dSisFact;
        GrupoOperacion = new GOpeDE(dCodSeg);
        GrupoTimbrado = new GTimb(iTiDE, dNumTim, dEst, dPunExp, dNumDoc, dFeIniT);
            
        CamposGenerales = new GDatGralOpe(dFeEmiDE, iTipTra, cMoneOpe, dDesMoneOpe, dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE,
            iNatRec, iTiContRec, iTiOpe, cPaisRec, dDesPaisRe, dNomRec, dRucRec, dDVRec, dTiCam);
            
        // Agregar la actividad económica si se proporcionó
        if (!string.IsNullOrEmpty(cActEco))
        {
            CamposGenerales.GrupoCamposEmisor.ActividadesEconomicas.Add(new GActEco(cActEco, dDesActEco));
        }

        CamposEspecificosTipoDocumento = new GDtipDE(iIndPres, iCondOpe, iCondCred);
    }
}

// Campos inherentes a la operación de Documentos Electrónicos (B001-B099)
public class GOpeDE // Nodo padre A001
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

// Campos de datos del Timbrado (C001-C099)
public class GTimb //  Nodo padre A001
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
            "1" => "Factura electrónica",
            "2" => "Factura electrónica de exportación",
            "3" => "Factura electrónica de importación",
            "4" => "Autofactura electrónica",
            "5" => "Nota de crédito electrónica",
            "6" => "Nota de débito electrónica",
            "7" => "Nota de remisión electrónica",
            "8" => "Comprobante de retención electrónica"
        };
    }
}

// Campos Generales del Documento Electrónico DE (D001-D299)
public class GDatGralOpe // Nodo padre A001
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

 /*   [XmlElement("gActEco")]
    
    public List<GActEco> ActividadesEconomicas { get; set; } = new List<GActEco>(); */

    [XmlElement("gDatRec")]
    public GDatRec GrupoDatosReceptor { get; set; }

    public GDatGralOpe(){}

    // Constructor para una sola actividad económica
    public GDatGralOpe(DateTime dFeEmiDE, string iTipTra, string cMoneOpe, string dDesMoneOpe, string dRucEm, int dDVEmi, int iTipCont, string dNomEmi, string dDirEmi, int dNumCas, int cDepEmi, string dDesDepEmi, int cDisEmi,
        string dDesDisEmi, int cCiuEmi, string dDesCiuEmi, string dTelEmi, string dEmailE, int iNatRec, int iTiContRec, int iTiOpe, string cPaisRec, string dDesPaisRe, string dNomRec, string dRucRec, int dDVRec, decimal dTiCam,
        List<ActividadEconomica> actividades = null, List<ObligacionAfectada> obligaciones = null)
    {
        FechaHoraEmision = dFeEmiDE;
        OperacionComercial = new GOpeCom(iTipTra, cMoneOpe, dDesMoneOpe, dTiCam, obligaciones);
        GrupoCamposEmisor = new GEmis(dRucEm, dDVEmi, iTipCont, dNomEmi, dDirEmi, dNumCas, cDepEmi, dDesDepEmi, cDisEmi, dDesDisEmi, cCiuEmi, dDesCiuEmi, dTelEmi, dEmailE);
        
        if (actividades != null && actividades.Any())
        {
            foreach (var actividad in actividades)
            {
                GrupoCamposEmisor.ActividadesEconomicas.Add(new GActEco(actividad.Codigo, actividad.Descripcion));
            }
        }

        GrupoDatosReceptor = new GDatRec(iNatRec, iTiContRec, iTiOpe, cPaisRec, dDesPaisRe, dNomRec, dRucRec, dDVRec);
    }
}

// Campos inherentes a la operación comercial (D010-D099)
public class GOpeCom // Nodo padre D001
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

    [XmlElement("dTiCam", Order = 8)]
    public decimal TipoCambio { get; set; }

    [XmlElement("gOblAfe", Order = 9)]
    public List<GOblAfe> ObligacionesAfectadas { get; set; } = new List<GOblAfe>();

    public GOpeCom(){}

    public GOpeCom(string iTipTra, string cMoneOpe, string dDesMoneOpe, decimal dCondTiCam, List<ObligacionAfectada> obligaciones = null)
    {
        TipoTransaccion = iTipTra;
        DescripcionTipoTransaccion = ObtenerDescripTipoTran(iTipTra);
        MonedaOperacion = cMoneOpe;
        DescripcionMoneda = dDesMoneOpe;
        TipoCambio = dCondTiCam;

        // Agregar obligaciones afectadas solo si la lista no es nula y tiene elementos
        if (obligaciones != null && obligaciones.Any())
        {
            foreach (var obligacion in obligaciones)
            {
                ObligacionesAfectadas.Add(new GOblAfe(obligacion.Codigo, obligacion.Descripcion));
            }
        }
    }

    public bool ShouldSerializeTipoCambio()
    {
        return TipoCambio > 1;
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

// Campos que identifican al emisor del Documento Electrónico DE (D100-D129)
public class GEmis // Nodo padre D001
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

    // Agregar la lista de actividades económicas
    [XmlElement("gActEco", Order = 15)]
    public List<GActEco> ActividadesEconomicas { get; set; } = new List<GActEco>();

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
        ActividadesEconomicas = new List<GActEco>();
    }
}

// Campos que describen la actividad económica del emisor (D130-D139)
public class GActEco // Nodo padre D100
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

// Campos que identifican al receptor del Documento Electrónico DE (D200-D299)
public class GDatRec // Nodo padre D001
{
    [XmlElement("iNatRec")]
    public int NaturalezaReceptor { get; set; }

    [XmlElement("iTiOpe")]
    public int TipoOperacion { get; set; }

    [XmlElement("cPaisRec")]
    public string PaisReceptor { get; set; }

    [XmlElement("dDesPaisRe")]
    public string DescPaisReceptor { get; set; }

    [XmlElement("iTiContRec")]
    public int TipoContReceptor { get; set; }

    [XmlElement("dRucRec")]
    public string RucReceptor { get; set; }

    [XmlElement("dDVRec")]
    public int DVReceptor { get; set; }

    [XmlElement("dNomRec")]
    public string NombreReceptor { get; set; }

    public GDatRec(){}

    public GDatRec (int iNatRec, int iTiContRec, int iTiOpe, string cPaisRec, string dDesPaisRe, string dNomRec, string dRucRec, int dDVRec)
    {
        NaturalezaReceptor = iNatRec;
        TipoOperacion = iTiOpe;
        PaisReceptor = cPaisRec;
        DescPaisReceptor = dDesPaisRe;
        TipoContReceptor = iTiContRec;	
        RucReceptor = dRucRec;
        DVReceptor = dDVRec;
        NombreReceptor = dNomRec;
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

// Campos específicos por tipo de Documento Electrónico (E001-E009)
public class GDtipDE // Nodo padre A001
{
    [XmlElement("gCamFE")]
    public GCamFE CamposFacturaElectronica { get; set; }

    [XmlElement("gCamCond")]
    public GCamCond CondicionOperacion { get; set; }

    [XmlElement("gCamItem")]
    public List<GCamItem> Items { get; set; } = new List<GCamItem>();

    public GDtipDE() {}

    public GDtipDE(int iIndPres, int iCondOpe, int iCondCred) 
    {
        CamposFacturaElectronica = new GCamFE(iIndPres);
        CondicionOperacion = new GCamCond(iCondOpe, iCondCred);
        Items = new List<GCamItem>();
    }
}

// Campos que componen la Factura Electrónica FE (E010-E099)
public class GCamFE // Nodo padre E001
{
    [XmlElement("iIndPres")]
    public int IndicadorPresencia { get; set; }

    [XmlElement("dDesIndPres")]
    public string DescripcionIndicadorPresencia { get; set; }

    public GCamFE() {}

    public GCamFE(int iIndPres)
    {
        IndicadorPresencia = iIndPres;
        DescripcionIndicadorPresencia = ObtenerDescripcionIndicadorPresencia(iIndPres);
    }

    private string ObtenerDescripcionIndicadorPresencia(int iIndPres)
    {
        return iIndPres switch
        {
            1 => "Operación presencial",
            2 => "Operación electrónica",
            3 => "Operación telemarketing",
            4 => "Venta a domicilio",
            5 => "Operación bancaria",
            6 => "Operación cíclica",
            9 => "Otro"
        };
    }
}

// Campos que describen la condición de la operación (E600-E699)
public class GCamCond // Nodo padre E001
{
    [XmlElement("iCondOpe")]
    public int CondicionOperacion { get; set; }

    [XmlElement("dDCondOpe")]
    public string DescCondicionOperacion { get; set;}

    [XmlElement("gPagCred")]
    public GPagCred OperacionCredito { get; set; }

    public GCamCond(){}

    public GCamCond(int iCondOpe, int? iCondCred = null, string dPlazoCre = null, int? dCuotas = null)
    {
        CondicionOperacion = iCondOpe;
        DescCondicionOperacion = ObtenerDescCondOperacion(iCondOpe);

        // Si la condición es "Crédito" y se proporcionan datos adicionales, inicializar gPagCred
        if (iCondOpe == 2)
        {
            if (iCondCred.HasValue)
            {
                OperacionCredito = new GPagCred(iCondCred.Value, dPlazoCre, dCuotas);
            }
            else
            {
                OperacionCredito = new GPagCred();
            }
        }
    }

    private string ObtenerDescCondOperacion(int iCondOpe)
    {
        return iCondOpe switch
        {
            1 => "Contado",
            2 => "Crédito"
        };
    }

    // Método para controlar si se serializa gPagCred
    public bool ShouldSerializeOperacionCredito()
    {
        return CondicionOperacion == 2; // Solo serializar si es crédito
    }
}

// Campos que describen la operación a crédito (E640-E649)
public class GPagCred // Nodo padre E600
{
    [XmlElement("iCondCred")]
    public int CondicionCredito { get; set; }

    [XmlElement("dDCondCred")]
    public string DescripcionCondicionCredito {get; set;}

    [XmlElement("dPlazoCre")]
    public string PlazoCredito { get; set; }

    [XmlElement("dCuotas")]
    public int? CantidadCuotas { get; set; }

    [XmlElement("gCuotas")]
    public List<GCuotas> Cuotas { get; set; }

    public GPagCred()
    {
        Cuotas = new List<GCuotas>();
    }

    public GPagCred(int iCondCred, string dPlazoCre, int? dCuotas = null)
    {
        CondicionCredito = iCondCred;
        DescripcionCondicionCredito = ObtenerDescripcionCondicionCredito(iCondCred);
        Cuotas = new List<GCuotas>();

        // Asignar valores según la condición de crédito
        if (iCondCred == 1) // Plazo
        {
            PlazoCredito = dPlazoCre;
        }
        else if (iCondCred == 2) // Cuota
        {
            CantidadCuotas = dCuotas;
        }
    }

    private string ObtenerDescripcionCondicionCredito(int iCondCred)
    {
        return iCondCred switch
        {
            1 => "Plazo",
            2 => "Cuota"
        };
    }

    // Métodos para controlar si se serializan campos opcionales
    public bool ShouldSerializePlazoCredito()
    {
        return CondicionCredito == 1 && !string.IsNullOrEmpty(PlazoCredito);
    }
    
    public bool ShouldSerializeCantidadCuotas()
    {
        return CondicionCredito == 2 && CantidadCuotas.HasValue;
    }

    public bool ShouldSerializeCuotas()
    {
        return Cuotas != null && Cuotas.Count > 0;
    }
}

// Campos que describen las cuotas (E650-E659)
public class GCuotas // Nodo padre E640
{
    [XmlElement("cMoneCuo")]
    public string MonedaCuota { get; set; }

    [XmlElement("dDMoneCuo")]
    public string DescripcionMonedaCuota { get; set; }

    [XmlElement("dMonCuota")]
    public decimal MontoCuota { get; set; }

    [XmlElement("dVencCuo")]
    public string FechaVencimientoCuota { get; set; }

    public GCuotas() { }

    public GCuotas(string cMoneCuo, string dDMoneCuo, decimal dMonCuota, DateTime dVencCuo)
    {
        MonedaCuota = cMoneCuo;
        DescripcionMonedaCuota = dDMoneCuo;
        MontoCuota = dMonCuota;
        FechaVencimientoCuota = dVencCuo.ToString("yyyy-MM-dd");
    }
}

// Campos que describen los ítems de la operación (E700-E899)
public class GCamItem // Nodo Padre E001
{
    [XmlElement("dCodInt")]
    public string CodigoItem { get; set; }
/*
    [XmlElement("dParAranc")]
    public int ParteArancelaria { get; set; } = 1111;
*/
    [XmlElement("dDesProSer")]
    public string DescripcionItem { get; set; }

    [XmlElement("cUniMed")]
    public int UnidadMedida { get; set; }

    [XmlElement("dDesUniMed")]
    public string DescripcionUnidadMedida { get; set; }

    [XmlIgnore]
    public decimal CantidadProducto { get; set; }

    [XmlElement("dCantProSer")]
    public string CantidadProductoString
    {
        get => CantidadProducto.ToString("F4", CultureInfo.InvariantCulture);
        set => CantidadProducto = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlElement("gValorItem")]
    public GValorItem ValorItem { get; set; }

    [XmlElement("gCamIVA")]
    public GCamIVA CamposIVA { get; set; }

    public GCamItem()
    {
        ValorItem = new GValorItem();
        CamposIVA = new GCamIVA();
    }
}

// Campos que describen el precio, tipo de cambio y valor total de la operación por ítem (E720-E729)
public class GValorItem // Nodo Padre E700dCodInt
{
    [XmlIgnore]
    public decimal PrecioUnitario { get; set; }

    [XmlElement("dPUniProSer")]
    public string PrecioUnitarioString
    {
        get => PrecioUnitario.ToString("F8", CultureInfo.InvariantCulture);
        set => PrecioUnitario = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlElement("dTiCamIt")]
    public decimal TipoCambioIt { get; set; }

    [XmlIgnore]
    public string MonedaOperacion { get; set; }

    public bool ShouldSerializeTipoCambioIt()
    {
        return MonedaOperacion != "PYG";
    }
    
    [XmlIgnore]
    public decimal TotalBrutoItem { get; set; }

    [XmlElement("dTotBruOpeItem")]
    public string TotalBrutoItemString
    {
        get => TotalBrutoItem.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalBrutoItem = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlElement("gValorRestaItem")]
    public GValorRestaItem ValorRestaItem { get; set; }
    
    public GValorItem()
    {
        ValorRestaItem = new GValorRestaItem();
    }
}

// E8.1.1 Campos que describen los descuentos, anticipos y valor total por ítem (EA001-EA050)
public class GValorRestaItem // Nodo Padre E720
{
    [XmlElement("dDescItem")]
    public decimal DescuentoItem { get; set; } = 0;
    
    [XmlElement("dPorcDesIt")]
    public decimal PorcentajeDescuentoItem { get; set; } = 0;
    
    [XmlElement("dDescGloItem")]
    public decimal DescuentoGlobalItem { get; set; } = 0;
    
    [XmlElement("dAntPreUniIt")] 
    public decimal AnticipoPreUnitarioItem { get; set; } = 0;
    
    [XmlElement("dAntGloPreUniIt")]
    public decimal AnticipoGlobalPreUnitarioItem { get; set; } = 0;
    
    [XmlIgnore]
    public decimal TotalOperacionItem { get; set; }

    [XmlElement("dTotOpeItem")]
    public string TotalOperacionItemStr
    {
        get => TotalOperacionItem.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalOperacionItem = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal? TotalOperacionGs { get; set; }

    [XmlElement("dTotOpeGs")]
    public string TotalOperacionGsStr
    {
        get => TotalOperacionGs?.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalOperacionGs = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public bool ShouldSerializeTotalOperacionGsStr() => TotalOperacionGs.HasValue;

    public GValorRestaItem(){}

}

// Campos que describen el IVA de la operación por ítem (E730-E739)
public class GCamIVA // Nodo Padre E730
{
    [XmlElement("iAfecIVA")]
    public int AfectacionIVA { get; set; }

    [XmlElement("dDesAfecIVA")]
    public string DescripcionAfectacionIVA {get; set; }

    [XmlElement("dPropIVA")]
    public int ProporcionIVA { get; set; }

    [XmlElement("dTasaIVA")]
    public int TasaIVA { get; set; }

    [XmlIgnore]
    public decimal BaseGravadaIVA { get; set; }

    [XmlElement("dBasGravIVA")]
    public string BaseGravadaIVAString
    {
        get => BaseGravadaIVA.ToString("F8", CultureInfo.InvariantCulture);
        set => BaseGravadaIVA = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal LiquidacionIVA { get; set; }

    [XmlElement("dLiqIVAItem")]
    public string LiquidacionIVAString
    {
        get => LiquidacionIVA.ToString("F8", CultureInfo.InvariantCulture);
        set => LiquidacionIVA = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal BaseExenta { get; set; }

    [XmlElement("dBasExe")]
    public string BaseExentaString
    {
        get => BaseExenta.ToString("F8", CultureInfo.InvariantCulture);
        set => BaseExenta = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public GCamIVA(){}
}

// Campos que describen los subtotales y totales de la transacción documentada (F001-F099)
public class GTotSub
{
    [XmlElement("dSubExe")]
    public decimal SubtotalExenta { get; set; }

    [XmlElement("dSubExo")]
    public decimal SubtotalExonerado { get; set; }

    [XmlElement("dSub5")]
    public decimal SubtotalTasa5 { get; set; }

    [XmlElement("dSub10")]
    public decimal SubtotalTasa10 { get; set; }

    [XmlElement("dTotOpe")]
    public decimal TotalBrutoOperacion { get; set; }

    [XmlElement("dTotDesc")]
    public decimal TotalDescuentoItem { get; set; }

    [XmlElement("dTotDescGlotem")]
    public decimal TotalDescuentoGlobal { get; set; }

    [XmlElement("dTotAntItem")]
    public decimal TotalAnticipoItem { get; set; }

    [XmlElement("dTotAnt")]
    public decimal TotalAnticipoGlobal { get; set; }

    [XmlElement("dPorcDescTotal")]
    public decimal PorcentajeDescuentoGlobal {get; set; }

    [XmlElement("dDescTotal")]
    public decimal TotalDescuentoOperacion { get; set; }

    [XmlElement("dAnticipo")]
    public decimal TotalAnticipoOperacion { get; set; }

    [XmlElement("dRedon")]
    public decimal RedondeoOperacion { get; set;}
/*
    [XmlElement("dComi")]
    public decimal ComisionOperacion { get; set; }
*/
    [XmlIgnore]
    public decimal TotalNetoOperacion { get; set; }

    [XmlElement("dTotGralOpe")]
    public string TotalNetoOperacionStr
    {
        get => TotalNetoOperacion.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalNetoOperacion = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal LiquidacionIVA5 { get; set; }

    [XmlElement("dIVA5")]
    public string LiquidacionIVA5Str
    {
        get => LiquidacionIVA5.ToString("F8", CultureInfo.InvariantCulture);
        set => LiquidacionIVA5 = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal LiquidacionIVA10 { get; set; }

    [XmlElement("dIVA10")]
    public string LiquidacionIVA10Str
    {
        get => LiquidacionIVA10.ToString("F8", CultureInfo.InvariantCulture);
        set => LiquidacionIVA10 = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

/*    [XmlElement("dLiqTotIVA5")]
    public decimal LiquidacionTotalIVA5 {get; set; }

    [XmlElement("dLiqTotIVA10")]
    public decimal LiquidacionTotalIVA10 { get; set; }

    [XmlElement("dIVAComi")]
    public decimal LiquidacionIVAComision { get; set; }
*/
    [XmlIgnore]
    public decimal LiquidacionTotalIVA { get; set; }

    [XmlElement("dTotIVA")]
    public string LiquidacionTotalIVAStr
    {
        get => LiquidacionTotalIVA.ToString("F8", CultureInfo.InvariantCulture);
        set => LiquidacionTotalIVA = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal TotalGravada5 { get; set; }

    [XmlElement("dBaseGrav5")]
    public string TotalGravada5Str
    {
        get => TotalGravada5.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalGravada5 = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal TotalGravada10 { get; set; }

    [XmlElement("dBaseGrav10")]
    public string TotalGravada10Str
    {
        get => TotalGravada10.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalGravada10 = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal TotalGravadaIVA { get; set; }

    [XmlElement("dTBasGraIVA")]
    public string TotalGravadaIVAStr
    {
        get => TotalGravadaIVA.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalGravadaIVA = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    [XmlIgnore]
    public decimal TotalGeneralOperacionGs { get; set; }

    [XmlElement("dTotalGs")]
    public string TotalGeneralOperacionGsStr
    {
        get => TotalGeneralOperacionGs.ToString("F8", CultureInfo.InvariantCulture);
        set => TotalGeneralOperacionGs = decimal.Parse(value, CultureInfo.InvariantCulture);
    }

    public bool ShouldSerializeTotalGeneralOperacionGsStr() => TotalGeneralOperacionGs != 0;

    public GTotSub(){}
}

public class gCamFuFD
{
    [XmlElement("dCarQR")]
    public string dCarQR { get; set; }

    [XmlElement("dInfAdic")]
    public string dInfAdic { get; set; } 
}

