using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/inventario")]
public class InventarioController(IInventarioService inventarioService) : ControllerBase
{
    // Hora de la última foto de existencias de World Office y cuántos productos faltan por tener precio.
    [HttpGet("estado")]
    public async Task<ActionResult<EstadoInventarioDto>> Estado(CancellationToken ct)
        => Ok(await inventarioService.ObtenerEstadoAsync(ct));

    // Carga del inventario exportado de WO (CSV que la pantalla "Importar inventario" parte en lotes).
    [HttpPost("importar")]
    [Authorize(Roles = "Admin")]
    [RequestSizeLimit(20_000_000)]
    public async Task<ActionResult<ImportarInventarioResultadoDto>> Importar(ImportarInventarioDto dto, CancellationToken ct)
        => Ok(await inventarioService.ImportarAsync(dto, ct));
}
