using System.ComponentModel.DataAnnotations;

namespace Jimaco.Remisiones.Modelo.Entidades;

// Por ahora un solo rol operativo ("Operador": crea remisiones, cobra y gestiona entregas).
// Admin además gestiona usuarios y puede anular remisiones ya pagadas. Más adelante se puede
// separar en cajero / administrador / logística sin tocar el resto.
public enum RolUsuario
{
    Admin = 1,
    Operador = 2,

    // Usuario de servicio del Sincronizador (agente en la red de World Office). Solo debe usarse
    // contra /api/sincronizacion/*; no es una persona.
    Sincronizador = 3
}

public class Usuario
{
    public int Id { get; set; }

    [MaxLength(200)]
    public required string Nombre { get; set; }

    [MaxLength(200)]
    public required string Email { get; set; }

    public required string PasswordHash { get; set; }

    public RolUsuario Rol { get; set; } = RolUsuario.Operador;

    public bool Activo { get; set; } = true;
}
