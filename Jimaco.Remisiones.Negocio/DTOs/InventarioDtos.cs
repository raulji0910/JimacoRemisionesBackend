using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.DTOs;

// Movimiento manual de stock (solo administrador).
//  - Entrada: llegó mercancía; Cantidad debe ser positiva.
//  - Ajuste: corrección (conteo físico, daño, etc.); Cantidad con signo (+ suma, - resta) y motivo obligatorio.
public record MovimientoManualDto(TipoMovimiento Tipo, int Cantidad, string? Motivo);

public record MovimientoDto(
    int Id,
    DateTime Fecha,
    TipoMovimiento Tipo,
    int Cantidad,
    int SaldoResultante,
    int? RemisionId,
    string? RemisionConsecutivo,
    string? Usuario,
    string? Motivo);
