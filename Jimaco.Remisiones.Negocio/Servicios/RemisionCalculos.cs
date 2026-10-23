using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Servicios;

public record ResultadoCalculo(
    decimal SubtotalItems,
    decimal Descuento,
    decimal SubtotalConDescuento,
    IReadOnlyList<IvaDesgloseDto> IvaDesglose,
    decimal TotalBruto,
    decimal ReteFuente,
    decimal ReteIca,
    decimal ReteIva,
    decimal TotalGeneral);

// Mismo criterio que el cotizador de Ferrealiados: el descuento (global, en pesos) se reparte
// proporcionalmente entre las tarifas de IVA presentes, según la participación de cada tramo en
// el subtotal. Así, si la remisión mezcla productos al 19%, 5% y 0%, cada tramo paga IVA sobre su
// propia base ya descontada, en vez de aplicar una sola tarifa al total.
public static class RemisionCalculos
{
    // Descuento en pesos de una línea: % sobre su valor antes de IVA (precio x cantidad).
    public static decimal DescuentoLinea(decimal precioUnitario, int cantidad, decimal porcentaje)
        => Math.Round(precioUnitario * cantidad * porcentaje / 100m, 2);

    // Cada línea lleva su propio descuento (no hay descuento general) y su IVA se calcula sobre su valor ya descontado.
    // Las retenciones NO tocan el IVA: ReteFuente y ReteICA se calculan sobre el valor antes de IVA (ya con descuentos)
    // y ReteIVA sobre el IVA; las tres se restan al final. `TotalGeneral` es el total a pagar (neto de retenciones) y
    // `TotalBruto` el total antes de ellas.
    public static ResultadoCalculo Calcular(
        IEnumerable<(decimal PrecioUnitario, int Cantidad, int Iva, decimal DescuentoPorcentaje)> lineas,
        decimal reteFuentePorcentaje = 0m,
        decimal reteIcaPorMil = 0m,
        decimal reteIvaPorcentaje = 0m)
    {
        var lista = lineas
            .Select(l =>
            {
                var bruto = l.PrecioUnitario * l.Cantidad;
                return (l.Iva, Bruto: bruto, Neto: bruto - DescuentoLinea(l.PrecioUnitario, l.Cantidad, l.DescuentoPorcentaje));
            })
            .ToList();
        var subtotalItems = lista.Sum(l => l.Bruto);
        var subtotalConDescuento = lista.Sum(l => l.Neto);
        var descuento = subtotalItems - subtotalConDescuento;

        var ivaDesglose = lista
            .GroupBy(l => l.Iva)
            .Where(g => g.Key > 0)
            .Select(g =>
            {
                var baseGravable = Math.Round(g.Sum(l => l.Neto), 2);
                var valorIva = Math.Round(baseGravable * g.Key / 100m, 2);
                return new IvaDesgloseDto(g.Key, baseGravable, valorIva);
            })
            .OrderByDescending(d => d.Tarifa)
            .ToList();

        var totalIva = ivaDesglose.Sum(d => d.Valor);
        var totalBruto = subtotalConDescuento + totalIva;
        var reteFuente = Math.Round(subtotalConDescuento * reteFuentePorcentaje / 100m, 2);
        var reteIca = Math.Round(subtotalConDescuento * reteIcaPorMil / 1000m, 2);
        var reteIva = Math.Round(totalIva * reteIvaPorcentaje / 100m, 2);

        return new ResultadoCalculo(subtotalItems, descuento, subtotalConDescuento, ivaDesglose, totalBruto,
            reteFuente, reteIca, reteIva, totalBruto - reteFuente - reteIca - reteIva);
    }

    // Margen sobre el precio de venta: (precio - costo) / costo, como "% de ganancia" que usaban
    // en el cotizador. Solo informativo.
    public static decimal? MargenPorcentaje(decimal precioUnitario, decimal? costo)
        => costo is null or 0 ? null : Math.Round((precioUnitario - costo.Value) / costo.Value * 100, 2);
}
