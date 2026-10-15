using System.Security.Claims;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Api.Controllers;

// Kardex y movimientos manuales de stock de un producto.
[ApiController]
[Authorize]
[Route("api/productos/{productoId:int}")]
public class InventarioController(IInventarioService inventarioService) : ControllerBase
{
    // Historial de movimientos, del más reciente al más antiguo.
    [HttpGet("movimientos")]
    public async Task<ActionResult<PaginaResultado<MovimientoDto>>> Movimientos(
        int productoId,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 20,
        CancellationToken ct = default)
    {
        var resultado = await inventarioService.ListarMovimientosAsync(productoId, pagina, tamanoPagina, ct);
        return resultado is null ? NotFound() : Ok(resultado);
    }

    // Entrada de mercancía o ajuste por conteo: solo administradores.
    [HttpPost("movimientos")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ProductoDto>> RegistrarMovimiento(int productoId, MovimientoManualDto dto, CancellationToken ct)
    {
        try
        {
            var producto = await inventarioService.RegistrarMovimientoAsync(productoId, dto, User.FindFirstValue(ClaimTypes.Name), ct);
            return producto is null ? NotFound() : Ok(producto);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { mensaje = "El stock de este producto cambió mientras lo editabas. Recarga e inténtalo de nuevo." });
        }
    }
}
