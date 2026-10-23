using System.Globalization;
using Microsoft.Extensions.Configuration;

namespace Jimaco.Remisiones.Negocio.Servicios;

/// <summary>
/// Retenciones que el vendedor puede aplicar: tarifas FIJAS (no se digitan) y el valor mínimo antes de IVA a partir del cual
/// proceden (10 UVT; UVT 2026 = $52.374 -> $523.740). Salen de la sección <c>Retenciones</c> de la configuración para poder
/// ajustarlas (p. ej. al cambiar la UVT cada año) sin tocar el código.
/// </summary>
public record ReglasRetenciones(decimal ReteFuentePorcentaje, decimal ReteIcaPorMil, decimal ReteIvaPorcentaje, decimal Uvt, int BaseMinimaUvt)
{
    public decimal BaseMinima => Uvt * BaseMinimaUvt;

    public static ReglasRetenciones Cargar(IConfiguration? configuracion)
    {
        decimal Leer(string clave, decimal porDefecto)
            => decimal.TryParse(configuracion?[$"Retenciones:{clave}"], NumberStyles.Number, CultureInfo.InvariantCulture, out var v) ? v : porDefecto;

        return new ReglasRetenciones(
            Leer("ReteFuentePorcentaje", 2.5m),
            Leer("ReteIcaPorMil", 6.9m),
            Leer("ReteIvaPorcentaje", 15m),
            Leer("Uvt", 52374m),
            (int)Leer("BaseMinimaUvt", 10m));
    }
}
