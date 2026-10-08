using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.DTOs;

public record CompradorDto(
    int Id,
    TipoPersona TipoPersona,
    string TipoDocumento,
    string NumeroDocumento,
    string? Dv,
    string PrimerNombre,
    string? SegundoNombre,
    string? PrimerApellido,
    string? SegundoApellido,
    string NombreCompleto,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string Email,
    OrigenRegistro Origen,
    bool Activo);

// Mismo cuerpo para crear y editar. En persona jurídica solo se usa PrimerNombre (razón social).
public record CompradorGuardarDto(
    TipoPersona TipoPersona,
    string TipoDocumento,
    string NumeroDocumento,
    string PrimerNombre,
    string? SegundoNombre,
    string? PrimerApellido,
    string? SegundoApellido,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string Email,
    bool Activo = true);
