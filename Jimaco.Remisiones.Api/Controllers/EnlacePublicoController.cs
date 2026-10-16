using System.Security.Cryptography;
using System.Text;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

public record EnlaceRemisionDto(string Token);

// Enlace público al PDF de una remisión para mandarlo por WhatsApp: el cliente lo abre sin iniciar sesión.
// El token es un HMAC del id con el secreto de la app, así que no se puede adivinar ni cambiar de remisión.
[ApiController]
public class EnlacePublicoController(IRemisionService remisionService, IConfiguration configuration) : ControllerBase
{
    private string Firmar(int id)
    {
        var llave = Encoding.UTF8.GetBytes(configuration["Jwt:Secret"] ?? throw new InvalidOperationException("Falta Jwt:Secret."));
        var hash = HMACSHA256.HashData(llave, Encoding.UTF8.GetBytes($"remision-pdf:{id}"));
        return Convert.ToHexString(hash)[..32].ToLowerInvariant();
    }

    [Authorize]
    [HttpGet("api/remisiones/{id:int}/enlace-publico")]
    public async Task<ActionResult<EnlaceRemisionDto>> ObtenerEnlace(int id, CancellationToken ct)
    {
        var remision = await remisionService.ObtenerPorIdAsync(id, ct);
        if (remision is null)
            return NotFound();
        if (remision.EstadoPago != EstadoPago.Pagada)
            return Conflict(new { mensaje = "Solo se comparte una remisión ya pagada." });
        return Ok(new EnlaceRemisionDto(Firmar(id)));
    }

    [AllowAnonymous]
    [HttpGet("api/publico/remisiones/{id:int}/pdf")]
    public async Task<IActionResult> ObtenerPdfPublico(int id, [FromQuery(Name = "t")] string? token, CancellationToken ct)
    {
        var esperado = Encoding.UTF8.GetBytes(Firmar(id));
        var recibido = Encoding.UTF8.GetBytes(token ?? "");
        if (!CryptographicOperations.FixedTimeEquals(esperado, recibido))
            return NotFound();

        var remision = await remisionService.ObtenerPorIdAsync(id, ct);
        if (remision is null || remision.EstadoPago != EstadoPago.Pagada)
            return NotFound();

        var pdf = await remisionService.GenerarPdfAsync(id, ct);
        if (pdf is null)
            return NotFound();
        // inline: se abre en el visor del celular en vez de forzar la descarga.
        Response.Headers.ContentDisposition = $"inline; filename=\"{remision.ConsecutivoFormateado}.pdf\"";
        return File(pdf, "application/pdf");
    }
}
