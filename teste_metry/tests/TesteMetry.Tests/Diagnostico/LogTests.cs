using TesteMetry.Diagnostico;

namespace TesteMetry.Tests.Diagnostico;

public class LogTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ConsoleLog_PassoSoNoVerboso_ErroSempre(bool verboso)
    {
        using var saida = new StringWriter();
        var log = new ConsoleLog(verboso, saida);

        log.Passo("passo-1");
        log.Erro("erro-1");

        var texto = saida.ToString();
        Assert.Equal(verboso, log.Verboso);
        Assert.Equal(verboso, texto.Contains("[verboso ", StringComparison.Ordinal) && texto.Contains("passo-1", StringComparison.Ordinal));
        Assert.Contains("[erro ", texto, StringComparison.Ordinal);
        Assert.Contains("erro-1", texto, StringComparison.Ordinal);
    }

    [Fact]
    public void ConsoleLog_SaidaNula_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => new ConsoleLog(true, null!));
    }

    [Fact]
    public void LogNulo_DescartaTudo()
    {
        var log = LogNulo.Instancia;

        log.Passo("x");
        log.Erro("y");

        Assert.False(log.Verboso);
        Assert.Same(LogNulo.Instancia, log);
    }
}
