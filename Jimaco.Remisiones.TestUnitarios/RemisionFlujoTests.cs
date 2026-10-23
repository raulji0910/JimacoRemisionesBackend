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
// IConfiguration mínima para las pruebas (solo se lee por clave).
internal sealed class ConfiguracionFalsa(Dictionary<string, string?> valores) : Microsoft.Extensions.Configuration.IConfiguration
{
    public string? this[string key] { get => valores.GetValueOrDefault(key); set => valores[key] = value; }
    public IEnumerable<Microsoft.Extensions.Configuration.IConfigurationSection> GetChildren() => [];
    public Microsoft.Extensions.Primitives.IChangeToken GetReloadToken() => throw new NotSupportedException();
    public Microsoft.Extensions.Configuration.IConfigurationSection GetSection(string key) => throw new NotSupportedException();
}

public class RemisionFlujoTests
{
    private static (RemisionService Servicio, AppDbContext Db, Mock<IEmailSender> Email) Crear(bool correoFalla = false, Dictionary<string, string?>? config = null)
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

        var email = new Mock<IEmailSender>();
        if (correoFalla)
            email.Setup(e => e.EnviarAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AdjuntoCorreo>?>(), It.IsAny<CancellationToken>()))
                .ThrowsAsync(new InvalidOperationException("SMTP sin configurar"));

        var pdf = new Mock<IRemisionPdfBuilder>();
        pdf.Setup(p => p.Generar(It.IsAny<RemisionDetalleDto>())).Returns([1, 2, 3]);

        var servicio = new RemisionService(db, pdf.Object, email.Object, TimeProvider.System, NullLogger<RemisionService>.Instance,
            config is null ? null : new ConfiguracionFalsa(config));
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
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, false, false, false, null, [new RemisionItemEntradaDto(1, 1)])));
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
    [InlineData(-1)]       // descuento de ítem negativo
    [InlineData(50.01)]    // descuento de ítem sobre el tope
    public async Task Rechaza_descuento_de_item_fuera_de_rango(double descuentoItem)
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, false, false, false, null, [new RemisionItemEntradaDto(1, 1, (decimal)descuentoItem)])));
        Assert.Contains("descuento", ex.Message.ToLowerInvariant());
    }

    [Fact]
    public async Task Guarda_el_descuento_por_item_sin_retenciones()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        // 2 x 1.000 al 19% con 10% de descuento: -200 -> base 1.800, IVA 342, total 2.142.
        var r = await servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, false, false, false, null, [new RemisionItemEntradaDto(1, 2, 10m)]));

        var item = r!.Items.Single();
        Assert.Equal((10m, 200m, 1800m), (item.DescuentoPorcentaje, item.Descuento, item.SubtotalNeto));
        Assert.Equal(200m, r.Descuento);
        Assert.Equal(342m, r.IvaDesglose.Single().Valor);
        Assert.Equal(2142m, r.TotalGeneral);
        Assert.Equal(0m, r.ReteFuente + r.ReteIca + r.ReteIva);
    }

    [Fact]
    public async Task Las_retenciones_usan_tarifas_fijas_y_se_restan_al_total()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente, stock: 1000);
        await SembrarCompradorAsync(db);

        // 600 x 1.000 al 19%: base 600.000 (supera el mínimo de 523.740), IVA 114.000, bruto 714.000.
        var r = await servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, true, true, true, null, [new RemisionItemEntradaDto(1, 600)]));

        Assert.Equal((2.5m, 6.9m, 15m), (r!.ReteFuentePorcentaje, r.ReteIcaPorMil, r.ReteIvaPorcentaje));
        Assert.Equal(714000m, r.TotalBruto);
        Assert.Equal(15000m, r.ReteFuente);      // 2,5% de 600.000
        Assert.Equal(4140m, r.ReteIca);          // 6,9 por mil de 600.000
        Assert.Equal(17100m, r.ReteIva);         // 15% del IVA (114.000)
        Assert.Equal(714000m - 15000m - 4140m - 17100m, r.TotalGeneral);
    }

    [Theory]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    public async Task Las_retenciones_se_rechazan_bajo_el_minimo_de_10_uvt(bool fuente, bool ica, bool iva)
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente, stock: 1000);
        await SembrarCompradorAsync(db);

        // 523 x 1.000 = 523.000 < 523.740 (10 UVT de 2026): no procede ninguna retención.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, fuente, ica, iva, null, [new RemisionItemEntradaDto(1, 523)])));
        Assert.Contains("523.740", ex.Message);
    }

    [Fact]
    public async Task El_minimo_de_las_retenciones_se_mide_despues_de_los_descuentos()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente, stock: 1000);
        await SembrarCompradorAsync(db);

        // 600.000 con 15% de descuento = 510.000 < 523.740: aunque el bruto lo supera, ya no procede.
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, true, false, false, null, [new RemisionItemEntradaDto(1, 600, 15m)])));
    }

    [Fact]
    public void Las_reglas_de_retenciones_salen_de_la_configuracion_con_valores_por_defecto()
    {
        var porDefecto = ReglasRetenciones.Cargar(null);
        Assert.Equal((2.5m, 6.9m, 15m, 523740m), (porDefecto.ReteFuentePorcentaje, porDefecto.ReteIcaPorMil, porDefecto.ReteIvaPorcentaje, porDefecto.BaseMinima));

        var otra = ReglasRetenciones.Cargar(new ConfiguracionFalsa(new() { ["Retenciones:Uvt"] = "60000", ["Retenciones:ReteIcaPorMil"] = "9.66" }));
        Assert.Equal(600000m, otra.BaseMinima);
        Assert.Equal(9.66m, otra.ReteIcaPorMil);
    }

    [Fact]
    public async Task Un_envio_posterior_exige_la_direccion_en_las_notas()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);
        var item = new[] { new RemisionItemEntradaDto(1, 1) };

        foreach (var nota in new string?[] { null, "", "   ", "corta" })
        {
            var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.Envio, false, false, false, nota, item)));
            Assert.Contains("dirección y la ciudad", ex.Message);
        }

        // Con la dirección escrita pasa; y una venta en sitio sigue sin pedir nota.
        var envio = await servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.Envio, false, false, false, "Calle 10 # 5-20, Bogotá", item));
        Assert.Equal("Calle 10 # 5-20, Bogotá", envio!.Nota);
        var enSitio = await servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, false, false, false, null, item));
        Assert.Null(enSitio!.Nota);
    }

    [Fact]
    public async Task La_nota_admite_hasta_1000_caracteres()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);
        var item = new[] { new RemisionItemEntradaDto(1, 1) };

        var larga = await servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.Envio, false, false, false, new string('x', 1000), item));
        Assert.Equal(1000, larga!.Nota!.Length);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.Envio, false, false, false, new string('x', 1001), item)));
    }

    [Fact]
    public async Task Cambiar_a_envio_posterior_exige_la_direccion()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.CambiarTipoEntregaAsync(id, new CambiarTipoEntregaDto(TipoEntrega.Envio)));
        var r = await servicio.CambiarTipoEntregaAsync(id, new CambiarTipoEntregaDto(TipoEntrega.Envio, "Carrera 7 # 80-10, Medellín"));
        Assert.Equal(TipoEntrega.Envio, r!.TipoEntrega);
        Assert.Equal("Carrera 7 # 80-10, Medellín", r.Nota);
    }

    [Fact]
    public async Task Las_remisiones_de_envio_posterior_se_copian_a_compras_y_las_de_sitio_no()
    {
        var config = new Dictionary<string, string?> { ["Correo:Contabilidad"] = "conta@jimaco.test", ["Correo:Compras"] = "compras@jimaco.test" };

        var (servicio, db, email) = Crear(config: config);
        var envio = await SembrarRemisionAsync(db, TipoEntrega.Envio);
        (await db.Remisiones.FindAsync(envio))!.Nota = "Calle 1 # 2-3, Cali";
        await db.SaveChangesAsync();
        await servicio.MarcarPagadaAsync(envio, new MarcarPagadaDto(MedioPago.Efectivo, null), "cajero");

        email.Verify(e => e.EnviarAsync("compras@jimaco.test", It.Is<string>(a => a.Contains("[Compras]")), It.Is<string>(c => c.Contains("Calle 1 # 2-3, Cali")), It.IsAny<IReadOnlyList<AdjuntoCorreo>?>(), It.IsAny<CancellationToken>()), Times.Once);
        email.Verify(e => e.EnviarAsync("conta@jimaco.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AdjuntoCorreo>?>(), It.IsAny<CancellationToken>()), Times.Once);

        var (servicio2, db2, email2) = Crear(config: config);
        var sitio = await SembrarRemisionAsync(db2, TipoEntrega.EnSitio);
        await servicio2.MarcarPagadaAsync(sitio, new MarcarPagadaDto(MedioPago.Efectivo, null), "cajero");
        email2.Verify(e => e.EnviarAsync("compras@jimaco.test", It.IsAny<string>(), It.IsAny<string>(), It.IsAny<IReadOnlyList<AdjuntoCorreo>?>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Un_producto_repetido_con_descuentos_distintos_se_rechaza()
    {
        var (servicio, db, _) = Crear();
        var id = await SembrarRemisionAsync(db, TipoEntrega.EnSitio, EstadoPago.Pendiente);
        await SembrarCompradorAsync(db);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(id, new RemisionGuardarDto(1, TipoEntrega.EnSitio, false, false, false, null, [new RemisionItemEntradaDto(1, 1, 5m), new RemisionItemEntradaDto(1, 1, 10m)])));
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

        var cierre = await new CierreCajaService(db).ObtenerAsync(dia, dia);

        Assert.Equal(6000m, cierre.TotalCobros);          // 1.000 + 2.000 + 3.000
        Assert.Equal(8000m, cierre.TotalDevoluciones);    // 3.000 + 5.000
        Assert.Equal(-2000m, cierre.Neto);
        Assert.Equal(1000m, cierre.PorMedio.Single(m => m.Medio == MedioPago.Datafono).Neto);
        Assert.Equal(2000m + 3000m - 3000m, cierre.PorMedio.Single(m => m.Medio == MedioPago.Efectivo).Neto);
        Assert.Equal(-5000m, cierre.PorMedio.Single(m => m.Medio == MedioPago.Qr).Neto);
        Assert.Equal(3, cierre.Cobros.Count);
        Assert.Equal(2, cierre.Devoluciones.Count);
    }

    [Fact]
    public async Task Cierre_de_caja_por_rango_separa_por_dia_y_por_usuario()
    {
        var (_, db, _) = Crear();
        db.Productos.Add(new Producto { Id = 1, Codigo = "A", Nombre = "Prod", PrecioVenta = 1000, Stock = 10, FechaCreacion = DateTime.UtcNow });
        var lunes = new DateTime(2026, 10, 19, 15, 0, 0, DateTimeKind.Utc);   // 10:00 en Colombia
        Remision Nueva(int n, MedioPago medio, DateTime pago, string usuario) => new()
        {
            Consecutivo = n, CompradorId = 1, CompradorTipoDocumentoSnapshot = "CC", CompradorDocumentoSnapshot = "1",
            CompradorNombreSnapshot = "C", CompradorEmailSnapshot = "c@c.co", EstadoPago = EstadoPago.Pagada, MedioPago = medio,
            FechaPago = pago, FechaCreacion = pago, PagadaPor = usuario, CreadoPor = usuario, ReciboRedeban = medio == MedioPago.Efectivo ? null : "R",
            Items = [new RemisionItem { ProductoId = 1, ProductoCodigoSnapshot = "A", ProductoNombreSnapshot = "P", PrecioUnitario = 1000, Iva = 0, Cantidad = n }]
        };
        db.Remisiones.AddRange(
            Nueva(1, MedioPago.Efectivo, lunes, "Ana"),                               // lunes 19: 1.000
            Nueva(2, MedioPago.Datafono, lunes.AddDays(1), "Luis"),                   // martes 20: 2.000
            Nueva(3, MedioPago.Efectivo, lunes.AddDays(1), "Ana"),                    // martes 20: 3.000
            Nueva(4, MedioPago.Qr, lunes.AddDays(5), "Ana"));                         // domingo 25: fuera del rango
        await db.SaveChangesAsync();

        var cierre = await new CierreCajaService(db).ObtenerAsync(new DateOnly(2026, 10, 19), new DateOnly(2026, 10, 21));

        Assert.Equal(3, cierre.PorDia.Count);                                         // lunes, martes y miércoles (sin movimiento)
        Assert.Equal(1000m, cierre.PorDia[0].Neto);
        Assert.Equal(5000m, cierre.PorDia[1].Neto);
        Assert.Equal((2000m, 0m, 3000m), (cierre.PorDia[1].Datafono, cierre.PorDia[1].Qr, cierre.PorDia[1].Efectivo));
        Assert.Equal(0m, cierre.PorDia[2].Neto);
        Assert.Equal(6000m, cierre.Neto);

        var ana = cierre.PorUsuario.Single(u => u.Usuario == "Ana");
        Assert.Equal((2, 4000m, 4000m), (ana.CantidadCobros, ana.TotalCobros, ana.Efectivo));
        Assert.Equal(2000m, cierre.PorUsuario.Single(u => u.Usuario == "Luis").TotalCobros);
    }

    [Fact]
    public async Task Cierre_de_caja_valida_el_rango()
    {
        var (_, db, _) = Crear();
        var servicio = new CierreCajaService(db);
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ObtenerAsync(new DateOnly(2026, 10, 20), new DateOnly(2026, 10, 19)));
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.ObtenerAsync(new DateOnly(2025, 1, 1), new DateOnly(2026, 10, 19)));
    }
}
