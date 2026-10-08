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
    bool Activo);

public record ProductoCrearDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva);

public record ProductoActualizarDto(string Codigo, string Nombre, string? UnidadMedida, decimal PrecioVenta, decimal? Costo, int Iva, bool Activo);
