using ApiMapas.Dominio;

namespace ApiMapas.Tests.Dominio;

public class ColunaMapaTests
{
    [Theory]
    [InlineData(ColunaMapa.Graphy, "graphy")]
    [InlineData(ColunaMapa.Metry, "metry")]
    public void Nome_DevolveNomePublico(ColunaMapa coluna, string esperado)
    {
        Assert.Equal(esperado, coluna.Nome());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    [InlineData(-1)]
    [InlineData(99)]
    public void Nome_ValorForaDoEnum_Lanca(int valor)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ((ColunaMapa)valor).Nome());
        Assert.Equal("coluna", ex.ParamName);
    }

    [Fact]
    public void Enum_TemSomenteDuasColunas()
    {
        Assert.Equal(new[] { ColunaMapa.Graphy, ColunaMapa.Metry }, Enum.GetValues<ColunaMapa>());
    }

    [Fact]
    public void GeometriaMapa_RecordGuardaValores()
    {
        var g = new GeometriaMapa("POINT", 4326, "POINT(1 2)", "{}", 1, 2, 1, 2);
        Assert.Equal(g, g with { });
        Assert.Equal("POINT", g.Tipo);
        Assert.Equal(2, g.Latitude);
    }
}
