using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/compradores")]
public class CompradoresController(ICompradorService compradorService) : ControllerBase
{
    [HttpGet]
    public async Task<ActionResult<PaginaResultado<CompradorDto>>> Buscar(
        [FromQuery] string? buscar,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 10,
        CancellationToken ct = default)
        => Ok(await compradorService.BuscarAsync(buscar, pagina, tamanoPagina, ct));

    // Autocompletar al digitar el NIT/cédula en una venta: 404 si es un comprador nuevo.
    [HttpGet("por-documento/{numeroDocumento}")]
    public async Task<ActionResult<CompradorDto>> ObtenerPorDocumento(string numeroDocumento, CancellationToken ct)
    {
        var comprador = await compradorService.ObtenerPorDocumentoAsync(numeroDocumento, ct);
        return comprador is null ? NotFound() : Ok(comprador);
    }

    [HttpGet("{id:int}")]
    public async Task<ActionResult<CompradorDto>> ObtenerPorId(int id, CancellationToken ct)
    {
        var comprador = await compradorService.ObtenerPorIdAsync(id, ct);
        return comprador is null ? NotFound() : Ok(comprador);
    }

    [HttpPost]
    public async Task<ActionResult<CompradorDto>> Crear(CompradorGuardarDto dto, CancellationToken ct)
    {
        try
        {
            var creado = await compradorService.CrearAsync(dto, ct);
            return CreatedAtAction(nameof(ObtenerPorId), new { id = creado.Id }, creado);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }

    [HttpPut("{id:int}")]
    public async Task<ActionResult<CompradorDto>> Actualizar(int id, CompradorGuardarDto dto, CancellationToken ct)
    {
        try
        {
            var actualizado = await compradorService.ActualizarAsync(id, dto, ct);
            return actualizado is null ? NotFound() : Ok(actualizado);
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
    }
}
