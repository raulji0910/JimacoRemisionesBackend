using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface IInventarioService
{
    Task<EstadoInventarioDto> ObtenerEstadoAsync(CancellationToken ct = default);

    // Carga/actualiza productos y su existencia desde un lote de filas exportadas de World Office. Es
    // idempotente: se puede repetir para refrescar la foto. Nunca borra productos.
    Task<ImportarInventarioResultadoDto> ImportarAsync(ImportarInventarioDto dto, CancellationToken ct = default);
}
