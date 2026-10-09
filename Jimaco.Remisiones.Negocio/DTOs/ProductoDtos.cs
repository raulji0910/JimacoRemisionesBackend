using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.DTOs;

public record ProductoDto(
    int Id,
    string Codigo,
    string Nombre,
    string? UnidadMedida,
    decimal PrecioVenta,
    decimal? Costo,
    int Iva,
    OrigenRegistro Origen,
    bool Activo,
    // IdInventario en World Office (null si el producto es solo local).
    int? IdWorldOffice,
    // Existencia en la bodega de venta según la última sincronización; null si nunca se sincronizó
    // (ej. producto local o aún sin movimientos en WO).
    decimal? Existencia,
    DateTime? ExistenciaActualizadaEn);

public record ProductoCrearDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva);

public record ProductoActualizarDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva, bool Activo);
