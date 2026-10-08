using System.ComponentModel.DataAnnotations;

namespace Jimaco.Remisiones.Modelo.Entidades;

public enum TipoPersona
{
    Natural = 1,
    Juridica = 2
}

// Quien compra en el sitio. Replica los campos del "tercero" de World Office (tipo/número/DV,
// nombres separados, dirección principal) para poder escribirlo allá en la Fase 2 sin transformar
// nada. No es un módulo de clientes completo: se crea en el momento de la venta y se reutiliza
// buscando por número de documento.
public class Comprador
{
    public int Id { get; set; }

    public TipoPersona TipoPersona { get; set; } = TipoPersona.Natural;

    // CC, NIT, CE, PAS, TI, etc. (mismos códigos que World Office).
    [MaxLength(10)]
    public required string TipoDocumento { get; set; }

    [MaxLength(30)]
    public required string NumeroDocumento { get; set; }

    // Dígito de verificación, solo aplica a NIT.
    [MaxLength(2)]
    public string? Dv { get; set; }

    // En persona jurídica, PrimerNombre guarda la razón social completa.
    [MaxLength(200)]
    public required string PrimerNombre { get; set; }

    [MaxLength(100)]
    public string? SegundoNombre { get; set; }

    [MaxLength(100)]
    public string? PrimerApellido { get; set; }

    [MaxLength(100)]
    public string? SegundoApellido { get; set; }

    // Nombre armado (razón social, o nombres + apellidos) — se guarda para poder buscar y mostrar.
    [MaxLength(400)]
    public required string NombreCompleto { get; set; }

    [MaxLength(300)]
    public string? Direccion { get; set; }

    [MaxLength(100)]
    public string? Ciudad { get; set; }

    [MaxLength(50)]
    public string? Telefono { get; set; }

    [MaxLength(200)]
    public required string Email { get; set; }

    public OrigenRegistro Origen { get; set; } = OrigenRegistro.Local;

    // IdTercero en World Office, cuando el registro viene de allá o ya se empujó (Fase 2).
    [MaxLength(50)]
    public string? CodigoWorldOffice { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; }
}
