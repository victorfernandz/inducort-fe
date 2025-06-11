using System;

public class GenerarCDC
{
    public static string GenerarCodigoCDC(string iTiDE, string dRucEm, string dDVEmi, string dEst, string dPunExp, string dNumDoc, string dTipCont, string dFeEmiDE, string iTipEmi, string dCodSeg)
    {
        string cdcBase = $"{iTiDE.Trim()}{dRucEm.Trim()}{dDVEmi.Trim()}{dEst.Trim()}{dPunExp.Trim()}{dNumDoc.Trim()}{dTipCont.Trim()}{dFeEmiDE.Trim()}{iTipEmi.Trim()}{dCodSeg.Trim()}";
        Console.WriteLine($"CDC base generado: {cdcBase} ");
        int dv = CalcularDV(cdcBase);
        /*    Console.WriteLine($"Dígito verificador: {dv}");*/
        Console.WriteLine($"CDC + dígito verificador: {cdcBase},{dv}");
        return cdcBase + dv;
    }

    /*
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
    */
    /*

    private static int CalcularDV(string cdcBase)
    {
        // Cambiamos el array de pesos para incluir valores del 2 al 11
        int[] pesos = { 2, 3, 4, 5, 6, 7, 8, 9, 10, 11 };
        int suma = 0;
        int longitud = cdcBase.Length;

        for (int i = 0; i < longitud; i++)
        {
            int digito = int.Parse(cdcBase[longitud - 1 - i].ToString());
            int peso = pesos[i % 10];
            suma += digito * peso;
        }

        int resto = suma % 11;
        if (resto == 0) return 0;
        if (resto == 1) return 1;
        return 11 - resto;
    } */
    
    private static int CalcularDV(string cdcBase)
    {
        string numeroConvertido = "";
        foreach (char c in cdcBase.ToUpper())
        {
            if (char.IsDigit(c))
            {
                numeroConvertido += c;
            }
            else
            {
                numeroConvertido += ((int)c).ToString();
            }
        }

        int total = 0;
        int k = 2;
        for (int i = numeroConvertido.Length - 1; i >= 0; i--)
        {
            if (k > 11) k = 2;
            int digito = int.Parse(numeroConvertido[i].ToString());
            total += digito * k;
            k++;
        }

        int resto = total % 11;
        int dv = (resto > 1) ? (11 - resto) : 0;
        return dv;
    }
}
