using TesteMetry.Validacao;

namespace TesteMetry.Tests.Validacao;

/// <summary>Tabela constante de SRIDs e faixas (D-M4/D-M6).</summary>
public class LimitesSridTests
{
    [Fact]
    public void Permitidos_OrdemETexto()
    {
        Assert.Equal(new[] { 4326, 4674, 3857, 31983 }, LimitesSrid.Permitidos);
        Assert.Equal("4326, 4674, 3857 ou 31983", LimitesSrid.PermitidosTexto);
        Assert.Equal(4326, LimitesSrid.SridPadrao);
    }

    [Theory]
    [InlineData(4326, true)]
    [InlineData(4674, true)]
    [InlineData(3857, true)]
    [InlineData(31983, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    [InlineData(2154, false)]
    [InlineData(4269, false)]
    [InlineData(900913, false)]
    public void EhPermitido(int srid, bool esperado)
    {
        Assert.Equal(esperado, LimitesSrid.EhPermitido(srid));
        if (esperado)
        {
            Assert.Equal(srid, LimitesSrid.Para(srid).Srid);
        }
        else
        {
            var ex = Assert.Throws<ArgumentOutOfRangeException>(() => LimitesSrid.Para(srid));
            Assert.Contains("use 4326, 4674, 3857 ou 31983", ex.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(4326, "WGS 84", "[-180, 180]", "[-90, 90]", "longitude", "latitude")]
    [InlineData(4674, "SIRGAS 2000", "[-180, 180]", "[-90, 90]", "longitude", "latitude")]
    [InlineData(3857, "Web Mercator", "[-20037508.3428, 20037508.3428]", "[-20037508.3428, 20037508.3428]", "X", "Y")]
    [InlineData(31983, "SIRGAS 2000 / UTM 23S", "[0, 1000000]", "[0, 10000000]", "E", "N")]
    public void Faixas(int srid, string nome, string faixaX, string faixaY, string eixoX, string eixoY)
    {
        var faixa = LimitesSrid.Para(srid);

        Assert.Equal(nome, faixa.Nome);
        Assert.Equal(faixaX, faixa.DescreverX());
        Assert.Equal(faixaY, faixa.DescreverY());
        Assert.Equal(eixoX, faixa.NomeX);
        Assert.Equal(eixoY, faixa.NomeY);
        Assert.True(faixa.XDentro(faixa.MinX));
        Assert.True(faixa.XDentro(faixa.MaxX));
        Assert.True(faixa.YDentro(faixa.MinY));
        Assert.True(faixa.YDentro(faixa.MaxY));
        Assert.False(faixa.XDentro(Math.BitDecrement(faixa.MinX)));
        Assert.False(faixa.XDentro(Math.BitIncrement(faixa.MaxX)));
        Assert.False(faixa.YDentro(Math.BitDecrement(faixa.MinY)));
        Assert.False(faixa.YDentro(Math.BitIncrement(faixa.MaxY)));
        Assert.False(faixa.XDentro(double.NaN));
        Assert.False(faixa.YDentro(double.PositiveInfinity));
    }
}
