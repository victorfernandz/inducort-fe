public class NotaCreditoResponse
{
    public CreditNotesData CreditNotes { get; set; }
    public DocumentLineData DocumentLines { get; set; }
    public BusinessPartnerData BusinessPartners { get; set; }
    public CurrenciesData Currencies { get; set; }
}

public class CreditNotesData
{
    public int DocEntry { get; set; }
    public string U_EXX_FE_CDC { get; set; }
    public string U_CDOC { get; set; }
    public string CardCode { get; set; }
    public string U_EST { get; set; }
    public string U_PDE { get; set; }
    public string FolioNumber { get; set; }
    public string DocDate { get; set; }
    public int DocTime { get; set; }
    public string U_FITE { get; set; }
    public int U_TIM { get; set; }
    public string U_EXX_FE_TipoTran { get; set; }
    public int U_EXX_FE_IndPresencia { get; set; }
    public int PaymentGroupCode { get; set; }
    public int NumberOfInstallments { get; set; }
    public decimal DocRate { get; set; }
    public string U_NUMFC { get; set; }
    public int U_TIMFC { get; set; }
}