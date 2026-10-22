using ApiMapas.Infraestrutura.Configuracao;
using Npgsql;

namespace ApiMapas.Tests.Infraestrutura;

public class ConexaoConfigTests
{
    private const string SenhaFicticia = "S3nh@Fict!cia";

    private static Func<string, string?> Ambiente(params (string Nome, string? Valor)[] extras)
    {
        var d = new Dictionary<string, string?>
        {
            ["PG_USER"] = "usuario_teste",
            ["PG_PASSWORD"] = SenhaFicticia,
        };
        foreach (var (nome, valor) in extras)
        {
            d[nome] = valor;
        }

        return nome => d.TryGetValue(nome, out var v) ? v : null;
    }

    [Fact]
    public void Padroes_QuandoSoCredenciais()
    {
        var c = ConexaoConfig.Carregar(Ambiente());

        Assert.Equal("192.168.168.106", c.Host);
        Assert.Equal(5432, c.Porta);
        Assert.Equal("casaos", c.BaseDados);
        Assert.Equal(SslMode.Require, c.SslMode);
        Assert.False(c.SslInseguro);
    }

    [Theory]
    [InlineData("PG_USER", null)]
    [InlineData("PG_USER", "")]
    [InlineData("PG_USER", "   ")]
    [InlineData("PG_PASSWORD", null)]
    [InlineData("PG_PASSWORD", "")]
    [InlineData("PG_PASSWORD", " \t ")]
    public void CredencialFaltando_LancaCitandoSoONome(string variavel, string? valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => ConexaoConfig.Carregar(Ambiente((variavel, valor))));

        Assert.Equal(variavel, ex.Variavel);
        Assert.Contains(variavel, ex.Message, StringComparison.Ordinal);
        Assert.Contains("obrigatoria", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SenhaFicticia, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, SslMode.Require, false)]
    [InlineData("Require", SslMode.Require, false)]
    [InlineData("require", SslMode.Require, false)]
    [InlineData(" VerifyFull ", SslMode.VerifyFull, false)]
    [InlineData("verifyca", SslMode.VerifyCA, false)]
    [InlineData("Disable", SslMode.Disable, true)]
    [InlineData("PREFER", SslMode.Prefer, true)]
    public void SslMode_Allowlist(string? valor, SslMode esperado, bool inseguro)
    {
        var c = ConexaoConfig.Carregar(Ambiente(("PG_SSLMODE", valor)));

        Assert.Equal(esperado, c.SslMode);
        Assert.Equal(inseguro, c.SslInseguro);
    }

    [Theory]
    [InlineData("Allow")]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("true")]
    [InlineData("0")]
    [InlineData("Require;Password=x")]
    public void SslMode_ForaDaAllowlist_LancaSemEcoarValor(string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => ConexaoConfig.Carregar(Ambiente(("PG_SSLMODE", valor))));

        Assert.Equal("PG_SSLMODE", ex.Variavel);
        if (valor.Trim().Length > 1)
        {
            Assert.DoesNotContain(valor, ex.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("65535", 65535)]
    [InlineData(" 6543 ", 6543)]
    [InlineData("", 5432)]
    [InlineData(null, 5432)]
    public void Porta_Valida(string? valor, int esperada)
    {
        Assert.Equal(esperada, ConexaoConfig.Carregar(Ambiente(("PG_PORT", valor))).Porta);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("65536")]
    [InlineData("-1")]
    [InlineData("+5432")]
    [InlineData("abc")]
    [InlineData("123456")]
    [InlineData("54 32")]
    public void Porta_Invalida_Lanca(string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => ConexaoConfig.Carregar(Ambiente(("PG_PORT", valor))));
        Assert.Equal("PG_PORT", ex.Variavel);
    }

    [Fact]
    public void HostEBase_ComEspacos_SaoAparados()
    {
        var c = ConexaoConfig.Carregar(Ambiente(("PG_HOST", "  db.local "), ("PG_DATABASE", " outra ")));

        Assert.Equal("db.local", c.Host);
        Assert.Equal("outra", c.BaseDados);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Disable")]
    public void Descricao_SemUsuarioESemSenha(string? ssl)
    {
        var c = ConexaoConfig.Carregar(Ambiente(("PG_HOST", "h"), ("PG_PORT", "6000"), ("PG_DATABASE", "b"), ("PG_SSLMODE", ssl)));

        var descricao = c.Descricao();

        Assert.Equal($"h:6000/b ssl={ssl ?? "Require"}", descricao);
        Assert.Equal(descricao, c.ToString());
        Assert.DoesNotContain(SenhaFicticia, descricao, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario_teste", descricao, StringComparison.Ordinal);
    }

    [Fact]
    public void Descricao_SanitizaControlesNoHost()
    {
        var c = ConexaoConfig.Carregar(Ambiente(("PG_HOST", "h\nFALSO"), ("PG_DATABASE", "b\u0007")));

        Assert.Equal("h?FALSO:5432/b? ssl=Require", c.Descricao());
    }

    [Fact]
    public void ConnectionString_ParametrosDeSeguranca()
    {
        var c = ConexaoConfig.Carregar(Ambiente(("PG_SSLMODE", "VerifyFull")));

        var cs = new NpgsqlConnectionStringBuilder(c.CriarConnectionString());

        Assert.Equal("usuario_teste", cs.Username);
        Assert.Equal(SenhaFicticia, cs.Password);
        Assert.Equal(SslMode.VerifyFull, cs.SslMode);
        Assert.Equal(10, cs.Timeout);
        Assert.Equal(15, cs.CommandTimeout);
        Assert.Equal(20, cs.MaxPoolSize);
        Assert.Equal("ApiMapas", cs.ApplicationName);
        Assert.False(cs.PersistSecurityInfo);
        Assert.False(cs.IncludeErrorDetail);
    }

    [Fact]
    public void CriarDataSource_NaoAbreConexao_ENaoExpoeSenha()
    {
        var c = ConexaoConfig.Carregar(Ambiente(("PG_HOST", "127.0.0.1"), ("PG_PORT", "1")));

        using var ds = c.CriarDataSource();

        Assert.NotNull(ds);
        Assert.DoesNotContain(SenhaFicticia, ds.ConnectionString, StringComparison.Ordinal);
    }

    [Fact]
    public void Carregar_LeitorNulo_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => ConexaoConfig.Carregar(null!));
    }

    [Fact]
    public void Constantes()
    {
        Assert.Equal(SslMode.Require, ConexaoConfig.SslModePadrao);
        Assert.Equal(10, ConexaoConfig.TimeoutConexaoSegundos);
        Assert.Equal(15, ConexaoConfig.TimeoutComandoSegundos);
        Assert.Equal(20, ConexaoConfig.TamanhoMaximoPool);
    }
}

public class ConfiguracaoInvalidaExceptionTests
{
    [Fact]
    public void Construtores()
    {
        Assert.Null(new ConfiguracaoInvalidaException().Variavel);
        Assert.Equal("m", new ConfiguracaoInvalidaException("m").Message);
        var interna = new InvalidOperationException();
        Assert.Same(interna, new ConfiguracaoInvalidaException("m", interna).InnerException);
        var ex = new ConfiguracaoInvalidaException("PG_X", "motivo");
        Assert.Equal("PG_X", ex.Variavel);
        Assert.Equal("Configuracao invalida: PG_X motivo.", ex.Message);
    }
}

public class TextoSeguroTests
{
    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("abc", "abc")]
    [InlineData("a b", "a b")]
    [InlineData("a\r\nb", "a??b")]
    [InlineData("a\tb", "a?b")]
    public void Sanitizar_TrocaControles(string? entrada, string esperado)
    {
        Assert.Equal(esperado, TextoSeguro.Sanitizar(entrada));
    }

    [Theory]
    [InlineData(0x00A0, true)]  // NBSP
    [InlineData(0x2028, true)]  // separador de linha
    [InlineData(0x2029, true)]  // separador de paragrafo
    [InlineData(0x200B, true)]  // formato (zero width space)
    [InlineData(0xE000, true)]  // uso privado
    [InlineData(0xD800, true)]  // surrogate solto
    [InlineData(0x0378, true)]  // nao atribuido
    [InlineData(0x00E7, false)] // c cedilha
    [InlineData(0x0020, false)] // espaco comum
    public void Sanitizar_CaracteresUnicode(int codigo, bool trocado)
    {
        var entrada = "a" + (char)codigo + "b";
        Assert.Equal(trocado ? "a?b" : entrada, TextoSeguro.Sanitizar(entrada));
    }

    [Theory]
    [InlineData(3, "abc...")]
    [InlineData(0, "...")]
    [InlineData(-5, "...")]
    [InlineData(10, "abcdef")]
    public void Sanitizar_Trunca(int maximo, string esperado)
    {
        Assert.Equal(esperado, TextoSeguro.Sanitizar("abcdef", maximo));
    }

    [Fact]
    public void Sanitizar_PadraoTrunca200()
    {
        var r = TextoSeguro.Sanitizar(new string('x', 250));
        Assert.Equal(203, r.Length);
        Assert.EndsWith("...", r, StringComparison.Ordinal);
    }
}
