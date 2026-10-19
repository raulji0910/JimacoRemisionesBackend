using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Servicios;

public record ResultadoCalculo(
    decimal SubtotalItems,
    decimal SubtotalConDescuento,
    IReadOnlyList<IvaDesgloseDto> IvaDesglose,
    decimal TotalGeneral);

// Mismo criterio que el cotizador de Ferrealiados: el descuento (global, en pesos) se reparte
// proporcionalmente entre las tarifas de IVA presentes, según la participación de cada tramo en
// el subtotal. Así, si la remisión mezcla productos al 19%, 5% y 0%, cada tramo paga IVA sobre su
// propia base ya descontada, en vez de aplicar una sola tarifa al total.
public static class RemisionCalculos
{
    // Descuento en pesos que resulta de aplicar el % sobre el valor antes de IVA.
    public static decimal CalcularDescuento(decimal subtotalItems, decimal porcentaje)
        => Math.Round(subtotalItems * porcentaje / 100m, 2);

    // `retencion` (pesos, opcional) se resta del valor antes de IVA igual que el descuento: ambos reducen la
    // base sobre la que se calcula el IVA, repartidos entre las tarifas según su participación.
    public static ResultadoCalculo Calcular(IEnumerable<(decimal PrecioUnitario, int Cantidad, int Iva)> lineas, decimal descuento, decimal retencion = 0m)
    {
        var reduccion = descuento + retencion;
        var lista = lineas.ToList();
        var subtotalItems = lista.Sum(l => l.PrecioUnitario * l.Cantidad);
        var subtotalConDescuento = subtotalItems - reduccion;

        var ivaDesglose = lista
            .GroupBy(l => l.Iva)
            .Where(g => g.Key > 0)
            .Select(g =>
            {
                var subtotalGrupo = g.Sum(l => l.PrecioUnitario * l.Cantidad);
                var proporcion = subtotalItems > 0 ? subtotalGrupo / subtotalItems : 0;
                var baseGravable = Math.Round(subtotalGrupo - (reduccion * proporcion), 2);
                var valorIva = Math.Round(baseGravable * g.Key / 100m, 2);
                return new IvaDesgloseDto(g.Key, baseGravable, valorIva);
            })
            .OrderByDescending(d => d.Tarifa)
            .ToList();

        return new ResultadoCalculo(subtotalItems, subtotalConDescuento, ivaDesglose, subtotalConDescuento + ivaDesglose.Sum(d => d.Valor));
    }

    // Margen sobre el precio de venta: (precio - costo) / costo, como "% de ganancia" que usaban
    // en el cotizador. Solo informativo.
    public static decimal? MargenPorcentaje(decimal precioUnitario, decimal? costo)
        => costo is null or 0 ? null : Math.Round((precioUnitario - costo.Value) / costo.Value * 100, 2);
}
