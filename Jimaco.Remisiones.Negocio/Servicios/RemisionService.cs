using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;
using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Jimaco.Remisiones.Negocio.Servicios;

public partial class RemisionService(
    AppDbContext db,
    IRemisionPdfBuilder pdfBuilder,
    IEmailSender emailSender,
    TimeProvider timeProvider,
    ILogger<RemisionService> logger,
    IConfiguration? configuration = null,
    ICorreoRemisionCola? cola = null) : IRemisionService
{
    // Reconoce el consecutivo formateado ("RMF-00173"), con o sin prefijo y ceros a la izquierda,
    // para que buscar por el número que ve el comprador también funcione.
    [GeneratedRegex(@"^(?:rmf?-?)?0*(\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex RegexConsecutivoBuscado();

    private static readonly CultureInfo CulturaCo = CultureInfo.GetCultureInfo("es-CO");

    public async Task<PaginaResultado<RemisionResumenDto>> BuscarAsync(EstadoPago? estadoPago, bool soloEnviosPendientes, string? texto, int pagina, int tamanoPagina, CancellationToken ct = default)
    {
        pagina = Math.Max(1, pagina);
        tamanoPagina = Math.Clamp(tamanoPagina, 1, 100);

        var query = db.Remisiones.AsQueryable();

        if (estadoPago is not null)
            query = query.Where(r => r.EstadoPago == estadoPago);

        // Bandeja de logística: ya pagadas, de envío, que todavía no se han entregado.
        if (soloEnviosPendientes)
            query = query.Where(r => r.EstadoPago == EstadoPago.Pagada
                && r.TipoEntrega == TipoEntrega.Envio
                && r.EstadoEntrega == EstadoEntrega.Pendiente);

        if (!string.IsNullOrWhiteSpace(texto))
        {
            var textoTrim = texto.Trim();
            var patron = $"%{textoTrim}%";

            int? consecutivoBuscado = null;
            var match = RegexConsecutivoBuscado().Match(textoTrim.Replace(" ", ""));
            if (match.Success && int.TryParse(match.Groups[1].Value, out var parsed))
                consecutivoBuscado = parsed;

            query = query.Where(r =>
                EF.Functions.Like(r.CompradorNombreSnapshot, patron) ||
                EF.Functions.Like(r.CompradorDocumentoSnapshot, patron) ||
                (r.CreadoPor != null && EF.Functions.Like(r.CreadoPor, patron)) ||
                (consecutivoBuscado != null && r.Consecutivo == consecutivoBuscado));
        }

        query = query.OrderByDescending(r => r.Consecutivo);

        var total = await query.CountAsync(ct);

        var filas = await query
            .Skip((pagina - 1) * tamanoPagina)
            .Take(tamanoPagina)
            .Select(r => new
            {
                r.Id,
                r.Consecutivo,
                r.CompradorNombreSnapshot,
                r.CompradorDocumentoSnapshot,
                r.FechaCreacion,
                r.ReteFuentePorcentaje,
                r.ReteIcaPorMil,
                r.ReteIvaPorcentaje,
                r.EstadoPago,
                r.MedioPago,
                r.TipoEntrega,
                r.EstadoEntrega,
                r.CorreoEnviadoEn,
                r.CreadoPor,
                Items = r.Items.Select(i => new { i.PrecioUnitario, i.Cantidad, i.Iva, i.DescuentoPorcentaje }).ToList()
            })
            .ToListAsync(ct);

        // El total (con IVA y descuento prorrateado) se calcula en memoria con la misma función
        // que usa el detalle, para que lista y detalle nunca difieran.
        var items = filas.Select(f =>
        {
            var calculo = RemisionCalculos.Calcular(f.Items.Select(i => (i.PrecioUnitario, i.Cantidad, i.Iva, i.DescuentoPorcentaje)), f.ReteFuentePorcentaje, f.ReteIcaPorMil, f.ReteIvaPorcentaje);
            return new RemisionResumenDto(
                f.Id, f.Consecutivo, Formatear(f.Consecutivo), f.CompradorNombreSnapshot, f.CompradorDocumentoSnapshot,
                f.FechaCreacion, calculo.TotalGeneral, f.EstadoPago, f.MedioPago, f.TipoEntrega, f.EstadoEntrega,
                f.CorreoEnviadoEn is not null, f.CreadoPor);
        }).ToList();

        return new PaginaResultado<RemisionResumenDto>(items, total, pagina, tamanoPagina);
    }

    public async Task<RemisionDetalleDto?> ObtenerPorIdAsync(int id, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        return remision is null ? null : MapDetalle(remision);
    }

    public async Task<RemisionDetalleDto> CrearAsync(RemisionGuardarDto dto, string? usuario, CancellationToken ct = default)
    {
        var comprador = await ObtenerCompradorActivoAsync(dto.CompradorId, ct);
        var items = await ConstruirItemsAsync(dto, ct);

        var remision = new Remision
        {
            Consecutivo = await ObtenerSiguienteConsecutivoAsync(ct),
            CompradorId = comprador.Id,
            CompradorTipoDocumentoSnapshot = comprador.TipoDocumento,
            CompradorDocumentoSnapshot = comprador.NumeroDocumento,
            CompradorNombreSnapshot = comprador.NombreCompleto,
            CompradorDireccionSnapshot = comprador.Direccion,
            CompradorCiudadSnapshot = comprador.Ciudad,
            CompradorTelefonoSnapshot = comprador.Telefono,
            CompradorEmailSnapshot = comprador.Email,
            Nota = dto.Nota?.Trim(),
            TipoEntrega = dto.TipoEntrega,
            FechaCreacion = timeProvider.GetUtcNow().UtcDateTime,
            CreadoPor = usuario,
            Items = items
        };

        AplicarRetenciones(remision, dto, items, comprador);
        db.Remisiones.Add(remision);
        await db.SaveChangesAsync(ct);

        return MapDetalle(remision);
    }

    public async Task<RemisionDetalleDto?> ActualizarAsync(int id, RemisionGuardarDto dto, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (remision is null)
            return null;

        if (remision.EstadoPago != EstadoPago.Pendiente)
            throw new InvalidOperationException("Solo se puede editar una remisión pendiente de pago.");

        var comprador = await ObtenerCompradorActivoAsync(dto.CompradorId, ct);
        var items = await ConstruirItemsAsync(dto, ct);

        remision.CompradorId = comprador.Id;
        remision.CompradorTipoDocumentoSnapshot = comprador.TipoDocumento;
        remision.CompradorDocumentoSnapshot = comprador.NumeroDocumento;
        remision.CompradorNombreSnapshot = comprador.NombreCompleto;
        remision.CompradorDireccionSnapshot = comprador.Direccion;
        remision.CompradorCiudadSnapshot = comprador.Ciudad;
        remision.CompradorTelefonoSnapshot = comprador.Telefono;
        remision.CompradorEmailSnapshot = comprador.Email;
        AplicarRetenciones(remision, dto, items, comprador);
        remision.Nota = dto.Nota?.Trim();
        remision.TipoEntrega = dto.TipoEntrega;

        // Se reemplazan las líneas completas, re-congelando precio/IVA desde el catálogo actual.
        db.RemisionItems.RemoveRange(remision.Items);
        remision.Items = items;

        await db.SaveChangesAsync(ct);
        return MapDetalle(remision);
    }

    public async Task<PagarResultadoDto?> MarcarPagadaAsync(int id, MarcarPagadaDto dto, string? usuario, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (remision is null)
            return null;

        if (remision.EstadoPago == EstadoPago.Anulada)
            throw new InvalidOperationException("La remisión está anulada.");
        if (remision.EstadoPago == EstadoPago.Pagada)
            throw new InvalidOperationException("La remisión ya está marcada como pagada.");
        if (remision.Items.Count == 0)
            throw new InvalidOperationException("La remisión no tiene ítems.");

        if (!Enum.IsDefined(dto.MedioPago))
            throw new InvalidOperationException("El medio de pago no es válido.");
        // Datáfono y QR pasan por el datáfono Redeban: llevan número de recibo. Efectivo no lleva ninguno.
        if (dto.MedioPago != MedioPago.Efectivo && string.IsNullOrWhiteSpace(dto.ReciboRedeban))
            throw new InvalidOperationException("Debes indicar el número de recibo de Redeban.");

        var ahora = timeProvider.GetUtcNow().UtcDateTime;

        // El stock sale en el momento del cobro. Si algún producto no alcanza, Kardex lanza y no se guarda nada.
        var productos = await CargarProductosAsync(remision, ct);
        foreach (var item in remision.Items)
            Kardex.Registrar(db, productos[item.ProductoId], TipoMovimiento.Venta, -item.Cantidad, ahora, usuario,
                remision.Id, $"Remisión {Formatear(remision.Consecutivo)}");

        remision.EstadoPago = EstadoPago.Pagada;
        remision.FechaPago = ahora;
        remision.PagadaPor = usuario;
        remision.MedioPago = dto.MedioPago;
        remision.ReciboRedeban = dto.MedioPago == MedioPago.Efectivo ? null : dto.ReciboRedeban!.Trim();

        // Entrega en sitio: el producto sale en el momento del cobro. Envío: queda pendiente
        // hasta que logística lo marque como entregado.
        if (remision.TipoEntrega == TipoEntrega.EnSitio)
        {
            remision.EstadoEntrega = EstadoEntrega.Entregada;
            remision.FechaEntrega = ahora;
        }

        await db.SaveChangesAsync(ct);

        // El pago ya quedó guardado; un fallo del correo nunca lo revierte. Con la cola registrada (API) el
        // correo sale en segundo plano y el pago responde al instante; sin cola (pruebas) se envía aquí mismo.
        if (cola is not null)
        {
            cola.Encolar(remision.Id);
            return new PagarResultadoDto(MapDetalle(remision), false, null, CorreoEnCola: true);
        }

        var (enviado, error) = await IntentarEnviarCorreoAsync(remision, ct);
        return new PagarResultadoDto(MapDetalle(remision), enviado, error);
    }

    public async Task<bool> EnviarCopiaComprasAsync(int id, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        var compras = configuration?["Correo:Compras"]?.Trim();
        if (remision is null || remision.EstadoPago != EstadoPago.Pagada || remision.TipoEntrega != TipoEntrega.Envio || string.IsNullOrWhiteSpace(compras))
            return false;

        try
        {
            var detalle = MapDetalle(remision);
            var adjunto = new AdjuntoCorreo($"{detalle.ConsecutivoFormateado}.pdf", pdfBuilder.Generar(detalle), "application/pdf");
            await emailSender.EnviarAsync(compras, AsuntoCompras(detalle),
                ConstruirCuerpoCorreo(detalle, DestinoCorreo.Compras), [adjunto], ct);
            return true;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo enviando la copia a compras de la remisión {Id}", id);
            return false;
        }
    }

    public async Task<EnviarCorreoResultadoDto?> EnviarCorreoAsync(int id, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (remision is null)
            return null;

        if (remision.EstadoPago != EstadoPago.Pagada)
            throw new InvalidOperationException("Solo se envía por correo una remisión ya pagada.");

        var (enviado, error) = await IntentarEnviarCorreoAsync(remision, ct);
        return new EnviarCorreoResultadoDto(MapDetalle(remision), enviado, error);
    }

    public async Task<RemisionDetalleDto?> CambiarTipoEntregaAsync(int id, CambiarTipoEntregaDto dto, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (remision is null)
            return null;

        if (remision.EstadoPago == EstadoPago.Anulada)
            throw new InvalidOperationException("La remisión está anulada.");
        if (remision.EstadoEntrega == EstadoEntrega.Entregada)
            throw new InvalidOperationException("La remisión ya fue entregada.");

        var pasaAEnvio = dto.TipoEntrega == TipoEntrega.Envio && remision.TipoEntrega != TipoEntrega.Envio;
        if (dto.TipoEntrega == TipoEntrega.Envio)
        {
            var nota = string.IsNullOrWhiteSpace(dto.Nota) ? remision.Nota : dto.Nota.Trim();
            ValidarNotaDeEnvio(dto.TipoEntrega, nota);
            remision.Nota = nota;
        }

        remision.TipoEntrega = dto.TipoEntrega;

        // Si ya está pagada y pasa a "en sitio", se entrega en ese momento.
        if (remision.EstadoPago == EstadoPago.Pagada && dto.TipoEntrega == TipoEntrega.EnSitio)
        {
            remision.EstadoEntrega = EstadoEntrega.Entregada;
            remision.FechaEntrega = timeProvider.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(ct);

        // Ya cobrada y ahora de envío posterior: compras todavía no la conoce, se le manda su copia.
        if (pasaAEnvio && remision.EstadoPago == EstadoPago.Pagada)
            cola?.Encolar(remision.Id, soloCompras: true);

        return MapDetalle(remision);
    }

    public async Task<RemisionDetalleDto?> MarcarEntregadaAsync(int id, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (remision is null)
            return null;

        if (remision.EstadoPago != EstadoPago.Pagada)
            throw new InvalidOperationException("Solo se entrega una remisión ya pagada.");
        if (remision.EstadoEntrega == EstadoEntrega.Entregada)
            throw new InvalidOperationException("La remisión ya fue entregada.");

        remision.EstadoEntrega = EstadoEntrega.Entregada;
        remision.FechaEntrega = timeProvider.GetUtcNow().UtcDateTime;

        await db.SaveChangesAsync(ct);
        return MapDetalle(remision);
    }

    public async Task<RemisionDetalleDto?> AnularAsync(int id, AnularRemisionDto dto, bool esAdmin, string? usuario = null, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        if (remision is null)
            return null;

        if (string.IsNullOrWhiteSpace(dto.Motivo))
            throw new InvalidOperationException("El motivo de anulación es obligatorio.");
        if (remision.EstadoPago == EstadoPago.Anulada)
            throw new InvalidOperationException("La remisión ya está anulada.");
        if (!esAdmin)
            throw new InvalidOperationException("Solo un administrador puede anular una remisión.");
        var ahora = timeProvider.GetUtcNow().UtcDateTime;

        // Una remisión ya pagada había sacado stock: se devuelve al anularla.
        if (remision.EstadoPago == EstadoPago.Pagada)
        {
            var productos = await CargarProductosAsync(remision, ct);
            foreach (var item in remision.Items)
                Kardex.Registrar(db, productos[item.ProductoId], TipoMovimiento.Reintegro, item.Cantidad, ahora, usuario,
                    remision.Id, $"Anulación de {Formatear(remision.Consecutivo)}");
        }

        remision.EstadoPago = EstadoPago.Anulada;
        remision.FechaAnulacion = ahora;
        remision.MotivoAnulacion = dto.Motivo.Trim();

        await db.SaveChangesAsync(ct);
        return MapDetalle(remision);
    }

    public async Task<byte[]?> GenerarPdfAsync(int id, CancellationToken ct = default)
    {
        var remision = await db.Remisiones.Include(r => r.Items).FirstOrDefaultAsync(r => r.Id == id, ct);
        return remision is null ? null : pdfBuilder.Generar(MapDetalle(remision));
    }

    // ---------------------------------------------------------------------------------------

    private async Task<(bool Enviado, string? Error)> IntentarEnviarCorreoAsync(Remision remision, CancellationToken ct)
    {
        try
        {
            var detalle = MapDetalle(remision);
            var pdf = pdfBuilder.Generar(detalle);
            var adjuntos = new[] { new AdjuntoCorreo($"{detalle.ConsecutivoFormateado}.pdf", pdf, "application/pdf") };
            var asunto = $"Remisión {detalle.ConsecutivoFormateado} - Jimaco";
            var contabilidad = configuration?["Correo:Contabilidad"]?.Trim();
            // Compras recibe copia de TODA remisión pagada (en sitio o envío posterior).
            var compras = configuration?["Correo:Compras"]?.Trim();

            // Se envían a la vez: el servidor SMTP tarda más de un minuto en aceptar mensajes hacia correos externos
            // (el del cliente), mientras que uno interno (contabilidad) lo acepta en menos de un segundo; en paralelo,
            // contabilidad recibe su copia de inmediato sin esperar al del cliente.
            var tareaCliente = emailSender.EnviarAsync(remision.CompradorEmailSnapshot, asunto,
                ConstruirCuerpoCorreo(detalle, DestinoCorreo.Cliente), adjuntos, ct);
            var tareaContabilidad = string.IsNullOrWhiteSpace(contabilidad)
                ? Task.CompletedTask
                : emailSender.EnviarAsync(contabilidad, $"[Contabilidad] {asunto} - {detalle.CompradorNombre}",
                    ConstruirCuerpoCorreo(detalle, DestinoCorreo.Contabilidad), adjuntos, ct);
            // Compras (Yazmin) recibe solo las remisiones de envío posterior, con la dirección de entrega destacada.
            var tareaCompras = string.IsNullOrWhiteSpace(compras)
                ? Task.CompletedTask
                : emailSender.EnviarAsync(compras, AsuntoCompras(detalle),
                    ConstruirCuerpoCorreo(detalle, DestinoCorreo.Compras), adjuntos, ct);

            var errorCliente = await EsperarAsync(tareaCliente);
            var errorContabilidad = await EsperarAsync(tareaContabilidad);
            var errorCompras = await EsperarAsync(tareaCompras);

            if (errorCliente is not null)
            {
                logger.LogError(errorCliente, "Fallo enviando el correo de la remisión {Id} al cliente", remision.Id);
                // Errores de configuración (ej. SMTP sin configurar) traen un mensaje pensado para el usuario.
                return (false, errorCliente is InvalidOperationException
                    ? errorCliente.Message
                    : "No se pudo enviar el correo. Puedes reintentarlo desde el detalle de la remisión.");
            }

            // El envío pudo tardar más de un minuto y la remisión cambiar mientras tanto (se entregó, se anuló...):
            // se recarga antes de marcarla para no chocar con el control de concurrencia.
            await db.Entry(remision).ReloadAsync(ct);
            remision.CorreoEnviadoEn = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);

            if (errorContabilidad is not null || errorCompras is not null)
            {
                logger.LogError(errorContabilidad ?? errorCompras, "Fallo enviando una copia interna de la remisión {Id}", remision.Id);
                return (true, "La remisión llegó al cliente, pero no se pudo enviar una copia interna (contabilidad o compras). Reintenta desde el detalle.");
            }
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            logger.LogWarning(ex, "No se pudo enviar el correo de la remisión {Id}", remision.Id);
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo enviando el correo de la remisión {Id}", remision.Id);
            return (false, "No se pudo enviar el correo. Puedes reintentarlo desde el detalle de la remisión.");
        }
    }

    internal static string NombreMedioPago(MedioPago? medio) => medio switch
    {
        MedioPago.Datafono => "Datáfono",
        MedioPago.Qr => "Pago con QR",
        MedioPago.Efectivo => "Efectivo",
        _ => "—"
    };

    private static async Task<Exception?> EsperarAsync(Task tarea)
    {
        try
        {
            await tarea;
            return null;
        }
        catch (Exception ex)
        {
            return ex;
        }
    }

    // La remisión completa va en el cuerpo del correo (visible sin abrir adjuntos); el PDF va además adjunto.
    private static string AsuntoCompras(RemisionDetalleDto d) => d.TipoEntrega == TipoEntrega.Envio
        ? $"[Compras] Envío posterior {d.ConsecutivoFormateado} - {d.CompradorNombre}"
        : $"[Compras] Remisión {d.ConsecutivoFormateado} - {d.CompradorNombre}";

    private enum DestinoCorreo { Cliente, Contabilidad, Compras }

    private static string ConstruirCuerpoCorreo(RemisionDetalleDto r, DestinoCorreo destino)
    {
        static string E(string? t) => WebUtility.HtmlEncode(t ?? "");
        static string M(decimal v) => $"$ {v.ToString("N0", CulturaCo)}";
        const string Td = "padding:6px;border-bottom:1px solid #eee";
        const string Et = "padding:2px 10px 2px 0;color:#777";

        // La nota de un envío posterior lleva la dirección y ciudad de entrega (puede traer saltos de línea).
        var notaHtml = E(r.Nota).Replace("\r\n", "<br>").Replace("\n", "<br>");
        var entrega = r.TipoEntrega == TipoEntrega.EnSitio
            ? "Entregada en el punto de venta."
            : $"Envío posterior a:<br><strong>{notaHtml}</strong>";

        var hayDescuentos = r.Items.Any(i => i.Descuento > 0);
        var filas = string.Concat(r.Items.Select(i =>
            $"<tr><td style=\"{Td}\">{E(i.ProductoCodigo)}</td><td style=\"{Td}\">{E(i.ProductoNombre)}</td>" +
            $"<td style=\"{Td};text-align:right\">{i.Cantidad}</td><td style=\"{Td};text-align:right\">{M(i.PrecioUnitario)}</td>" +
            $"<td style=\"{Td};text-align:right\">{i.Iva}%</td>" +
            (hayDescuentos ? $"<td style=\"{Td};text-align:right\">{(i.DescuentoPorcentaje > 0 ? $"{i.DescuentoPorcentaje:0.##}%" : "—")}</td>" : "") +
            $"<td style=\"{Td};text-align:right\">{M(i.SubtotalNeto)}</td></tr>"));
        var encabezadoDescuento = hayDescuentos ? "<th style=\"padding:6px;text-align:right\">Desc.</th>" : "";

        const string Tot = "padding:2px 6px;text-align:right";
        var ivas = string.Concat(r.IvaDesglose.Select(d =>
            $"<tr><td style=\"{Tot}\">IVA {d.Tarifa}%</td><td style=\"{Tot}\">{M(d.Valor)}</td></tr>"));
        var descuento = r.Descuento > 0
            ? $"<tr><td style=\"{Tot}\">Descuentos</td><td style=\"{Tot}\">- {M(r.Descuento)}</td></tr>"
            : "";
        var hayRetenciones = r.ReteFuente > 0 || r.ReteIca > 0 || r.ReteIva > 0;
        var retenciones = !hayRetenciones ? "" :
            $"<tr><td style=\"{Tot}\">Total antes de retenciones</td><td style=\"{Tot}\">{M(r.TotalBruto)}</td></tr>" +
            (r.ReteFuente > 0 ? $"<tr><td style=\"{Tot}\">ReteFuente ({r.ReteFuentePorcentaje:0.##}%)</td><td style=\"{Tot}\">- {M(r.ReteFuente)}</td></tr>" : "") +
            (r.ReteIca > 0 ? $"<tr><td style=\"{Tot}\">ReteICA ({r.ReteIcaPorMil:0.###}‰)</td><td style=\"{Tot}\">- {M(r.ReteIca)}</td></tr>" : "") +
            (r.ReteIva > 0 ? $"<tr><td style=\"{Tot}\">ReteIVA ({r.ReteIvaPorcentaje:0.##}% del IVA)</td><td style=\"{Tot}\">- {M(r.ReteIva)}</td></tr>" : "");

        var saludo = destino == DestinoCorreo.Compras && r.TipoEntrega == TipoEntrega.EnSitio
            ? $"<p>Nueva venta: la remisión <strong>{r.ConsecutivoFormateado}</strong> ya está pagada y fue <strong>entregada en el punto de venta</strong>. Copia para compras:</p>"
            : destino == DestinoCorreo.Compras
            ? $"<p><strong>Envío posterior pendiente de despachar.</strong> La remisión <strong>{r.ConsecutivoFormateado}</strong> ya está pagada. Entregar en:</p><p style=\"background:#fff8e1;border:1px solid #f0d58a;padding:10px\">{notaHtml}</p>"
            : destino == DestinoCorreo.Contabilidad
            ? $"<p>Nueva venta registrada: remisión <strong>{r.ConsecutivoFormateado}</strong>. Datos del cliente y de la venta:</p>"
            : $"<p>Hola <strong>{E(r.CompradorNombre)}</strong>,</p><p>Gracias por su compra. Esta es su remisión <strong>{r.ConsecutivoFormateado}</strong> (también adjunta en PDF).</p>";

        var reciboFila = string.IsNullOrWhiteSpace(r.ReciboRedeban)
            ? ""
            : $"<tr><td style=\"{Et}\">Recibo Redeban</td><td><strong>{E(r.ReciboRedeban)}</strong></td></tr>";
        var fechaPago = r.FechaPago?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CulturaCo);

        return $"""
            <div style="font-family:Arial,Helvetica,sans-serif;color:#333;max-width:680px">
              <div style="background:#1c6521;color:#fff;padding:14px 18px;font-size:18px;font-weight:bold">JIMACO · Remisión {r.ConsecutivoFormateado}</div>
              <div style="padding:18px;border:1px solid #ddd;border-top:0">
                {saludo}
                <table style="font-size:13px;margin-bottom:12px">
                  <tr><td style="{Et}">Cliente</td><td>{E(r.CompradorNombre)}</td></tr>
                  <tr><td style="{Et}">Documento</td><td>{E(r.CompradorTipoDocumento)} {E(r.CompradorDocumento)}</td></tr>
                  <tr><td style="{Et}">Correo</td><td>{E(r.CompradorEmail)}</td></tr>
                  <tr><td style="{Et}">Teléfono</td><td>{E(r.CompradorTelefono)}</td></tr>
                  <tr><td style="{Et}">Ciudad</td><td>{E(r.CompradorCiudad)}</td></tr>
                  <tr><td style="{Et}">Dirección</td><td>{E(r.CompradorDireccion)}</td></tr>
                  <tr><td style="{Et}">Fecha de pago</td><td>{fechaPago}</td></tr>
                  <tr><td style="{Et}">Medio de pago</td><td><strong>{NombreMedioPago(r.MedioPago)}</strong></td></tr>
                  {reciboFila}
                  <tr><td style="{Et}">Entrega</td><td>{entrega}</td></tr>
                </table>
                <table style="width:100%;border-collapse:collapse;font-size:13px">
                  <tr style="background:#1c6521;color:#fff">
                    <th style="padding:6px;text-align:left">Código</th><th style="padding:6px;text-align:left">Producto</th>
                    <th style="padding:6px;text-align:right">Cant.</th><th style="padding:6px;text-align:right">Precio</th>
                    <th style="padding:6px;text-align:right">IVA</th>{encabezadoDescuento}<th style="padding:6px;text-align:right">Subtotal</th>
                  </tr>
                  {filas}
                </table>
                <table style="margin-left:auto;margin-top:10px;font-size:13px">
                  <tr><td style="{Tot}">Subtotal ítems</td><td style="{Tot}">{M(r.SubtotalItems)}</td></tr>
                  {descuento}
                  <tr><td style="{Tot}">Valor antes de IVA</td><td style="{Tot}">{M(r.SubtotalConDescuento)}</td></tr>
                  {ivas}
                  {retenciones}
                  <tr><td style="{Tot};font-weight:bold">{(hayRetenciones ? "TOTAL A PAGAR" : "TOTAL")}</td><td style="{Tot};font-weight:bold">{M(r.TotalGeneral)}</td></tr>
                </table>
                <p style="color:#777;font-size:12px">Este documento es una remisión de entrega y no reemplaza la factura de venta.</p>
              </div>
            </div>
            """;
    }

    private async Task<Comprador> ObtenerCompradorActivoAsync(int compradorId, CancellationToken ct)
    {
        var comprador = await db.Compradores.FirstOrDefaultAsync(c => c.Id == compradorId, ct);
        if (comprador is null || !comprador.Activo)
            throw new InvalidOperationException("El comprador indicado no existe o no está activo.");
        // Los compradores traídos de World Office pueden no tener correo; sin él no hay a dónde enviar la remisión.
        if (string.IsNullOrWhiteSpace(comprador.Email))
            throw new InvalidOperationException("El comprador no tiene correo electrónico. Edítalo y agrégalo: ahí se envía la remisión.");
        return comprador;
    }

    // En un envío posterior las notas llevan la dirección y la ciudad de entrega: son obligatorias (y admiten bastante texto).
    private static void ValidarNotaDeEnvio(TipoEntrega tipo, string? nota)
    {
        if (nota is { Length: > 1000 })
            throw new InvalidOperationException("La nota admite máximo 1.000 caracteres.");
        if (tipo == TipoEntrega.Envio && (nota is null || nota.Trim().Length < 10))
            throw new InvalidOperationException("Para un envío posterior debes escribir en las notas la dirección y la ciudad de entrega.");
    }

    // Solo se maneja ReteFuente (ReteICA y ReteIVA no se aplican: se ignoran aunque lleguen activadas). Tarifa FIJA (no se digita),
    // solo a persona jurídica (NIT) y solo si el valor antes de IVA (ya con descuentos) llega al mínimo.
    private void AplicarRetenciones(Remision remision, RemisionGuardarDto dto, IEnumerable<RemisionItem> items, Comprador comprador)
    {
        var reglas = ReglasRetenciones.Cargar(configuration);
        if (dto.ReteFuente)
        {
            if (!string.Equals(comprador.TipoDocumento, "NIT", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("La retención en la fuente solo aplica a personas jurídicas (NIT).");

            var baseAntesDeIva = RemisionCalculos
                .Calcular(items.Select(i => (i.PrecioUnitario, i.Cantidad, i.Iva, i.DescuentoPorcentaje)))
                .SubtotalConDescuento;
            if (baseAntesDeIva < reglas.BaseMinima)
                throw new InvalidOperationException(
                    $"La retención solo aplica cuando el valor antes de IVA es de $ {reglas.BaseMinima.ToString("N0", CulturaCo)} o más.");
        }

        remision.ReteFuentePorcentaje = dto.ReteFuente ? reglas.ReteFuentePorcentaje : 0;
        remision.ReteIcaPorMil = 0;
        remision.ReteIvaPorcentaje = 0;
    }

    // Valida las líneas y las congela desde el catálogo vigente. Mismo producto repetido se suma.
    private async Task<List<RemisionItem>> ConstruirItemsAsync(RemisionGuardarDto dto, CancellationToken ct)
    {
        if (dto.Items is null || dto.Items.Count == 0)
            throw new InvalidOperationException("La remisión debe tener al menos un ítem.");
        if (dto.Items.Any(i => i.Cantidad < 1))
            throw new InvalidOperationException("La cantidad de cada ítem debe ser mayor a cero.");
        if (dto.Items.Any(i => i.DescuentoPorcentaje < 0 || i.DescuentoPorcentaje > 50))
            throw new InvalidOperationException("El descuento de cada ítem debe estar entre 0% y 50%.");
        // Un mismo producto repetido se suma; si viene con descuentos distintos no hay a cuál línea aplicarlos.
        if (dto.Items.GroupBy(i => i.ProductoId).Any(g => g.Select(i => i.DescuentoPorcentaje).Distinct().Count() > 1))
            throw new InvalidOperationException("Un producto repetido tiene descuentos distintos; deja una sola línea por producto.");
        ValidarNotaDeEnvio(dto.TipoEntrega, dto.Nota);

        var cantidades = dto.Items
            .GroupBy(i => i.ProductoId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Cantidad));
        var descuentos = dto.Items
            .GroupBy(i => i.ProductoId)
            .ToDictionary(g => g.Key, g => g.First().DescuentoPorcentaje);

        var ids = cantidades.Keys.ToList();
        var productos = await db.Productos.Where(p => ids.Contains(p.Id)).ToListAsync(ct);

        var items = new List<RemisionItem>();
        foreach (var (productoId, cantidad) in cantidades)
        {
            var producto = productos.FirstOrDefault(p => p.Id == productoId)
                ?? throw new InvalidOperationException($"No existe el producto con id {productoId}.");
            if (!producto.Activo)
                throw new InvalidOperationException($"El producto '{producto.Codigo}' está inactivo.");
            if (producto.PrecioVenta <= 0)
                throw new InvalidOperationException($"El producto '{producto.Codigo}' no tiene precio de venta. Asígnalo en Productos antes de venderlo.");
            // El stock es de esta app, así que se puede validar de verdad: no se arma una remisión con más de lo que hay.
            if (cantidad > producto.Stock)
                throw new InvalidOperationException($"Stock insuficiente de '{producto.Codigo}': hay {producto.Stock} y pides {cantidad}.");

            items.Add(new RemisionItem
            {
                ProductoId = producto.Id,
                ProductoCodigoSnapshot = producto.Codigo,
                ProductoNombreSnapshot = producto.Nombre,
                PrecioUnitario = producto.PrecioVenta,
                CostoSnapshot = producto.Costo,
                Iva = producto.Iva,
                Cantidad = cantidad,
                DescuentoPorcentaje = descuentos[productoId]
            });
        }

        return items;
    }

    // SELECT NEXT VALUE FOR no se puede ejecutar vía SqlQuery de EF (lo envuelve en una
    // subconsulta y SQL Server lo rechaza, error 11719), por eso se usa ADO.NET directo.
    private async Task<int> ObtenerSiguienteConsecutivoAsync(CancellationToken ct)
    {
        var connection = db.Database.GetDbConnection();
        await db.Database.OpenConnectionAsync(ct);
        try
        {
            using var command = connection.CreateCommand();
            // Nombre literal a propósito: debe coincidir con AppDbContext.SecuenciaConsecutivoRemision.
            command.CommandText = "SELECT NEXT VALUE FOR dbo.SecuenciaConsecutivoRemision";
            var resultado = await command.ExecuteScalarAsync(ct);
            return Convert.ToInt32(resultado);
        }
        finally
        {
            await db.Database.CloseConnectionAsync();
        }
    }

    internal static string Formatear(int consecutivo) => $"RMF-{consecutivo:D5}";

    // Productos de la remisión, rastreados para poder mover su stock.
    private async Task<Dictionary<int, Producto>> CargarProductosAsync(Remision remision, CancellationToken ct)
    {
        var ids = remision.Items.Select(i => i.ProductoId).Distinct().ToList();
        var productos = await db.Productos.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var faltante = ids.FirstOrDefault(id => !productos.ContainsKey(id));
        if (faltante != 0)
            throw new InvalidOperationException($"El producto con id {faltante} ya no existe.");
        return productos;
    }

    private static RemisionDetalleDto MapDetalle(Remision r)
    {
        // Orden estable por Id: sin esto SQL Server no garantiza el orden de la colección.
        var lineas = r.Items.OrderBy(i => i.Id).ToList();
        var calculo = RemisionCalculos.Calcular(lineas.Select(i => (i.PrecioUnitario, i.Cantidad, i.Iva, i.DescuentoPorcentaje)), r.ReteFuentePorcentaje, r.ReteIcaPorMil, r.ReteIvaPorcentaje);

        var items = lineas.Select(i => new RemisionItemDto(
            i.Id, i.ProductoId, i.ProductoCodigoSnapshot, i.ProductoNombreSnapshot, i.PrecioUnitario, i.Iva,
            i.Cantidad, i.PrecioUnitario * i.Cantidad, i.DescuentoPorcentaje,
            RemisionCalculos.DescuentoLinea(i.PrecioUnitario, i.Cantidad, i.DescuentoPorcentaje),
            i.PrecioUnitario * i.Cantidad - RemisionCalculos.DescuentoLinea(i.PrecioUnitario, i.Cantidad, i.DescuentoPorcentaje),
            i.CostoSnapshot,
            RemisionCalculos.MargenPorcentaje(i.PrecioUnitario, i.CostoSnapshot))).ToList();

        return new RemisionDetalleDto(
            r.Id, r.Consecutivo, Formatear(r.Consecutivo), r.CompradorId,
            r.CompradorTipoDocumentoSnapshot, r.CompradorDocumentoSnapshot, r.CompradorNombreSnapshot,
            r.CompradorDireccionSnapshot, r.CompradorCiudadSnapshot, r.CompradorTelefonoSnapshot, r.CompradorEmailSnapshot,
            r.Nota, r.FechaCreacion, r.CreadoPor,
            r.EstadoPago, r.FechaPago, r.PagadaPor, r.ReciboRedeban, r.MedioPago,
            r.TipoEntrega, r.EstadoEntrega, r.FechaEntrega, r.CorreoEnviadoEn,
            r.FechaAnulacion, r.MotivoAnulacion,
            items, calculo.SubtotalItems, calculo.Descuento, r.ReteFuentePorcentaje, r.ReteIcaPorMil, r.ReteIvaPorcentaje,
            calculo.SubtotalConDescuento, calculo.IvaDesglose, calculo.TotalBruto, calculo.ReteFuente, calculo.ReteIca, calculo.ReteIva, calculo.TotalGeneral);
    }
}
