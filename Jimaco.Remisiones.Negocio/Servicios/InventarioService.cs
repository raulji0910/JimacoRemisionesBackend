using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Negocio.Servicios;

public class InventarioService(AppDbContext db, TimeProvider timeProvider) : IInventarioService
{
    public async Task<PaginaResultado<MovimientoDto>?> ListarMovimientosAsync(int productoId, int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        if (!await db.Productos.AnyAsync(p => p.Id == productoId, ct))
            return null;

        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, 100);

        var query = db.MovimientosInventario.Where(m => m.ProductoId == productoId);
        var total = await query.CountAsync(ct);

        var filas = await query
            .OrderByDescending(m => m.Fecha).ThenByDescending(m => m.Id)
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .ToListAsync(ct);

        // Consecutivo de las remisiones que originaron los movimientos de esta página.
        var remisionIds = filas.Where(m => m.RemisionId != null).Select(m => m.RemisionId!.Value).Distinct().ToList();
        var consecutivos = remisionIds.Count == 0
            ? new Dictionary<int, int>()
            : await db.Remisiones.Where(r => remisionIds.Contains(r.Id)).ToDictionaryAsync(r => r.Id, r => r.Consecutivo, ct);

        var items = filas.Select(m => new MovimientoDto(
            m.Id, m.Fecha, m.Tipo, m.Cantidad, m.SaldoResultante, m.RemisionId,
            m.RemisionId is not null && consecutivos.TryGetValue(m.RemisionId.Value, out var c) ? RemisionService.Formatear(c) : null,
            m.Usuario, m.Motivo)).ToList();

        return new PaginaResultado<MovimientoDto>(items, total, pagina, tamanoPagina);
    }

    public async Task<ProductoDto?> RegistrarMovimientoAsync(int productoId, MovimientoManualDto dto, string? usuario, CancellationToken ct = default)
    {
        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == productoId, ct);
        if (producto is null)
            return null;

        var motivo = string.IsNullOrWhiteSpace(dto.Motivo) ? null : dto.Motivo.Trim();

        switch (dto.Tipo)
        {
            case TipoMovimiento.Entrada:
                if (dto.Cantidad <= 0)
                    throw new InvalidOperationException("La cantidad de una entrada debe ser mayor a cero.");
                break;

            case TipoMovimiento.Ajuste:
                if (dto.Cantidad == 0)
                    throw new InvalidOperationException("El ajuste no puede ser de cero unidades.");
                if (motivo is null)
                    throw new InvalidOperationException("Un ajuste de inventario requiere un motivo.");
                break;

            default:
                throw new InvalidOperationException("Solo se pueden registrar a mano entradas y ajustes.");
        }

        // Kardex valida que el stock no quede negativo.
        Kardex.Registrar(db, producto, dto.Tipo, dto.Cantidad, timeProvider.GetUtcNow().UtcDateTime, usuario, motivo: motivo);
        await db.SaveChangesAsync(ct);

        return ProductoService.Mapear(producto);
    }
}
