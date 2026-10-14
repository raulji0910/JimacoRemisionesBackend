namespace Jimaco.Remisiones.Negocio.DTOs;

// Estado de la "foto" de inventario de World Office que tiene la app.
public record EstadoInventarioDto(
    // Hora (UTC) de la última foto de existencias; null si nunca se ha cargado.
    DateTime? UltimaFotoEn,
    string BodegaVenta,
    int ProductosConExistencia,
    // Productos activos que no se pueden vender todavía por no tener precio.
    int ProductosSinPrecio);

// Una fila del inventario exportado de WO (por consulta en SSMS -> CSV). El precio es opcional: WO no
// tiene precios en el catálogo, se propone el último precio de venta.
public record ImportarInventarioFilaDto(
    int IdWorldOffice,
    string Codigo,
    string Nombre,
    string? Unidad,
    int Iva,
    bool Activo,
    decimal? Existencia,
    decimal? Precio);

public record ImportarInventarioDto(
    IReadOnlyList<ImportarInventarioFilaDto> Filas,
    // false: el precio solo se asigna a productos que aún no tienen uno. true: se pisa el precio actual.
    bool ActualizarPrecios,
    // Cuándo se exportó de WO (para la hora de la foto); si no viene, se usa el momento de la carga.
    DateTime? ExportadoEn);

public record ImportarInventarioResultadoDto(
    int ProductosCreados,
    int ProductosActualizados,
    int Omitidos,
    int ExistenciasCargadas,
    int PreciosAsignados,
    IReadOnlyList<string> Avisos);

public record RegistrarEnWoDto(string Documento);
