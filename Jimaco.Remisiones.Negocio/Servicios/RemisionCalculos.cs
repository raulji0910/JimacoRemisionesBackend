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
    public static ResultadoCalculo Calcular(IEnumerable<(decimal PrecioUnitario, int Cantidad, int Iva)> lineas, decimal descuento)
    {
        var lista = lineas.ToList();
        var subtotalItems = lista.Sum(l => l.PrecioUnitario * l.Cantidad);
        var subtotalConDescuento = subtotalItems - descuento;

        var ivaDesglose = lista
            .GroupBy(l => l.Iva)
            .Where(g => g.Key > 0)
            .Select(g =>
            {
                var subtotalGrupo = g.Sum(l => l.PrecioUnitario * l.Cantidad);
                var proporcion = subtotalItems > 0 ? subtotalGrupo / subtotalItems : 0;
                var baseGravable = Math.Round(subtotalGrupo - (descuento * proporcion), 2);
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
