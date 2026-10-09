using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface ISincronizacionService
{
    // Upsert por IdWorldOffice (o por código si el producto ya existía local). Nunca toca el
    // precio de venta ni el costo: son de esta app.
    Task<SincResultadoDto> SincronizarProductosAsync(IReadOnlyList<SincProductoDto> items, CancellationToken ct = default);

    Task<SincResultadoDto> SincronizarExistenciasAsync(IReadOnlyList<SincExistenciaDto> items, CancellationToken ct = default);

    // Upsert por IdWorldOffice (o por documento si el comprador ya existía local). WO manda en todo
    // salvo el correo: solo se completa si el comprador local no tenía uno.
    Task<SincResultadoDto> SincronizarCompradoresAsync(IReadOnlyList<SincCompradorDto> items, CancellationToken ct = default);
}
