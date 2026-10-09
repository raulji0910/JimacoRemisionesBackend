using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

// Entrada del Sincronizador (agente dentro de la red de World Office). Se autentica como un
// usuario de servicio con rol Sincronizador por la misma puerta de login que todos.
[ApiController]
[Authorize(Roles = "Sincronizador,Admin")]
[Route("api/sincronizacion")]
public class SincronizacionController(ISincronizacionService sincronizacionService) : ControllerBase
{
    [HttpPost("productos")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<SincResultadoDto>> Productos(SincLoteProductosDto lote, CancellationToken ct)
        => Ok(await sincronizacionService.SincronizarProductosAsync(lote.Items, ct));

    [HttpPost("existencias")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<SincResultadoDto>> Existencias(SincLoteExistenciasDto lote, CancellationToken ct)
        => Ok(await sincronizacionService.SincronizarExistenciasAsync(lote.Items, ct));

    [HttpPost("compradores")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<SincResultadoDto>> Compradores(SincLoteCompradoresDto lote, CancellationToken ct)
        => Ok(await sincronizacionService.SincronizarCompradoresAsync(lote.Items, ct));
}
