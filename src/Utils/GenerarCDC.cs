using System;

public class GenerarCDC
{
    public static string GenerarCodigoCDC(
        string iTiDE, string dRucEm, string dDVEmi, 
        string dEst, string dPunExp, string dNumDoc, 
        string dTipCont, string dFecha, string iTipEmi, 
        string dCodSeg)
    {
        string cdcBase = $"{iTiDE}{dRucEm}{dDVEmi}{dEst}{dPunExp}{dNumDoc}{dTipCont}{dFecha}{iTipEmi}{dCodSeg}";
        int dv = CalcularDV(cdcBase);
        return cdcBase + dv;
    }

    private static int CalcularDV(string cdcBase)
    {
        int[] pesos = { 2, 3, 4, 5, 6, 7 };
        int suma = 0;
        int longitud = cdcBase.Length;

        for (int i = 0; i < longitud; i++)
        {
            int digito = int.Parse(cdcBase[longitud - 1 - i].ToString());
            int peso = pesos[i % 6];
            suma += digito * peso;
        }

        int resto = suma % 11;
        if (resto == 0) return 0;
        if (resto == 1) return 1;
        return 11 - resto;
    }
}
