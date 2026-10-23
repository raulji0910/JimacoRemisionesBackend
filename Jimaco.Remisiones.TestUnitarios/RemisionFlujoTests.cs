using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jimaco.Remisiones.TestUnitarios;

// CrearAsync no se prueba aquí: el consecutivo sale de una SQL SEQUENCE que InMemory no soporta
// (se valida contra SQL Server real). Estas pruebas siembran la remisión directo y cubren el
// resto del ciclo: pagar, entregar, anular, correo.
public class RemisionFlujoTests
{
    private static (RemisionService Servicio, AppDbContext Db, Mock<IEmailSender> Email) Crear(bool correoFalla = false)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var email = new Mock<IEmailSender>();
        if (correoFalla)
            email.Setup(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AdjuntoCorreo>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SMTP sin configurar"));

        var pdf = new Mock<IRemisionPdfBuilder>();
        pdf.Setup(p => p.Generar(It.IsAny<RemisionDetalleDto>())).Returns([1, 2, 3]);

        var servicio = new RemisionService(db, pdf.Object, email.Object, TimeProvider.System, NullLogger<RemisionService>.Instance);
        return (servicio, db, email);
    }

    // El producto sembrado tiene 10 unidades; la remisión pide 2.
    private static async Task<int> SembrarRemisionAsync(AppDbContext db, TipoEntrega entrega, EstadoPago estado = EstadoPago.Pendiente, int stock = 10)
    {
        db.Productos.Add(new Producto { Id = 1, Codigo = "A", Nombre = "Prod", PrecioVenta = 1000, Stock = stock, FechaCreacion = DateTime.UtcNow });
        var remision = new Remision
        {
            Consecutivo = 1,
            CompradorId = 1,
            CompradorTipoDocumentoSnapshot = "CC",
            CompradorDocumentoSnapshot = "123",
            CompradorNombreSnapshot = "Comprador Prueba",
            CompradorEmailSnapshot = "comprador@example.com",
            TipoEntrega = entrega,
            EstadoPago = estado,
            FechaCreacion = DateTime.UtcNow,
            Items = [new RemisionItem { ProductoId = 1, ProductoCodigoSnapshot = "A", ProductoNombreSnapshot = "Prod", PrecioUnitario = 1000, Iva = 19, Cantidad = 2 }]
        };
        db.Remisiones.Add(remision);
        await db.SaveChangesAsync();
        return remision.Id;
    }

    [Fact]
    public async Task Pagar_con_entrega_en_sitio_deja_entregada_y_envia_correo()
    {
        var (servicio, db, email) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio);

        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto(MedioPago.Datafono, "VOUCHER-1"), "cajero");

        Assert.NotNull(res);
        Assert.Equal(EstadoPago.Pagada, res.Remision.EstadoPago);
        Assert.Equal(EstadoEntrega.Entregada, res.Remision.EstadoEntrega);
        Assert.Equal("VOUCHER-1", res.Remision.ReciboRedeban);
        Assert.True(res.CorreoEnviado);
        Assert.NotNull(res.Remision.CorreoEnviadoEn);
        email.Verify(e => e.EnviarAsync("comprador@example.com", It.IsAny<string>(), It.IsAny<string>(), It.Is<IReadOnlyList<AdjuntoCorreo>?>(a => a!.Count == 1), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Pagar_con_envio_deja_la_entrega_pendiente()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.Envio);

        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto(MedioPago.Datafono, "R-1"), "cajero");

        Assert.Equal(EstadoEntrega.Pendiente, res!.Remision.EstadoEntrega);

        var entregada = await servicio.MarcarEntregadaAsync(id);
        Assert.Equal(EstadoEntrega.Entregada, entregada!.EstadoEntrega);
    }

    [Fact]
    public async Task Si_el_correo_falla_el_pago_igual_queda_guardado()
    {
        var (servicio, db, _) = Crear(correoFalla: true);
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio);

        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto(MedioPago.Datafono, "R-1"), "cajero");

        Assert.Equal(EstadoPago.Pagada, res!.Remision.EstadoPago);
        Assert.False(res.CorreoEnviado);
        Assert.Equal("SMTP sin configurar", res.ErrorCorreo);
        Assert.Null(res.Remision.CorreoEnviadoEn);
    }

    [Fact]
    public async Task No_se_paga_dos_veces()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pagada);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarPagadaAsync(id, new MarcarPagadaDto(MedioPago.Datafono, "R-1"), "x"));
    }

    [Fact]
    public async Task Anular_pagada_exige_admin_y_motivo()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pagada);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AnularAsync(id, new AnularRemisionDto("error"), esAdmin: false));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.AnularAsync(id, new AnularRemisionDto(" "), esAdmin: true));

        var anulada = await servicio.AnularAsync(id, new AnularRemisionDto("Cobro duplicado"), esAdmin: true);
        Assert.Equal(EstadoPago.Anulada, anulada!.EstadoPago);
        Assert.Equal("Cobro duplicado", anulada.MotivoAnulacion);
    }

    [Fact]
    public async Task Anular_pendiente_no_exige_admin()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio);

        var anulada = await servicio.AnularAsync(id, new AnularRemisionDto("Cliente se arrepintió"), esAdmin: false);
        Assert.Equal(EstadoPago.Anulada, anulada!.EstadoPago);
    }

    [Fact]
    public async Task Solo_se_edita_una_remision_pendiente()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pagada);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, 0, 0, 0, null, [new RemisionItemEntradaDto(1, 1)])));
    }

    private static async Task SembrarCompradorAsync(AppDbContext db)
    {
        db.Compradores.Add(new Comprador
        {
            Id = 1, TipoDocumento = "CC", NumeroDocumento = "1", PrimerNombre = "A", NombreCompleto = "A", Email = "a@a.com", FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
    }

    [Theory]
    [InlineData(-1, 0, 0, 0)]       // descuento de ítem negativo
    [InlineData(50.01, 0, 0, 0)]    // descuento de ítem sobre el tope
    [InlineData(0, -1, 0, 0)]       // ReteFuente negativa
    [InlineData(0, 36, 0, 0)]       // ReteFuente sobre el tope
    [InlineData(0, 0, -1, 0)]       // ReteICA negativa
    [InlineData(0, 0, 21, 0)]       // ReteICA sobre el tope
    [InlineData(0, 0, 0, -1)]       // ReteIVA negativa
    [InlineData(0, 0, 0, 101)]      // ReteIVA sobre el tope
    public async Task Rechaza_descuento_o_retenciones_fuera_de_rango(double descuentoItem, double reteFuente, double reteIca, double reteIva)
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, (decimal)reteFuente, (decimal)reteIca, (decimal)reteIva, null, [new RemisionItemEntradaDto(1, 1, (decimal)descuentoItem)])));
        // Debe fallar por la regla de descuento/retención y no por otra cosa (comprador, stock...).
        Assert.Matches("descuento|rete", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Guarda_el_descuento_por_item_y_las_tres_retenciones_con_sus_valores()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        // 2 x 1.000 al 19% con 10% de descuento: -200 -> base 1.800, IVA 342, bruto 2.142.
        var r = await servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, 2.5m, 9.66m, 15m, null, [new RemisionItemEntradaDto(1, 2, 10m)]));

        var item = r!.Items.Single();
        Assert.Equal((10m, 200m, 1800m), (item.DescuentoPorcentaje, item.Descuento, item.SubtotalNeto));
        Assert.Equal(200m, r.Descuento);
        Assert.Equal((2.5m, 9.66m, 15m), (r.ReteFuentePorcentaje, r.ReteIcaPorMil, r.ReteIvaPorcentaje));
        Assert.Equal(342m, r.IvaDesglose.Single().Valor);
        Assert.Equal(2142m, r.TotalBruto);
        Assert.Equal(45m, r.ReteFuente);       // 2,5% de 1.800
        Assert.Equal(17.39m, r.ReteIca);       // 9,66 por mil de 1.800
        Assert.Equal(51.30m, r.ReteIva);       // 15% de 342
        Assert.Equal(2142m - 45m - 17.39m - 51.30m, r.TotalGeneral);
    }

    [Fact]
    public async Task Un_producto_repetido_con_descuentos_distintos_se_rechaza()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, 0, 0, 0, null, [new RemisionItemEntradaDto(1, 1, 5m), new RemisionItemEntradaDto(1, 1, 10m)])));
        Assert.Contains("descuentos distintos", ex.Message);
    }

    [Fact]
    public async Task Pagar_en_efectivo_no_pide_recibo_y_queda_registrado()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio);

        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto(MedioPago.Efectivo, "ignorado"), "cajero");

        Assert.Equal(MedioPago.Efectivo, res!.Remision.MedioPago);
        Assert.Null(res.Remision.ReciboRedeban);
        Assert.Equal(EstadoPago.Pagada, res.Remision.EstadoPago);
    }

    [Theory]
    [InlineData(MedioPago.Datafono)]
    [InlineData(MedioPago.Qr)]
    public async Task Datafono_y_qr_exigen_numero_de_recibo(MedioPago medio)
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarPagadaAsync(id, new MarcarPagadaDto(medio, "  "), "cajero"));
        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto(medio, "A-123"), "cajero");

        Assert.Equal(medio, res!.Remision.MedioPago);
        Assert.Equal("A-123", res.Remision.ReciboRedeban);
    }

    [Fact]
    public async Task Cierre_de_caja_suma_por_medio_y_resta_las_devoluciones()
    {
        var (_, db, _) = Crear();
        db.Productos.Add(new Producto { Id = 1, Codigo = "A", Nombre = "Prod", PrecioVenta = 1000, Stock = 10, FechaCreacion = DateTime.UtcNow });
        // Hoy (hora de Colombia) a las 10:00 = 15:00 UTC; ayer a las 10:00 queda fuera del día consultado.
        var dia = new DateOnly(2026, 10, 20);
        var hoy = new DateTime(2026, 10, 20, 15, 0, 0, DateTimeKind.Utc);
        Remision Nueva(int n, MedioPago medio, DateTime pago, EstadoPago estado = EstadoPago.Pagada, DateTime? anulada = null) => new()
        {
            Consecutivo = n, CompradorId = 1, CompradorTipoDocumentoSnapshot = "CC", CompradorDocumentoSnapshot = "1",
            CompradorNombreSnapshot = "C", CompradorEmailSnapshot = "c@c.co", EstadoPago = estado, MedioPago = medio,
            FechaPago = pago, FechaAnulacion = anulada, FechaCreacion = pago, ReciboRedeban = medio == MedioPago.Efectivo ? null : "R",
            Items = [new RemisionItem { ProductoId = 1, ProductoCodigoSnapshot = "A", ProductoNombreSnapshot = "P", PrecioUnitario = 1000, Iva = 0, Cantidad = n }]
        };
        db.Remisiones.AddRange(
            Nueva(1, MedioPago.Datafono, hoy),                                        // 1.000
            Nueva(2, MedioPago.Efectivo, hoy),                                        // 2.000
            Nueva(3, MedioPago.Efectivo, hoy, EstadoPago.Anulada, hoy.AddHours(1)),   // 3.000 cobrado y devuelto el mismo día
            Nueva(4, MedioPago.Qr, hoy.AddDays(-1)),                                  // ayer: no entra
            Nueva(5, MedioPago.Qr, hoy.AddDays(-1), EstadoPago.Anulada, hoy));        // cobrada ayer, devuelta hoy: 5.000 de devolución
        await db.SaveChangesAsync();

        var cierre = await new CierreCajaService(db).ObtenerAsync(dia);

        Assert.Equal(6000m, cierre.TotalCobros);          // 1.000 + 2.000 + 3.000
        Assert.Equal(8000m, cierre.TotalDevoluciones);    // 3.000 + 5.000
        Assert.Equal(-2000m, cierre.Neto);
        Assert.Equal(1000m, cierre.PorMedio.Single(m => m.Medio == MedioPago.Datafono).Neto);
        Assert.Equal(2000m + 3000m - 3000m, cierre.PorMedio.Single(m => m.Medio == MedioPago.Efectivo).Neto);
        Assert.Equal(-5000m, cierre.PorMedio.Single(m => m.Medio == MedioPago.Qr).Neto);
        Assert.Equal(3, cierre.Cobros.Count);
        Assert.Equal(2, cierre.Devoluciones.Count);
    }
}
