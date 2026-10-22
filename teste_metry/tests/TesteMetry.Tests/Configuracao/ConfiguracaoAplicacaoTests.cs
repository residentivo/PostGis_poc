using TesteMetry.Configuracao;

namespace TesteMetry.Tests.Configuracao;

public class ConfiguracaoAplicacaoTests
{
    [Theory]
    [InlineData("{\"Verboso\": true}", true, false)]
    [InlineData("{\"Verboso\": false}", false, false)]
    [InlineData("{}", false, false)]
    [InlineData("[1, 2]", false, false)]
    [InlineData("true", false, false)]
    [InlineData("{\"verboso\": true}", false, false)]
    [InlineData("{\"Verboso\": true, \"PG_PASSWORD\": \"x\", \"Outra\": {\"a\": 1}}", true, false)]
    [InlineData("{\n // comentario\n \"Verboso\": true,\n}", true, false)]
    [InlineData("{\"Verboso\": \"true\"}", false, true)]
    [InlineData("{\"Verboso\": 1}", false, true)]
    [InlineData("{\"Verboso\": null}", false, true)]
    [InlineData("{\"Verboso\": {}}", false, true)]
    [InlineData("{", false, true)]
    [InlineData("", false, true)]
    [InlineData("nao e json", false, true)]
    [InlineData("{\"Verboso\": true} lixo", false, true)]
    public void Interpretar(string json, bool verboso, bool comAviso)
    {
        var config = ConfiguracaoAplicacao.Interpretar(json);

        Assert.Equal(verboso, config.Verboso);
        Assert.Equal(comAviso, config.Aviso is not null);
        if (comAviso)
        {
            Assert.Contains("modo verboso desligado", config.Aviso, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(7, true, false)]
    [InlineData(8, false, true)]
    [InlineData(20, false, true)]
    public void Interpretar_ProfundidadeMaxima8(int arraysAninhados, bool verboso, bool comAviso)
    {
        // objeto raiz = nivel 1; cada array acrescenta 1 nivel.
        var json = "{\"Verboso\": true, \"x\": " + new string('[', arraysAninhados) + new string(']', arraysAninhados) + "}";

        var config = ConfiguracaoAplicacao.Interpretar(json);

        Assert.Equal(verboso, config.Verboso);
        Assert.Equal(comAviso, config.Aviso is not null);
    }

    [Fact]
    public void Carregar_ArquivoAusente_VerbosoDesligadoSemAviso()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"inexistente-{Guid.NewGuid():N}.json");

        var config = ConfiguracaoAplicacao.Carregar(caminho);

        Assert.False(config.Verboso);
        Assert.Null(config.Aviso);
    }

    [Theory]
    [InlineData("{\"Verboso\": true}", true, false)]
    [InlineData("{\"Verboso\": false}", false, false)]
    [InlineData("{ invalido", false, true)]
    public void Carregar_ArquivoExistente(string conteudo, bool verboso, bool comAviso)
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"cfg-{Guid.NewGuid():N}.json");
        File.WriteAllText(caminho, conteudo);
        try
        {
            var config = ConfiguracaoAplicacao.Carregar(caminho);

            Assert.Equal(verboso, config.Verboso);
            Assert.Equal(comAviso, config.Aviso is not null);
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void Carregar_ArquivoBloqueado_AvisoComTipoDaExcecao()
    {
        var caminho = Path.Combine(Path.GetTempPath(), $"cfg-lock-{Guid.NewGuid():N}.json");
        File.WriteAllText(caminho, "{\"Verboso\": true}");
        try
        {
            using (new FileStream(caminho, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
            {
                var config = ConfiguracaoAplicacao.Carregar(caminho);

                Assert.False(config.Verboso);
                Assert.Contains("IOException", config.Aviso, StringComparison.Ordinal);
            }
        }
        finally
        {
            File.Delete(caminho);
        }
    }

    [Fact]
    public void AppsettingsDistribuido_TemSoVerbosoFalse()
    {
        var caminho = Path.Combine(AppContext.BaseDirectory, ConfiguracaoAplicacao.NomeArquivo);

        var config = ConfiguracaoAplicacao.Carregar(caminho);

        Assert.True(File.Exists(caminho));
        Assert.False(config.Verboso);
        Assert.Null(config.Aviso);
        Assert.DoesNotContain("PASSWORD", File.ReadAllText(caminho), StringComparison.OrdinalIgnoreCase);
    }
}
