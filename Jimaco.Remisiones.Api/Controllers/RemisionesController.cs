using System.Security.Claims;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/remisiones")]
public class RemisionesController(IRemisionService remisionService) : ControllerBase
{
    private string? NombreUsuario => User.FindFirstValue(ClaimTypes.Name) ?? User.FindFirstValue(ClaimTypes.Email);

    [HttpGet]
    public async Task<ActionResult<PaginaResultado<RemisionResumenDto>>> Buscar(
        [FromQuery] EstadoPago? estadoPago,
        [FromQuery] bool soloEnviosPendientes = false,
        [FromQuery] string? buscar = null,
        [FromQuery] int pagina = 1,
        [FromQuery] int tamanoPagina = 10,
        CancellationToken ct = default)
        => Ok(await remisionService.BuscarAsync(estadoPago, soloEnviosPendientes, buscar, pagina, tamanoPagina, ct));

    [HttpGet("{id:int}")]
    public async Task<ActionResult<RemisionDetalleDto>> ObtenerPorId(int id, CancellationToken ct)
    {
        var remision = await remisionService.ObtenerPorIdAsync(id, ct);
        return remision is null ? NotFound() : Ok(remision);
    }

    [HttpGet("{id:int}/pdf")]
    public async Task<IActionResult> ObtenerPdf(int id, CancellationToken ct)
    {
        var pdf = await remisionService.GenerarPdfAsync(id, ct);
        return pdf is null ? NotFound() : File(pdf, "application/pdf");
    }

    [HttpPost]
    public Task<ActionResult<RemisionDetalleDto>> Crear(RemisionGuardarDto dto, CancellationToken ct)
        => Ejecutar<RemisionDetalleDto>(async () =>
        {
            var creada = await remisionService.CrearAsync(dto, NombreUsuario, ct);
            return CreatedAtAction(nameof(ObtenerPorId), new { id = creada.Id }, creada);
        });

    [HttpPut("{id:int}")]
    public Task<ActionResult<RemisionDetalleDto>> Actualizar(int id, RemisionGuardarDto dto, CancellationToken ct)
        => Ejecutar<RemisionDetalleDto>(async () => Respuesta(await remisionService.ActualizarAsync(id, dto, ct)));

    [HttpPost("{id:int}/pagar")]
    public Task<ActionResult<PagarResultadoDto>> Pagar(int id, MarcarPagadaDto dto, CancellationToken ct)
        => Ejecutar<PagarResultadoDto>(async () => Respuesta(await remisionService.MarcarPagadaAsync(id, dto, NombreUsuario, ct)));

    [HttpPost("{id:int}/enviar-correo")]
    public Task<ActionResult<EnviarCorreoResultadoDto>> EnviarCorreo(int id, CancellationToken ct)
        => Ejecutar<EnviarCorreoResultadoDto>(async () => Respuesta(await remisionService.EnviarCorreoAsync(id, ct)));

    [HttpPut("{id:int}/entrega")]
    public Task<ActionResult<RemisionDetalleDto>> CambiarTipoEntrega(int id, CambiarTipoEntregaDto dto, CancellationToken ct)
        => Ejecutar<RemisionDetalleDto>(async () => Respuesta(await remisionService.CambiarTipoEntregaAsync(id, dto, ct)));

    [HttpPost("{id:int}/entregar")]
    public Task<ActionResult<RemisionDetalleDto>> Entregar(int id, CancellationToken ct)
        => Ejecutar<RemisionDetalleDto>(async () => Respuesta(await remisionService.MarcarEntregadaAsync(id, ct)));

    [HttpPost("{id:int}/anular")]
    public Task<ActionResult<RemisionDetalleDto>> Anular(int id, AnularRemisionDto dto, CancellationToken ct)
        => Ejecutar<RemisionDetalleDto>(async () => Respuesta(await remisionService.AnularAsync(id, dto, User.IsInRole("Admin"), NombreUsuario, ct)));

    private ActionResult<T> Respuesta<T>(T? valor) where T : class
        => valor is null ? NotFound() : Ok(valor);

    // Traduce los errores de regla de negocio a 409 y la concurrencia optimista (RowVersion: dos
    // personas tocando la misma remisión a la vez) a 409 con un mensaje claro.
    private async Task<ActionResult<T>> Ejecutar<T>(Func<Task<ActionResult<T>>> accion)
    {
        try
        {
            return await accion();
        }
        catch (InvalidOperationException ex)
        {
            return Conflict(new { mensaje = ex.Message });
        }
        catch (DbUpdateConcurrencyException)
        {
            return Conflict(new { mensaje = "La remisión fue modificada por otra persona. Recárgala e inténtalo de nuevo." });
        }
    }
}
