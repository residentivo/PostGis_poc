using TesteGraphy.Comum;

namespace TesteGraphy.Tests.Comum;

public class TextoSeguroTests
{
    [Theory]
    [InlineData('\r')]
    [InlineData('\n')]
    [InlineData('\t')]
    [InlineData('\u001b')]
    [InlineData('\u0000')]
    [InlineData('\u007f')]
    [InlineData('\u2028')]
    [InlineData('\u2029')]
    [InlineData('\u0085')]
    public void Sanitizar_TrocaCaracteresDeControleESeparadoresPorInterrogacao(char controle)
    {
        var resultado = TextoSeguro.Sanitizar($"a{controle}b{controle}");

        Assert.Equal("a?b?", resultado);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Sanitizar_NuloOuVazio_RetornaVazio(string? texto)
    {
        Assert.Equal(string.Empty, TextoSeguro.Sanitizar(texto));
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(199, false)]
    [InlineData(200, false)]
    [InlineData(201, true)]
    [InlineData(5000, true)]
    public void Sanitizar_TruncaAcimaDe200ComReticencias(int tamanho, bool truncado)
    {
        var texto = new string('x', tamanho);

        var resultado = TextoSeguro.Sanitizar(texto);

        if (truncado)
        {
            Assert.Equal(new string('x', TextoSeguro.TamanhoMaximoEco) + TextoSeguro.Reticencias, resultado);
        }
        else
        {
            Assert.Equal(texto, resultado);
        }
    }

    [Theory]
    [InlineData("abcdef", 5, "abcde...")]
    [InlineData("abcde", 5, "abcde")]
    [InlineData("a\nbcdef", 3, "a?b...")]
    public void Sanitizar_TamanhoMaximoInformado(string texto, int maximo, string esperado)
    {
        Assert.Equal(esperado, TextoSeguro.Sanitizar(texto, maximo));
    }

    [Theory]
    [InlineData("password")]
    [InlineData("PASSWORD=abc")]
    [InlineData("Host=x;Password=S3nh4-Sentinela-XYZ")]
    [InlineData("senha")]
    [InlineData("SeNhA: 123")]
    [InlineData("pwd=")]
    [InlineData("PWD=xyz")]
    [InlineData("POINT(0 0) pAsSwOrD")]
    public void EcoWkt_ComIndicadorDeCredencial_EmQualquerCaixa_RetornaEcoOmitido(string wkt)
    {
        Assert.Equal(TextoSeguro.EcoOmitido, TextoSeguro.EcoWkt(wkt));
    }

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("POINT(0 0)", "POINT(0 0)")]
    [InlineData("pwd sem igual", "pwd sem igual")]
    [InlineData("POINT(0 0)\r\n\u001b[31m", "POINT(0 0)???[31m")]
    public void EcoWkt_SemCredencial_RetornaTextoSanitizado(string? wkt, string esperado)
    {
        Assert.Equal(esperado, TextoSeguro.EcoWkt(wkt));
    }

    [Fact]
    public void EcoWkt_Longo_ETruncado()
    {
        var eco = TextoSeguro.EcoWkt(new string('1', 300));

        Assert.Equal(TextoSeguro.TamanhoMaximoEco + TextoSeguro.Reticencias.Length, eco.Length);
        Assert.EndsWith(TextoSeguro.Reticencias, eco, StringComparison.Ordinal);
    }
}
