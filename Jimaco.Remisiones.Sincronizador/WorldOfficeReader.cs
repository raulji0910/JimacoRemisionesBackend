using Dapper;
using Microsoft.Data.SqlClient;

namespace Jimaco.Remisiones.Sincronizador;

public class ProductoWoRow
{
    public int IdWorldOffice { get; set; }
    public string Codigo { get; set; } = "";
    public string Nombre { get; set; } = "";
    public string? UnidadMedida { get; set; }
    // En WO el IVA es una fracción (0.19); a veces podría venir ya en porcentaje.
    public decimal? IvaFraccion { get; set; }
    // Convención Access: -1 = sí, 0 = no.
    public short Activo { get; set; }
    public byte[] Version { get; set; } = [];
}

public class ExistenciaWoRow
{
    public int IdWorldOffice { get; set; }
    public string Bodega { get; set; } = "";
    public decimal Cantidad { get; set; }
}

public class CompradorWoRow
{
    public int IdWorldOffice { get; set; }
    public int? IdTipo { get; set; }
    public string? TipoAbrev { get; set; }
    public string? Identificacion { get; set; }
    public string? Nombre { get; set; }
    public string? Apellidos { get; set; }
    public string? Primer_Nombre { get; set; }
    public string? Segundo_Nombre { get; set; }
    public string? Primer_Apellido { get; set; }
    public string? Segundo_Apellido { get; set; }
    public short Activo { get; set; }
    public string? Direccion { get; set; }
    public string? Telefonos { get; set; }
    public string? Telefono2 { get; set; }
    public string? Email { get; set; }
    public string? Ciudad { get; set; }
    public byte[] Version { get; set; } = [];
}

/// <summary>
/// Todo lo que el Sincronizador lee de la base de World Office. Estrictamente de solo lectura (ni
/// un INSERT/UPDATE en este proyecto): pensado para correr con un login de solo lectura. Las
/// existencias salen de la vista que ya usa WO (Vista_ExistenciasPorBodegas = Σ Cantidad ×
/// EfectoSobreElInventario del tipo de documento), no de una fórmula propia, para que cualquier
/// cambio interno de WO en cómo cuenta el stock se herede solo.
/// </summary>
public class WorldOfficeReader(string connectionString)
{
    private static readonly byte[] CeroVersion = new byte[8];

    /// <summary>Productos modificados desde la versión <paramref name="desde"/> (rowversion, inclusive).</summary>
    public async Task<IReadOnlyList<ProductoWoRow>> ObtenerProductosAsync(byte[]? desde, int lote, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (@Lote)
                i.IdInventario     AS IdWorldOffice,
                i.CódigoInventario AS Codigo,
                i.Descripción      AS Nombre,
                i.UnidadDeMedida   AS UnidadMedida,
                i.Iva              AS IvaFraccion,
                i.Activo           AS Activo,
                i.VersionCol       AS Version
            FROM dbo.Inventarios i
            WHERE i.VersionCol >= @Desde
            ORDER BY i.VersionCol;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Lote = lote, Desde = desde ?? CeroVersion }, cancellationToken: ct, commandTimeout: 120);
        return (await conexion.QueryAsync<ProductoWoRow>(comando)).ToList();
    }

    /// <summary>Existencia actual por producto y bodega (todas las filas con movimiento).</summary>
    public async Task<IReadOnlyList<ExistenciaWoRow>> ObtenerExistenciasAsync(CancellationToken ct = default)
    {
        const string sql = """
            SELECT IdInventario        AS IdWorldOffice,
                   IdBodegaInventario  AS Bodega,
                   SUM(Existencia)     AS Cantidad
            FROM dbo.Vista_ExistenciasPorBodegas
            GROUP BY IdInventario, IdBodegaInventario;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, cancellationToken: ct, commandTimeout: 300);
        return (await conexion.QueryAsync<ExistenciaWoRow>(comando)).ToList();
    }

    /// <summary>
    /// Terceros con propiedad Cliente, con su dirección principal. La propiedad vive como texto en
    /// <c>Terceros.Propiedades</c> y el formato es inconsistente ("Cliente;", "Cliente",
    /// "Cliente;Proveedor;"...), por eso el LIKE. La "versión" del cliente es la mayor entre la de
    /// su fila y la de su dirección principal, para que un cambio solo de dirección también se
    /// sincronice.
    /// </summary>
    public async Task<IReadOnlyList<CompradorWoRow>> ObtenerCompradoresAsync(byte[]? desde, int lote, CancellationToken ct = default)
    {
        const string sql = """
            SELECT TOP (@Lote)
                t.IdTercero              AS IdWorldOffice,
                t.IdTipoIdentificacion   AS IdTipo,
                COALESCE(NULLIF(ti.Abreviatura, ''), ti.TipoDeIdentificacion) AS TipoAbrev,
                t.Identificacion,
                t.Nombre, t.Apellidos,
                t.Primer_Nombre, t.Segundo_Nombre, t.Primer_Apellido, t.Segundo_Apellido,
                t.Activo,
                d.Dirección              AS Direccion,
                d.Teléfonos              AS Telefonos,
                CAST(d.Telefono2 AS NVARCHAR(50)) AS Telefono2,
                d.EMail                  AS Email,
                c.Ciudad                 AS Ciudad,
                CASE WHEN d.VersionCol IS NOT NULL AND d.VersionCol > t.VersionCol THEN d.VersionCol ELSE t.VersionCol END AS Version
            FROM dbo.Terceros t
            LEFT JOIN dbo.[Terceros - Tipo de Identificación] ti ON ti.IdTipoIdentificacion = t.IdTipoIdentificacion
            OUTER APPLY (
                SELECT TOP 1 x.*
                FROM dbo.[Terceros - Direcciones] x
                WHERE x.IdTercero = t.IdTercero
                ORDER BY x.senDirecPpal, x.IdDirección
            ) d
            LEFT JOIN dbo.Ciudades c ON c.IdCiudad = COALESCE(d.IdCiudad, t.IdentificacionCiudad)
            WHERE t.Propiedades LIKE '%Cliente%'
              AND (CASE WHEN d.VersionCol IS NOT NULL AND d.VersionCol > t.VersionCol THEN d.VersionCol ELSE t.VersionCol END) >= @Desde
            ORDER BY Version;
            """;

        await using var conexion = new SqlConnection(connectionString);
        var comando = new CommandDefinition(sql, new { Lote = lote, Desde = desde ?? CeroVersion }, cancellationToken: ct, commandTimeout: 120);
        return (await conexion.QueryAsync<CompradorWoRow>(comando)).ToList();
    }
}
