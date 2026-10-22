using Npgsql;
using TesteGraphy.Configuracao;
using TesteGraphy.Tests.Fakes;

namespace TesteGraphy.Tests.Configuracao;

public class ConexaoConfigTests
{
    private const string Sentinela = Ambiente.Sentinela;
    private const string Injecao = ";Password=x;Include Error Detail=true";

    [Fact]
    public void Carregar_SomenteObrigatorias_AplicaPadroes()
    {
        var config = Ambiente.Config();

        Assert.Equal(ConexaoConfig.HostPadrao, config.Host);
        Assert.Equal(ConexaoConfig.PortaPadrao, config.Porta);
        Assert.Equal(ConexaoConfig.BasePadrao, config.BaseDados);
        Assert.Equal("usuario_teste", config.Usuario);
        Assert.Equal(SslMode.Prefer, config.SslMode);
        Assert.True(config.SslInseguro);
    }

    [Fact]
    public void Carregar_ValoresInformados_ComEspacosNasPontas_SaoAparados()
    {
        var config = Ambiente.Config(
            (ConexaoConfig.VarHost, "  meu-host  "),
            (ConexaoConfig.VarBase, " base1 "),
            (ConexaoConfig.VarUsuario, " joao "));

        Assert.Equal("meu-host", config.Host);
        Assert.Equal("base1", config.BaseDados);
        Assert.Equal("joao", config.Usuario);
    }

    [Fact]
    public void Carregar_LeitorNulo_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => ConexaoConfig.Carregar(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Carregar_UsuarioAusenteOuVazio_ErroCitaSoONome(string? usuario)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(
            () => Ambiente.Config((ConexaoConfig.VarUsuario, usuario)));

        Assert.Equal(ConexaoConfig.VarUsuario, ex.NomeVariavel);
        Assert.Contains(ConexaoConfig.VarUsuario, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Carregar_SenhaAusenteOuVazia_ErroCitaSoONome(string? senha)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(
            () => Ambiente.Config((ConexaoConfig.VarSenha, senha)));

        Assert.Equal(ConexaoConfig.VarSenha, ex.NomeVariavel);
        Assert.Equal("Variavel de ambiente PG_PASSWORD: obrigatoria e nao definida.", ex.Message);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("abc")]
    [InlineData("+5432")]
    [InlineData("-1")]
    [InlineData("5432.0")]
    [InlineData("1e3")]
    [InlineData("99999999999")]
    [InlineData("5432;Password=x")]
    public void Carregar_PortaInvalida_ErroCitaSoONome(string porta)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(
            () => Ambiente.Config((ConexaoConfig.VarPorta, porta)));

        Assert.Equal(ConexaoConfig.VarPorta, ex.NomeVariavel);
        Assert.Equal("Variavel de ambiente PG_PORT: deve ser um numero inteiro entre 1 e 65535.", ex.Message);
        Assert.DoesNotContain(Sentinela, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("5433", 5433)]
    [InlineData("65535", 65535)]
    [InlineData(" 5432 ", 5432)]
    [InlineData("", ConexaoConfig.PortaPadrao)]
    [InlineData("  ", ConexaoConfig.PortaPadrao)]
    public void Carregar_PortaValida(string porta, int esperada)
    {
        var config = Ambiente.Config((ConexaoConfig.VarPorta, porta));

        Assert.Equal(esperada, config.Porta);
    }

    [Theory]
    [InlineData("Disable", SslMode.Disable, true)]
    [InlineData("disable", SslMode.Disable, true)]
    [InlineData("Prefer", SslMode.Prefer, true)]
    [InlineData("PREFER", SslMode.Prefer, true)]
    [InlineData("Require", SslMode.Require, false)]
    [InlineData("rEqUiRe", SslMode.Require, false)]
    [InlineData("VerifyCA", SslMode.VerifyCA, false)]
    [InlineData("verifyca", SslMode.VerifyCA, false)]
    [InlineData("VerifyFull", SslMode.VerifyFull, false)]
    [InlineData(" VERIFYFULL ", SslMode.VerifyFull, false)]
    public void Carregar_SslModePermitido_CaseInsensitive_AvisoSoParaDisableOuPrefer(string valor, SslMode esperado, bool aviso)
    {
        var config = Ambiente.Config((ConexaoConfig.VarSslMode, valor));

        Assert.Equal(esperado, config.SslMode);
        Assert.Equal(aviso, config.SslInseguro);
        if (aviso)
        {
            Assert.NotNull(config.AvisoSsl);
            Assert.Contains(esperado.ToString(), config.AvisoSsl, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentinela, config.AvisoSsl, StringComparison.Ordinal);
        }
        else
        {
            Assert.Null(config.AvisoSsl);
        }
    }

    [Theory]
    [InlineData("Allow")]
    [InlineData("allow")]
    [InlineData("Verify-Full")]
    [InlineData("true")]
    [InlineData("Require;Trust Server Certificate=true")]
    public void Carregar_SslModeNaoPermitido_ErroCitaSoONome(string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(
            () => Ambiente.Config((ConexaoConfig.VarSslMode, valor)));

        Assert.Equal(ConexaoConfig.VarSslMode, ex.NomeVariavel);
        Assert.DoesNotContain(valor, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, ex.Message, StringComparison.Ordinal);
    }

    /// <summary>
    /// Requisito SecBrain (caso 4): PG_SSLMODE vazio deve ser rejeitado.
    /// Comportamento atual: vazio/espacos sao tratados como "nao definido" e caem no padrao Prefer.
    /// </summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Carregar_SslModeVazio_DeveSerRejeitado(string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(
            () => Ambiente.Config((ConexaoConfig.VarSslMode, valor)));

        Assert.Equal(ConexaoConfig.VarSslMode, ex.NomeVariavel);
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Prefer")]
    [InlineData("Require")]
    [InlineData("VerifyCA")]
    [InlineData("VerifyFull")]
    public void Sentinela_NaoApareceEmDescricaoToStringEndpointNemLog(string ssl)
    {
        var log = new LogCaptura();
        var config = ConexaoConfig.Carregar(Ambiente.Variaveis((ConexaoConfig.VarSslMode, ssl)), log);

        Assert.DoesNotContain(Sentinela, config.Descricao(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, config.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, config.Endpoint(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, config.AvisoSsl ?? string.Empty, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, log.Tudo, StringComparison.Ordinal);
        Assert.NotEmpty(log.Passos);
    }

    [Theory]
    [InlineData(ConexaoConfig.VarUsuario, null)]
    [InlineData(ConexaoConfig.VarPorta, "abc")]
    [InlineData(ConexaoConfig.VarSslMode, "Allow")]
    public void Sentinela_NaoApareceEmConfiguracaoInvalidaException(string variavel, string? valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Ambiente.Config((variavel, valor)));

        Assert.DoesNotContain(Sentinela, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, ex.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void DescricaoEEndpoint_FormatoEsperado()
    {
        var config = Ambiente.Config((ConexaoConfig.VarHost, "h1"), (ConexaoConfig.VarPorta, "6000"), (ConexaoConfig.VarBase, "b1"));

        Assert.Equal("h1:6000/b1 (usuario: usuario_teste)", config.Descricao());
        Assert.Equal(config.Descricao(), config.ToString());
        Assert.Equal("h1:6000", config.Endpoint());
    }

    [Theory]
    [InlineData(ConexaoConfig.VarHost, "h\r\nx")]
    [InlineData(ConexaoConfig.VarHost, "h\u001b[2Jx")]
    [InlineData(ConexaoConfig.VarBase, "b\nx")]
    [InlineData(ConexaoConfig.VarBase, "b\u2028x")]
    [InlineData(ConexaoConfig.VarUsuario, "u\tx")]
    [InlineData(ConexaoConfig.VarUsuario, "u\u0085x")]
    public void DescricaoEEndpoint_ControlesSanitizados(string variavel, string valor)
    {
        var config = Ambiente.Config((variavel, valor));

        foreach (var texto in new[] { config.Descricao(), config.Endpoint(), config.ToString() })
        {
            Assert.DoesNotContain(texto, c => char.IsControl(c) || c is '\u2028' or '\u2029');
        }

        var alvo = variavel == ConexaoConfig.VarHost ? config.Endpoint() : config.Descricao();
        Assert.Contains('?', alvo);
    }

    [Theory]
    [InlineData("Disable", SslMode.Disable)]
    [InlineData("Prefer", SslMode.Prefer)]
    [InlineData("Require", SslMode.Require)]
    [InlineData("VerifyCA", SslMode.VerifyCA)]
    [InlineData("VerifyFull", SslMode.VerifyFull)]
    public void CriarConnectionString_ParametrosDeSeguranca(string ssl, SslMode esperado)
    {
        var config = Ambiente.Config((ConexaoConfig.VarSslMode, ssl));

        var cs = config.CriarConnectionString();
        var lida = new NpgsqlConnectionStringBuilder(cs);

        Assert.Contains("Persist Security Info=False", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Timeout=10", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Command Timeout=30", cs, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Application Name=TesteGraphy", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Include Error Detail", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("IncludeErrorDetail", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Trust Server Certificate", cs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TrustServerCertificate", cs, StringComparison.OrdinalIgnoreCase);

        Assert.False(lida.PersistSecurityInfo);
        Assert.Equal(ConexaoConfig.TimeoutConexaoSegundos, lida.Timeout);
        Assert.Equal(ConexaoConfig.TimeoutComandoSegundos, lida.CommandTimeout);
        Assert.Equal(ConexaoConfig.NomeAplicacao, lida.ApplicationName);
        Assert.Equal(esperado, lida.SslMode);
        Assert.False(lida.IncludeErrorDetail);
        Assert.Equal(Sentinela, lida.Password);
        Assert.Equal(ConexaoConfig.HostPadrao, lida.Host);
        Assert.Equal(ConexaoConfig.PortaPadrao, lida.Port);
        Assert.Equal(ConexaoConfig.BasePadrao, lida.Database);
    }

    [Fact]
    public void CriarConnectionString_SslModePadraoPrefer_ExplicitoNaString()
    {
        var cs = Ambiente.Config().CriarConnectionString();

        Assert.Equal(SslMode.Prefer, new NpgsqlConnectionStringBuilder(cs).SslMode);
    }

    [Theory]
    [InlineData(ConexaoConfig.VarHost)]
    [InlineData(ConexaoConfig.VarUsuario)]
    [InlineData(ConexaoConfig.VarBase)]
    public void CriarConnectionString_InjecaoEmVariavel_NaoGeraChavesExtras(string variavel)
    {
        var config = Ambiente.Config((variavel, "valor" + Injecao));
        var referencia = new NpgsqlConnectionStringBuilder(Ambiente.Config().CriarConnectionString());

        var lida = new NpgsqlConnectionStringBuilder(config.CriarConnectionString());

        Assert.Equal(referencia.Keys.Cast<string>().OrderBy(k => k), lida.Keys.Cast<string>().OrderBy(k => k));
        Assert.Equal(Sentinela, lida.Password);
        Assert.False(lida.IncludeErrorDetail);
        var campo = variavel switch
        {
            ConexaoConfig.VarHost => lida.Host,
            ConexaoConfig.VarUsuario => lida.Username,
            _ => lida.Database,
        };
        Assert.Equal("valor" + Injecao, campo);
    }

    [Fact]
    public void Descricao_ComInjecao_NaoContemSentinela()
    {
        var config = Ambiente.Config((ConexaoConfig.VarHost, "h" + Injecao), (ConexaoConfig.VarUsuario, "u" + Injecao));

        Assert.DoesNotContain(Sentinela, config.Descricao(), StringComparison.Ordinal);
    }
}
