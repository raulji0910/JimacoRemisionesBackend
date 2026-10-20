using System.Reflection;
using System.Text.Json;

namespace Jimaco.Remisiones.Negocio.Servicios;

public record MunicipioDane(string Codigo, string Nombre, string Departamento, string Tipo)
{
    // Texto que se guarda como ciudad del comprador y sale en el PDF/correo: "MEDELLÍN, ANTIOQUIA".
    // Bogotá D.C. se llama igual que su departamento, así que no se repite.
    public string Etiqueta => Nombre == Departamento ? Nombre : $"{Nombre}, {Departamento}";
}

/// <summary>
/// Divipola del DANE (datos.gov.co, dataset gdxc-w37w): 1.122 entradas con su código de 5 dígitos
/// (1.103 municipios, San Andrés y 18 áreas no municipalizadas). Es la única fuente de ciudades de la app:
/// el comprador guarda el código y la ciudad se completa desde aquí, nunca se escribe a mano.
/// </summary>
public static class MunicipiosDane
{
    private sealed record Fila(string C, string N, string D, string T);

    private static readonly Lazy<IReadOnlyList<MunicipioDane>> TodosLazy = new(Cargar);
    private static readonly Lazy<IReadOnlyDictionary<string, MunicipioDane>> PorCodigoLazy =
        new(() => TodosLazy.Value.ToDictionary(m => m.Codigo));

    public static IReadOnlyList<MunicipioDane> Todos => TodosLazy.Value;

    public static MunicipioDane? PorCodigo(string? codigo)
        => codigo is not null && PorCodigoLazy.Value.TryGetValue(codigo.Trim(), out var m) ? m : null;

    private static IReadOnlyList<MunicipioDane> Cargar()
    {
        var asm = typeof(MunicipiosDane).Assembly;
        var nombre = asm.GetManifestResourceNames().Single(n => n.EndsWith("municipios-dane.json", StringComparison.Ordinal));
        using var stream = asm.GetManifestResourceStream(nombre)!;
        var filas = JsonSerializer.Deserialize<List<Fila>>(stream, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        return filas.Select(f => new MunicipioDane(f.C, f.N, f.D, f.T)).ToList();
    }
}
