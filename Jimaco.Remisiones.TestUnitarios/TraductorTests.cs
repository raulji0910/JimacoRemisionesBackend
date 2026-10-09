using Jimaco.Remisiones.Modelo.Entidades;
using Jimaco.Remisiones.Sincronizador;

namespace Jimaco.Remisiones.TestUnitarios;

public class TraductorTests
{
    [Theory]
    [InlineData(0.19, 19)]
    [InlineData(0.05, 5)]
    [InlineData(0, 0)]
    [InlineData(19, 19)]   // si ya viniera en porcentaje se respeta
    public void Iva_de_wo_es_una_fraccion(double wo, int esperado)
        => Assert.Equal(esperado, Traductor.Iva((decimal)wo));

    [Fact]
    public void Producto_activo_en_wo_es_menos_uno()
    {
        var fila = new ProductoWoRow { IdWorldOffice = 1, Codigo = " PJ-1 ", Nombre = " Algo ", UnidadMedida = "Und.", IvaFraccion = 0.19m, Activo = -1 };
        var dto = Traductor.Producto(fila);
        Assert.Equal("PJ-1", dto.Codigo);
        Assert.Equal(19, dto.Iva);
        Assert.True(dto.Activo);
        Assert.False(Traductor.Producto(new ProductoWoRow { Codigo = "X", Nombre = "Y", Activo = 0 }).Activo);
    }

    [Fact]
    public void Nit_sin_apellidos_es_persona_juridica()
    {
        var dto = Traductor.Comprador(new CompradorWoRow
        {
            IdWorldOffice = 11188, IdTipo = 31, Identificacion = "902020510", Nombre = "CONSORCIO CONSTRUCTOR ANDES", Apellidos = "",
            Primer_Nombre = "CONSORCIO CONSTRUCTOR ANDES", Activo = -1, Telefonos = "3218614753", Email = "factura@andes.com"
        }, out var motivo);

        Assert.Null(motivo);
        Assert.Equal(TipoPersona.Juridica, dto!.TipoPersona);
        Assert.Equal("NIT", dto.TipoDocumento);
        Assert.Equal("CONSORCIO CONSTRUCTOR ANDES", dto.PrimerNombre);
    }

    [Fact]
    public void Persona_natural_usa_los_campos_separados_de_nombre_y_apellido()
    {
        var dto = Traductor.Comprador(new CompradorWoRow
        {
            IdWorldOffice = 2, IdTipo = 13, Identificacion = "1015424228", Nombre = "CAMILO", Apellidos = "PIRAGAUTA",
            Primer_Nombre = "CAMILO", Segundo_Nombre = "ANDRES", Primer_Apellido = "PIRAGAUTA", Segundo_Apellido = "PAEZ", Activo = -1
        }, out _);

        Assert.Equal(TipoPersona.Natural, dto!.TipoPersona);
        Assert.Equal("CAMILO", dto.PrimerNombre);
        Assert.Equal("ANDRES", dto.SegundoNombre);
        Assert.Equal("PIRAGAUTA", dto.PrimerApellido);
        Assert.Equal("PAEZ", dto.SegundoApellido);
    }

    [Fact]
    public void Sin_campos_separados_se_usan_nombre_y_apellidos()
    {
        var dto = Traductor.Comprador(new CompradorWoRow
        {
            IdWorldOffice = 4, IdTipo = 22, Identificacion = "123456", Nombre = "JOHN", Apellidos = "SMITH", Activo = -1
        }, out _);

        Assert.Equal("JOHN", dto!.PrimerNombre);
        Assert.Equal("SMITH", dto.PrimerApellido);
        Assert.Equal("CE", dto.TipoDocumento);
    }

    [Fact]
    public void Correo_con_varias_direcciones_toma_la_primera_valida_y_telefono_cero_es_nulo()
    {
        var dto = Traductor.Comprador(new CompradorWoRow
        {
            IdWorldOffice = 2, IdTipo = 13, Identificacion = "1", Primer_Nombre = "A", Primer_Apellido = "B", Activo = -1,
            Email = "kmilo@hotmail.com; otro@correo.com", Telefonos = "0", Telefono2 = "32143137"
        }, out _);

        Assert.Equal("kmilo@hotmail.com", dto!.Email);
        Assert.Equal("32143137", dto.Telefono);
    }

    [Fact]
    public void Tercero_sin_identificacion_o_con_tipo_desconocido_se_omite_con_motivo()
    {
        Assert.Null(Traductor.Comprador(new CompradorWoRow { IdWorldOffice = 9, IdTipo = 13, Identificacion = " " }, out var m1));
        Assert.NotNull(m1);

        Assert.Null(Traductor.Comprador(new CompradorWoRow { IdWorldOffice = 5, IdTipo = 7, Identificacion = "222", Nombre = "CF" }, out var m2));
        Assert.NotNull(m2);
    }
}
