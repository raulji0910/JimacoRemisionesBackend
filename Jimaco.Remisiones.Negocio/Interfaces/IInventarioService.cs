using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface IInventarioService
{
    // Historial (kardex) de un producto, del más reciente al más antiguo.
    Task<PaginaResultado<MovimientoDto>?> ListarMovimientosAsync(int productoId, int pagina, int tamanoPagina, CancellationToken ct = default);

    // Entrada o ajuste manual. Devuelve el producto con su nuevo stock; null si no existe.
    Task<ProductoDto?> RegistrarMovimientoAsync(int productoId, MovimientoManualDto dto, string? usuario, CancellationToken ct = default);
}
