using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/productos")]
public class ProductosController(IProductoService productoService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginaResultado<ProductoDto>>> Buscar(
        [FromQuery] string? buscar,
        [FromQuery] bool soloActivos = false,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 10,
        CancellationToken ct = default)
        => Ok(await productoService.BuscarAsync(buscar, soloActivos, pagina, tamanoPagina, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<ProductoDto>> ObtenerPorId(int id, CancellationToken ct)
    {
        var producto = await productoService.ObtenerPorIdAsync(id, ct);
        return producto is null ? NotFound() : Ok(producto);
    }

    [HttpPost]
    public async Task<ActionResult<ProductoDto>> Crear(ProductoCrearDto dto, CancellationToken ct)
    {
        try
        {
            var creado = await productoService.CrearAsync(dto, ct);
            return CreatedAtAction(nameof(ObtenerPorId), new { id = creado.Id }, creado);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<ProductoDto>> Actualizar(int id, ProductoActualizarDto dto, CancellationToken ct)
    {
        try
        {
            var actualizado = await productoService.ActualizarAsync(id, dto, ct);
            return actualizado is null ? NotFound() : Ok(actualizado);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpDelete("{id:int}")]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> Eliminar(int id, CancellationToken ct)
    {
        try
        {
            var eliminado = await productoService.EliminarAsync(id, ct);
            return eliminado ? NoContent() : NotFound();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
}
