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

        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto("VOUCHER-1"), "cajero");

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

        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto("R-1"), "cajero");

        Assert.Equal(EstadoEntrega.Pendiente, res!.Remision.EstadoEntrega);

        var entregada = await servicio.MarcarEntregadaAsync(id);
        Assert.Equal(EstadoEntrega.Entregada, entregada!.EstadoEntrega);
    }

    [Fact]
    public async Task Si_el_correo_falla_el_pago_igual_queda_guardado()
    {
        var (servicio, db, _) = Crear(correoFalla: true);
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio);

        var res = await servicio.MarcarPagadaAsync(id, new MarcarPagadaDto("R-1"), "cajero");

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

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarPagadaAsync(id, new MarcarPagadaDto("R-1"), "x"));
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
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, 0, 0, null, [new RemisionItemEntradaDto(1, 1)])));
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
    [InlineData(-1, 0)]
    [InlineData(50.01, 0)]
    [InlineData(0, -5)]
    [InlineData(0, 999999999)]
    public async Task Rechaza_descuento_o_retencion_fuera_de_rango(double porcentaje, double retencion)
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, (decimal)porcentaje, (decimal)retencion, null, [new RemisionItemEntradaDto(1, 1)])));
        // Debe fallar por la regla de descuento/retención y no por otra cosa (comprador, stock...).
        Assert.Matches("descuento|retenci", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Guarda_el_porcentaje_el_valor_del_descuento_y_la_retencion()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        var r = await servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, 10m, 100m, null, [new RemisionItemEntradaDto(1, 2)]));

        Assert.Equal(10m, r!.DescuentoPorcentaje);
        Assert.Equal(r.SubtotalItems * 0.10m, r.Descuento);
        Assert.Equal(100m, r.Retencion);
        Assert.Equal(r.SubtotalItems - r.Descuento - 100m, r.SubtotalConDescuento);
    }
}
