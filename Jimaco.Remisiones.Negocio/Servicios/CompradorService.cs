using System.Text.RegularExpressions;
using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.Negocio.Servicios;

public partial class CompradorService(AppDbContext db, TimeProvider timeProvider) : ICompradorService
{
    // Mismos códigos de tipo de documento que usa World Office para los terceros.
    internal static readonly string[] TiposDocumento = ["CC", "NIT", "CE", "PAS", "TI", "RC", "NUIP", "DIE", "TE", "PEP", "PPT", "NITE", "CD", "CN", "AS", "OTRO"];

    [GeneratedRegex(@"^[^@\s]+@[^@\s]+\.[^@\s]+$")]
    private static partial Regex RegexEmail();

    public async Task<PaginaResultado<CompradorDto>> BuscarAsync(string? texto, int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, 100);

        var query = db.Compradores.AsQueryable();

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var patron = $"%{texto.Trim()}%";
            var soloDigitos = new string(texto.Where(char.IsDigit).ToArray());
            var patronDigitos = $"%{soloDigitos}%";
            query = query.Where(c =>
                EF.Functions.Like(c.NombreCompleto, patron) ||
                EF.Functions.Like(c.NumeroDocumento, patron) ||
                (soloDigitos.Length > 0 && EF.Functions.Like(c.NumeroDocumento, patronDigitos)));
        }

        query = query.OrderBy(c => c.NombreCompleto);

        var total = await query.CountAsync(ct);
        var items = await query
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(c => Proyectar(c))
            .ToListAsync(ct);

        return new PaginaResultado<CompradorDto>(items, total, pagina, tamanoPagina);
    }

    public async Task<CompradorDto?> ObtenerPorDocumentoAsync(string numeroDocumento, CancellationToken ct = default)
    {
        var normalizado = NormalizarDocumento(numeroDocumento);
        if (normalizado.Length == 0)
            return null;

        return await db.Compradores
            .Where(c => c.NumeroDocumento == normalizado)
            .Select(c => Proyectar(c))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CompradorDto?> ObtenerPorIdAsync(int id, CancellationToken ct = default)
    {
        return await db.Compradores
            .Where(c => c.Id == id)
            .Select(c => Proyectar(c))
            .FirstOrDefaultAsync(ct);
    }

    public async Task<CompradorDto> CrearAsync(CompradorGuardarDto dto, CancellationToken ct = default)
    {
        var numero = NormalizarDocumento(dto.NumeroDocumento);
        Validar(dto, numero);

        if (await db.Compradores.AnyAsync(c => c.NumeroDocumento == numero, ct))
            throw new InvalidOperationException($"Ya existe un comprador con el documento '{numero}'.");

        var comprador = new Comprador
        {
            TipoDocumento = dto.TipoDocumento.Trim().ToUpperInvariant(),
            NumeroDocumento = numero,
            PrimerNombre = dto.PrimerNombre.Trim(),
            NombreCompleto = "",
            Email = dto.Email.Trim(),
            FechaCreacion = timeProvider.GetUtcNow().UtcDateTime
        };
        Aplicar(comprador, dto);

        db.Compradores.Add(comprador);
        await db.SaveChangesAsync(ct);

        return ProyectarEnMemoria(comprador);
    }

    public async Task<CompradorDto?> ActualizarAsync(int id, CompradorGuardarDto dto, CancellationToken ct = default)
    {
        var comprador = await db.Compradores.FirstOrDefaultAsync(c => c.Id == id, ct);
        if (comprador is null)
            return null;

        var numero = NormalizarDocumento(dto.NumeroDocumento);
        Validar(dto, numero);

        if (await db.Compradores.AnyAsync(c => c.Id != id && c.NumeroDocumento == numero, ct))
            throw new InvalidOperationException($"Ya existe un comprador con el documento '{numero}'.");

        comprador.TipoDocumento = dto.TipoDocumento.Trim().ToUpperInvariant();
        comprador.NumeroDocumento = numero;
        Aplicar(comprador, dto);

        await db.SaveChangesAsync(ct);
        return ProyectarEnMemoria(comprador);
    }

    // Copia los campos editables y recalcula lo derivado (nombre completo, DV).
    internal static void Aplicar(Comprador c, CompradorGuardarDto dto)
    {
        c.TipoPersona = dto.TipoPersona;
        c.PrimerNombre = dto.PrimerNombre.Trim();

        if (dto.TipoPersona == TipoPersona.Juridica)
        {
            c.SegundoNombre = null;
            c.PrimerApellido = null;
            c.SegundoApellido = null;
            c.NombreCompleto = c.PrimerNombre;
        }
        else
        {
            c.SegundoNombre = Limpiar(dto.SegundoNombre);
            c.PrimerApellido = Limpiar(dto.PrimerApellido);
            c.SegundoApellido = Limpiar(dto.SegundoApellido);
            c.NombreCompleto = string.Join(' ', new[] { c.PrimerNombre, c.SegundoNombre, c.PrimerApellido, c.SegundoApellido }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
        }

        c.Dv = c.TipoDocumento == "NIT" ? DigitoVerificacion.Calcular(c.NumeroDocumento) : null;
        c.Direccion = Limpiar(dto.Direccion);
        c.Ciudad = Limpiar(dto.Ciudad);
        c.Telefono = Limpiar(dto.Telefono);
        c.Email = dto.Email.Trim();
        c.Activo = dto.Activo;
    }

    private static void Validar(CompradorGuardarDto dto, string numeroNormalizado)
    {
        if (!TiposDocumento.Contains(dto.TipoDocumento?.Trim().ToUpperInvariant()))
            throw new InvalidOperationException($"Tipo de documento inválido. Usa uno de: {string.Join(", ", TiposDocumento)}.");
        if (numeroNormalizado.Length == 0)
            throw new InvalidOperationException("El número de documento es obligatorio.");
        if (string.IsNullOrWhiteSpace(dto.PrimerNombre))
            throw new InvalidOperationException(dto.TipoPersona == TipoPersona.Juridica
                ? "La razón social es obligatoria."
                : "El primer nombre es obligatorio.");
        if (dto.TipoPersona == TipoPersona.Natural && string.IsNullOrWhiteSpace(dto.PrimerApellido))
            throw new InvalidOperationException("El primer apellido es obligatorio.");
        if (dto.TipoPersona == TipoPersona.Juridica && dto.TipoDocumento!.Trim().ToUpperInvariant() != "NIT")
            throw new InvalidOperationException("Una persona jurídica se identifica con NIT.");
        // El correo es obligatorio: a ese correo se envía la remisión una vez pagada.
        if (string.IsNullOrWhiteSpace(dto.Email) || !RegexEmail().IsMatch(dto.Email.Trim()))
            throw new InvalidOperationException("El correo electrónico es obligatorio y debe ser válido (ahí se envía la remisión).");
    }

    // Deja solo letras/dígitos en mayúscula. Si viene con DV ("900.632.353-1"), se descarta lo que
    // sigue al guion: el DV no vive en este campo, se calcula aparte (DigitoVerificacion).
    internal static string NormalizarDocumento(string? numero)
    {
        var texto = (numero ?? "").Trim();
        var guion = texto.IndexOf('-');
        if (guion > 0)
            texto = texto[..guion];
        return new string(texto.Where(char.IsLetterOrDigit).ToArray()).ToUpperInvariant();
    }

    private static string? Limpiar(string? valor) => string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();

    // Expresión traducible a SQL (para Select dentro de queries).
    private static CompradorDto Proyectar(Comprador c) => new(
        c.Id, c.TipoPersona, c.TipoDocumento, c.NumeroDocumento, c.Dv, c.PrimerNombre, c.SegundoNombre,
        c.PrimerApellido, c.SegundoApellido, c.NombreCompleto, c.Direccion, c.Ciudad, c.Telefono, c.Email, c.Activo);

    private static CompradorDto ProyectarEnMemoria(Comprador c) => Proyectar(c);
}
