using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.DTOs;

// Lo que el Sincronizador (agente dentro de la red de World Office) empuja a esta API. Todo es
// lectura de WO: ningún DTO de aquí escribe de vuelta en World Office.

// Maestro de producto (tabla Inventarios de WO). NO trae precio ni costo: el precio de venta se
// define en esta app (WO casi no tiene precios en el catálogo, se digitan en cada documento).
public record SincProductoDto(
    int IdWorldOffice,
    string Codigo,
    string Nombre,
    string? UnidadMedida,
    // En WO el IVA es una fracción (0.19); el Sincronizador ya lo manda en porcentaje (19).
    int Iva,
    bool Activo);

// Existencia de un producto en una bodega (Vista_ExistenciasPorBodegas de WO).
public record SincExistenciaDto(int IdWorldOffice, string Bodega, decimal Cantidad);

// Tercero con propiedad Cliente (tablas Terceros + Terceros - Direcciones de WO).
public record SincCompradorDto(
    int IdWorldOffice,
    string TipoDocumento,
    string NumeroDocumento,
    TipoPersona TipoPersona,
    string PrimerNombre,
    string? SegundoNombre,
    string? PrimerApellido,
    string? SegundoApellido,
    string? Direccion,
    string? Ciudad,
    string? Telefono,
    string? Email,
    bool Activo);

public record SincLoteProductosDto(IReadOnlyList<SincProductoDto> Items);

// LeidoEn: instante (UTC) en que el Sincronizador leyó WO. Se manda aunque Items venga vacío: es el
// "latido" que dice hasta cuándo la app está al día con WO.
public record SincLoteExistenciasDto(IReadOnlyList<SincExistenciaDto> Items, DateTime LeidoEn);

public record SincLoteCompradoresDto(IReadOnlyList<SincCompradorDto> Items);

public record SincResultadoDto(int Creados, int Actualizados, int Omitidos, IReadOnlyList<string> Avisos);
