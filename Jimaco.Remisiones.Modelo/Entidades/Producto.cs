using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Remisiones.Modelo.Entidades;

// Catálogo de venta directa: un solo precio de venta fijo por producto (sin proveedores ni
// cotizaciones que comparar). El código es el de World Office y es obligatorio y único.
public class Producto
{
    public int Id { get; set; }

    [MaxLength(50)]
    public required string Codigo { get; set; }

    [MaxLength(500)]
    public required string Nombre { get; set; }

    [MaxLength(50)]
    public string? UnidadMedida { get; set; }

    // Precio de venta al público, SIN IVA (el IVA se desglosa aparte en la remisión, igual que
    // en el cotizador de Ferrealiados).
    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioVenta { get; set; }

    // Costo del producto, solo para calcular margen. Nunca se muestra en la remisión.
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Costo { get; set; }

    // Tarifa de IVA en %: 0, 5 o 19.
    public int Iva { get; set; } = 19;

    public OrigenRegistro Origen { get; set; } = OrigenRegistro.Local;

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; }
}
