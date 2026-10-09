using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Remisiones.Modelo.Entidades;

// Existencia de un producto en una bodega de World Office, copiada por el Sincronizador desde la
// vista Vista_ExistenciasPorBodegas de WO (suma de Cantidad x EfectoSobreElInventario de cada
// documento). Es solo un espejo de lectura: WO sigue siendo quien manda sobre el inventario.
public class ProductoExistencia
{
    public int Id { get; set; }

    public int ProductoId { get; set; }
    public Producto? Producto { get; set; }

    // Código de bodega tal cual en WO (texto, ej. "Uno").
    [MaxLength(30)]
    public required string Bodega { get; set; }

    [Column(TypeName = "decimal(18,4)")]
    public decimal Cantidad { get; set; }
}
