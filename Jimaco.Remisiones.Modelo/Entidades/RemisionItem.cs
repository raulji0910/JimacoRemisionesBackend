using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Remisiones.Modelo.Entidades;

// Línea de una remisión. Guarda snapshot del producto (código, nombre, precio, IVA, costo) para
// que la remisión no cambie si el catálogo se edita después.
public class RemisionItem
{
    public int Id { get; set; }

    public int RemisionId { get; set; }
    public Remision? Remision { get; set; }

    public int ProductoId { get; set; }

    [MaxLength(50)]
    public required string ProductoCodigoSnapshot { get; set; }

    [MaxLength(500)]
    public required string ProductoNombreSnapshot { get; set; }

    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioUnitario { get; set; }

    // Solo para margen interno; nunca sale en el PDF.
    [Column(TypeName = "decimal(18,2)")]
    public decimal? CostoSnapshot { get; set; }

    public int Iva { get; set; }

    public int Cantidad { get; set; } = 1;
}
