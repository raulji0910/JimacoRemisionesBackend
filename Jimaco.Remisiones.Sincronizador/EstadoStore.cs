using System.Text.Json;

namespace Jimaco.Remisiones.Sincronizador;

/// <summary>Lo que el Sincronizador recuerda entre corridas (archivo JSON local).</summary>
public class Estado
{
    // Última versión (rowversion en hexadecimal) de Inventarios / Terceros ya enviada.
    public string? ProductosDesde { get; set; }
    public string? CompradoresDesde { get; set; }

    // "idInventario|bodega" -> última cantidad enviada. Solo se reenvía lo que cambió.
    public Dictionary<string, decimal> Existencias { get; set; } = new();
}

/// <summary>
/// Un archivo alcanza porque el Sincronizador corre como una sola instancia (Tarea Programada de
/// Windows con "no iniciar una nueva instancia"). Se escribe a un temporal y se renombra para no
/// dejar el archivo a medias si el proceso se corta justo al guardar.
/// </summary>
public class EstadoStore(string ruta)
{
    public Estado Leer()
    {
        if (!File.Exists(ruta))
            return new Estado();

        return JsonSerializer.Deserialize<Estado>(File.ReadAllText(ruta)) ?? new Estado();
    }

    public void Guardar(Estado estado)
    {
        var temporal = ruta + ".tmp";
        File.WriteAllText(temporal, JsonSerializer.Serialize(estado));
        File.Move(temporal, ruta, overwrite: true);
    }

    public static string Hex(byte[] version) => Convert.ToHexString(version);

    public static byte[]? DesdeHex(string? hex) => string.IsNullOrEmpty(hex) ? null : Convert.FromHexString(hex);
}
