using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.DTOs;

// Una remisión cobrada (o devuelta) en el día. `Anulada` marca las cobradas que después se anularon.
public record CierreLineaDto(
    int Id,
    string Consecutivo,
    DateTime Fecha,
    string Cliente,
    MedioPago? Medio,
    string? Recibo,
    decimal Total,
    string? Usuario,
    bool Anulada);

// Totales por medio: lo cobrado ese día, lo devuelto (remisiones pagadas que se anularon ese día) y el neto.
public record CierreMedioDto(
    MedioPago Medio,
    int CantidadCobros,
    decimal TotalCobros,
    int CantidadDevoluciones,
    decimal TotalDevoluciones,
    decimal Neto);

public record CierreCajaDto(
    string Fecha,
    IReadOnlyList<CierreMedioDto> PorMedio,
    decimal TotalCobros,
    decimal TotalDevoluciones,
    decimal Neto,
    IReadOnlyList<CierreLineaDto> Cobros,
    IReadOnlyList<CierreLineaDto> Devoluciones);
