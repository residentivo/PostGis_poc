using System.Net;
using ApiMapas.Api;
using ApiMapas.Api.Inicializacao;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Tests.Suporte;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;

namespace ApiMapas.Tests.Api;

/// <summary>
/// P6.3 / CP-11 - Program.CriarAplicacao com a escrita: saida com codigo 1 (ConfiguracaoInvalidaException)
/// e PUT mapeado ou nao, sem acessar banco (PG_HOST 127.0.0.1:1).
/// </summary>
public class ProgramEscritaTests
{
    private static Func<string, string?> Ambiente(params (string Nome, string? Valor)[] extras)
    {
        var mapa = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PG_USER"] = "u_leitura_ficticio",
            ["PG_PASSWORD"] = "senha_leitura_ficticia",
            ["PG_HOST"] = "127.0.0.1",
            ["PG_PORT"] = "1",
        };
        foreach (var (nome, valor) in extras)
        {
            mapa[nome] = valor;
        }

        return n => mapa.TryGetValue(n, out var v) ? v : null;
    }

    private static IEnumerable<string> RotasPut(WebApplication app) =>
        ((IEndpointRouteBuilder)app).DataSources
            .SelectMany(d => d.Endpoints)
            .OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<Microsoft.AspNetCore.Routing.HttpMethodMetadata>()?.HttpMethods.Contains("PUT") == true)
            .Select(e => e.RoutePattern.RawText!);

    public static TheoryData<string?, string?, string> SaidasCodigo1 => new()
    {
        { "api_mapas_escrita", null, "PG_ESCRITA_PASSWORD" },
        { null, "senha_escrita_ficticia", "PG_ESCRITA_USER" },
        { "u_leitura_ficticio", "senha_escrita_ficticia", "PG_ESCRITA_USER" },
        { " U_LEITURA_FICTICIO ", "senha_escrita_ficticia", "PG_ESCRITA_USER" },
        { "api_mapas_escrita", "senha_leitura_ficticia", "PG_ESCRITA_PASSWORD" },
    };

    [Theory]
    [MemberData(nameof(SaidasCodigo1))]
    public void CriarAplicacao_EscritaMalConfigurada_Lanca(string? usuario, string? senha, string variavel)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() =>
            Program.CriarAplicacao(Array.Empty<string>(), Ambiente(("PG_ESCRITA_USER", usuario), ("PG_ESCRITA_PASSWORD", senha))));

        Assert.Equal(variavel, ex.Variavel);
        Assert.DoesNotContain("senha_", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("u_leitura_ficticio", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("--ApiMapas:LimiteBytesCorpoPut=0", "ApiMapas:LimiteBytesCorpoPut")]
    [InlineData("--ApiMapas:LimiteBytesCorpoPut=1048577", "ApiMapas:LimiteBytesCorpoPut")]
    [InlineData("--ApiMapas:LimiteBytesCorpoPut=-1", "ApiMapas:LimiteBytesCorpoPut")]
    [InlineData("--ApiMapas:LimiteBytesCorpoPut=1MB", "ApiMapas:LimiteBytesCorpoPut")]
    [InlineData("--ApiMapas:LimiteEscritasPorMinuto=0", "ApiMapas:LimiteEscritasPorMinuto")]
    [InlineData("--ApiMapas:LimiteEscritasPorMinuto=61", "ApiMapas:LimiteEscritasPorMinuto")]
    [InlineData("--ApiMapas:LimiteEscritasPorMinuto=dez", "ApiMapas:LimiteEscritasPorMinuto")]
    public void CriarAplicacao_LimitesDoPutForaDaFaixa_Lanca(string arg, string chave)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Program.CriarAplicacao(new[] { arg }, Ambiente()));
        Assert.Equal(chave, ex.Variavel);
    }

    [Fact]
    public void Main_EscritaMalConfigurada_DevolveCodigo1()
    {
        var erroOriginal = Console.Error;
        using var sw = new StringWriter();
        Console.SetError(sw);
        try
        {
            var codigo = Program.Main(new[] { "--ApiMapas:LimiteEscritasPorMinuto=999" });

            Assert.Equal(1, codigo);
            Assert.Contains("ApiMapas:LimiteEscritasPorMinuto", sw.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetError(erroOriginal);
        }
    }

    [Fact]
    public async Task CriarAplicacao_SemVariaveisDeEscrita_PutNaoMapeado()
    {
        await using var app = Program.CriarAplicacao(Array.Empty<string>(), Ambiente());
        Assert.Empty(RotasPut(app));
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Prefer")]
    public async Task CriarAplicacao_SslInseguro_PutNaoMapeado(string ssl)
    {
        await using var app = Program.CriarAplicacao(Array.Empty<string>(),
            Ambiente(("PG_SSLMODE", ssl), ("PG_ESCRITA_USER", "api_mapas_escrita"), ("PG_ESCRITA_PASSWORD", "senha_escrita_ficticia")));
        Assert.Empty(RotasPut(app));
    }

    /// <summary>R-1 real: o banco de escrita nao responde (127.0.0.1:1) -> a API sobe e o PUT nao e mapeado.</summary>
    [Fact]
    public async Task CriarAplicacao_R1BancoSemResposta_ApiSobe_PutNaoMapeado()
    {
        await using var app = Program.CriarAplicacao(Array.Empty<string>(),
            Ambiente(("PG_ESCRITA_USER", "api_mapas_escrita"), ("PG_ESCRITA_PASSWORD", "senha_escrita_ficticia")));
        Assert.Empty(RotasPut(app));
        Assert.Equal(15, Program.PrazoVerificacaoRoleSegundos);
    }

    [Fact]
    public async Task ApiEscritaFactory_EscritaHabilitada_MapeiaAsRotasPutEPost()
    {
        await using var f = new ApiEscritaFactory { Repositorio = RepositorioEscritaFalso.Sucesso() };
        using var cliente = f.CreateClient();
        var r = await cliente.PutAsync("/api/mapas/1/graphy", Http.Json(CorposPut.Valido));

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        var fontes = f.Services.GetService(typeof(EndpointDataSource)) as EndpointDataSource;
        var rotas = fontes!.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("PUT") == true)
            .Select(e => e.RoutePattern.RawText).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "/api/mapas/{id}/graphy", "/api/mapas/{id}/metry" }, rotas);
        var post = fontes.Endpoints.OfType<RouteEndpoint>()
            .Where(e => e.Metadata.GetMetadata<HttpMethodMetadata>()?.HttpMethods.Contains("POST") == true)
            .Select(e => e.RoutePattern.RawText).OrderBy(x => x).ToList();
        Assert.Equal(new[] { "/api/mapas/graphy", "/api/mapas/metry" }, post);
    }
}

/// <summary>ConfiguracaoApi - chaves novas do PUT (RP-6).</summary>
public class ConfiguracaoApiEscritaTests
{
    private static IConfiguration Config(params (string Chave, string? Valor)[] pares) =>
        new ConfigurationBuilder().AddInMemoryCollection(pares.Select(p => new KeyValuePair<string, string?>(p.Chave, p.Valor))).Build();

    [Fact]
    public void Padroes_1MiB_E_10PorMinuto()
    {
        var c = ConfiguracaoApi.Carregar(Config());

        Assert.Equal(65_536, c.LimiteBytesCorpoPut);
        Assert.Equal(10, c.LimiteEscritasPorMinuto);
        Assert.Equal(65_536, c.CriarOpcoesAtualizacao().LimiteBytesCorpo);
        Assert.Equal(TimeSpan.FromSeconds(30), c.CriarOpcoesAtualizacao().PrazoTotal);
    }

    [Theory]
    [InlineData("1", "1", 1, 1)]
    [InlineData("1048576", "60", 1_048_576, 60)]
    [InlineData(" 2048 ", " 5 ", 2048, 5)]
    [InlineData("", "", 65_536, 10)]
    public void Limites_NaFaixa(string bytes, string escritas, long esperadoBytes, int esperadoEscritas)
    {
        var c = ConfiguracaoApi.Carregar(Config(("ApiMapas:LimiteBytesCorpoPut", bytes), ("ApiMapas:LimiteEscritasPorMinuto", escritas)));

        Assert.Equal(esperadoBytes, c.LimiteBytesCorpoPut);
        Assert.Equal(esperadoEscritas, c.LimiteEscritasPorMinuto);
    }

    [Theory]
    [InlineData("ApiMapas:LimiteBytesCorpoPut", "0")]
    [InlineData("ApiMapas:LimiteBytesCorpoPut", "1048577")]
    [InlineData("ApiMapas:LimiteBytesCorpoPut", "+5")]
    [InlineData("ApiMapas:LimiteBytesCorpoPut", "1e3")]
    [InlineData("ApiMapas:LimiteBytesCorpoPut", "99999999999999999999")]
    [InlineData("ApiMapas:LimiteEscritasPorMinuto", "0")]
    [InlineData("ApiMapas:LimiteEscritasPorMinuto", "61")]
    [InlineData("ApiMapas:LimiteEscritasPorMinuto", "-1")]
    [InlineData("ApiMapas:LimiteEscritasPorMinuto", "10.5")]
    public void Limites_ForaDaFaixa_LancamCitandoAChave(string chave, string valor)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => ConfiguracaoApi.Carregar(Config((chave, valor))));

        Assert.Equal(chave, ex.Variavel);
        Assert.DoesNotContain(valor.Trim(), ex.Message.Replace("1048576", string.Empty, StringComparison.Ordinal).Replace(" 60", string.Empty, StringComparison.Ordinal).Replace(" 1 ", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
