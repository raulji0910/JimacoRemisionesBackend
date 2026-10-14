using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jimaco.Remisiones.Negocio.Servicios;

public class InventarioService(
    AppDbContext db,
    ISincronizacionService sincronizacion,
    IOptions<WorldOfficeOptions> woOptions,
    TimeProvider timeProvider) : IInventarioService
{
    public async Task<EstadoInventarioDto> ObtenerEstadoAsync(CancellationToken ct = default)
    {
        var foto = await db.EstadoIntegracionWo.Select(e => e.UltimaFotoExistenciasEn).FirstOrDefaultAsync(ct);
        var conExistencia = await db.Productos.CountAsync(p => p.ExistenciaActualizadaEn != null, ct);
        var sinPrecio = await db.Productos.CountAsync(p => p.Activo && p.PrecioVenta <= 0, ct);
        return new EstadoInventarioDto(foto, woOptions.Value.BodegaVenta, conExistencia, sinPrecio);
    }

    public async Task<ImportarInventarioResultadoDto> ImportarAsync(ImportarInventarioDto dto, CancellationToken ct = default)
    {
        var avisos = new List<string>();
        var validas = dto.Filas
            .Where(f => f.IdWorldOffice > 0 && !string.IsNullOrWhiteSpace(f.Codigo) && !string.IsNullOrWhiteSpace(f.Nombre))
            .ToList();
        var invalidas = dto.Filas.Count - validas.Count;
        if (invalidas > 0)
            avisos.Add($"{invalidas} fila(s) sin IdInventario, código o nombre se omitieron.");

        // 1) Productos (maestro)
        var productos = await sincronizacion.SincronizarProductosAsync(
            validas.Select(f => new SincProductoDto(f.IdWorldOffice, f.Codigo, f.Nombre, f.Unidad, f.Iva, f.Activo)).ToList(), ct);
        avisos.AddRange(productos.Avisos);

        // 2) Existencias de la bodega de venta (la hora de la foto es la de la exportación)
        var bodega = woOptions.Value.BodegaVenta;
        var existencias = validas
            .Where(f => f.Existencia is not null)
            .Select(f => new SincExistenciaDto(f.IdWorldOffice, bodega, f.Existencia!.Value))
            .ToList();
        var foto = (dto.ExportadoEn ?? timeProvider.GetUtcNow().UtcDateTime).ToUniversalTime();
        var resultadoExistencias = await sincronizacion.SincronizarExistenciasAsync(existencias, foto, ct);

        // 3) Precios iniciales (último precio de venta en WO). Por defecto solo a los que no tienen precio.
        var conPrecio = validas.Where(f => f.Precio is > 0).ToList();
        var preciosAsignados = 0;
        if (conPrecio.Count > 0)
        {
            var ids = conPrecio.Select(f => f.IdWorldOffice).ToList();
            var porId = await db.Productos
                .Where(p => p.IdWorldOffice != null && ids.Contains(p.IdWorldOffice.Value))
                .ToDictionaryAsync(p => p.IdWorldOffice!.Value, ct);

            foreach (var fila in conPrecio)
            {
                if (!porId.TryGetValue(fila.IdWorldOffice, out var producto))
                    continue;
                if (!dto.ActualizarPrecios && producto.PrecioVenta > 0)
                    continue;
                if (producto.PrecioVenta == fila.Precio!.Value)
                    continue;

                producto.PrecioVenta = Math.Round(fila.Precio!.Value, 2);
                preciosAsignados++;
            }

            await db.SaveChangesAsync(ct);
        }

        return new ImportarInventarioResultadoDto(
            productos.Creados,
            productos.Actualizados,
            productos.Omitidos + invalidas,
            resultadoExistencias.Creados + resultadoExistencias.Actualizados,
            preciosAsignados,
            avisos);
    }
}
