using TesteGraphy.Exemplos;
using TesteGraphy.Validacao;

namespace TesteGraphy.Tests.Exemplos;

public class ExemplosFixosTests
{
    [Theory]
    [InlineData(ExemplosFixos.PracaDaSe, "POINT")]
    [InlineData(ExemplosFixos.Linha, "LINESTRING")]
    [InlineData(ExemplosFixos.Poligono, "POLYGON")]
    public void CadaExemplo_PassaNoValidador(string wkt, string tipo)
    {
        var resultado = new WktValidator().Validar(wkt);

        Assert.True(resultado.Valido, resultado.Erro);
        Assert.Equal(tipo, resultado.Tipo);
    }

    [Fact]
    public void Todos_ContemOs3NaOrdem()
    {
        Assert.Equal([ExemplosFixos.PracaDaSe, ExemplosFixos.Linha, ExemplosFixos.Poligono], ExemplosFixos.Todos);
        Assert.All(ExemplosFixos.Todos, wkt => Assert.True(new WktValidator().Validar(wkt).Valido));
    }
}
