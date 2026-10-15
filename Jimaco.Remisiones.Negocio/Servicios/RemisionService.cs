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
    IConfiguration? configuration = null) : IRemisionService
{
    // Reconoce el consecutivo formateado ("RM-00173"), con o sin prefijo y ceros a la izquierda,
    // para que buscar por el número que ve el comprador también funcione.
    [GeneratedRegex(@"^(?:rm-?)?0*(\d+)$", RegexOptions.IgnoreCase)]
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
                r.Descuento,
                r.EstadoPago,
                r.TipoEntrega,
                r.EstadoEntrega,
                r.CorreoEnviadoEn,
                Items = r.Items.Select(i => new { i.PrecioUnitario, i.Cantidad, i.Iva }).ToList()
            })
            .ToListAsync(ct);

        // El total (con IVA y descuento prorrateado) se calcula en memoria con la misma función
        // que usa el detalle, para que lista y detalle nunca difieran.
        var items = filas.Select(f =>
        {
            var calculo = RemisionCalculos.Calcular(f.Items.Select(i => (i.PrecioUnitario, i.Cantidad, i.Iva)), f.Descuento);
            return new RemisionResumenDto(
                f.Id, f.Consecutivo, Formatear(f.Consecutivo), f.CompradorNombreSnapshot, f.CompradorDocumentoSnapshot,
                f.FechaCreacion, calculo.TotalGeneral, f.EstadoPago, f.TipoEntrega, f.EstadoEntrega,
                f.CorreoEnviadoEn is not null);
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
            Descuento = dto.Descuento,
            Nota = dto.Nota?.Trim(),
            TipoEntrega = dto.TipoEntrega,
            FechaCreacion = timeProvider.GetUtcNow().UtcDateTime,
            CreadoPor = usuario,
            Items = items
        };

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
        remision.Descuento = dto.Descuento;
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

        if (string.IsNullOrWhiteSpace(dto.ReciboRedeban))
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
        remision.ReciboRedeban = dto.ReciboRedeban.Trim();

        // Entrega en sitio: el producto sale en el momento del cobro. Envío: queda pendiente
        // hasta que logística lo marque como entregado.
        if (remision.TipoEntrega == TipoEntrega.EnSitio)
        {
            remision.EstadoEntrega = EstadoEntrega.Entregada;
            remision.FechaEntrega = ahora;
        }

        await db.SaveChangesAsync(ct);

        // El pago ya quedó guardado; un fallo del correo nunca lo revierte.
        var (enviado, error) = await IntentarEnviarCorreoAsync(remision, ct);
        return new PagarResultadoDto(MapDetalle(remision), enviado, error);
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

        remision.TipoEntrega = dto.TipoEntrega;

        // Si ya está pagada y pasa a "en sitio", se entrega en ese momento.
        if (remision.EstadoPago == EstadoPago.Pagada && dto.TipoEntrega == TipoEntrega.EnSitio)
        {
            remision.EstadoEntrega = EstadoEntrega.Entregada;
            remision.FechaEntrega = timeProvider.GetUtcNow().UtcDateTime;
        }

        await db.SaveChangesAsync(ct);
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
        if (remision.EstadoPago == EstadoPago.Pagada && !esAdmin)
            throw new InvalidOperationException("Solo un administrador puede anular una remisión ya pagada.");
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

            // Cliente: es el destinatario que importa; si falla, se informa y se puede reintentar.
            await emailSender.EnviarAsync(remision.CompradorEmailSnapshot, asunto,
                ConstruirCuerpoCorreo(detalle, paraContabilidad: false), adjuntos, ct);
            remision.CorreoEnviadoEn = timeProvider.GetUtcNow().UtcDateTime;
            await db.SaveChangesAsync(ct);

            // Contabilidad: copia con los datos completos del cliente. Un fallo aquí no invalida el envío al cliente.
            var contabilidad = configuration?["Correo:Contabilidad"];
            if (!string.IsNullOrWhiteSpace(contabilidad))
            {
                try
                {
                    await emailSender.EnviarAsync(contabilidad.Trim(), $"[Contabilidad] {asunto} - {detalle.CompradorNombre}",
                        ConstruirCuerpoCorreo(detalle, paraContabilidad: true), adjuntos, ct);
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Fallo enviando la copia a contabilidad de la remisión {Id}", remision.Id);
                    return (true, "La remisión llegó al cliente, pero no se pudo enviar la copia a contabilidad. Reintenta desde el detalle.");
                }
            }
            return (true, null);
        }
        catch (InvalidOperationException ex)
        {
            // Errores de configuración con mensaje pensado para el usuario (ej. SMTP sin configurar).
            logger.LogWarning(ex, "No se pudo enviar el correo de la remisión {Id}", remision.Id);
            return (false, ex.Message);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Fallo enviando el correo de la remisión {Id}", remision.Id);
            return (false, "No se pudo enviar el correo. Puedes reintentarlo desde el detalle de la remisión.");
        }
    }

    // La remisión completa va en el cuerpo del correo (visible sin abrir adjuntos); el PDF va además adjunto.
    private static string ConstruirCuerpoCorreo(RemisionDetalleDto r, bool paraContabilidad)
    {
        static string E(string? t) => WebUtility.HtmlEncode(t ?? "");
        static string M(decimal v) => $"$ {v.ToString("N0", CulturaCo)}";
        const string Td = "padding:6px;border-bottom:1px solid #eee";
        const string Et = "padding:2px 10px 2px 0;color:#777";

        var entrega = r.TipoEntrega == TipoEntrega.EnSitio
            ? "Entregada en el punto de venta."
            : "Se despachará a la dirección indicada; le avisaremos cuando salga.";

        var filas = string.Concat(r.Items.Select(i =>
            $"<tr><td style=\"{Td}\">{E(i.ProductoCodigo)}</td><td style=\"{Td}\">{E(i.ProductoNombre)}</td>" +
            $"<td style=\"{Td};text-align:right\">{i.Cantidad}</td><td style=\"{Td};text-align:right\">{M(i.PrecioUnitario)}</td>" +
            $"<td style=\"{Td};text-align:right\">{i.Iva}%</td><td style=\"{Td};text-align:right\">{M(i.Subtotal)}</td></tr>"));

        const string Tot = "padding:2px 6px;text-align:right";
        var ivas = string.Concat(r.IvaDesglose.Select(d =>
            $"<tr><td style=\"{Tot}\">IVA {d.Tarifa}%</td><td style=\"{Tot}\">{M(d.Valor)}</td></tr>"));
        var descuento = r.Descuento > 0
            ? $"<tr><td style=\"{Tot}\">Descuento</td><td style=\"{Tot}\">- {M(r.Descuento)}</td></tr>"
            : "";

        var saludo = paraContabilidad
            ? $"<p>Nueva venta registrada: remisión <strong>{r.ConsecutivoFormateado}</strong>. Datos del cliente y de la venta:</p>"
            : $"<p>Hola <strong>{E(r.CompradorNombre)}</strong>,</p><p>Gracias por su compra. Esta es su remisión <strong>{r.ConsecutivoFormateado}</strong> (también adjunta en PDF).</p>";

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
                  <tr><td style="{Et}">Recibo Redeban</td><td><strong>{E(r.ReciboRedeban)}</strong></td></tr>
                  <tr><td style="{Et}">Entrega</td><td>{entrega}</td></tr>
                </table>
                <table style="width:100%;border-collapse:collapse;font-size:13px">
                  <tr style="background:#1c6521;color:#fff">
                    <th style="padding:6px;text-align:left">Código</th><th style="padding:6px;text-align:left">Producto</th>
                    <th style="padding:6px;text-align:right">Cant.</th><th style="padding:6px;text-align:right">Precio</th>
                    <th style="padding:6px;text-align:right">IVA</th><th style="padding:6px;text-align:right">Subtotal</th>
                  </tr>
                  {filas}
                </table>
                <table style="margin-left:auto;margin-top:10px;font-size:13px">
                  <tr><td style="{Tot}">Subtotal</td><td style="{Tot}">{M(r.SubtotalConDescuento)}</td></tr>
                  {descuento}
                  {ivas}
                  <tr><td style="{Tot};font-weight:bold">TOTAL</td><td style="{Tot};font-weight:bold">{M(r.TotalGeneral)}</td></tr>
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

    // Valida las líneas y las congela desde el catálogo vigente. Mismo producto repetido se suma.
    private async Task<List<RemisionItem>> ConstruirItemsAsync(RemisionGuardarDto dto, CancellationToken ct)
    {
        if (dto.Items is null || dto.Items.Count == 0)
            throw new InvalidOperationException("La remisión debe tener al menos un ítem.");
        if (dto.Items.Any(i => i.Cantidad < 1))
            throw new InvalidOperationException("La cantidad de cada ítem debe ser mayor a cero.");
        if (dto.Descuento < 0)
            throw new InvalidOperationException("El descuento no puede ser negativo.");

        var cantidades = dto.Items
            .GroupBy(i => i.ProductoId)
            .ToDictionary(g => g.Key, g => g.Sum(i => i.Cantidad));

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
                Cantidad = cantidad
            });
        }

        var subtotal = items.Sum(i => i.PrecioUnitario * i.Cantidad);
        if (dto.Descuento > subtotal)
            throw new InvalidOperationException("El descuento no puede ser mayor al subtotal de la remisión.");

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

    internal static string Formatear(int consecutivo) => $"RM-{consecutivo:D5}";

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
        var calculo = RemisionCalculos.Calcular(lineas.Select(i => (i.PrecioUnitario, i.Cantidad, i.Iva)), r.Descuento);

        var items = lineas.Select(i => new RemisionItemDto(
            i.Id, i.ProductoId, i.ProductoCodigoSnapshot, i.ProductoNombreSnapshot, i.PrecioUnitario, i.Iva,
            i.Cantidad, i.PrecioUnitario * i.Cantidad, i.CostoSnapshot,
            RemisionCalculos.MargenPorcentaje(i.PrecioUnitario, i.CostoSnapshot))).ToList();

        return new RemisionDetalleDto(
            r.Id, r.Consecutivo, Formatear(r.Consecutivo), r.CompradorId,
            r.CompradorTipoDocumentoSnapshot, r.CompradorDocumentoSnapshot, r.CompradorNombreSnapshot,
            r.CompradorDireccionSnapshot, r.CompradorCiudadSnapshot, r.CompradorTelefonoSnapshot, r.CompradorEmailSnapshot,
            r.Nota, r.FechaCreacion, r.CreadoPor,
            r.EstadoPago, r.FechaPago, r.PagadaPor, r.ReciboRedeban,
            r.TipoEntrega, r.EstadoEntrega, r.FechaEntrega, r.CorreoEnviadoEn,
            r.FechaAnulacion, r.MotivoAnulacion,
            items, calculo.SubtotalItems, r.Descuento, calculo.SubtotalConDescuento, calculo.IvaDesglose, calculo.TotalGeneral);
    }
}
