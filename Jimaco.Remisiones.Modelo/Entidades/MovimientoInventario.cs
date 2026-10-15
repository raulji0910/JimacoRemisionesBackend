using System.ComponentModel.DataAnnotations;

namespace Jimaco.Remisiones.Modelo.Entidades;

public enum TipoMovimiento
{
    // Saldo con el que arranca un producto (al crearlo).
    CargaInicial = 1,
    // Mercancía que llega (suma).
    Entrada = 2,
    // Salida por una remisión pagada (resta).
    Venta = 3,
    // Devolución al stock por anular una remisión ya pagada (suma).
    Reintegro = 4,
    // Corrección manual (suma o resta), p. ej. por un conteo físico.
    Ajuste = 5
}

// Una línea del kardex. Es de solo agregar: nunca se edita ni se borra (salvo al eliminar el producto
// completo, que solo se permite si no tiene remisiones).
public class MovimientoInventario
{
    public int Id { get; set; }

    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }

    public DateTime Fecha { get; set; }

    public TipoMovimiento Tipo { get; set; }

    // Con signo: positivo entra, negativo sale.
    public int Cantidad { get; set; }

    // Stock del producto DESPUÉS de este movimiento.
    public int SaldoResultante { get; set; }

    // Remisión que originó el movimiento (Venta / Reintegro).
    public int? RemisionId { get; set; }

    [MaxLength(200)]
    public string? Usuario { get; set; }

    [MaxLength(500)]
    public string? Motivo { get; set; }
}
