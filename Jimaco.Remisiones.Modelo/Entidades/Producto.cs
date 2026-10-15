using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Remisiones.Modelo.Entidades;

// Producto de venta directa: un solo precio de venta fijo (sin proveedores ni cotizaciones). El
// inventario se maneja SOLO en esta aplicación: Stock es la existencia actual y cada cambio queda
// registrado en MovimientoInventario (kardex). No hay integración con World Office.
public class Producto
{
    public int Id { get; set; }

    // Código tal como está en el listado de herramientas (ej. "PJ-26550"). Único.
    [MaxLength(50)]
    public required string Codigo { get; set; }

    [MaxLength(500)]
    public required string Nombre { get; set; }

    [MaxLength(50)]
    public string? UnidadMedida { get; set; }

    // Precio de venta por unidad, ANTES de IVA (el IVA se desglosa aparte en la remisión).
    [Column(TypeName = "decimal(18,2)")]
    public decimal PrecioVenta { get; set; }

    // Costo, solo para calcular margen. Nunca se muestra en la remisión.
    [Column(TypeName = "decimal(18,2)")]
    public decimal? Costo { get; set; }

    // Tarifa de IVA en %: 0, 5 o 19.
    public int Iva { get; set; } = 19;

    // Existencia actual en unidades. Solo cambia a través de movimientos (ver Kardex).
    public int Stock { get; set; }

    public bool Activo { get; set; } = true;

    public DateTime FechaCreacion { get; set; }

    // Concurrencia optimista: dos ventas simultáneas del mismo producto no pueden dejar el stock negativo.
    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<MovimientoInventario> Movimientos { get; set; } = new List<MovimientoInventario>();
}
