using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Negocio.Servicios;

/// <summary>
/// Resumen de caja de un rango de fechas para conciliar: cuánto se cobró por cada medio (datáfono, QR, efectivo), cuánto se devolvió
/// por remisiones pagadas que se anularon en el rango, el neto, el detalle por día y por usuario. Los días son del calendario de
/// Colombia (UTC-5, sin horario de verano).
/// </summary>
public class CierreCajaService(AppDbContext db) : ICierreCajaService
{
    private static readonly TimeSpan OffsetColombia = TimeSpan.FromHours(-5);
    public const int MaximoDias = 366;

    public async Task<CierreCajaDto> ObtenerAsync(DateOnly desdeDia, DateOnly hastaDia, CancellationToken ct = default)
    {
        if (hastaDia < desdeDia)
            throw new InvalidOperationException("La fecha final no puede ser anterior a la inicial.");
        if (hastaDia.DayNumber - desdeDia.DayNumber + 1 > MaximoDias)
            throw new InvalidOperationException($"El rango no puede superar {MaximoDias} días.");

        // Medianoche local -> UTC: 00:00 en Colombia son las 05:00 UTC.
        var desde = DateTime.SpecifyKind(desdeDia.ToDateTime(TimeOnly.MinValue) - OffsetColombia, DateTimeKind.Utc);
        var hasta = DateTime.SpecifyKind(hastaDia.AddDays(1).ToDateTime(TimeOnly.MinValue) - OffsetColombia, DateTimeKind.Utc);

        var remisiones = await db.Remisiones
            .Include(r => r.Items)
            .Where(r => r.FechaPago != null &&
                        ((r.FechaPago >= desde && r.FechaPago < hasta) ||
                         (r.FechaAnulacion != null && r.FechaAnulacion >= desde && r.FechaAnulacion < hasta)))
            .ToListAsync(ct);

        static decimal Total(Remision r) => RemisionCalculos
            .Calcular(r.Items.Select(i => (i.PrecioUnitario, i.Cantidad, i.Iva, i.DescuentoPorcentaje)), r.ReteFuentePorcentaje, r.ReteIcaPorMil, r.ReteIvaPorcentaje).TotalGeneral;

        static string DiaLocal(DateTime utc) => DateOnly.FromDateTime(utc + OffsetColombia).ToString("yyyy-MM-dd");

        CierreLineaDto Linea(Remision r, DateTime fechaLinea) => new(
            r.Id, RemisionService.Formatear(r.Consecutivo), fechaLinea, r.CompradorNombreSnapshot, r.MedioPago,
            r.ReciboRedeban, Total(r), r.PagadaPor, r.CreadoPor, r.EstadoPago == EstadoPago.Anulada);

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

        // Un renglón por cada día del rango (también los días sin movimiento, para que el rango se lea completo).
        var porDia = new List<CierreDiaDto>();
        for (var dia = desdeDia; dia <= hastaDia; dia = dia.AddDays(1))
        {
            var clave = dia.ToString("yyyy-MM-dd");
            var c = cobros.Where(l => DiaLocal(l.Fecha) == clave).ToList();
            var d = devoluciones.Where(l => DiaLocal(l.Fecha) == clave).ToList();
            decimal Neto(MedioPago m) => c.Where(l => l.Medio == m).Sum(l => l.Total) - d.Where(l => l.Medio == m).Sum(l => l.Total);
            porDia.Add(new CierreDiaDto(clave, c.Count, Neto(MedioPago.Datafono), Neto(MedioPago.Qr), Neto(MedioPago.Efectivo),
                c.Sum(l => l.Total), d.Sum(l => l.Total), c.Sum(l => l.Total) - d.Sum(l => l.Total)));
        }

        var porUsuario = cobros
            .GroupBy(l => string.IsNullOrWhiteSpace(l.Usuario) ? "(sin usuario)" : l.Usuario!)
            .Select(g => new CierreUsuarioDto(
                g.Key, g.Count(),
                g.Where(l => l.Medio == MedioPago.Datafono).Sum(l => l.Total),
                g.Where(l => l.Medio == MedioPago.Qr).Sum(l => l.Total),
                g.Where(l => l.Medio == MedioPago.Efectivo).Sum(l => l.Total),
                g.Sum(l => l.Total)))
            .OrderByDescending(u => u.TotalCobros)
            .ToList();

        var totalCobros = cobros.Sum(l => l.Total);
        var totalDevoluciones = devoluciones.Sum(l => l.Total);
        return new CierreCajaDto(desdeDia.ToString("yyyy-MM-dd"), hastaDia.ToString("yyyy-MM-dd"), porMedio, totalCobros, totalDevoluciones,
            totalCobros - totalDevoluciones, porDia, porUsuario, cobros, devoluciones);
    }
}
