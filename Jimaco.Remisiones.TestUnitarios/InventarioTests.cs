using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Jimaco.Remisiones.TestUnitarios;

public class InventarioTests
{
    private static readonly DateTime T0 = new(2026, 10, 14, 15, 0, 0, DateTimeKind.Utc);

    private static AppDbContext NuevaDb()
        => new(new DbContextOptionsBuilder<AppDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);

    private static IOptions<WorldOfficeOptions> Opciones => Options.Create(new WorldOfficeOptions { BodegaVenta = "Uno" });

    // Producto sincronizado con 10 unidades en la bodega de venta y una remisión de 4 unidades.
    private static async Task<(ProductoService Servicio, AppDbContext Db, int ProductoId, Remision Remision)> Escenario(
        EstadoPago estadoPago = EstadoPago.Pagada,
        EstadoSincronizacionWo sincWo = EstadoSincronizacionWo.Pendiente,
        DateTime? sincronizadaEn = null,
        DateTime? fotoEn = null)
    {
        var db = NuevaDb();
        var producto = new Producto
        {
            Codigo = "PJ-1", Nombre = "Producto", PrecioVenta = 1000, IdWorldOffice = 1,
            ExistenciaActualizadaEn = T0, FechaCreacion = T0,
            Existencias = [new ProductoExistencia { Bodega = "Uno", Cantidad = 10 }, new ProductoExistencia { Bodega = "Dos", Cantidad = 99 }]
        };
        db.Productos.Add(producto);

        var remision = new Remision
        {
            Consecutivo = 1, CompradorId = 1, CompradorTipoDocumentoSnapshot = "CC", CompradorDocumentoSnapshot = "1",
            CompradorNombreSnapshot = "X", CompradorEmailSnapshot = "x@x.com", FechaCreacion = T0,
            EstadoPago = estadoPago, SincronizacionWo = sincWo, SincronizadaWoEn = sincronizadaEn,
            Items = [new RemisionItem { ProductoId = 0, ProductoCodigoSnapshot = "PJ-1", ProductoNombreSnapshot = "Producto", PrecioUnitario = 1000, Iva = 19, Cantidad = 4 }]
        };
        await db.SaveChangesAsync();
        remision.Items.First().ProductoId = producto.Id;
        db.Remisiones.Add(remision);
        if (fotoEn is not null)
            db.EstadoIntegracionWo.Add(new EstadoIntegracionWo { Id = 1, UltimaFotoExistenciasEn = fotoEn });
        await db.SaveChangesAsync();

        return (new ProductoService(db, TimeProvider.System, Opciones), db, producto.Id, remision);
    }

    [Fact]
    public async Task Venta_pagada_que_wo_aun_no_refleja_se_resta_del_disponible()
    {
        var (servicio, _, id, _) = await Escenario();

        var p = (await servicio.ObtenerPorIdAsync(id))!;

        Assert.Equal(10, p.Existencia);     // solo la bodega de venta, no la 'Dos'
        Assert.Equal(4, p.Reservado);
        Assert.Equal(6, p.Disponible);
    }

    [Fact]
    public async Task Venta_ya_incluida_en_una_foto_posterior_deja_de_reservar()
    {
        // Registrada en WO a las 15:05 y la foto de WO se leyó a las 15:10: ya está dentro del número.
        var (servicio, _, id, _) = await Escenario(sincWo: EstadoSincronizacionWo.Sincronizada,
            sincronizadaEn: T0.AddMinutes(5), fotoEn: T0.AddMinutes(10));

        var p = (await servicio.ObtenerPorIdAsync(id))!;

        Assert.Equal(0, p.Reservado);
        Assert.Equal(10, p.Disponible);
    }

    [Fact]
    public async Task Venta_registrada_en_wo_pero_con_foto_anterior_sigue_reservando()
    {
        // Registrada en WO a las 15:05 pero la última foto es de las 15:02: todavía no la incluye.
        var (servicio, _, id, _) = await Escenario(sincWo: EstadoSincronizacionWo.Sincronizada,
            sincronizadaEn: T0.AddMinutes(5), fotoEn: T0.AddMinutes(2));

        var p = (await servicio.ObtenerPorIdAsync(id))!;

        Assert.Equal(4, p.Reservado);
        Assert.Equal(6, p.Disponible);
    }

    [Theory]
    [InlineData(EstadoPago.Pendiente)]
    [InlineData(EstadoPago.Anulada)]
    public async Task Remisiones_no_pagadas_no_reservan(EstadoPago estado)
    {
        var (servicio, _, id, _) = await Escenario(estadoPago: estado);

        var p = (await servicio.ObtenerPorIdAsync(id))!;

        Assert.Equal(0, p.Reservado);
        Assert.Equal(10, p.Disponible);
    }

    [Fact]
    public async Task Producto_nunca_sincronizado_no_tiene_disponible()
    {
        var db = NuevaDb();
        db.Productos.Add(new Producto { Codigo = "L-1", Nombre = "Local", PrecioVenta = 500, FechaCreacion = T0 });
        await db.SaveChangesAsync();
        var servicio = new ProductoService(db, TimeProvider.System, Opciones);

        var p = (await servicio.BuscarAsync(null, false, 1, 10)).Items.Single();

        Assert.Null(p.Existencia);
        Assert.Null(p.Disponible);
    }

    // ---- Importación ----

    private static (InventarioService Servicio, AppDbContext Db) Importador()
    {
        var db = NuevaDb();
        var sinc = new SincronizacionService(db, TimeProvider.System);
        return (new InventarioService(db, sinc, Opciones, TimeProvider.System), db);
    }

    private static ImportarInventarioFilaDto Fila(int id, string codigo, decimal? existencia, decimal? precio, bool activo = true)
        => new(id, codigo, $"Producto {codigo}", "Und.", 19, activo, existencia, precio);

    [Fact]
    public async Task Importar_crea_productos_con_existencia_precio_y_hora_de_la_foto()
    {
        var (servicio, db) = Importador();
        var exportado = new DateTime(2026, 10, 14, 14, 30, 0, DateTimeKind.Utc);

        var r = await servicio.ImportarAsync(new ImportarInventarioDto(
            [Fila(1, "PJ-1", 96, 49370), Fila(2, "PJ-2", 0, null)], false, exportado));

        Assert.Equal(2, r.ProductosCreados);
        Assert.Equal(2, r.ExistenciasCargadas);
        Assert.Equal(1, r.PreciosAsignados);

        var p1 = await db.Productos.Include(p => p.Existencias).SingleAsync(p => p.Codigo == "PJ-1");
        Assert.Equal(49370, p1.PrecioVenta);
        Assert.Equal(96, p1.Existencias.Single().Cantidad);
        Assert.Equal("Uno", p1.Existencias.Single().Bodega);

        var p2 = await db.Productos.SingleAsync(p => p.Codigo == "PJ-2");
        Assert.Equal(0, p2.PrecioVenta);                     // sin precio: no se puede vender todavía
        Assert.NotNull(p2.ExistenciaActualizadaEn);           // pero su existencia (0) sí quedó cargada

        var estado = await servicio.ObtenerEstadoAsync();
        Assert.Equal(exportado, estado.UltimaFotoEn);
        Assert.Equal(2, estado.ProductosConExistencia);
        Assert.Equal(1, estado.ProductosSinPrecio);
    }

    [Fact]
    public async Task Reimportar_actualiza_existencia_pero_no_pisa_un_precio_ya_fijado()
    {
        var (servicio, db) = Importador();
        await servicio.ImportarAsync(new ImportarInventarioDto([Fila(1, "PJ-1", 10, 1000)], false, null));
        var p = await db.Productos.SingleAsync();
        p.PrecioVenta = 2500;   // alguien lo ajustó en la app
        await db.SaveChangesAsync();

        var r = await servicio.ImportarAsync(new ImportarInventarioDto([Fila(1, "PJ-1", 7, 1000)], false, null));

        Assert.Equal(0, r.PreciosAsignados);
        Assert.Equal(2500, (await db.Productos.SingleAsync()).PrecioVenta);
        Assert.Equal(7, (await db.ProductoExistencias.SingleAsync()).Cantidad);

        // Con "actualizar precios" sí se pisa.
        var r2 = await servicio.ImportarAsync(new ImportarInventarioDto([Fila(1, "PJ-1", 7, 1000)], true, null));
        Assert.Equal(1, r2.PreciosAsignados);
        Assert.Equal(1000, (await db.Productos.SingleAsync()).PrecioVenta);
    }

    [Fact]
    public async Task Importar_omite_filas_sin_id_codigo_o_nombre()
    {
        var (servicio, db) = Importador();

        var r = await servicio.ImportarAsync(new ImportarInventarioDto(
            [Fila(1, "PJ-1", 5, null), new ImportarInventarioFilaDto(0, "PJ-X", "sin id", null, 19, true, 1, null),
             new ImportarInventarioFilaDto(3, " ", "sin código", null, 19, true, 1, null)], false, null));

        Assert.Equal(1, r.ProductosCreados);
        Assert.Equal(2, r.Omitidos);
        Assert.Single(db.Productos);
    }

    [Fact]
    public async Task Registrar_en_wo_exige_remision_pagada_y_documento()
    {
        var (_, db, _, remision) = await Escenario(estadoPago: EstadoPago.Pendiente);
        var pdf = new Moq.Mock<Jimaco.Remisiones.Negocio.Interfaces.IRemisionPdfBuilder>();
        var email = new Moq.Mock<Jimaco.Remisiones.Negocio.Interfaces.IEmailSender>();
        var servicio = new RemisionService(db, pdf.Object, email.Object, TimeProvider.System,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<RemisionService>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RegistrarEnWoAsync(remision.Id, new RegistrarEnWoDto("REM 29301")));

        remision.EstadoPago = EstadoPago.Pagada;
        await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => servicio.RegistrarEnWoAsync(remision.Id, new RegistrarEnWoDto("  ")));

        var r = await servicio.RegistrarEnWoAsync(remision.Id, new RegistrarEnWoDto(" REM 29301 "));
        Assert.Equal(EstadoSincronizacionWo.Sincronizada, r!.SincronizacionWo);
        Assert.Equal("REM 29301", r.DocumentoWorldOffice);
    }
}
