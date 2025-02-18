public class FacturaResponse
{
    public InvoiceData Invoices { get; set; }
    public BusinessPartnerData BusinessPartners { get; set; }
}

public class InvoiceData
{
    public int DocEntry { get; set; }
    public string U_EXX_FE_CDC { get; set; }
    public string U_CDOC { get; set; }
    public string CardCode { get; set; }
    public string U_EST { get; set; }
    public string U_PDE { get; set; }
    public string FolioNumber { get; set; }
    public string DocDate { get; set; }
    public string U_FITE { get; set; }
    public int U_TIM { get; set; }
}

public class BusinessPartnerData
{
    public string CardCode { get; set; }
    public string FederalTaxID { get; set; }
    public string U_TIPCONT { get; set; }
}
