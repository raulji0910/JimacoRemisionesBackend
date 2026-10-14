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
    DateTime? ExistenciaActualizadaEn,
    // Unidades vendidas desde esta app (remisiones pagadas) que World Office todavía no refleja en su
    // existencia. Se restan para no vender dos veces lo mismo mientras WO se pone al día.
    decimal? Reservado = null,
    // Existencia de WO - Reservado (null si nunca se sincronizó). Es la cifra que se debe mirar al vender.
    decimal? Disponible = null);

public record ProductoCrearDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva);

public record ProductoActualizarDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva, bool Activo);
