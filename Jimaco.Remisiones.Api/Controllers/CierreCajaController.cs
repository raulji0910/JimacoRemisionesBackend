using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/cierre-caja")]
public class CierreCajaController(ICierreCajaService cierre, TimeProvider tiempo) : ControllerBase
{
    // desde / hasta = días calendario de Colombia (yyyy-MM-dd), ambos incluidos; sin parámetros, hoy.
    [HttpGet]
    public async Task<ActionResult<CierreCajaDto>> Obtener([FromQuery] DateOnly? desde, [FromQuery] DateOnly? hasta, CancellationToken ct)
    {
        var hoy = DateOnly.FromDateTime(tiempo.GetUtcNow().UtcDateTime.AddHours(-5));
        try
        {
            return Ok(await cierre.ObtenerAsync(desde ?? hasta ?? hoy, hasta ?? desde ?? hoy, ct));
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
}
