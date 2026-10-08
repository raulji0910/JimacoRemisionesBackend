using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Jimaco.Remisiones.Modelo.Entidades;

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

// Estado de la sincronización de la remisión con World Office (descuento de inventario).
// Fase 1: todas quedan en Pendiente; el Sincronizador (Fase 2) las irá marcando.
public enum EstadoSincronizacionWo
{
    Pendiente = 1,
    Sincronizada = 2,
    Error = 3
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

    // Descuento global en pesos sobre el subtotal de ítems; se prorratea entre las tarifas de IVA
    // (ver RemisionCalculos).
    [Column(TypeName = "decimal(18,2)")]
    public decimal Descuento { get; set; }

    [MaxLength(500)]
    public string? Nota { get; set; }

    public EstadoPago EstadoPago { get; set; } = EstadoPago.Pendiente;

    public DateTime? FechaPago { get; set; }

    [MaxLength(200)]
    public string? PagadaPor { get; set; }

    // Número de aprobación / voucher del datáfono, para conciliar.
    [MaxLength(100)]
    public string? ReferenciaPago { get; set; }

    public TipoEntrega TipoEntrega { get; set; } = TipoEntrega.EnSitio;

    public EstadoEntrega EstadoEntrega { get; set; } = EstadoEntrega.Pendiente;

    public DateTime? FechaEntrega { get; set; }

    // Cuándo se envió el correo con la remisión al comprador (null = aún no se ha enviado).
    public DateTime? CorreoEnviadoEn { get; set; }

    public DateTime? FechaAnulacion { get; set; }

    [MaxLength(500)]
    public string? MotivoAnulacion { get; set; }

    public EstadoSincronizacionWo SincronizacionWo { get; set; } = EstadoSincronizacionWo.Pendiente;

    public DateTime? SincronizadaWoEn { get; set; }

    // Referencia del documento creado en World Office (prefijo + número), cuando se sincronice.
    [MaxLength(50)]
    public string? DocumentoWorldOffice { get; set; }

    [MaxLength(1000)]
    public string? ErrorSincronizacionWo { get; set; }

    public DateTime FechaCreacion { get; set; }

    [MaxLength(200)]
    public string? CreadoPor { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<RemisionItem> Items { get; set; } = new List<RemisionItem>();
}
