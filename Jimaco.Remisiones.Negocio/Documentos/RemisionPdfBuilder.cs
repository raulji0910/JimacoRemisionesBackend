using System.Globalization;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Microsoft.Extensions.Options;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace Jimaco.Remisiones.Negocio.Documentos;

// Genera el PDF de la remisión con la marca de Jimaco (verde + logo). Misma estructura que la
// cotización de Ferrealiados: encabezado, grilla de datos del comprador, tabla de ítems, y bloque
// de observaciones + desglose de totales con IVA por tarifa. No muestra costo ni margen.
public class RemisionPdfBuilder(IOptions<DatosEmpresaOptions> datosEmpresaOptions) : IRemisionPdfBuilder
{
    private const string VerdeMarca = "#1C6521";
    private const string VerdeMedio = "#3F863E";
    private const string VerdeClaro = "#ECF5E8";
    private const string GrisTexto = "#4A4A4A";
    private const string RojoAnulada = "#B3261E";

    private static readonly CultureInfo CulturaMoneda = CultureInfo.GetCultureInfo("es-CO");

    // Observaciones fijas de toda remisión: política de devolución de Jimaco (texto aprobado por la empresa).
    private const string PoliticaTitulo = "POLÍTICA DE DEVOLUCIÓN DE MERCANCÍA Y DINERO";

    private const string PoliticaTexto =
        "Toda solicitud de devolución deberá presentarse dentro de los cinco (5) días hábiles siguientes a la entrega de la mercancía, " +
        "a través del correo electrónico contabilidad@jimaco.com.co o del asesor comercial responsable, indicando el motivo y la factura " +
        "correspondiente. La mercancía deberá estar sin uso, completa, en buen estado y con su empaque original en condiciones aptas para " +
        "su comercialización. Toda devolución estará sujeta a revisión y aprobación previa. Los reembolsos aprobados se efectuarán " +
        "exclusivamente mediante transferencia bancaria, en un plazo máximo de ocho (8) días hábiles contados a partir de la aprobación " +
        "de la devolución. No se realizan devoluciones de dinero en efectivo. Estos plazos no limitan los derechos legales del consumidor " +
        "cuando sean aplicables.";

    private static readonly byte[] LogoBytes = CargarLogo();

    private readonly DatosEmpresaOptions empresa = datosEmpresaOptions.Value;

    public byte[] Generar(RemisionDetalleDto remision)
    {
        var documento = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(25);
                page.DefaultTextStyle(x => x.FontSize(9).FontColor(GrisTexto));

                page.Header().Element(c => ComponerEncabezado(c, remision));
                page.Content().PaddingTop(15).Element(c => ComponerContenido(c, remision));
                page.Footer().Element(ComponerPiePagina);
            });
        });

        return documento.GeneratePdf();
    }

    private void ComponerEncabezado(IContainer container, RemisionDetalleDto remision)
    {
        var (textoEstado, colorEstado) = remision.EstadoPago switch
        {
            EstadoPago.Pagada => ("PAGADA", VerdeMedio),
            EstadoPago.Anulada => ("ANULADA", RojoAnulada),
            _ => ("PENDIENTE DE PAGO", "#B26A00")
        };

        container.Column(col =>
        {
            col.Item().Row(row =>
            {
                // El logo ya trae el nombre: va con un tamaño moderado y los datos de la empresa quedan centrados justo debajo
                // (sin repetir el nombre), en una columna del mismo ancho del logo para que se vean como un solo bloque.
                row.ConstantItem(165).Column(datos =>
                {
                    datos.Item().Image(LogoBytes).FitWidth();
                    datos.Item().PaddingTop(1).AlignCenter().Column(texto =>
                    {
                        if (!string.IsNullOrWhiteSpace(empresa.Nit))
                            texto.Item().AlignCenter().Text($"NIT {empresa.Nit}").FontSize(9).Bold();
                        if (!string.IsNullOrWhiteSpace(empresa.Direccion))
                            texto.Item().AlignCenter().Text(empresa.Direccion).FontSize(9);
                        if (!string.IsNullOrWhiteSpace(empresa.Ciudad))
                            texto.Item().AlignCenter().Text(empresa.Ciudad).FontSize(9);
                    });
                });

                row.RelativeItem();

                row.ConstantItem(150).Column(badge =>
                {
                    badge.Item().Background(VerdeMarca).Padding(6).AlignCenter()
                        .Text("REMISIÓN").FontColor(Colors.White).Bold().FontSize(11);
                    badge.Item().BorderColor(VerdeMarca).Border(1).PaddingVertical(6).AlignCenter()
                        .Text(remision.ConsecutivoFormateado).FontColor(VerdeMarca).Bold().FontSize(11);
                    badge.Item().PaddingTop(3).AlignCenter()
                        .Text(textoEstado).FontColor(colorEstado).Bold().FontSize(9);
                });
            });

            col.Item().PaddingTop(12).Element(c => ComponerGrillaComprador(c, remision));
        });
    }

    private static void ComponerGrillaComprador(IContainer container, RemisionDetalleDto r)
    {
        container.Table(tabla =>
        {
            tabla.ColumnsDefinition(columnas =>
            {
                columnas.RelativeColumn(2);
                columnas.RelativeColumn(3);
                columnas.RelativeColumn(2);
                columnas.RelativeColumn(2);
            });

            EtiquetaCelda(tabla.Cell(), "DOCUMENTO");
            EtiquetaCelda(tabla.Cell(), "COMPRADOR");
            EtiquetaCelda(tabla.Cell(), "FECHA");
            EtiquetaCelda(tabla.Cell(), "CIUDAD");

            ValorCelda(tabla.Cell(), $"{r.CompradorTipoDocumento} {r.CompradorDocumento}");
            ValorCelda(tabla.Cell(), r.CompradorNombre);
            ValorCelda(tabla.Cell(), r.FechaCreacion.ToLocalTime().ToString("dd/MM/yyyy", CulturaMoneda));
            ValorCelda(tabla.Cell(), r.CompradorCiudad);

            EtiquetaCelda(tabla.Cell(), "TELÉFONO");
            EtiquetaCelda(tabla.Cell(), "DIRECCIÓN");
            EtiquetaCelda(tabla.Cell(), "EMAIL");
            EtiquetaCelda(tabla.Cell(), "ENTREGA");

            ValorCelda(tabla.Cell(), r.CompradorTelefono);
            ValorCelda(tabla.Cell(), r.CompradorDireccion);
            ValorCelda(tabla.Cell(), r.CompradorEmail);
            ValorCelda(tabla.Cell(), r.TipoEntrega == TipoEntrega.EnSitio ? "En el punto de venta" : "Envío posterior");

            if (r.FechaPago is not null)
            {
                EtiquetaCelda(tabla.Cell(), "MEDIO DE PAGO");
                EtiquetaCelda(tabla.Cell(), string.IsNullOrWhiteSpace(r.ReciboRedeban) ? "" : "RECIBO REDEBAN");
                EtiquetaCelda(tabla.Cell(), "FECHA DE PAGO");
                EtiquetaCelda(tabla.Cell(), "");

                ValorCelda(tabla.Cell(), Servicios.RemisionService.NombreMedioPago(r.MedioPago));
                ValorCelda(tabla.Cell(), r.ReciboRedeban);
                ValorCelda(tabla.Cell(), r.FechaPago?.ToLocalTime().ToString("dd/MM/yyyy HH:mm", CulturaMoneda));
                ValorCelda(tabla.Cell(), "");
            }
        });
    }

    private static void ComponerContenido(IContainer container, RemisionDetalleDto remision)
    {
        container.Column(col =>
        {
            col.Spacing(15);

            col.Item().Element(c => ComponerTablaItems(c, remision));
            col.Item().Element(c => ComponerBloqueFinal(c, remision));
            col.Item().PaddingTop(25).Element(ComponerFirmas);
        });
    }

    private static void ComponerTablaItems(IContainer container, RemisionDetalleDto remision)
    {
        // La columna de descuento solo aparece si alguna línea lo tiene (los descuentos son por ítem).
        var hayDescuentos = remision.Items.Any(i => i.Descuento > 0);

        container.Table(tabla =>
        {
            tabla.ColumnsDefinition(columnas =>
            {
                columnas.ConstantColumn(24);
                columnas.RelativeColumn(1.4f);
                columnas.RelativeColumn(4f);
                columnas.RelativeColumn(1.1f);
                columnas.RelativeColumn(1.8f);
                columnas.RelativeColumn(1f);
                if (hayDescuentos)
                    columnas.RelativeColumn(1.1f);
                columnas.RelativeColumn(1.9f);
            });

            tabla.Header(header =>
            {
                EncabezadoItemCelda(header.Cell(), "#");
                EncabezadoItemCelda(header.Cell(), "Código");
                EncabezadoItemCelda(header.Cell(), "Descripción");
                EncabezadoItemCelda(header.Cell(), "Cant.");
                EncabezadoItemCelda(header.Cell(), "Valor unitario");
                EncabezadoItemCelda(header.Cell(), "IVA");
                if (hayDescuentos)
                    EncabezadoItemCelda(header.Cell(), "Desc.");
                EncabezadoItemCelda(header.Cell(), "Valor total");
            });

            for (var i = 0; i < remision.Items.Count; i++)
            {
                var item = remision.Items[i];
                var fondo = i % 2 == 0 ? VerdeClaro : "#FFFFFF";

                FilaItemCelda(tabla.Cell(), fondo, (i + 1).ToString(CulturaMoneda), false);
                FilaItemCelda(tabla.Cell(), fondo, item.ProductoCodigo, false);
                FilaItemCelda(tabla.Cell(), fondo, item.ProductoNombre, false);
                FilaItemCelda(tabla.Cell(), fondo, item.Cantidad.ToString(CulturaMoneda), true);
                FilaItemCelda(tabla.Cell(), fondo, FormatearMoneda(item.PrecioUnitario), true);
                FilaItemCelda(tabla.Cell(), fondo, item.Iva > 0 ? $"{item.Iva}%" : "0%", true);
                if (hayDescuentos)
                    FilaItemCelda(tabla.Cell(), fondo, item.DescuentoPorcentaje > 0 ? $"{item.DescuentoPorcentaje:0.##}%" : "—", true);
                FilaItemCelda(tabla.Cell(), fondo, FormatearMoneda(item.SubtotalNeto), true);
            }
        });
    }

    private static void ComponerBloqueFinal(IContainer container, RemisionDetalleDto remision)
    {
        container.Row(row =>
        {
            row.RelativeItem(3).Column(col =>
            {
                col.Item().Background(VerdeMarca).Padding(5).Text("Observaciones").FontColor(Colors.White).Bold();
                col.Item().PaddingTop(5).Column(obs =>
                {
                    obs.Item().Text(PoliticaTitulo).FontSize(7.5f).Bold();
                    obs.Item().PaddingTop(2).Text(PoliticaTexto).FontSize(7.5f).Justify();

                    if (!string.IsNullOrWhiteSpace(remision.Nota))
                        obs.Item().PaddingTop(3).Text(remision.TipoEntrega == TipoEntrega.Envio
                            ? $"* Entregar en: {remision.Nota}"
                            : $"* Nota: {remision.Nota}").FontSize(7.5f).Bold();

                    if (remision.EstadoPago == EstadoPago.Anulada && !string.IsNullOrWhiteSpace(remision.MotivoAnulacion))
                        obs.Item().PaddingTop(3).Text($"* Anulada: {remision.MotivoAnulacion}").FontSize(7.5f).Bold().FontColor(RojoAnulada);
                });
            });

            row.ConstantItem(15);

            row.RelativeItem(2).Column(col =>
            {
                FilaTotal(col, "Subtotal ítems", remision.SubtotalItems);
                if (remision.Descuento > 0)
                    FilaTotal(col, "Descuentos", -remision.Descuento);
                if (remision.Descuento > 0)
                    FilaTotal(col, "Valor antes de IVA", remision.SubtotalConDescuento);

                foreach (var tramo in remision.IvaDesglose)
                    FilaTotal(col, $"IVA ({tramo.Tarifa}%)", tramo.Valor);

                // Retenciones (las practica el comprador): no cambian el IVA, solo el total a pagar.
                var hayRetenciones = remision.ReteFuente > 0 || remision.ReteIca > 0 || remision.ReteIva > 0;
                if (hayRetenciones)
                {
                    FilaTotal(col, "Total antes de retenciones", remision.TotalBruto);
                    if (remision.ReteFuente > 0)
                        FilaTotal(col, $"ReteFuente ({remision.ReteFuentePorcentaje:0.##}%)", -remision.ReteFuente);
                    if (remision.ReteIca > 0)
                        FilaTotal(col, $"ReteICA ({remision.ReteIcaPorMil:0.###}‰)", -remision.ReteIca);
                    if (remision.ReteIva > 0)
                        FilaTotal(col, $"ReteIVA ({remision.ReteIvaPorcentaje:0.##}% del IVA)", -remision.ReteIva);
                }

                col.Item().PaddingTop(4).Background(VerdeMarca).Padding(8).Row(total =>
                {
                    total.RelativeItem().Text(hayRetenciones ? "TOTAL A PAGAR" : "TOTAL").FontColor(Colors.White).Bold().FontSize(12);
                    total.RelativeItem().AlignRight().Text(FormatearMoneda(remision.TotalGeneral)).FontColor(Colors.White).Bold().FontSize(12);
                });
            });
        });
    }

    private static void ComponerFirmas(IContainer container)
    {
        container.Row(row =>
        {
            row.RelativeItem().PaddingRight(30).Column(c =>
            {
                c.Item().BorderBottom(0.75f).BorderColor(Colors.Grey.Medium).Height(30);
                c.Item().PaddingTop(3).AlignCenter().Text("Entregado por").FontSize(8);
            });
            row.RelativeItem().PaddingLeft(30).Column(c =>
            {
                c.Item().BorderBottom(0.75f).BorderColor(Colors.Grey.Medium).Height(30);
                c.Item().PaddingTop(3).AlignCenter().Text("Recibí conforme (nombre y documento)").FontSize(8);
            });
        });
    }

    private void ComponerPiePagina(IContainer container)
    {
        var partes = new List<string>();
        if (!string.IsNullOrWhiteSpace(empresa.Telefono)) partes.Add(empresa.Telefono!);
        if (!string.IsNullOrWhiteSpace(empresa.Direccion)) partes.Add(empresa.Direccion!);
        if (!string.IsNullOrWhiteSpace(empresa.Email)) partes.Add(empresa.Email!);
        if (!string.IsNullOrWhiteSpace(empresa.SitioWeb)) partes.Add(empresa.SitioWeb!);

        if (partes.Count == 0)
            return;

        container.Background(VerdeMarca).Padding(6).AlignCenter()
            .Text(string.Join("   -   ", partes)).FontColor(Colors.White).FontSize(8);
    }

    private static void FilaTotal(ColumnDescriptor col, string etiqueta, decimal valor)
    {
        col.Item().Row(fila =>
        {
            fila.RelativeItem().Text(etiqueta).FontSize(9);
            fila.RelativeItem().AlignRight().Text(FormatearMoneda(valor)).FontSize(9).Bold();
        });
    }

    private static void EtiquetaCelda(IContainer container, string texto)
        => container.Background(VerdeMarca).Padding(4).Text(texto).FontColor(Colors.White).Bold().FontSize(8);

    private static void ValorCelda(IContainer container, string? texto)
        => container.BorderBottom(0.5f).BorderColor(Colors.Grey.Lighten2).Padding(4).Text(string.IsNullOrWhiteSpace(texto) ? "-" : texto).FontSize(8.5f);

    private static void EncabezadoItemCelda(IContainer container, string texto)
        => container.Background(VerdeMarca).Padding(6).Text(texto).FontColor(Colors.White).Bold().FontSize(9);

    private static void FilaItemCelda(IContainer container, string fondo, string texto, bool alinearDerecha)
    {
        var celda = container.Background(fondo).Padding(6);
        var celdaAlineada = alinearDerecha ? celda.AlignRight() : celda;
        celdaAlineada.Text(texto).FontSize(8.5f);
    }

    private static string FormatearMoneda(decimal valor)
    {
        var signo = valor < 0 ? "-" : "";
        return $"{signo}$ {Math.Abs(valor).ToString("N0", CulturaMoneda)}";
    }

    private static byte[] CargarLogo()
    {
        var assembly = typeof(RemisionPdfBuilder).Assembly;
        var nombreRecurso = assembly.GetManifestResourceNames().First(n => n.EndsWith("logo-jimaco.png", StringComparison.Ordinal));
        using var stream = assembly.GetManifestResourceStream(nombreRecurso)!;
        using var memoria = new MemoryStream();
        stream.CopyTo(memoria);
        return memoria.ToArray();
    }
}
