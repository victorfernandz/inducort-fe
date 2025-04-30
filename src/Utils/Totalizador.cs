using System;
using System.Collections.Generic;
using System.Linq;

public class Totalizador
{
    // Método principal para calcular subtotales y totales a partir de los items
    public static GTotSub CalcularTotalesFactura(List<Item> items, decimal tipoCambio, string moneda)
    {
        var totales = new GTotSub();
        
        // Inicializar todos los valores a 0
        totales.SubtotalExenta = 0;
        totales.SubtotalExonerado = 0;
        totales.SubtotalTasa5 = 0;
        totales.SubtotalTasa10 = 0;
        totales.TotalBrutoOperacion = 0;
        totales.TotalDescuentoItem = 0;
        totales.TotalDescuentoGlobal = 0;
        totales.TotalAnticipoItem = 0;
        totales.TotalAnticipoGlobal = 0;
        totales.PorcentajeDescuentoGlobal = 0;
        totales.TotalDescuentoOperacion = 0;
        totales.TotalAnticipoOperacion = 0;
        totales.RedondeoOperacion = 0;
        totales.ComisionOperacion = 0;
        totales.TotalNetoOperacion = 0;
        totales.LiquidacionIVA5 = 0;
        totales.LiquidacionIVA10 = 0;
        totales.LiquidacionTotalIVA5 = 0;
        totales.LiquidacionTotalIVA10 = 0;
        totales.LiquidacionIVAComision = 0;      
        totales.LiquidacionTotalIVA = 0;      
        totales.TotalGravada5 = 0;
        totales.TotalGravada10 = 0;
        totales.TotalGravadaIVA = 0;
        
        // Si no hay items, retornar el objeto con valores en 0
        if (items == null || !items.Any())
            return totales;

        // 1. Calcular subtotales por tipo de afectación y tasa
        foreach (var item in items)
        {
            // Subtotales basados en la afectación de IVA
            if (item.iAfecIVA == 3) // Exento
            {
                totales.SubtotalExenta += item.dTotBruOpeItem;
            }
            else if (item.iAfecIVA == 2) // Exonerado
            {
                totales.SubtotalExenta += item.dTotBruOpeItem;
            }
            else // Gravado (iAfecIVA = 1 o 4)
            {
                if (item.dTasaIVA == 5)
                {
                    totales.SubtotalTasa5 += item.dTotBruOpeItem;
                    totales.TotalGravada5 += item.dBasGravIVA;
                    totales.LiquidacionIVA5 += item.dLiqIVAItem;
                }
                else if (item.dTasaIVA == 10)
                {
                    totales.SubtotalTasa10 += item.dTotBruOpeItem;
                    totales.TotalGravada10 += item.dBasGravIVA;
                    totales.LiquidacionIVA10 += item.dLiqIVAItem;
                }
            }

            // Acumular totales brutos
            totales.TotalBrutoOperacion += item.dTotBruOpeItem;

            // Acumular descuentos por ítem
            if (item.dDescItem > 0)
            {
                totales.TotalDescuentoItem += item.dDescItem * item.dCantProSer;
            }

            // Acumular descuentos globales por ítem
            if (item.dDescGloItem > 0)
            {
                totales.TotalDescuentoGlobal += item.dDescGloItem * item.dCantProSer;
            }

            // Acumular anticipos por ítem 
            if (item.dAntPreUniIt > 0)
            {
                totales.TotalAnticipoItem += item.dAntPreUniIt * item.dCantProSer;
            }

            // Acumular anticipos globales por ítem
            if (item.dAntGloPreUniIt > 0)
            {
                totales.TotalAnticipoGlobal += item.dAntGloPreUniIt * item.dCantProSer;
            }
        }

        // 2. Calcular total de descuentos y anticipos
        totales.TotalDescuentoOperacion = totales.TotalDescuentoItem + totales.TotalDescuentoGlobal;
        totales.TotalAnticipoOperacion = totales.TotalAnticipoItem + totales.TotalAnticipoGlobal;

        // 3. Calcular porcentaje de descuento total
        if (totales.TotalBrutoOperacion > 0)
        {
            totales.PorcentajeDescuentoGlobal = Math.Round((totales.TotalDescuentoOperacion * 100) / totales.TotalBrutoOperacion, 8);
        }

        // 4. Calcular redondeo (según reglas SEDECO)
        decimal totalSinRedondeo = totales.TotalBrutoOperacion - totales.TotalDescuentoOperacion - totales.TotalAnticipoOperacion;
        decimal totalRedondeado = totalSinRedondeo;
/*        decimal totalRedondeado = RedondearSEDECO(totalSinRedondeo);
        totales.RedondeoOperacion = totalRedondeado - totalSinRedondeo; */

        // 5. Calcular el total neto de la operación
        totales.TotalNetoOperacion = totalRedondeado;// + totales.ComisionOperacion; 
/*
        // 6. Calcular totales de IVA por tasas 
        totales.LiquidacionTotalIVA5 = totales.LiquidacionIVA5;
        totales.LiquidacionTotalIVA10 = totales.LiquidacionIVA10;*/
        
        // 7. Calcular total de IVA
        totales.LiquidacionTotalIVA = totales.LiquidacionIVA5 + totales.LiquidacionIVA10; //+ totales.LiquidacionIVAComision;

        // 8. Calcular total base gravada
        totales.TotalGravadaIVA = totales.TotalGravada5 + totales.TotalGravada10;

        // 9. Calcular el total en guaraníes si la moneda no es Guaraní
        if (moneda != "PYG" && tipoCambio > 1)
        {
            totales.TotalGeneralOperacionGs = (totales.TotalNetoOperacion * tipoCambio);
        }
        else if (moneda == "PYG")
        {
            totales.TotalGeneralOperacionGs = null;
        }

        return totales;
    }

    // Método para redondear según reglas SEDECO (múltiplos de 50 Gs)
    /*
    private static decimal RedondearSEDECO(decimal monto)
    {
        // Si la moneda no es guaraníes o tiene decimales, no aplicamos reglas SEDECO
        if (monto != Math.Floor(monto))
            return monto;

        decimal resto = monto % 50;
        
        if (resto <= 25)
            return monto - resto; // Redondeo hacia abajo
        else
            return monto + (50 - resto); // Redondeo hacia arriba
    } */
}