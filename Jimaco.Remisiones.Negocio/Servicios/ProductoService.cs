using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Negocio.Servicios;

public class ProductoService(AppDbContext db, TimeProvider timeProvider) : IProductoService
{
    private static readonly int[] TarifasIva = [0, 5, 19];

    public async Task<PaginaResultado<ProductoDto>> BuscarAsync(string? texto, bool soloActivos, int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, 100);

        var query = db.Productos.AsQueryable();

        if (soloActivos)
            query = query.Where(p => p.Activo);

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var patron = $"%{texto.Trim()}%";
            query = query.Where(p => EF.Functions.Like(p.Codigo, patron) || EF.Functions.Like(p.Nombre, patron));
        }

        query = query.OrderBy(p => p.Nombre);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(p => new ProductoDto(p.Id, p.Codigo, p.Nombre, p.UnidadMedida, p.PrecioVenta, p.Costo, p.Iva, p.Origen, p.Activo))
            .ToListAsync(ct);

        return new PaginaResultado<ProductoDto>(items, total, pagina, tamanoPagina);
    }

    public async Task<ProductoDto?> ObtenerPorIdAsync(int id, CancellationToken ct = default)
    {
        return await db.Productos
            .Where(p => p.Id == id)
            .Select(p => new ProductoDto(p.Id, p.Codigo, p.Nombre, p.UnidadMedida, p.PrecioVenta, p.Costo, p.Iva, p.Origen, p.Activo))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<ProductoDto> CrearAsync(ProductoCrearDto dto, CancellationToken ct = default)
    {
        var codigo = NormalizarCodigo(dto.Codigo);
        Validar(codigo, dto.Nombre, dto.PrecioVenta, dto.Costo, dto.Iva);

        if (await db.Productos.AnyAsync(p => p.Codigo == codigo, ct))
            throw new InvalidOperationException($"Ya existe un producto con el código '{codigo}'.");

        var producto = new Producto
        {
            Codigo = codigo,
            Nombre = dto.Nombre.Trim(),
            UnidadMedida = dto.UnidadMedida?.Trim(),
            PrecioVenta = dto.PrecioVenta,
            Costo = dto.Costo,
            Iva = dto.Iva,
            Origen = OrigenRegistro.Local,
            Activo = true,
            FechaCreacion = timeProvider.GetUtcNow().UtcDateTime
        };

        db.Productos.Add(producto);
        await db.SaveChangesAsync(ct);

        return Map(producto);
    }

    public async Task<ProductoDto?> ActualizarAsync(int id, ProductoActualizarDto dto, CancellationToken ct = default)
    {
        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (producto is null)
            return null;

        var codigo = NormalizarCodigo(dto.Codigo);
        Validar(codigo, dto.Nombre, dto.PrecioVenta, dto.Costo, dto.Iva);

        if (await db.Productos.AnyAsync(p => p.Id != id && p.Codigo == codigo, ct))
            throw new InvalidOperationException($"Ya existe un producto con el código '{codigo}'.");

        // El código de un producto que ya viene de World Office no se cambia: es la llave con la
        // que se descuenta el inventario allá.
        if (producto.Origen == OrigenRegistro.WorldOffice && producto.Codigo != codigo)
            throw new InvalidOperationException("No se puede cambiar el código de un producto que viene de World Office.");

        producto.Codigo = codigo;
        producto.Nombre = dto.Nombre.Trim();
        producto.UnidadMedida = dto.UnidadMedida?.Trim();
        producto.PrecioVenta = dto.PrecioVenta;
        producto.Costo = dto.Costo;
        producto.Iva = dto.Iva;
        producto.Activo = dto.Activo;

        await db.SaveChangesAsync(ct);
        return Map(producto);
    }

    public async Task<bool> EliminarAsync(int id, CancellationToken ct = default)
    {
        var producto = await db.Productos.FirstOrDefaultAsync(p => p.Id == id, ct);
        if (producto is null)
            return false;

        if (await db.RemisionItems.AnyAsync(i => i.ProductoId == id, ct))
            throw new InvalidOperationException("No se puede eliminar: este producto está en una o más remisiones. Desactívalo en su lugar.");

        db.Productos.Remove(producto);
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static void Validar(string codigo, string nombre, decimal precioVenta, decimal? costo, int iva)
    {
        if (string.IsNullOrWhiteSpace(codigo))
            throw new InvalidOperationException("El código es obligatorio.");
        if (string.IsNullOrWhiteSpace(nombre))
            throw new InvalidOperationException("El nombre es obligatorio.");
        if (precioVenta < 0)
            throw new InvalidOperationException("El precio de venta no puede ser negativo.");
        if (costo is < 0)
            throw new InvalidOperationException("El costo no puede ser negativo.");
        if (!TarifasIva.Contains(iva))
            throw new InvalidOperationException("El IVA debe ser 0, 5 o 19.");
    }

    // Mayúsculas y sin espacios en los extremos: el código es llave de cruce con World Office.
    private static string NormalizarCodigo(string codigo) => (codigo ?? "").Trim().ToUpperInvariant();

    private static ProductoDto Map(Producto p)
        => new(p.Id, p.Codigo, p.Nombre, p.UnidadMedida, p.PrecioVenta, p.Costo, p.Iva, p.Origen, p.Activo);
}
