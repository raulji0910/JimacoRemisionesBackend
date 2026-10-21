using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface ICierreCajaService
{
    Task<CierreCajaDto> ObtenerAsync(DateOnly fecha, CancellationToken ct = default);
}
