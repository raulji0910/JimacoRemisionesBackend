using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.DTOs;

// Una remisión cobrada (o devuelta) en el rango. `Anulada` marca las cobradas que después se anularon.
// Usuario = quien la cobró; Vendedor = quien la creó (pueden ser distintos).
public record CierreLineaDto(
    int Id,
    string Consecutivo,
    DateTime Fecha,
    string Cliente,
    MedioPago? Medio,
    string? Recibo,
    decimal Total,
    string? Usuario,
    string? Vendedor,
    bool Anulada);

// Totales por medio: lo cobrado en el rango, lo devuelto (remisiones pagadas que se anularon en el rango) y el neto.
public record CierreMedioDto(
    MedioPago Medio,
    int CantidadCobros,
    decimal TotalCobros,
    int CantidadDevoluciones,
    decimal TotalDevoluciones,
    decimal Neto);

// Un día del rango (calendario de Colombia). Datafono / Qr / Efectivo son el neto del día en cada medio (cobros menos devoluciones).
public record CierreDiaDto(
    string Fecha,
    int Cobros,
    decimal Datafono,
    decimal Qr,
    decimal Efectivo,
    decimal TotalCobros,
    decimal Devoluciones,
    decimal Neto);

// Lo cobrado por cada usuario en el rango (quién tramitó el cobro), por medio.
public record CierreUsuarioDto(
    string Usuario,
    int CantidadCobros,
    decimal Datafono,
    decimal Qr,
    decimal Efectivo,
    decimal TotalCobros);

public record CierreCajaDto(
    string Desde,
    string Hasta,
    IReadOnlyList<CierreMedioDto> PorMedio,
    decimal TotalCobros,
    decimal TotalDevoluciones,
    decimal Neto,
    IReadOnlyList<CierreDiaDto> PorDia,
    IReadOnlyList<CierreUsuarioDto> PorUsuario,
    IReadOnlyList<CierreLineaDto> Cobros,
    IReadOnlyList<CierreLineaDto> Devoluciones);
