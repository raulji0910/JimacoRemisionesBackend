using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface IProductoService
{
    Task<PaginaResultado<ProductoDto>> BuscarAsync(string? texto, bool soloActivos, int pagina, int tamanoPagina, CancellationToken ct = default);
    Task<ProductoDto?> ObtenerPorIdAsync(int id, CancellationToken ct = default);
    Task<ProductoDto> CrearAsync(ProductoCrearDto dto, string? usuario = null, CancellationToken ct = default);
    Task<ProductoDto?> ActualizarAsync(int id, ProductoActualizarDto dto, CancellationToken ct = default);
    Task<bool> EliminarAsync(int id, CancellationToken ct = default);
}
