using Distrito.Shared.Economy;

namespace Distrito.Shared.Tests;

public class SupplierRulesTests
{
    [Fact]
    public void Descuento_por_volumen_desde_20()
    {
        Assert.Equal(19 * 300, SupplierRules.GoodsCents(300, 19));
        Assert.Equal(20 * 300 * 90 / 100, SupplierRules.GoodsCents(300, 20));
    }

    [Fact]
    public void Envio_es_10_por_ciento_con_minimo_de_un_peso()
    {
        Assert.Equal(100, SupplierRules.DeliveryCents(500));
        Assert.Equal(540, SupplierRules.DeliveryCents(5400));
    }

    [Fact]
    public void Preparacion_un_ciclo_cada_10_y_cola_de_50_por_ciclo()
    {
        Assert.Equal(1, SupplierRules.PrepCycles(5, 0));
        Assert.Equal(2, SupplierRules.PrepCycles(11, 0));
        Assert.Equal(1, SupplierRules.PrepCycles(5, 40));   // entra en el mismo ciclo
        Assert.Equal(2, SupplierRules.PrepCycles(5, 50));   // la cola llena el primer ciclo
        Assert.Equal(5, SupplierRules.PrepCycles(50, 0));   // 50 unidades: 5 ciclos propios
    }
}

public class StockVisualRulesTests
{
    [Fact]
    public void Bultos_agrupados_con_tope()
    {
        Assert.Equal(0, StockVisualRules.Props(new StockPile("bolsa-cafe", ItemKinds.RawMaterial, 0, 0)));
        Assert.Equal(5, StockVisualRules.Props(new StockPile("bolsa-cafe", ItemKinds.RawMaterial, 5, 25)));
        Assert.Equal(StockVisualRules.MaxPropsPerPile, StockVisualRules.Props(new StockPile("caja-repuestos", ItemKinds.Goods, 400, 4800)));
        Assert.Equal(3, StockVisualRules.Props(new StockPile("bala-pistola", ItemKinds.Ammo, 120, 2.4))); // cajas de 50
        Assert.Equal(3, StockVisualRules.ShelfLevel(99_000));
        Assert.Equal(1, StockVisualRules.ShelfLevel(10));
        Assert.Equal(0, StockVisualRules.ShelfLevel(0));
    }
}
