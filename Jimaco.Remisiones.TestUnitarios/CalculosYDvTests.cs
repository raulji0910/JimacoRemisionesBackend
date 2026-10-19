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
            (1000m, 2, 19),  // 2.000 al 19%
            (500m, 2, 5),    // 1.000 al 5%
            (300m, 1, 0)     //   300 exento
        ], 0m);

        Assert.Equal(3300m, r.SubtotalItems);
        Assert.Equal(3300m, r.SubtotalConDescuento);
        Assert.Equal(2, r.IvaDesglose.Count);
        Assert.Equal(380m, r.IvaDesglose.Single(d => d.Tarifa == 19).Valor);
        Assert.Equal(50m, r.IvaDesglose.Single(d => d.Tarifa == 5).Valor);
        Assert.Equal(3730m, r.TotalGeneral);
    }

    [Fact]
    public void Calcular_prorratea_el_descuento_entre_tarifas()
    {
        // Subtotal 3.000: 2.000 al 19% (2/3) y 1.000 al 5% (1/3). Descuento 300 -> 200 / 100.
        var r = RemisionCalculos.Calcular([(1000m, 2, 19), (1000m, 1, 5)], 300m);

        var iva19 = r.IvaDesglose.Single(d => d.Tarifa == 19);
        var iva5 = r.IvaDesglose.Single(d => d.Tarifa == 5);
        Assert.Equal(1800m, iva19.Base);
        Assert.Equal(342m, iva19.Valor);
        Assert.Equal(900m, iva5.Base);
        Assert.Equal(45m, iva5.Valor);
        Assert.Equal(2700m, r.SubtotalConDescuento);
        Assert.Equal(3087m, r.TotalGeneral);
    }

    [Fact]
    public void Calcular_descuento_porcentual_y_retencion_reducen_la_base_antes_del_iva()
    {
        // Subtotal 3.000 (2.000 al 19% y 1.000 al 5%). Descuento 10% = 300 y retención 150: la base baja 450 en total,
        // repartida 2/3 y 1/3 -> bases 1.700 y 850; IVA 323 y 42,5.
        var descuento = RemisionCalculos.CalcularDescuento(3000m, 10m);
        var r = RemisionCalculos.Calcular([(1000m, 2, 19), (1000m, 1, 5)], descuento, 150m);

        Assert.Equal(300m, descuento);
        Assert.Equal(2550m, r.SubtotalConDescuento);
        Assert.Equal(1700m, r.IvaDesglose.Single(d => d.Tarifa == 19).Base);
        Assert.Equal(323m, r.IvaDesglose.Single(d => d.Tarifa == 19).Valor);
        Assert.Equal(42.5m, r.IvaDesglose.Single(d => d.Tarifa == 5).Valor);
        Assert.Equal(2915.5m, r.TotalGeneral);
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
}
