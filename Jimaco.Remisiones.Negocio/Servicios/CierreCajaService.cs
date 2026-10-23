using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Negocio.Servicios;

/// <summary>
/// Resumen de caja de un día para conciliar: cuánto se cobró por cada medio (datáfono, QR, efectivo), cuánto se devolvió
/// por remisiones pagadas que se anularon ese día, y el neto. El día es el calendario de Colombia (UTC-5, sin horario de verano).
/// </summary>
public class CierreCajaService(AppDbContext db) : ICierreCajaService
{
    private static readonly TimeSpan OffsetColombia = TimeSpan.FromHours(-5);

    public async Task<CierreCajaDto> ObtenerAsync(DateOnly fecha, CancellationToken ct = default)
    {
        // Medianoche local -> UTC: 00:00 en Colombia son las 05:00 UTC.
        var desde = DateTime.SpecifyKind(fecha.ToDateTime(TimeOnly.MinValue) - OffsetColombia, DateTimeKind.Utc);
        var hasta = desde.AddDays(1);

        var remisiones = await db.Remisiones
            .Include(r => r.Items)
            .Where(r => r.FechaPago != null &&
                        ((r.FechaPago >= desde && r.FechaPago < hasta) ||
                         (r.FechaAnulacion != null && r.FechaAnulacion >= desde && r.FechaAnulacion < hasta)))
            .ToListAsync(ct);

        static decimal Total(Remision r) => RemisionCalculos
            .Calcular(r.Items.Select(i => (i.PrecioUnitario, i.Cantidad, i.Iva, i.DescuentoPorcentaje)), r.ReteFuentePorcentaje, r.ReteIcaPorMil, r.ReteIvaPorcentaje).TotalGeneral;

        CierreLineaDto Linea(Remision r, DateTime fechaLinea) => new(
            r.Id, RemisionService.Formatear(r.Consecutivo), fechaLinea, r.CompradorNombreSnapshot, r.MedioPago,
            r.ReciboRedeban, Total(r), r.PagadaPor, r.EstadoPago == EstadoPago.Anulada);

        var cobros = remisiones
            .Where(r => r.FechaPago >= desde && r.FechaPago < hasta)
            .OrderBy(r => r.FechaPago)
            .Select(r => Linea(r, r.FechaPago!.Value))
            .ToList();

        var devoluciones = remisiones
            .Where(r => r.EstadoPago == EstadoPago.Anulada && r.FechaAnulacion >= desde && r.FechaAnulacion < hasta)
            .OrderBy(r => r.FechaAnulacion)
            .Select(r => Linea(r, r.FechaAnulacion!.Value))
            .ToList();

        var porMedio = Enum.GetValues<MedioPago>().Select(medio =>
        {
            var c = cobros.Where(l => l.Medio == medio).ToList();
            var d = devoluciones.Where(l => l.Medio == medio).ToList();
            return new CierreMedioDto(medio, c.Count, c.Sum(l => l.Total), d.Count, d.Sum(l => l.Total), c.Sum(l => l.Total) - d.Sum(l => l.Total));
        }).ToList();

        var totalCobros = cobros.Sum(l => l.Total);
        var totalDevoluciones = devoluciones.Sum(l => l.Total);
        return new CierreCajaDto(fecha.ToString("yyyy-MM-dd"), porMedio, totalCobros, totalDevoluciones,
            totalCobros - totalDevoluciones, cobros, devoluciones);
    }
}
