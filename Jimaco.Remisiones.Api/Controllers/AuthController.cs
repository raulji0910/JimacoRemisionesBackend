using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace Jimaco.Remisiones.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController(IAuthService authService) : ControllerBase
{
    [HttpPost("login")]
    public async Task<ActionResult<LoginResponseDto>> Login(LoginRequestDto dto, CancellationToken ct)
    {
        var resultado = await authService.LoginAsync(dto.Email, dto.Password, ct);
        if (resultado is null)
            return Unauthorized(new { mensaje = "Credenciales inválidas." });

        return Ok(resultado);
    }
}
