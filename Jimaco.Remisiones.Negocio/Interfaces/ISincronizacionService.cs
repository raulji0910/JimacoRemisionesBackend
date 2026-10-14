using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface ISincronizacionService
{
    // Upsert por IdWorldOffice (o por código si el producto ya existía local). Nunca toca el
    // precio de venta ni el costo: son de esta app.
    Task<SincResultadoDto> SincronizarProductosAsync(IReadOnlyList<SincProductoDto> items, CancellationToken ct = default);

    // leidoEn: cuándo se leyó WO (UTC). Actualiza la hora de la última foto aunque items venga vacío.
    Task<SincResultadoDto> SincronizarExistenciasAsync(IReadOnlyList<SincExistenciaDto> items, DateTime leidoEn, CancellationToken ct = default);

    // Upsert por IdWorldOffice (o por documento si el comprador ya existía local). WO manda en todo
    // salvo el correo: solo se completa si el comprador local no tenía uno.
    Task<SincResultadoDto> SincronizarCompradoresAsync(IReadOnlyList<SincCompradorDto> items, CancellationToken ct = default);
}
