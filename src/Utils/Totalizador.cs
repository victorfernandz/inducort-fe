using System;
using System.Collections.Generic;
using System.Linq;

public class Totalizador
{
    // Método para calcular subtotales y totales a partir de los items
    public static GTotSub CalcularTotalesFactura(List<Item> items, decimal tipoCambio, string moneda)
    {
        var totales = new GTotSub();
        
        // Inicializar todos los valores a 0
   /*     totales.SubtotalExenta = 0;
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
        totales.TotalGravadaIVA = 0;*/
        
        // Si no hay items, retornar el objeto con valores en 0
        if (items == null || !items.Any())
            return totales;

        decimal baseDescuentoGlobal = 0m;

        // Calcular subtotales por tipo de afectación y tasa
        foreach (var item in items)
        {
            // Base para porcentaje de descuento global (F010)
            // Se calcula sobre el bruto del ítem, no sobre el neto
            baseDescuentoGlobal += item.dTotBruOpeItem;

            // Subtotales basados en la afectación de IVA
            if (item.iAfecIVA == 3) // Exento
            {
                //totales.SubtotalExenta += item.dTotBruOpeItem;
                totales.SubtotalExenta += item.dTotOpeItem;
            }
            else if (item.iAfecIVA == 2) // Exonerado
            {
                //totales.SubtotalExonerado += item.dTotBruOpeItem;
                totales.SubtotalExonerado += item.dTotOpeItem;
            }
            else if (item.iAfecIVA == 4) // Exenta / Parcial
            {
                totales.SubtotalExenta += item.dBasExe;
                
                if (item.dTasaIVA == 5)
                {
                    //totales.SubtotalTasa5 += item.dTotBruOpeItem;
                    //totales.SubtotalTasa5 += item.dBasGravIVA + item.dLiqIVAItem;
                    totales.SubtotalTasa5 += item.dTotOpeItem;
                    totales.TotalGravada5 += item.dBasGravIVA;
                    totales.LiquidacionIVA5 += item.dLiqIVAItem;
                }
                else if (item.dTasaIVA == 10)
                {
                    //totales.SubtotalTasa10 += item.dTotBruOpeItem;
                    totales.SubtotalTasa10 += item.dTotOpeItem;
                    totales.TotalGravada10 += item.dBasGravIVA;
                    totales.LiquidacionIVA10 += item.dLiqIVAItem;
                }
            }
            else // Gravado (iAfecIVA = 1)
            {
                if (item.dTasaIVA == 5)
                {
                    //totales.SubtotalTasa5 += item.dTotBruOpeItem;
                    totales.SubtotalTasa5 += item.dTotOpeItem;
                    totales.TotalGravada5 += item.dBasGravIVA;
                    totales.LiquidacionIVA5 += item.dLiqIVAItem;
                }
                else if (item.dTasaIVA == 10)
                {
                    //totales.SubtotalTasa10 += item.dTotBruOpeItem;
                    totales.SubtotalTasa10 += item.dTotOpeItem;
                    totales.TotalGravada10 += item.dBasGravIVA;
                    totales.LiquidacionIVA10 += item.dLiqIVAItem;
                }
            }

            //totales.TotalBrutoOperacion += item.dTotBruOpeItem;
            totales.TotalBrutoOperacion += item.dTotOpeItem;

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

        // Calcular total de descuentos y anticipos
        totales.TotalDescuentoOperacion = totales.TotalDescuentoItem + totales.TotalDescuentoGlobal;
        totales.TotalAnticipoOperacion = totales.TotalAnticipoItem + totales.TotalAnticipoGlobal;

        if (baseDescuentoGlobal > 0)
        {
            totales.PorcentajeDescuentoGlobal = Math.Round((totales.TotalDescuentoOperacion * 100m) / baseDescuentoGlobal, 8);
        }
        else
        {
            totales.PorcentajeDescuentoGlobal = 0m;
        }

       // Calcular redondeo (según reglas SEDECO)
        decimal totalSinRedondeo = totales.TotalBrutoOperacion - totales.TotalAnticipoOperacion;
        decimal totalRedondeado = totalSinRedondeo;
        /*        decimal totalRedondeado = RedondearSEDECO(totalSinRedondeo);
                totales.RedondeoOperacion = totalRedondeado - totalSinRedondeo; */

        // Calcular el total general de la operación
        totales.TotalNetoOperacion = totalRedondeado;// + totales.ComisionOperacion; 
/*
        // Calcular totales de IVA por tasas 
        totales.LiquidacionTotalIVA5 = totales.LiquidacionIVA5;
        totales.LiquidacionTotalIVA10 = totales.LiquidacionIVA10;*/
        
        // Calcular total de IVA
        totales.LiquidacionTotalIVA = totales.LiquidacionIVA5 + totales.LiquidacionIVA10; //+ totales.LiquidacionIVAComision;

        // Calcular total base gravada
        totales.TotalGravadaIVA = totales.TotalGravada5 + totales.TotalGravada10;

        // Calcular el total en guaraníes si la moneda no es Guaraní
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
}