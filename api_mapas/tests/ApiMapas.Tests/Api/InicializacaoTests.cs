using ApiMapas.Api;
using ApiMapas.Api.Inicializacao;
using ApiMapas.Infraestrutura.Configuracao;
using Microsoft.Extensions.Configuration;

namespace ApiMapas.Tests.Api;

public class ValidadorEnderecosLoopbackTests
{
    [Theory]
    [InlineData("http://localhost:5080")]
    [InlineData("https://LOCALHOST:7080")]
    [InlineData("http://127.0.0.1:5080")]
    [InlineData("http://127.0.0.2:5080")]
    [InlineData("http://[::1]:5080")]
    [InlineData("http://localhost")]
    [InlineData("http://unix:///tmp/api.sock")]
    [InlineData("http://[::ffff:127.0.0.1]:5080")]
    public void EhLoopback_Aceita(string url)
    {
        Assert.True(ValidadorEnderecosLoopback.EhLoopback(url));
    }

    [Theory]
    [InlineData("http://0.0.0.0:5080")]
    [InlineData("http://*:5080")]
    [InlineData("http://+:5080")]
    [InlineData("http://[::]:5080")]
    [InlineData("http://192.168.0.10:5080")]
    [InlineData("http://192.168.168.106:5080")]
    [InlineData("http://example.com:5080")]
    [InlineData("http://localhost.evil.com:5080")]
    [InlineData("http://[::ffff:192.168.0.1]:5080")]
    [InlineData("lixo")]
    [InlineData("localhost:5080")]
    public void EhLoopback_Recusa(string url)
    {
        Assert.False(ValidadorEnderecosLoopback.EhLoopback(url));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("   ", null)]
    [InlineData("http://localhost:5080", null)]
    [InlineData("http://localhost:5080; http://127.0.0.1:5081 ;", null)]
    [InlineData("http://localhost:5080;http://0.0.0.0:5081", "http://0.0.0.0:5081")]
    [InlineData("http://*:1;http://0.0.0.0:2", "http://*:1")]
    public void PrimeiraNaoLoopback(string? urls, string? esperada)
    {
        Assert.Equal(esperada, ValidadorEnderecosLoopback.PrimeiraNaoLoopback(urls));
    }
}

public class ConfiguracaoApiTests
{
    private static IConfiguration Config(params (string Chave, string? Valor)[] pares) =>
        new ConfigurationBuilder().AddInMemoryCollection(pares.Select(p => new KeyValuePair<string, string?>(p.Chave, p.Valor))).Build();

    [Fact]
    public void Padroes()
    {
        var c = ConfiguracaoApi.Carregar(Config());

        Assert.False(c.Verboso);
        Assert.Equal(5L * 1024 * 1024, c.LimiteBytesGeometrias);
        Assert.Equal(60, c.LimiteRequisicoesPorMinuto);
        Assert.Equal(c.LimiteBytesGeometrias, c.CriarOpcoesConsulta().LimiteBytesGeometrias);
        Assert.Equal("http://localhost:5080", ConfiguracaoApi.UrlPadrao);
    }

    [Theory]
    [InlineData("true", true)]
    [InlineData("True", true)]
    [InlineData("false", false)]
    [InlineData("", false)]
    public void Verboso_Valido(string valor, bool esperado)
    {
        Assert.Equal(esperado, ConfiguracaoApi.Carregar(Config(("Verboso", valor))).Verboso);
    }

    [Theory]
    [InlineData("sim")]
    [InlineData("1")]
    public void Verboso_Invalido_Lanca(string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => ConfiguracaoApi.Carregar(Config(("Verboso", valor))));
        Assert.Equal("Verboso", ex.Variavel);
    }

    [Theory]
    [InlineData("ApiMapas:LimiteBytesGeometrias", "0")]
    [InlineData("ApiMapas:LimiteBytesGeometrias", "-1")]
    [InlineData("ApiMapas:LimiteBytesGeometrias", "abc")]
    [InlineData("ApiMapas:LimiteBytesGeometrias", "1.5")]
    [InlineData("ApiMapas:LimiteRequisicoesPorMinuto", "0")]
    [InlineData("ApiMapas:LimiteRequisicoesPorMinuto", "-10")]
    [InlineData("ApiMapas:LimiteRequisicoesPorMinuto", "2147483648")]
    [InlineData("ApiMapas:LimiteRequisicoesPorMinuto", "99999999999999999999")]
    public void Limites_Invalidos_LancamCitandoAChave(string chave, string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => ConfiguracaoApi.Carregar(Config((chave, valor))));
        Assert.Equal(chave, ex.Variavel);
    }

    [Theory]
    [InlineData("1024", "10", 1024, 10)]
    [InlineData(" 2048 ", "2147483647", 2048, int.MaxValue)]
    public void Limites_Validos(string bytes, string req, long esperadoBytes, int esperadoReq)
    {
        var c = ConfiguracaoApi.Carregar(Config(("ApiMapas:LimiteBytesGeometrias", bytes), ("ApiMapas:LimiteRequisicoesPorMinuto", req)));

        Assert.Equal(esperadoBytes, c.LimiteBytesGeometrias);
        Assert.Equal(esperadoReq, c.LimiteRequisicoesPorMinuto);
    }

    [Fact]
    public void Carregar_ConfiguracaoNula_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => ConfiguracaoApi.Carregar(null!));
    }
}

/// <summary>Program.CriarAplicacao: recusa subir fora do loopback e sem credenciais (RA-5/RA-8/RA-9).</summary>
public class ProgramInicializacaoTests
{
    private static string? Credenciais(string nome) => nome switch
    {
        "PG_USER" => "u_ficticio",
        "PG_PASSWORD" => "senha_ficticia",
        "PG_HOST" => "127.0.0.1",
        "PG_PORT" => "1",
        _ => null,
    };

    [Theory]
    [InlineData("--urls=http://0.0.0.0:5080", "urls/ASPNETCORE_URLS")]
    [InlineData("--urls=http://*:5080", "urls/ASPNETCORE_URLS")]
    [InlineData("--urls=http://localhost:5080;http://192.168.0.5:5080", "urls/ASPNETCORE_URLS")]
    [InlineData("--Kestrel:Endpoints:Http:Url=http://0.0.0.0:5000", "Kestrel:Endpoints")]
    [InlineData("--http_ports=8080", "HTTP_PORTS/HTTPS_PORTS")]
    [InlineData("--https_ports=8443", "HTTP_PORTS/HTTPS_PORTS")]
    [InlineData("--Verboso=talvez", "Verboso")]
    [InlineData("--ApiMapas:LimiteBytesGeometrias=0", "ApiMapas:LimiteBytesGeometrias")]
    public void CriarAplicacao_ConfiguracaoInvalida_Lanca(string arg, string variavel)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Program.CriarAplicacao(new[] { arg }, Credenciais));
        Assert.Equal(variavel, ex.Variavel);
    }

    [Theory]
    [InlineData("PG_USER")]
    [InlineData("PG_PASSWORD")]
    public void CriarAplicacao_SemCredencial_Lanca(string faltando)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() =>
            Program.CriarAplicacao(Array.Empty<string>(), n => n == faltando ? null : Credenciais(n)));
        Assert.Equal(faltando, ex.Variavel);
    }

    [Theory]
    [InlineData("")]
    [InlineData("--urls=http://localhost:5099;http://127.0.0.1:5098|--Verboso=true")]
    [InlineData("--Kestrel:Endpoints:Http:Url=http://[::1]:5097")]
    public async Task CriarAplicacao_ConfiguracaoValida_MontaSemConectar(string argsJuntos)
    {
        var args = argsJuntos.Split('|', StringSplitOptions.RemoveEmptyEntries);
        await using var app = Program.CriarAplicacao(args, Credenciais);
        Assert.NotNull(app);
    }

    [Fact]
    public async Task CriarAplicacao_SslInseguro_Monta()
    {
        await using var app = Program.CriarAplicacao(Array.Empty<string>(), n => n == "PG_SSLMODE" ? "Disable" : Credenciais(n));
        Assert.NotNull(app);
    }

    [Fact]
    public void Main_ConfiguracaoInvalida_DevolveCodigo1()
    {
        var erroOriginal = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var codigo = Program.Main(new[] { "--urls=http://0.0.0.0:5080" });

            Assert.Equal(Program.CodigoSaidaConfiguracao, codigo);
            Assert.Contains("[ApiMapas] A API nao foi iniciada.", sw.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(erroOriginal);
        }
    }
}
