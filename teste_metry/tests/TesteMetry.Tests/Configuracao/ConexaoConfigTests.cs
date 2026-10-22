using Npgsql;
using TesteMetry.Configuracao;
using TesteMetry.Tests.Fakes;
using TesteMetry.Validacao;

namespace TesteMetry.Tests.Configuracao;

/// <summary>M6.1 - ConexaoConfig, com foco em PG_SRID (D-M4, RQ-1, RQ-2, REC-1).</summary>
public class ConexaoConfigTests
{
    private const string Sentinela = Ambiente.Sentinela;
    private const string Permitidos = "valor nao permitido (use 4326, 4674, 3857 ou 31983)";

    public enum Motivo
    {
        Vazia,
        ForaDaLista,
        NaoDigito,
        ZeroEsquerda,
    }

    public static TheoryData<string, Motivo> SridsInvalidos => new()
    {
        { string.Empty, Motivo.Vazia },
        { " ", Motivo.Vazia },
        { "      ", Motivo.Vazia },
        { "\t", Motivo.Vazia },
        { "04326", Motivo.ZeroEsquerda },
        { "004326", Motivo.ZeroEsquerda },
        { "000000", Motivo.ZeroEsquerda },
        { "0", Motivo.ZeroEsquerda },
        { "03857", Motivo.ZeroEsquerda },
        { "4326;DROP", Motivo.ForaDaLista },
        { "4326;DROP TABLE mapas", Motivo.ForaDaLista },
        { "4326;", Motivo.NaoDigito },
        { " 4326", Motivo.NaoDigito },
        { "4326 ", Motivo.NaoDigito },
        { "-4326", Motivo.NaoDigito },
        { "+4326", Motivo.NaoDigito },
        { "0x10E6", Motivo.NaoDigito },
        { "43.26", Motivo.NaoDigito },
        { "4,326", Motivo.NaoDigito },
        { "4326\n", Motivo.NaoDigito },
        { "\u0664\u0663\u0662\u0666", Motivo.NaoDigito },   // digitos arabico-indicos
        { "\uFF14\uFF13\uFF12\uFF16", Motivo.NaoDigito },   // digitos de largura total
        { "\u096A\u0969\u0968\u096C", Motivo.NaoDigito },   // digitos devanagari
        { "2154", Motivo.ForaDaLista },
        { "4269", Motivo.ForaDaLista },
        { "1", Motivo.ForaDaLista },
        { "999999", Motivo.ForaDaLista },
        { "99999999999", Motivo.ForaDaLista },              // overflow de int (rejeitado pelo tamanho)
        { "2147483648", Motivo.ForaDaLista },
        { "4326000", Motivo.ForaDaLista },
    };

    [Fact]
    public void Srid_Ausente_Usa4326()
    {
        var log = new LogCaptura();

        var config = ConexaoConfig.Carregar(Ambiente.Variaveis(), log);

        Assert.Equal(4326, config.Srid);
        Assert.Equal(LimitesSrid.SridPadrao, config.Srid);
        Assert.Contains(log.Passos, p => p.Contains("PG_SRID ausente; usando o padrao 4326", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("4326", 4326)]
    [InlineData("4674", 4674)]
    [InlineData("3857", 3857)]
    [InlineData("31983", 31983)]
    public void Srid_Permitido(string valor, int esperado)
    {
        var log = new LogCaptura();

        var config = ConexaoConfig.Carregar(Ambiente.Variaveis((ConexaoConfig.VarSrid, valor)), log);

        Assert.Equal(esperado, config.Srid);
        Assert.EndsWith($"srid={esperado}", config.Descricao(), StringComparison.Ordinal);
        Assert.Contains(log.Passos, p => p == $"PG_SRID validado: {esperado}.");
    }

    [Theory]
    [MemberData(nameof(SridsInvalidos))]
    public void Srid_Invalido_ConfiguracaoInvalidaSemEcoarValor(string valor, Motivo motivo)
    {
        var log = new LogCaptura();

        var ex = Assert.Throws<ConfiguracaoInvalidaException>(
            () => ConexaoConfig.Carregar(Ambiente.Variaveis((ConexaoConfig.VarSrid, valor)), log));

        var esperado = motivo switch
        {
            Motivo.Vazia => $"definida porem vazia; {Permitidos}",
            Motivo.NaoDigito => $"deve conter somente digitos 0-9; {Permitidos}",
            Motivo.ZeroEsquerda => $"zeros a esquerda nao sao aceitos; {Permitidos}",
            _ => Permitidos,
        };

        // Mensagem exata (prova que o valor recebido nao e ecoado - RQ-1).
        Assert.Equal(ConexaoConfig.VarSrid, ex.NomeVariavel);
        Assert.Equal($"Variavel de ambiente PG_SRID: {esperado}.", ex.Message);

        // Log verboso: so o passo inicial generico, nada com o valor.
        var passo = Assert.Single(log.Passos);
        Assert.StartsWith("Lendo PG_HOST", passo, StringComparison.Ordinal);
        Assert.Empty(log.Erros);
        Assert.DoesNotContain("DROP", ex.Message + log.Tudo, StringComparison.Ordinal);
    }

    [Fact]
    public void Srid_InvalidoAntesDeUsuario_ErroCitaPgSrid()
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => ConexaoConfig.Carregar(
            n => n == ConexaoConfig.VarSrid ? "2154" : null));

        Assert.Equal(ConexaoConfig.VarSrid, ex.NomeVariavel);
    }

    [Fact]
    public void Padroes()
    {
        var config = Ambiente.Config();

        Assert.Equal("192.168.168.106", config.Host);
        Assert.Equal(5432, config.Porta);
        Assert.Equal("casaos", config.BaseDados);
        Assert.Equal("usuario_teste", config.Usuario);
        Assert.Equal(SslMode.Prefer, config.SslMode);
        Assert.Equal(4326, config.Srid);
        Assert.True(config.SslInseguro);
        Assert.Contains("PG_SSLMODE efetivo = Prefer", config.AvisoSsl, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("  meu-host  ", "meu-host")]
    [InlineData("", "192.168.168.106")]
    [InlineData("   ", "192.168.168.106")]
    [InlineData(null, "192.168.168.106")]
    public void Host_Trim_E_Padrao(string? valor, string esperado)
    {
        Assert.Equal(esperado, Ambiente.Config((ConexaoConfig.VarHost, valor)).Host);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    [InlineData(" 5433 ", 5433)]
    [InlineData(null, 5432)]
    [InlineData("", 5432)]
    public void Porta_Valida(string? valor, int esperado)
    {
        Assert.Equal(esperado, Ambiente.Config((ConexaoConfig.VarPorta, valor)).Porta);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("+5432")]
    [InlineData("54 32")]
    [InlineData("abc")]
    [InlineData("99999999999")]
    public void Porta_Invalida(string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Ambiente.Config((ConexaoConfig.VarPorta, valor)));

        Assert.Equal(ConexaoConfig.VarPorta, ex.NomeVariavel);
        Assert.DoesNotContain(valor, ex.Message.Replace("65535", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, SslMode.Prefer, true)]
    [InlineData("disable", SslMode.Disable, true)]
    [InlineData("Prefer", SslMode.Prefer, true)]
    [InlineData("REQUIRE", SslMode.Require, false)]
    [InlineData(" VerifyCA ", SslMode.VerifyCA, false)]
    [InlineData("verifyfull", SslMode.VerifyFull, false)]
    public void SslMode_Permitido(string? valor, SslMode esperado, bool inseguro)
    {
        var config = Ambiente.Config((ConexaoConfig.VarSslMode, valor));

        Assert.Equal(esperado, config.SslMode);
        Assert.Equal(inseguro, config.SslInseguro);
        Assert.Equal(inseguro, config.AvisoSsl is not null);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Allow")]
    [InlineData("Require;Trust Server Certificate=true")]
    public void SslMode_Invalido(string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Ambiente.Config((ConexaoConfig.VarSslMode, valor)));

        Assert.Equal(ConexaoConfig.VarSslMode, ex.NomeVariavel);
        Assert.DoesNotContain("Trust", ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(ConexaoConfig.VarUsuario, null)]
    [InlineData(ConexaoConfig.VarUsuario, "  ")]
    [InlineData(ConexaoConfig.VarSenha, null)]
    [InlineData(ConexaoConfig.VarSenha, "")]
    [InlineData(ConexaoConfig.VarSenha, "   ")]
    public void Obrigatorias(string nome, string? valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Ambiente.Config((nome, valor)));

        Assert.Equal(nome, ex.NomeVariavel);
        Assert.Equal($"Variavel de ambiente {nome}: obrigatoria e nao definida.", ex.Message);
    }

    [Fact]
    public void Carregar_LeitorNulo_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => ConexaoConfig.Carregar(null!));
    }

    [Fact]
    public void SenhaNuncaExposta_ExcetoNaConnectionString()
    {
        var log = new LogCaptura();
        var config = ConexaoConfig.Carregar(Ambiente.Variaveis((ConexaoConfig.VarSrid, "31983")), log);

        foreach (var texto in new[] { config.ToString(), config.Descricao(), config.Endpoint(), config.AvisoSsl ?? string.Empty, log.Tudo })
        {
            Assert.DoesNotContain(Sentinela, texto, StringComparison.Ordinal);
        }

        var cs = new NpgsqlConnectionStringBuilder(config.CriarConnectionString());
        Assert.Equal(Sentinela, cs.Password);
        Assert.False(cs.PersistSecurityInfo);
        Assert.False(cs.IncludeErrorDetail);
        Assert.Equal(ConexaoConfig.TimeoutConexaoSegundos, cs.Timeout);
        Assert.Equal(ConexaoConfig.TimeoutComandoSegundos, cs.CommandTimeout);
        Assert.Equal("TesteMetry", cs.ApplicationName);
        Assert.Equal("usuario_teste", cs.Username);
    }

    [Fact]
    public void ConnectionString_ValoresComPontoEVirgulaNaoInjetamChaves()
    {
        var config = Ambiente.Config(
            (ConexaoConfig.VarHost, "h;Include Error Detail=true"),
            (ConexaoConfig.VarBase, "b;Server Compatibility Mode=NoTypeLoading"));

        var cs = new NpgsqlConnectionStringBuilder(config.CriarConnectionString());

        Assert.Equal("h;Include Error Detail=true", cs.Host);
        Assert.Equal("b;Server Compatibility Mode=NoTypeLoading", cs.Database);
        Assert.False(cs.IncludeErrorDetail);
    }

    [Fact]
    public void Descricao_SanitizaControles()
    {
        var config = Ambiente.Config((ConexaoConfig.VarHost, "h\u001B[31m"), (ConexaoConfig.VarUsuario, "u\r\nx"));

        Assert.Equal("h?[31m:5432/casaos usuario=u??x srid=4326", config.Descricao());
        Assert.Equal("h?[31m:5432", config.Endpoint());
        Assert.Equal(config.Descricao(), config.ToString());
    }
}
