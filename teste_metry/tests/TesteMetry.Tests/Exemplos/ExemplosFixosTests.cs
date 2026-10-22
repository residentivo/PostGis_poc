using TesteMetry.Exemplos;
using TesteMetry.Validacao;

namespace TesteMetry.Tests.Exemplos;

/// <summary>M6.3 - ExemplosFixos.Para(srid) aprovados pelo validador nos 4 SRIDs.</summary>
public class ExemplosFixosTests
{
    [Theory]
    [InlineData(4326, ExemplosFixos.PracaDaSeGraus)]
    [InlineData(4674, ExemplosFixos.PracaDaSeGraus)]
    [InlineData(3857, ExemplosFixos.PracaDaSeWebMercator)]
    [InlineData(31983, ExemplosFixos.PracaDaSeUtm23S)]
    public void Para_TresExemplosValidosNoSrid(int srid, string primeiro)
    {
        var exemplos = ExemplosFixos.Para(srid);
        var validador = new WktValidator(srid);

        Assert.Equal(3, exemplos.Count);
        Assert.Equal(primeiro, exemplos[0]);
        Assert.Equal(new[] { "POINT", "LINESTRING", "POLYGON" }, exemplos.Select(w => validador.Validar(w).Tipo));
        Assert.All(exemplos, w => Assert.True(validador.Validar(w).Valido, w));
    }

    [Theory]
    [InlineData(4326, 31983)]
    [InlineData(3857, 4326)]
    [InlineData(31983, 4674)]
    public void Para_ExemplosDeUmSridNaoPassamEmOutro(int sridExemplos, int sridValidador)
    {
        var validador = new WktValidator(sridValidador);

        Assert.All(ExemplosFixos.Para(sridExemplos), w => Assert.False(validador.Validar(w).Valido, w));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2154)]
    [InlineData(4269)]
    public void Para_SridForaDaLista_Lanca(int srid)
    {
        var ex = Assert.Throws<ArgumentOutOfRangeException>(() => ExemplosFixos.Para(srid));

        Assert.Equal("srid", ex.ParamName);
    }
}
