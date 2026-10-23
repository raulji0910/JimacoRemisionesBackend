using Jimaco.Remisiones.Negocio.Servicios;

namespace Jimaco.Remisiones.TestUnitarios;

public class CalculosYDvTests
{
    [Theory]
    [InlineData("800197268", "4")] // NIT de la DIAN
    [InlineData("900123456", "8")]
    [InlineData("890903938", "8")] // Bancolombia
    public void DigitoVerificacion_calcula_el_dv_del_nit(string nit, string esperado)
        => Assert.Equal(esperado, DigitoVerificacion.Calcular(nit));

    [Fact]
    public void Calcular_sin_descuento_desglosa_iva_por_tarifa()
    {
        var r = RemisionCalculos.Calcular(
        [
            (1000m, 2, 19, 0m),  // 2.000 al 19%
            (500m, 2, 5, 0m),    // 1.000 al 5%
            (300m, 1, 0, 0m)     //   300 exento
        ]);

        Assert.Equal(3300m, r.SubtotalItems);
        Assert.Equal(0m, r.Descuento);
        Assert.Equal(3300m, r.SubtotalConDescuento);
        Assert.Equal(2, r.IvaDesglose.Count);
        Assert.Equal(380m, r.IvaDesglose.Single(d => d.Tarifa == 19).Valor);
        Assert.Equal(50m, r.IvaDesglose.Single(d => d.Tarifa == 5).Valor);
        Assert.Equal(3730m, r.TotalGeneral);
    }

    [Fact]
    public void Cada_linea_lleva_su_propio_descuento_y_su_iva_sobre_lo_descontado()
    {
        // Línea A: 2 x 1.000 al 19% con 10% = -200 -> 1.800 (IVA 342). Línea B: 1 x 1.000 al 5% sin descuento (IVA 50).
        var r = RemisionCalculos.Calcular([(1000m, 2, 19, 10m), (1000m, 1, 5, 0m)]);

        Assert.Equal(3000m, r.SubtotalItems);
        Assert.Equal(200m, r.Descuento);
        Assert.Equal(2800m, r.SubtotalConDescuento);
        Assert.Equal(1800m, r.IvaDesglose.Single(d => d.Tarifa == 19).Base);
        Assert.Equal(342m, r.IvaDesglose.Single(d => d.Tarifa == 19).Valor);
        Assert.Equal(1000m, r.IvaDesglose.Single(d => d.Tarifa == 5).Base);
        Assert.Equal(50m, r.IvaDesglose.Single(d => d.Tarifa == 5).Valor);
        Assert.Equal(3192m, r.TotalGeneral);
    }

    [Fact]
    public void Retenciones_no_cambian_el_iva_y_se_restan_al_total_a_pagar()
    {
        // Línea A: 2 x 1.000 al 19% con 10% -> 1.800 (IVA 342). Línea B: 1 x 1.000 al 5% con 10% -> 900 (IVA 45).
        // Base 2.700, IVA 387. ReteFuente 2,5% de 2.700 = 67,50; ReteICA 9,66 por mil de 2.700 = 26,08; ReteIVA 15% de 387 = 58,05.
        var r = RemisionCalculos.Calcular([(1000m, 2, 19, 10m), (1000m, 1, 5, 10m)], 2.5m, 9.66m, 15m);

        Assert.Equal(300m, r.Descuento);
        Assert.Equal(2700m, r.SubtotalConDescuento);
        Assert.Equal(342m, r.IvaDesglose.Single(d => d.Tarifa == 19).Valor);   // el IVA sigue sobre la base completa
        Assert.Equal(3087m, r.TotalBruto);
        Assert.Equal(67.50m, r.ReteFuente);
        Assert.Equal(26.08m, r.ReteIca);
        Assert.Equal(58.05m, r.ReteIva);
        Assert.Equal(3087m - 67.50m - 26.08m - 58.05m, r.TotalGeneral);
    }

    [Fact]
    public void Sin_retenciones_el_total_a_pagar_es_el_bruto()
    {
        var r = RemisionCalculos.Calcular([(1000m, 3, 19, 0m)]);
        Assert.Equal(r.TotalBruto, r.TotalGeneral);
        Assert.Equal(0m, r.ReteFuente + r.ReteIca + r.ReteIva);
    }

    [Theory]
    [InlineData(1500, 1000, 50)]
    [InlineData(1000, 1000, 0)]
    public void Margen_es_porcentaje_sobre_el_costo(decimal precio, decimal costo, decimal esperado)
        => Assert.Equal(esperado, RemisionCalculos.MargenPorcentaje(precio, costo));

    [Fact]
    public void Margen_sin_costo_es_nulo()
    {
        Assert.Null(RemisionCalculos.MargenPorcentaje(1000, null));
        Assert.Null(RemisionCalculos.MargenPorcentaje(1000, 0));
    }

    [Fact]
    public void Municipios_dane_trae_el_listado_oficial_completo()
    {
        Assert.Equal(1122, MunicipiosDane.Todos.Count);
        Assert.Equal(1122, MunicipiosDane.Todos.Select(m => m.Codigo).Distinct().Count());
        Assert.All(MunicipiosDane.Todos, m => Assert.Matches(@"^\d{5}$", m.Codigo));

        Assert.Equal("BOGOTÁ, D.C.", MunicipiosDane.PorCodigo("11001")!.Etiqueta);
        Assert.Equal("MEDELLÍN, ANTIOQUIA", MunicipiosDane.PorCodigo("05001")!.Etiqueta);
        Assert.Equal("SANTIAGO DE CALI, VALLE DEL CAUCA", MunicipiosDane.PorCodigo("76001")!.Etiqueta);
        Assert.Null(MunicipiosDane.PorCodigo("99999"));
        Assert.Null(MunicipiosDane.PorCodigo("Bogota"));
    }
}
