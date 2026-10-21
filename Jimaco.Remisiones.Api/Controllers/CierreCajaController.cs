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
    // fecha = día calendario de Colombia (yyyy-MM-dd); sin parámetro, hoy.
    [HttpGet]
    public async Task<ActionResult<CierreCajaDto>> Obtener([FromQuery] DateOnly? fecha, CancellationToken ct)
    {
        var dia = fecha ?? DateOnly.FromDateTime(tiempo.GetUtcNow().UtcDateTime.AddHours(-5));
        return Ok(await cierre.ObtenerAsync(dia, ct));
    }
}
