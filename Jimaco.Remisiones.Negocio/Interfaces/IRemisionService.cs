using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface IRemisionService
{
    Task<PaginaResultado<RemisionResumenDto>> BuscarAsync(EstadoPago? estadoPago, bool soloEnviosPendientes, string? texto, int pagina, int tamanoPagina, CancellationToken ct = default);
    Task<RemisionDetalleDto?> ObtenerPorIdAsync(int id, CancellationToken ct = default);
    Task<RemisionDetalleDto> CrearAsync(RemisionGuardarDto dto, string? usuario, CancellationToken ct = default);

    // Solo mientras está Pendiente de pago.
    Task<RemisionDetalleDto?> ActualizarAsync(int id, RemisionGuardarDto dto, CancellationToken ct = default);

    // Pendiente -> Pagada. Si la entrega es en sitio queda Entregada de una vez. Envía el correo.
    Task<PagarResultadoDto?> MarcarPagadaAsync(int id, MarcarPagadaDto dto, string? usuario, CancellationToken ct = default);

    Task<EnviarCorreoResultadoDto?> EnviarCorreoAsync(int id, CancellationToken ct = default);

    // Copia a compras de una remisión pagada de envío posterior (se usa cuando pasa a envío después de cobrada).
    Task<bool> EnviarCopiaComprasAsync(int id, CancellationToken ct = default);
    Task<RemisionDetalleDto?> CambiarTipoEntregaAsync(int id, CambiarTipoEntregaDto dto, CancellationToken ct = default);
    Task<RemisionDetalleDto?> MarcarEntregadaAsync(int id, CancellationToken ct = default);

    // Pagada: solo Admin (y devuelve el stock). Pendiente: cualquiera.
    Task<RemisionDetalleDto?> AnularAsync(int id, AnularRemisionDto dto, bool esAdmin, string? usuario = null, CancellationToken ct = default);

    Task<byte[]?> GenerarPdfAsync(int id, CancellationToken ct = default);
}
