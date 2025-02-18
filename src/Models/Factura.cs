public class Factura
{
    public int DocEntry { get; set; }
    public string U_EXX_FE_CDC { get; set; }
    public string U_CDOC { get; set; }
    public string CardCode { get; set; }
    public string U_EST { get; set; }
    public string U_PDE { get; set; }
    public string FolioNum { get; set; }    
    public string DocDate { get; set; }
    public int iTipEmi { get; set; }
    public string dFecha { get; set; }
    public int U_TIM { get; set; }
    public string U_FITE { get; set; }
    public string iTiDE { get; set; }
    public BusinessPartner BusinessPartner { get; set; }
}

public class BusinessPartner
{
    public string CardCode { get; set; }
    public string FederalTaxID { get; set; }
    public string U_TIPCONT { get; set; }
}
