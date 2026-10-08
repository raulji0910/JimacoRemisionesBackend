using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface ICompradorService
{
    Task<PaginaResultado<CompradorDto>> BuscarAsync(string? texto, int pagina, int tamanoPagina, CancellationToken ct = default);

    // Autocompletar: coincidencia exacta por número de documento (ignora puntos, espacios y guiones).
    Task<CompradorDto?> ObtenerPorDocumentoAsync(string numeroDocumento, CancellationToken ct = default);
    Task<CompradorDto?> ObtenerPorIdAsync(int id, CancellationToken ct = default);
    Task<CompradorDto> CrearAsync(CompradorGuardarDto dto, CancellationToken ct = default);
    Task<CompradorDto?> ActualizarAsync(int id, CompradorGuardarDto dto, CancellationToken ct = default);
}
