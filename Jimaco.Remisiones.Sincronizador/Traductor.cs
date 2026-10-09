using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;

namespace Jimaco.Remisiones.Sincronizador;

/// <summary>
/// Traduce las filas crudas de World Office a los DTO de la API de Remisiones (formatos, códigos
/// y convenciones de WO: IVA como fracción, Activo = -1, tipo de documento numérico...).
/// </summary>
public static class Traductor
{
    // IdTipoIdentificacion de WO (= código DIAN) -> código de tipo de documento de esta app.
    // Salió de la tabla "Terceros - Tipo de Identificación" de Jimaco; 7 (Consumidor final) no se
    // sincroniza.
    private static readonly Dictionary<int, string> TiposPorId = new()
    {
        [13] = "CC", [31] = "NIT", [22] = "CE", [41] = "PAS", [12] = "TI", [11] = "RC", [21] = "TE",
        [91] = "NUIP", [42] = "DIE", [47] = "PEP", [44] = "PPT", [43] = "NITE", [46] = "CD",
        [103] = "CN", [102] = "AS", [4] = "OTRO"
    };

    public static SincProductoDto Producto(ProductoWoRow fila) => new(
        fila.IdWorldOffice,
        fila.Codigo.Trim(),
        fila.Nombre.Trim(),
        string.IsNullOrWhiteSpace(fila.UnidadMedida) ? null : fila.UnidadMedida.Trim(),
        Iva(fila.IvaFraccion),
        fila.Activo != 0);

    // 0.19 -> 19. Si ya viniera como porcentaje (19) se respeta.
    public static int Iva(decimal? valor)
    {
        if (valor is null) return 0;
        var porcentaje = valor.Value <= 1m ? valor.Value * 100m : valor.Value;
        return (int)Math.Round(porcentaje, MidpointRounding.AwayFromZero);
    }

    /// <summary>Null si el tercero no se puede sincronizar (sin documento, sin nombre o tipo no soportado).</summary>
    public static SincCompradorDto? Comprador(CompradorWoRow fila, out string? motivoOmitido)
    {
        motivoOmitido = null;

        var documento = fila.Identificacion?.Trim();
        if (string.IsNullOrEmpty(documento))
        {
            motivoOmitido = $"IdTercero {fila.IdWorldOffice}: sin identificación.";
            return null;
        }

        string? tipo = null;
        if (fila.IdTipo is not null && TiposPorId.TryGetValue(fila.IdTipo.Value, out var porId))
            tipo = porId;
        else if (!string.IsNullOrWhiteSpace(fila.TipoAbrev))
        {
            // Respaldo si WO agrega un tipo nuevo con id distinto: se acepta solo si la abreviatura es de las que esta app conoce.
            var abrev = fila.TipoAbrev.Trim().ToUpperInvariant() switch { "PA" => "PAS", var otro => otro };
            if (TiposPorId.ContainsValue(abrev)) tipo = abrev;
        }

        if (tipo is null)
        {
            motivoOmitido = $"IdTercero {fila.IdWorldOffice}: tipo de identificación {fila.IdTipo} no soportado.";
            return null;
        }

        var nombre = Limpio(fila.Nombre);
        var primerNombre = Limpio(fila.Primer_Nombre);
        var primerApellido = Limpio(fila.Primer_Apellido);
        var apellidos = Limpio(fila.Apellidos);

        // Un NIT sin apellidos es una empresa (razón social); un NIT con apellidos es una persona
        // natural con negocio. Todo lo que no es NIT es persona natural.
        var juridica = tipo == "NIT" && primerApellido is null && apellidos is null;

        string? nombreFinal;
        string? segundoNombre = null, apellidoUno = null, apellidoDos = null;

        if (juridica)
        {
            nombreFinal = nombre ?? primerNombre;
        }
        else
        {
            nombreFinal = primerNombre ?? nombre;
            segundoNombre = Limpio(fila.Segundo_Nombre);
            apellidoUno = primerApellido ?? apellidos;
            apellidoDos = Limpio(fila.Segundo_Apellido);
        }

        if (nombreFinal is null)
        {
            motivoOmitido = $"IdTercero {fila.IdWorldOffice}: sin nombre.";
            return null;
        }

        return new SincCompradorDto(
            fila.IdWorldOffice,
            tipo,
            documento,
            juridica ? TipoPersona.Juridica : TipoPersona.Natural,
            nombreFinal,
            segundoNombre,
            apellidoUno,
            apellidoDos,
            Limpio(fila.Direccion),
            Limpio(fila.Ciudad),
            Telefono(fila.Telefonos) ?? Telefono(fila.Telefono2),
            Correo(fila.Email),
            fila.Activo != 0);
    }

    private static string? Limpio(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // WO a veces guarda "0" o vacío cuando no hay teléfono.
    private static string? Telefono(string? valor)
    {
        var limpio = Limpio(valor);
        return limpio is null or "0" ? null : limpio;
    }

    // Campo libre: puede traer varios correos separados por ; , o espacio. Se toma el primero válido.
    private static string? Correo(string? valor)
    {
        var limpio = Limpio(valor);
        if (limpio is null) return null;

        foreach (var candidato in limpio.Split([';', ',', ' ', '/'], StringSplitOptions.RemoveEmptyEntries))
        {
            var c = candidato.Trim();
            var arroba = c.IndexOf('@');
            if (arroba > 0 && arroba < c.Length - 1 && c.IndexOf('.', arroba) > arroba)
                return c;
        }

        return null;
    }
}
