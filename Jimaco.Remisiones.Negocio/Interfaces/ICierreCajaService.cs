using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Negocio.Interfaces;

public interface ICierreCajaService
{
    // Rango de días calendario de Colombia, ambos extremos incluidos.
    Task<CierreCajaDto> ObtenerAsync(DateOnly desde, DateOnly hasta, CancellationToken ct = default);
}
