namespace Jimaco.Remisiones.Negocio.DTOs;

public record ProductoDto(
    int Id,
    string Codigo,
    string Nombre,
    string? UnidadMedida,
    // Precio por unidad, antes de IVA.
    decimal PrecioVenta,
    decimal? Costo,
    int Iva,
    bool Activo,
    // Existencia actual (la maneja esta app; cambia con ventas, entradas y ajustes).
    int Stock);

// StockInicial: unidades con las que arranca el producto (queda como "Carga inicial" en el historial).
public record ProductoCrearDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva, int StockInicial = 0);

// El stock NO se edita aquí: solo cambia con movimientos (ventas, entradas, ajustes).
public record ProductoActualizarDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva, bool Activo);
