using Jimaco.Remisiones.Modelo;
using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Negocio.DTOs;
using Jimaco.Remisiones.Negocio.Servicios;
using Microsoft.EntityFrameworkCore;

namespace Jimaco.Remisiones.TestUnitarios;

public class SincronizacionTests
{
    private static (SincronizacionService Servicio, AppDbContext Db) Crear()
    {
        var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options);
        return (new SincronizacionService(db, TimeProvider.System), db);
    }

    private static SincProductoDto Prod(int id, string codigo = "PJ-1", string nombre = "Producto", int iva = 19, bool activo = true)
        => new(id, codigo, nombre, "Und.", iva, activo);

    [Fact]
    public async Task Producto_nuevo_de_wo_nace_sin_precio_y_en_origen_wo()
    {
        var (servicio, db) = Crear();

        var r = await servicio.SincronizarProductosAsync([Prod(10, "pj-4137 ")]);

        Assert.Equal(1, r.Creados);
        var p = await db.Productos.SingleAsync();
        Assert.Equal("PJ-4137", p.Codigo);       // normalizado: mayúsculas y sin espacios
        Assert.Equal(0, p.PrecioVenta);
        Assert.Equal(OrigenRegistro.WorldOffice, p.Origen);
        Assert.Equal(10, p.IdWorldOffice);
    }

    [Fact]
    public async Task Iva_no_soportado_se_guarda_como_19_pero_avisa()
    {
        var (servicio, db) = Crear();

        var r = await servicio.SincronizarProductosAsync([Prod(10, "PJ-8", iva: 8), Prod(11, "PJ-5", iva: 5)]);

        Assert.Equal(19, (await db.Productos.SingleAsync(p => p.Codigo == "PJ-8")).Iva);
        Assert.Equal(5, (await db.Productos.SingleAsync(p => p.Codigo == "PJ-5")).Iva);
        Assert.Single(r.Avisos);
        Assert.Contains("PJ-8", r.Avisos[0]);
    }

    [Fact]
    public async Task Resincronizar_nunca_pisa_el_precio_ni_el_costo_locales()
    {
        var (servicio, db) = Crear();
        await servicio.SincronizarProductosAsync([Prod(10)]);
        var p = await db.Productos.SingleAsync();
        p.PrecioVenta = 25000; p.Costo = 18000;
        await db.SaveChangesAsync();

        var r = await servicio.SincronizarProductosAsync([Prod(10, nombre: "Nombre nuevo en WO", iva: 5)]);

        Assert.Equal(1, r.Actualizados);
        p = await db.Productos.SingleAsync();
        Assert.Equal("Nombre nuevo en WO", p.Nombre);
        Assert.Equal(5, p.Iva);
        Assert.Equal(25000, p.PrecioVenta);
        Assert.Equal(18000, p.Costo);
    }

    [Fact]
    public async Task Producto_local_con_el_mismo_codigo_se_enlaza_en_vez_de_duplicarse()
    {
        var (servicio, db) = Crear();
        db.Productos.Add(new Producto { Codigo = "PJ-1", Nombre = "Creado a mano", PrecioVenta = 9000, FechaCreacion = DateTime.UtcNow });
        await db.SaveChangesAsync();

        await servicio.SincronizarProductosAsync([Prod(77, "PJ-1", "Nombre de WO")]);

        var p = await db.Productos.SingleAsync();
        Assert.Equal(77, p.IdWorldOffice);
        Assert.Equal(OrigenRegistro.WorldOffice, p.Origen);
        Assert.Equal(9000, p.PrecioVenta);
    }

    [Fact]
    public async Task Existencias_se_actualizan_por_bodega_y_las_de_productos_desconocidos_se_ignoran()
    {
        var (servicio, db) = Crear();
        await servicio.SincronizarProductosAsync([Prod(10)]);

        var r1 = await servicio.SincronizarExistenciasAsync(
        [
            new SincExistenciaDto(10, "Uno", 96), new SincExistenciaDto(10, "Dos", 12), new SincExistenciaDto(999, "Uno", 5)
        ], DateTime.UtcNow);
        Assert.Equal(2, r1.Creados);
        Assert.Equal(1, r1.Omitidos);

        var r2 = await servicio.SincronizarExistenciasAsync([new SincExistenciaDto(10, "Uno", 90)], DateTime.UtcNow);
        Assert.Equal(1, r2.Actualizados);

        var uno = await db.ProductoExistencias.SingleAsync(e => e.Bodega == "Uno");
        Assert.Equal(90, uno.Cantidad);
        Assert.NotNull((await db.Productos.SingleAsync()).ExistenciaActualizadaEn);
    }

    private static SincCompradorDto Cli(int id, string doc, string? email, string nombre = "Constructora Andes", TipoPersona tp = TipoPersona.Juridica, string tipo = "NIT")
        => new(id, tipo, doc, tp, nombre, null, null, null, "CL 104 # 18", "Bogotá", "3218614753", email, true);

    [Fact]
    public async Task Comprador_nuevo_de_wo_se_crea_con_dv_y_nombre_completo()
    {
        var (servicio, db) = Crear();

        var r = await servicio.SincronizarCompradoresAsync([Cli(11188, "902.020.510-2", "factura@andes.com")]);

        Assert.Equal(1, r.Creados);
        var c = await db.Compradores.SingleAsync();
        Assert.Equal("902020510", c.NumeroDocumento);   // sin puntos ni DV
        Assert.Equal("2", c.Dv);                         // el mismo DV que tiene WO para este NIT
        Assert.Equal("Constructora Andes", c.NombreCompleto);
        Assert.Equal("11188", c.CodigoWorldOffice);
        Assert.Equal(OrigenRegistro.WorldOffice, c.Origen);
    }

    [Fact]
    public async Task Comprador_local_con_el_mismo_documento_se_enlaza_y_conserva_su_correo()
    {
        var (servicio, db) = Crear();
        db.Compradores.Add(new Comprador
        {
            TipoDocumento = "NIT", NumeroDocumento = "902020510", PrimerNombre = "Andes", NombreCompleto = "Andes",
            Email = "digitado-en-caja@andes.com", FechaCreacion = DateTime.UtcNow
        });
        await db.SaveChangesAsync();

        var r = await servicio.SincronizarCompradoresAsync([Cli(11188, "902020510", "correo-de-wo@andes.com")]);

        Assert.Equal(1, r.Actualizados);
        var c = await db.Compradores.SingleAsync();
        Assert.Equal("11188", c.CodigoWorldOffice);
        Assert.Equal("digitado-en-caja@andes.com", c.Email);   // el correo local no se pisa
    }

    [Fact]
    public async Task El_correo_de_wo_solo_completa_cuando_el_local_esta_vacio()
    {
        var (servicio, db) = Crear();
        await servicio.SincronizarCompradoresAsync([Cli(1, "123", null, "John Smith", TipoPersona.Natural, "CE")]);
        Assert.Equal("", (await db.Compradores.SingleAsync()).Email);

        await servicio.SincronizarCompradoresAsync([Cli(1, "123", "john@x.com", "John Smith", TipoPersona.Natural, "CE")]);
        Assert.Equal("john@x.com", (await db.Compradores.SingleAsync()).Email);
    }

    [Fact]
    public async Task Tipo_de_documento_no_soportado_se_omite_con_aviso()
    {
        var (servicio, db) = Crear();

        var r = await servicio.SincronizarCompradoresAsync([Cli(5, "222222", null, "Consumidor", TipoPersona.Natural, "CONSUMIDOR FINAL")]);

        Assert.Equal(1, r.Omitidos);
        Assert.Single(r.Avisos);
        Assert.Empty(db.Compradores);
    }
}
