using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.Servicios;

// Único punto por el que cambia el stock de un producto: aplica el movimiento sobre Producto.Stock y deja
// la línea del kardex en el mismo SaveChanges (misma transacción). Nunca se modifica Stock directamente.
public static class Kardex
{
    /// <param name="cantidad">Con signo: positivo entra, negativo sale.</param>
    /// <exception cref="InvalidOperationException">Si el stock quedaría negativo.</exception>
    public static MovimientoInventario Registrar(
        AppDbContext db,
        Producto producto,
        TipoMovimiento tipo,
        int cantidad,
        DateTime ahora,
        string? usuario,
        int? remisionId = null,
        string? motivo = null)
    {
        var nuevoSaldo = producto.Stock + cantidad;
        if (nuevoSaldo < 0)
            throw new InvalidOperationException(
                $"Stock insuficiente de '{producto.Codigo}': hay {producto.Stock} y se necesitan {-cantidad}.");

        producto.Stock = nuevoSaldo;

        var movimiento = new MovimientoInventario
        {
            Producto = producto,
            Fecha = ahora,
            Tipo = tipo,
            Cantidad = cantidad,
            SaldoResultante = nuevoSaldo,
            RemisionId = remisionId,
            Usuario = usuario,
            Motivo = motivo
        };
        db.MovimientosInventario.Add(movimiento);
        return movimiento;
    }
}
