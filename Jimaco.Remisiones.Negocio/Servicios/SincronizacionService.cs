using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Negocio.Servicios;

// Recibe lo que el Sincronizador lee de World Office. Es idempotente (se puede reenviar el mismo
// lote sin duplicar) y nunca escribe de vuelta en WO. Los lotes vienen acotados por el
// Sincronizador (~1000 filas), así que se cargan completos en memoria sin paginar.
public class SincronizacionService(AppDbContext db, TimeProvider timeProvider) : ISincronizacionService
{
    public async Task<SincResultadoDto> SincronizarProductosAsync(IReadOnlyList<SincProductoDto> items, CancellationToken ct = default)
    {
        var avisos = new List<string>();
        int creados = 0, actualizados = 0, omitidos = 0;

        var ids = items.Select(i => i.IdWorldOffice).Distinct().ToList();
        var codigos = items.Select(i => ProductoService.NormalizarCodigo(i.Codigo)).Where(c => c.Length > 0).Distinct().ToList();

        var existentes = await db.Productos
            .Where(p => (p.IdWorldOffice != null && ids.Contains(p.IdWorldOffice.Value)) || codigos.Contains(p.Codigo))
            .ToListAsync(ct);

        var porIdWo = existentes.Where(p => p.IdWorldOffice != null).ToDictionary(p => p.IdWorldOffice!.Value);
        var porCodigo = existentes.ToDictionary(p => p.Codigo);
        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var vistosEnLote = new HashSet<int>();

        foreach (var item in items)
        {
            var codigo = ProductoService.NormalizarCodigo(item.Codigo);
            if (codigo.Length == 0 || string.IsNullOrWhiteSpace(item.Nombre))
            {
                omitidos++;
                avisos.Add($"IdInventario {item.IdWorldOffice}: sin código o nombre, se omite.");
                continue;
            }

            if (!vistosEnLote.Add(item.IdWorldOffice))
            {
                omitidos++;
                continue;
            }

            var iva = item.Iva is 0 or 5 or 19 ? item.Iva : 19;
            var nombre = Truncar(item.Nombre.Trim(), 500)!;
            var unidad = Truncar(item.UnidadMedida?.Trim(), 50);

            if (porIdWo.TryGetValue(item.IdWorldOffice, out var producto))
            {
                // Ya lo conocemos por su IdInventario: se refrescan los datos de WO. El código solo
                // cambia si no choca con otro producto (si choca, se avisa y se deja el anterior).
                if (producto.Codigo != codigo)
                {
                    if (porCodigo.ContainsKey(codigo))
                        avisos.Add($"IdInventario {item.IdWorldOffice}: el código '{codigo}' ya lo usa otro producto, se conserva '{producto.Codigo}'.");
                    else
                    {
                        porCodigo.Remove(producto.Codigo);
                        producto.Codigo = codigo;
                        porCodigo[codigo] = producto;
                    }
                }

                producto.Nombre = nombre;
                producto.UnidadMedida = unidad;
                producto.Iva = iva;
                producto.Activo = item.Activo;
                actualizados++;
            }
            else if (porCodigo.TryGetValue(codigo, out var local))
            {
                if (local.IdWorldOffice is not null && local.IdWorldOffice != item.IdWorldOffice)
                {
                    omitidos++;
                    avisos.Add($"El código '{codigo}' ya está enlazado al IdInventario {local.IdWorldOffice}; el IdInventario {item.IdWorldOffice} se omite.");
                    continue;
                }

                // Producto creado a mano antes de que llegara el de WO con el mismo código: se enlaza.
                local.IdWorldOffice = item.IdWorldOffice;
                local.Origen = OrigenRegistro.WorldOffice;
                local.Nombre = nombre;
                local.UnidadMedida = unidad;
                local.Iva = iva;
                local.Activo = item.Activo;
                porIdWo[item.IdWorldOffice] = local;
                actualizados++;
            }
            else
            {
                // Producto nuevo de WO: nace SIN precio de venta (WO no lo tiene en el catálogo);
                // hasta que se le ponga uno no se puede vender (ver RemisionService).
                var nuevo = new Producto
                {
                    Codigo = codigo,
                    Nombre = nombre,
                    UnidadMedida = unidad,
                    PrecioVenta = 0,
                    Iva = iva,
                    Origen = OrigenRegistro.WorldOffice,
                    Activo = item.Activo,
                    IdWorldOffice = item.IdWorldOffice,
                    FechaCreacion = ahora
                };
                db.Productos.Add(nuevo);
                porIdWo[item.IdWorldOffice] = nuevo;
                porCodigo[codigo] = nuevo;
                creados++;
            }
        }

        await db.SaveChangesAsync(ct);
        return new SincResultadoDto(creados, actualizados, omitidos, avisos);
    }

    public async Task<SincResultadoDto> SincronizarExistenciasAsync(IReadOnlyList<SincExistenciaDto> items, DateTime leidoEn, CancellationToken ct = default)
    {
        var avisos = new List<string>();
        int creados = 0, actualizados = 0, omitidos = 0;

        var idsWo = items.Select(i => i.IdWorldOffice).Distinct().ToList();
        var mapa = await db.Productos
            .Where(p => p.IdWorldOffice != null && idsWo.Contains(p.IdWorldOffice.Value))
            .Select(p => new { p.Id, IdWo = p.IdWorldOffice!.Value })
            .ToDictionaryAsync(x => x.IdWo, x => x.Id, ct);

        var productoIds = mapa.Values.ToList();
        var actuales = await db.ProductoExistencias
            .Where(e => productoIds.Contains(e.ProductoId))
            .ToDictionaryAsync(e => (e.ProductoId, e.Bodega), ct);

        var tocados = new HashSet<int>();

        foreach (var item in items)
        {
            if (!mapa.TryGetValue(item.IdWorldOffice, out var productoId))
            {
                // Existencia de un producto que aún no llegó (o que no sincronizamos): se ignora; la
                // próxima corrida la trae cuando el producto exista.
                omitidos++;
                continue;
            }

            var bodega = Truncar(item.Bodega.Trim(), 30)!;
            if (actuales.TryGetValue((productoId, bodega), out var fila))
            {
                if (fila.Cantidad != item.Cantidad)
                {
                    fila.Cantidad = item.Cantidad;
                    actualizados++;
                }
            }
            else
            {
                var nueva = new ProductoExistencia { ProductoId = productoId, Bodega = bodega, Cantidad = item.Cantidad };
                db.ProductoExistencias.Add(nueva);
                actuales[(productoId, bodega)] = nueva;
                creados++;
            }

            tocados.Add(productoId);
        }

        await db.SaveChangesAsync(ct);

        if (tocados.Count > 0)
        {
            var ahora = timeProvider.GetUtcNow().UtcDateTime;
            var ids = tocados.ToList();
            foreach (var producto in await db.Productos.Where(p => ids.Contains(p.Id)).ToListAsync(ct))
                producto.ExistenciaActualizadaEn = ahora;
            await db.SaveChangesAsync(ct);
        }

        // Hora de la "foto": se registra siempre, aunque no haya cambios (es el latido de la sincronización).
        var estado = await db.EstadoIntegracionWo.FirstOrDefaultAsync(ct);
        if (estado is null)
        {
            estado = new EstadoIntegracionWo { Id = 1 };
            db.EstadoIntegracionWo.Add(estado);
        }

        var leidoUtc = leidoEn.ToUniversalTime();
        if (estado.UltimaFotoExistenciasEn is null || leidoUtc > estado.UltimaFotoExistenciasEn)
            estado.UltimaFotoExistenciasEn = leidoUtc;
        await db.SaveChangesAsync(ct);

        if (omitidos > 0)
            avisos.Add($"{omitidos} existencia(s) de productos desconocidos se ignoraron.");

        return new SincResultadoDto(creados, actualizados, omitidos, avisos);
    }

    public async Task<SincResultadoDto> SincronizarCompradoresAsync(IReadOnlyList<SincCompradorDto> items, CancellationToken ct = default)
    {
        var avisos = new List<string>();
        int creados = 0, actualizados = 0, omitidos = 0;

        var idsTexto = items.Select(i => i.IdWorldOffice.ToString()).Distinct().ToList();
        var documentos = items.Select(i => CompradorService.NormalizarDocumento(i.NumeroDocumento)).Where(d => d.Length > 0).Distinct().ToList();

        var existentes = await db.Compradores
            .Where(c => (c.CodigoWorldOffice != null && idsTexto.Contains(c.CodigoWorldOffice)) || documentos.Contains(c.NumeroDocumento))
            .ToListAsync(ct);

        var porCodigoWo = existentes.Where(c => c.CodigoWorldOffice != null)
            .GroupBy(c => c.CodigoWorldOffice!).ToDictionary(g => g.Key, g => g.First());
        var porDocumento = existentes.ToDictionary(c => c.NumeroDocumento);
        var ahora = timeProvider.GetUtcNow().UtcDateTime;
        var vistos = new HashSet<int>();

        foreach (var item in items)
        {
            var documento = CompradorService.NormalizarDocumento(item.NumeroDocumento);
            var tipo = (item.TipoDocumento ?? "").Trim().ToUpperInvariant();

            if (documento.Length == 0 || string.IsNullOrWhiteSpace(item.PrimerNombre))
            {
                omitidos++;
                avisos.Add($"IdTercero {item.IdWorldOffice}: sin documento o nombre, se omite.");
                continue;
            }

            if (!CompradorService.TiposDocumento.Contains(tipo))
            {
                omitidos++;
                avisos.Add($"IdTercero {item.IdWorldOffice}: tipo de documento '{tipo}' no soportado, se omite.");
                continue;
            }

            if (!vistos.Add(item.IdWorldOffice))
            {
                omitidos++;
                continue;
            }

            var codigoWo = item.IdWorldOffice.ToString();
            var esNuevo = false;

            if (!porCodigoWo.TryGetValue(codigoWo, out var comprador))
            {
                if (porDocumento.TryGetValue(documento, out var local))
                {
                    if (local.CodigoWorldOffice is not null && local.CodigoWorldOffice != codigoWo)
                    {
                        omitidos++;
                        avisos.Add($"El documento {documento} ya está enlazado al tercero WO {local.CodigoWorldOffice}; IdTercero {item.IdWorldOffice} se omite.");
                        continue;
                    }

                    comprador = local; // creado a mano antes: se enlaza con el tercero de WO
                }
                else
                {
                    comprador = new Comprador
                    {
                        TipoDocumento = tipo,
                        NumeroDocumento = documento,
                        PrimerNombre = item.PrimerNombre.Trim(),
                        NombreCompleto = "",
                        Email = "",
                        FechaCreacion = ahora
                    };
                    db.Compradores.Add(comprador);
                    esNuevo = true;
                }
            }
            else if (comprador.NumeroDocumento != documento && porDocumento.ContainsKey(documento))
            {
                omitidos++;
                avisos.Add($"IdTercero {item.IdWorldOffice}: el documento nuevo {documento} ya lo tiene otro comprador; se conserva el anterior.");
                continue;
            }

            comprador.Origen = OrigenRegistro.WorldOffice;
            comprador.CodigoWorldOffice = codigoWo;
            comprador.TipoDocumento = tipo;
            comprador.NumeroDocumento = documento;

            // WO manda en todo salvo el correo: si el comprador ya tenía uno aquí se respeta (el que
            // se digitó en el punto de venta), y solo se completa cuando estaba vacío.
            var correoWo = item.Email?.Trim() ?? "";
            var correo = string.IsNullOrWhiteSpace(comprador.Email) ? correoWo : comprador.Email;

            CompradorService.Aplicar(comprador, new CompradorGuardarDto(
                item.TipoPersona,
                tipo,
                documento,
                Truncar(item.PrimerNombre.Trim(), 200)!,
                Truncar(item.SegundoNombre?.Trim(), 100),
                Truncar(item.PrimerApellido?.Trim(), 100),
                Truncar(item.SegundoApellido?.Trim(), 100),
                Truncar(item.Direccion?.Trim(), 300),
                Truncar(item.Ciudad?.Trim(), 100),
                Truncar(item.Telefono?.Trim(), 50),
                Truncar(correo, 200) ?? "",
                item.Activo));

            porCodigoWo[codigoWo] = comprador;
            porDocumento[documento] = comprador;
            if (esNuevo) creados++; else actualizados++;
        }

        await db.SaveChangesAsync(ct);
        return new SincResultadoDto(creados, actualizados, omitidos, avisos);
    }

    private static string? Truncar(string? valor, int max)
        => string.IsNullOrEmpty(valor) ? valor : (valor.Length <= max ? valor : valor[..max]);
}
