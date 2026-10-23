using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Remisiones.Modelo.Entidades;

// Cómo se cobró. Datáfono y QR pasan por el mismo datáfono Redeban (ambos llevan número de recibo);
// Efectivo solo se marca como pagado en efectivo.
public enum MedioPago
{
    Datafono = 1,
    Qr = 2,
    Efectivo = 3
}

public enum EstadoPago
{
    Pendiente = 1,
    Pagada = 2,
    Anulada = 3
}

public enum TipoEntrega
{
    EnSitio = 1,
    Envio = 2
}

public enum EstadoEntrega
{
    Pendiente = 1,
    Entregada = 2
}

// Documento de venta directa. Nace Pendiente (de pago) con su consecutivo oficial (SEQUENCE) y
// los datos del comprador congelados (snapshot). Mientras está Pendiente se puede editar;
// al marcarla Pagada queda fija (solo se puede anular, con motivo).
public class Remision
{
    public int Id { get; set; }

    public int Consecutivo { get; set; }

    public int CompradorId { get; set; }
    public Comprador? Comprador { get; set; }

    [MaxLength(10)]
    public required string CompradorTipoDocumentoSnapshot { get; set; }

    [MaxLength(30)]
    public required string CompradorDocumentoSnapshot { get; set; }

    [MaxLength(400)]
    public required string CompradorNombreSnapshot { get; set; }

    [MaxLength(300)]
    public string? CompradorDireccionSnapshot { get; set; }

    [MaxLength(100)]
    public string? CompradorCiudadSnapshot { get; set; }

    [MaxLength(50)]
    public string? CompradorTelefonoSnapshot { get; set; }

    [MaxLength(200)]
    public required string CompradorEmailSnapshot { get; set; }

    // Retenciones (opcionales; el vendedor activa solo las que apliquen a ese cliente). Son lo que el comprador,
    // como agente retenedor, descuenta del pago: NO cambian el IVA, solo el total a pagar.
    // ReteFuente: % sobre el valor antes de IVA. ReteICA: por mil (‰) sobre el valor antes de IVA. ReteIVA: % sobre el IVA.
    [Column(TypeName = "decimal(5,2)")]
    public decimal ReteFuentePorcentaje { get; set; }

    [Column(TypeName = "decimal(6,3)")]
    public decimal ReteIcaPorMil { get; set; }

    [Column(TypeName = "decimal(5,2)")]
    public decimal ReteIvaPorcentaje { get; set; }

    // En un envío posterior es obligatoria y lleva la dirección y ciudad de entrega, por eso admite bastante texto.
    [MaxLength(1000)]
    public string? Nota { get; set; }

    public EstadoPago EstadoPago { get; set; } = EstadoPago.Pendiente;

    public DateTime? FechaPago { get; set; }

    [MaxLength(200)]
    public string? PagadaPor { get; set; }

    // Número de aprobación / voucher del datáfono, para conciliar.
    [MaxLength(100)]
    public string? ReciboRedeban { get; set; }

    // Se fija al cobrar; null mientras la remisión está pendiente.
    public MedioPago? MedioPago { get; set; }

    public TipoEntrega TipoEntrega { get; set; } = TipoEntrega.EnSitio;

    public EstadoEntrega EstadoEntrega { get; set; } = EstadoEntrega.Pendiente;

    public DateTime? FechaEntrega { get; set; }

    // Cuándo se envió el correo con la remisión al comprador (null = aún no se ha enviado).
    public DateTime? CorreoEnviadoEn { get; set; }

    public DateTime? FechaAnulacion { get; set; }

    [MaxLength(500)]
    public string? MotivoAnulacion { get; set; }

    public DateTime FechaCreacion { get; set; }

    [MaxLength(200)]
    public string? CreadoPor { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<RemisionItem> Items { get; set; } = new List<RemisionItem>();
}
