using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Interfaces;
using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;

namespace Jimaco.Remisiones.TestUnitarios;

// El inventario es propio de la app: Producto.Stock cambia solo con movimientos del kardex.
public class StockTests
{
    private static AppDbContext NuevaDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static RemisionService NuevoServicioRemision(AppDbContext db)
    {
        var pdf = new Mock<IRemisionPdfBuilder>();
        pdf.Setup(p => p.Generar(It.IsAny<RemisionDetalleDto>())).Returns([1]);
        return new RemisionService(db, pdf.Object, new Mock<IEmailSender>().Object, TimeProvider.System,
            NullLogger<RemisionService>.Instance);
    }

    // Producto con `stock` unidades y una remisión que pide `cantidad`.
    private static async Task<(AppDbContext Db, RemisionService Servicio, int RemisionId, int ProductoId)> Escenario(
        int stock, int cantidad, EstadoPago estado = EstadoPago.Pendiente)
    {
        var db = NuevaDb();
        var producto = new Producto { Codigo = "PJ-1", Nombre = "Herramienta", PrecioVenta = 10000, Stock = stock, FechaCreacion = DateTime.UtcNow };
        db.Productos.Add(producto);
        await db.SaveChangesAsync();

        var remision = new Remision
        {
            Consecutivo = 7, CompradorId = 1, CompradorTipoDocumentoSnapshot = "CC", CompradorDocumentoSnapshot = "1",
            CompradorNombreSnapshot = "X", CompradorEmailSnapshot = "x@x.com", FechaCreacion = DateTime.UtcNow, EstadoPago = estado,
            Items = [new RemisionItem { ProductoId = producto.Id, ProductoCodigoSnapshot = "PJ-1", ProductoNombreSnapshot = "Herramienta", PrecioUnitario = 10000, Iva = 19, Cantidad = cantidad }]
        };
        db.Remisiones.Add(remision);
        await db.SaveChangesAsync();
        return (db, NuevoServicioRemision(db), remision.Id, producto.Id);
    }

    [Fact]
    public async Task Pagar_descuenta_el_stock_y_deja_la_linea_del_kardex()
    {
        var (db, servicio, remisionId, productoId) = await Escenario(stock: 10, cantidad: 4);

        await servicio.MarcarPagadaAsync(remisionId, new MarcarPagadaDto("R-1"), "cajero");

        Assert.Equal(6, (await db.Productos.FindAsync(productoId))!.Stock);
        var mov = await db.MovimientosInventario.SingleAsync();
        Assert.Equal(TipoMovimiento.Venta, mov.Tipo);
        Assert.Equal(-4, mov.Cantidad);
        Assert.Equal(6, mov.SaldoResultante);
        Assert.Equal(remisionId, mov.RemisionId);
        Assert.Equal("cajero", mov.Usuario);
        Assert.Contains("RMF-00007", mov.Motivo);
    }

    [Fact]
    public async Task Pagar_sin_stock_suficiente_falla_y_no_cambia_nada()
    {
        var (db, servicio, remisionId, productoId) = await Escenario(stock: 3, cantidad: 4);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.MarcarPagadaAsync(remisionId, new MarcarPagadaDto("R-1"), "cajero"));

        Assert.Contains("Stock insuficiente", ex.Message);
        Assert.Equal(3, (await db.Productos.FindAsync(productoId))!.Stock);
        Assert.Empty(db.MovimientosInventario);
        Assert.Equal(EstadoPago.Pendiente, (await db.Remisiones.FindAsync(remisionId))!.EstadoPago);
    }

    [Fact]
    public async Task Anular_una_remision_pagada_devuelve_el_stock()
    {
        var (db, servicio, remisionId, productoId) = await Escenario(stock: 10, cantidad: 4);
        await servicio.MarcarPagadaAsync(remisionId, new MarcarPagadaDto("R-1"), "cajero");

        await servicio.AnularAsync(remisionId, new AnularRemisionDto("Cobro duplicado"), esAdmin: true, usuario: "admin");

        Assert.Equal(10, (await db.Productos.FindAsync(productoId))!.Stock);
        var movimientos = await db.MovimientosInventario.OrderBy(m => m.Id).ToListAsync();
        Assert.Equal([TipoMovimiento.Venta, TipoMovimiento.Reintegro], movimientos.Select(m => m.Tipo));
        Assert.Equal(4, movimientos[1].Cantidad);
        Assert.Equal(10, movimientos[1].SaldoResultante);
    }

    [Fact]
    public async Task Anular_una_remision_pendiente_no_toca_el_stock()
    {
        var (db, servicio, remisionId, productoId) = await Escenario(stock: 10, cantidad: 4);

        await servicio.AnularAsync(remisionId, new AnularRemisionDto("Se arrepintió"), esAdmin: false);

        Assert.Equal(10, (await db.Productos.FindAsync(productoId))!.Stock);
        Assert.Empty(db.MovimientosInventario);
    }

    [Fact]
    public async Task No_se_arma_una_remision_con_mas_unidades_de_las_que_hay()
    {
        var db = NuevaDb();
        var producto = new Producto { Codigo = "PJ-1", Nombre = "Herramienta", PrecioVenta = 10000, Stock = 5, FechaCreacion = DateTime.UtcNow };
        var comprador = new Comprador
        {
            TipoDocumento = "CC", NumeroDocumento = "1", PrimerNombre = "A", NombreCompleto = "A", Email = "a@a.com", FechaCreacion = DateTime.UtcNow
        };
        db.Productos.Add(producto);
        db.Compradores.Add(comprador);
        await db.SaveChangesAsync();
        var servicio = NuevoServicioRemision(db);

        // ActualizarAsync valida las líneas igual que CrearAsync (CrearAsync necesita la SEQUENCE de SQL Server).
        var remision = new Remision
        {
            Consecutivo = 1, CompradorId = comprador.Id, CompradorTipoDocumentoSnapshot = "CC", CompradorDocumentoSnapshot = "1",
            CompradorNombreSnapshot = "A", CompradorEmailSnapshot = "a@a.com", FechaCreacion = DateTime.UtcNow
        };
        db.Remisiones.Add(remision);
        await db.SaveChangesAsync();

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.ActualizarAsync(remision.Id, new RemisionGuardarDto(comprador.Id, TipoEntrega.EnSitio, 0, 0, null, [new RemisionItemEntradaDto(producto.Id, 6)])));
        Assert.Contains("hay 5 y pides 6", ex.Message);

        var ok = await servicio.ActualizarAsync(remision.Id, new RemisionGuardarDto(comprador.Id, TipoEntrega.EnSitio, 0, 0, null, [new RemisionItemEntradaDto(producto.Id, 5)]));
        Assert.NotNull(ok);
    }

    // ---- Productos e inventario manual ----

    [Fact]
    public async Task Crear_un_producto_con_stock_inicial_deja_la_carga_inicial_en_el_kardex()
    {
        var db = NuevaDb();
        var servicio = new ProductoService(db, TimeProvider.System);

        var creado = await servicio.CrearAsync(new ProductoCrearDto("pj-26550 ", "MC4 KEY KIT MC4-K01", "Unidad", 12000, null, 19, 250), "admin");

        Assert.Equal("PJ-26550", creado.Codigo);
        Assert.Equal(250, creado.Stock);
        var mov = await db.MovimientosInventario.SingleAsync();
        Assert.Equal(TipoMovimiento.CargaInicial, mov.Tipo);
        Assert.Equal(250, mov.Cantidad);
        Assert.Equal(250, mov.SaldoResultante);
    }

    [Fact]
    public async Task El_stock_inicial_no_puede_ser_negativo()
    {
        var servicio = new ProductoService(NuevaDb(), TimeProvider.System);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servicio.CrearAsync(new ProductoCrearDto("X", "X", null, 1, null, 19, -1)));
    }

    [Fact]
    public async Task Actualizar_un_producto_nunca_cambia_su_stock()
    {
        var db = NuevaDb();
        var productos = new ProductoService(db, TimeProvider.System);
        var creado = await productos.CrearAsync(new ProductoCrearDto("PJ-1", "A", null, 100, null, 19, 8));

        var actualizado = await productos.ActualizarAsync(creado.Id, new ProductoActualizarDto("PJ-1", "A nuevo", null, 150, null, 19, true));

        Assert.Equal(8, actualizado!.Stock);
        Assert.Equal(150, actualizado.PrecioVenta);
    }

    [Fact]
    public async Task Entradas_y_ajustes_mueven_el_stock_con_sus_reglas()
    {
        var db = NuevaDb();
        var productos = new ProductoService(db, TimeProvider.System);
        var inventario = new InventarioService(db, TimeProvider.System);
        var p = await productos.CrearAsync(new ProductoCrearDto("PJ-1", "A", null, 100, null, 19, 10));

        var conEntrada = await inventario.RegistrarMovimientoAsync(p.Id, new MovimientoManualDto(TipoMovimiento.Entrada, 5, "Llegó pedido"), "admin");
        Assert.Equal(15, conEntrada!.Stock);

        var conAjuste = await inventario.RegistrarMovimientoAsync(p.Id, new MovimientoManualDto(TipoMovimiento.Ajuste, -3, "Conteo físico"), "admin");
        Assert.Equal(12, conAjuste!.Stock);

        // Reglas
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventario.RegistrarMovimientoAsync(p.Id, new MovimientoManualDto(TipoMovimiento.Entrada, 0, null), "admin"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventario.RegistrarMovimientoAsync(p.Id, new MovimientoManualDto(TipoMovimiento.Entrada, -2, null), "admin"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventario.RegistrarMovimientoAsync(p.Id, new MovimientoManualDto(TipoMovimiento.Ajuste, -1, "  "), "admin"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventario.RegistrarMovimientoAsync(p.Id, new MovimientoManualDto(TipoMovimiento.Ajuste, -13, "Merma"), "admin"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => inventario.RegistrarMovimientoAsync(p.Id, new MovimientoManualDto(TipoMovimiento.Venta, 1, "x"), "admin"));
        Assert.Equal(12, (await db.Productos.FindAsync(p.Id))!.Stock);
        Assert.Null(await inventario.RegistrarMovimientoAsync(999, new MovimientoManualDto(TipoMovimiento.Entrada, 1, null), "admin"));
    }

    [Fact]
    public async Task El_kardex_se_lista_del_mas_reciente_al_mas_antiguo_con_el_consecutivo_de_la_remision()
    {
        var (db, servicio, remisionId, productoId) = await Escenario(stock: 10, cantidad: 2);
        await servicio.MarcarPagadaAsync(remisionId, new MarcarPagadaDto("R-1"), "cajero");
        var inventario = new InventarioService(db, TimeProvider.System);
        await inventario.RegistrarMovimientoAsync(productoId, new MovimientoManualDto(TipoMovimiento.Entrada, 3, "Llegó pedido"), "admin");

        var pagina = (await inventario.ListarMovimientosAsync(productoId, 1, 10))!;

        Assert.Equal(2, pagina.Total);
        Assert.Equal(TipoMovimiento.Entrada, pagina.Items[0].Tipo);
        Assert.Equal(TipoMovimiento.Venta, pagina.Items[1].Tipo);
        Assert.Equal("RMF-00007", pagina.Items[1].RemisionConsecutivo);
        Assert.Null(await inventario.ListarMovimientosAsync(999, 1, 10));
    }

    [Fact]
    public async Task No_se_elimina_un_producto_con_remisiones_pero_si_uno_solo_con_carga_inicial()
    {
        var (db, _, _, productoConRemision) = await Escenario(stock: 10, cantidad: 2);
        var productos = new ProductoService(db, TimeProvider.System);
        var suelto = await productos.CrearAsync(new ProductoCrearDto("PJ-2", "B", null, 100, null, 19, 5));

        await Assert.ThrowsAsync<InvalidOperationException>(() => productos.EliminarAsync(productoConRemision));

        Assert.True(await productos.EliminarAsync(suelto.Id));
        Assert.Empty(db.MovimientosInventario.Where(m => m.ProductoId == suelto.Id));
    }
}
