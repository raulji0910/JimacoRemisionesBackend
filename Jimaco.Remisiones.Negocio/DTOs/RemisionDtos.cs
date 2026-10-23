using Jimaco.Remisiones.Modelo.Entidades;

namespace Jimaco.Remisiones.Negocio.DTOs;

public record RemisionItemEntradaDto(int ProductoId, int Cantidad, decimal DescuentoPorcentaje = 0);

public record RemisionGuardarDto(
    int CompradorId,
    TipoEntrega TipoEntrega,
    // Las retenciones se activan o no; sus tarifas son fijas (ver ReglasRetenciones) y solo proceden desde un valor mínimo antes de IVA.
    bool ReteFuente,
    bool ReteIca,
    bool ReteIva,
    string? Nota,
    IReadOnlyList<RemisionItemEntradaDto> Items);

public record MarcarPagadaDto(MedioPago MedioPago, string? ReciboRedeban);

public record AnularRemisionDto(string Motivo);

// Al pasar a envío posterior se exige la dirección y ciudad de entrega (Nota), igual que al crear la remisión.
public record CambiarTipoEntregaDto(TipoEntrega TipoEntrega, string? Nota = null);

public record RemisionItemDto(
    int Id,
    int ProductoId,
    string ProductoCodigo,
    string ProductoNombre,
    decimal PrecioUnitario,
    int Iva,
    int Cantidad,
    // Subtotal = valor bruto (precio x cantidad); Descuento y SubtotalNeto son los de esta línea.
    decimal Subtotal,
    decimal DescuentoPorcentaje,
    decimal Descuento,
    decimal SubtotalNeto,
    // Solo informativos en la app (nunca en el PDF). Null si el producto no tenía costo.
    decimal? Costo,
    decimal? MargenPorcentaje);

// Desglose del IVA por cada tarifa presente. Base = subtotal de esa tarifa ya con el descuento
// prorrateado.
public record IvaDesgloseDto(int Tarifa, decimal Base, decimal Valor);

public record RemisionResumenDto(
    int Id,
    int Consecutivo,
    string ConsecutivoFormateado,
    string CompradorNombre,
    string CompradorDocumento,
    DateTime FechaCreacion,
    decimal Total,
    EstadoPago EstadoPago,
    MedioPago? MedioPago,
    TipoEntrega TipoEntrega,
    EstadoEntrega EstadoEntrega,
    bool CorreoEnviado,
    string? CreadoPor);

public record RemisionDetalleDto(
    int Id,
    int Consecutivo,
    string ConsecutivoFormateado,
    int CompradorId,
    string CompradorTipoDocumento,
    string CompradorDocumento,
    string CompradorNombre,
    string? CompradorDireccion,
    string? CompradorCiudad,
    string? CompradorTelefono,
    string CompradorEmail,
    string? Nota,
    DateTime FechaCreacion,
    string? CreadoPor,
    EstadoPago EstadoPago,
    DateTime? FechaPago,
    string? PagadaPor,
    string? ReciboRedeban,
    MedioPago? MedioPago,
    TipoEntrega TipoEntrega,
    EstadoEntrega EstadoEntrega,
    DateTime? FechaEntrega,
    DateTime? CorreoEnviadoEn,
    DateTime? FechaAnulacion,
    string? MotivoAnulacion,
    IReadOnlyList<RemisionItemDto> Items,
    decimal SubtotalItems,
    decimal Descuento,
    decimal ReteFuentePorcentaje,
    decimal ReteIcaPorMil,
    decimal ReteIvaPorcentaje,
    decimal SubtotalConDescuento,
    IReadOnlyList<IvaDesgloseDto> IvaDesglose,
    decimal TotalBruto,
    decimal ReteFuente,
    decimal ReteIca,
    decimal ReteIva,
    decimal TotalGeneral);

// Resultado de marcar como pagada: el pago nunca falla por el correo, pero se informa si el envío
// automático no salió (ej. SMTP sin configurar) para que el operador pueda reintentar.
public record PagarResultadoDto(RemisionDetalleDto Remision, bool CorreoEnviado, string? ErrorCorreo, bool CorreoEnCola = false);

public record EnviarCorreoResultadoDto(RemisionDetalleDto Remision, bool CorreoEnviado, string? ErrorCorreo);
