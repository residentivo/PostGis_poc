using System.Net;
using System.Net.Sockets;
using System.Text;
using ApiMapas.Api.Endpoints;
using ApiMapas.Api.Seguranca;
using ApiMapas.Tests.Suporte;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Primitives;

namespace ApiMapas.Tests.Api;

/// <summary>
/// PO6.2 / PO6.6 - OrigemEscritaMiddleware (RS-22, CN-7): 403 em PUT e POST para cada uma das 3
/// condicoes, SEM consumir os limites; Sec-Fetch-Site ausente, none ou same-origin passa; o GET nao e
/// afetado; nada e ecoado; log Debug 4012 sem os valores dos cabecalhos.
/// </summary>
public class OrigemEscritaTests
{
    private const string Marcador = "MARCADOR-ORIGEM-QA";
    private const string Titulo403 = "Origem não permitida";
    private const string Detalhe403 = "Escrita aceita somente sem Origin e com Sec-Fetch-Site ausente, none ou same-origin.";

    /// <summary>Cenarios de recusa: nome -> cabecalhos (mais de um valor = cabecalho repetido).</summary>
    private static readonly Dictionary<string, (string Nome, string[] Valores)[]> Recusas = new(StringComparer.Ordinal)
    {
        ["origin-url"] = new[] { ("Origin", new[] { $"http://{Marcador}.example" }) },
        ["origin-localhost"] = new[] { ("Origin", new[] { "http://localhost" }) },
        ["origin-null"] = new[] { ("Origin", new[] { "null" }) },
        ["origin-com-sfs-none"] = new[] { ("Origin", new[] { "null" }), ("Sec-Fetch-Site", new[] { "none" }) },
        ["sfs-repetido"] = new[] { ("Sec-Fetch-Site", new[] { "none", "none" }) },
        ["sfs-repetido-misto"] = new[] { ("Sec-Fetch-Site", new[] { "same-origin", "cross-site" }) },
        ["sfs-virgula"] = new[] { ("Sec-Fetch-Site", new[] { "none, same-origin" }) },
        ["sfs-virgula-sem-espaco"] = new[] { ("Sec-Fetch-Site", new[] { "same-origin," }) },
        ["sfs-cross-site"] = new[] { ("Sec-Fetch-Site", new[] { "cross-site" }) },
        ["sfs-same-site"] = new[] { ("Sec-Fetch-Site", new[] { "same-site" }) },
        ["sfs-marcador"] = new[] { ("Sec-Fetch-Site", new[] { Marcador }) },
        ["sfs-none-prefixo"] = new[] { ("Sec-Fetch-Site", new[] { "nonex" }) },
    };

    private static readonly Dictionary<string, string> MotivoPorCenario = new(StringComparer.Ordinal)
    {
        ["origin-url"] = "cabecalho Origin presente",
        ["origin-localhost"] = "cabecalho Origin presente",
        ["origin-null"] = "cabecalho Origin presente",
        ["origin-com-sfs-none"] = "cabecalho Origin presente",
        ["sfs-repetido"] = "Sec-Fetch-Site com mais de um valor",
        ["sfs-repetido-misto"] = "Sec-Fetch-Site com mais de um valor",
        ["sfs-virgula"] = "Sec-Fetch-Site com mais de um valor",
        ["sfs-virgula-sem-espaco"] = "Sec-Fetch-Site com mais de um valor",
        ["sfs-cross-site"] = "Sec-Fetch-Site diferente de none/same-origin",
        ["sfs-same-site"] = "Sec-Fetch-Site diferente de none/same-origin",
        ["sfs-marcador"] = "Sec-Fetch-Site diferente de none/same-origin",
        ["sfs-none-prefixo"] = "Sec-Fetch-Site diferente de none/same-origin",
    };

    private static void Aplicar(HttpRequestMessage req, IEnumerable<(string Nome, string[] Valores)> cabecalhos)
    {
        foreach (var (nome, valores) in cabecalhos)
        {
            Assert.True(req.Headers.TryAddWithoutValidation(nome, valores), nome);
        }
    }

    public static TheoryData<string, string, string> CasosRecusa()
    {
        var dados = new TheoryData<string, string, string>();
        foreach (var cenario in Recusas.Keys)
        {
            dados.Add("POST", "/api/mapas/graphy", cenario);
            dados.Add("PUT", "/api/mapas/1/metry", cenario);
        }

        dados.Add("POST", "/api/mapas/metry", "origin-null");
        dados.Add("PUT", "/api/mapas/1/graphy", "sfs-repetido");
        return dados;
    }

    /// <summary>
    /// Origin vazio e Sec-Fetch-Site vazio ficam no OrigemEContentTypeKestrelTests (linha HTTP crua):
    /// o HttpClient do TestServer descarta cabecalho com valor vazio.
    /// CN-7: com 1 escrita/min e 2 req/min, cinco recusas 403 seguidas nao gastam cota: a escrita
    /// valida seguinte passa e so a outra recebe 429.
    /// </summary>
    [Theory]
    [MemberData(nameof(CasosRecusa))]
    public async Task Recusa403_SemConsumirLimites_SemEco_SemGravar(string metodo, string caminho, string cenario)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }
            .ComConfiguracao("ApiMapas:LimiteEscritasPorMinuto", "1")
            .ComConfiguracao("ApiMapas:LimiteRequisicoesPorMinuto", "2")
            .ComConfiguracao("Verboso", "true");
        using var cliente = f.CreateClient();
        var http = new HttpMethod(metodo);

        for (var i = 0; i < 5; i++)
        {
            var (r, corpo) = await Http.EnviarAsync(cliente, http, caminho, Http.Json(CorposPut.Valido), req => Aplicar(req, Recusas[cenario]));

            var json = Http.AssertProblem(r, corpo, HttpStatusCode.Forbidden, Titulo403, Detalhe403);
            Assert.Equal("https://tools.ietf.org/html/rfc9110#section-15.5.4", json.GetProperty("type").GetString());
            Assert.DoesNotContain(Marcador, corpo, StringComparison.Ordinal);
            Assert.DoesNotContain(r.Headers, h => h.Value.Any(v => v.Contains(Marcador, StringComparison.Ordinal)));
            Assert.Null(r.Headers.RetryAfter);
            Assert.Null(r.Headers.Location);
        }

        Assert.Empty(repo.Chamadas);

        var (valida, _) = await Http.EnviarAsync(cliente, http, caminho, Http.Json(CorposPut.Valido));
        var (excedente, _) = await Http.EnviarAsync(cliente, http, caminho, Http.Json(CorposPut.Valido));

        Assert.True(valida.IsSuccessStatusCode, $"a recusa 403 consumiu limite: {(int)valida.StatusCode}");
        Assert.Equal(HttpStatusCode.TooManyRequests, excedente.StatusCode);
        Assert.Single(repo.Chamadas);

        var recusas = f.Logs.Mensagens.Where(m => m.Contains("recusada pelo filtro de origem", StringComparison.Ordinal)).ToList();
        Assert.Equal(5, recusas.Count);
        Assert.All(recusas, m =>
        {
            Assert.StartsWith("Debug", m, StringComparison.Ordinal);
            Assert.Contains($"Escrita {metodo} recusada", m, StringComparison.Ordinal);
            Assert.Contains(MotivoPorCenario[cenario], m, StringComparison.Ordinal);
        });
        Assert.DoesNotContain(Marcador, f.TextoLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("cross-site", f.TextoLogs, StringComparison.Ordinal);
        Assert.DoesNotContain("same-site", f.TextoLogs, StringComparison.Ordinal);
    }

    public static TheoryData<string?> SecFetchSiteAceitos => new()
    {
        null,
        "none",
        "same-origin",
        "NONE",
        "Same-Origin",
        "  same-origin  ",
        "\tnone",
    };

    [Theory]
    [MemberData(nameof(SecFetchSiteAceitos))]
    public async Task SemOrigin_SecFetchSiteAusenteNoneOuSameOrigin_Passa(string? secFetchSite)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("Verboso", "true");
        using var cliente = f.CreateClient();
        void Ajustar(HttpRequestMessage req)
        {
            if (secFetchSite is not null)
            {
                req.Headers.TryAddWithoutValidation("Sec-Fetch-Site", secFetchSite);
            }
        }

        var (post, _) = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/mapas/metry", Http.Json(CorposPut.Valido), Ajustar);
        var (put, _) = await Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/2/graphy", Http.Json(CorposPut.Valido), Ajustar);

        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        Assert.Equal(2, repo.Chamadas.Count);
        Assert.Equal(2, f.Logs.Mensagens.Count(m => m.Contains("filtro de origem aprovou", StringComparison.Ordinal)));
    }

    [Theory]
    [InlineData("origin-url")]
    [InlineData("sfs-cross-site")]
    [InlineData("sfs-repetido")]
    public async Task Get_NaoAfetadoPeloFiltroDeOrigem(string cenario)
    {
        await using var f = new ApiEscritaFactory { Repositorio = RepositorioEscritaFalso.Sucesso() }.ComConfiguracao("Verboso", "true");
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Get, "/api/mapas/1/graphy", ajustar: req => Aplicar(req, Recusas[cenario]));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("filtro de origem", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Recusa403_AntesDe415E413_NaoLeOCorpo()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteBytesCorpoPut", "10");
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/mapas/graphy", Http.Json(CorposPut.Valido, "text/plain"),
            req => req.Headers.Add("Origin", "http://x.example"));
        var (rPut, _) = await Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/abc/graphy", Http.Json("{", "text/plain"),
            req => req.Headers.Add("Origin", "http://x.example"));

        Http.AssertProblem(r, corpo, HttpStatusCode.Forbidden, Titulo403);
        Assert.Equal(HttpStatusCode.Forbidden, rPut.StatusCode);
        Assert.Empty(repo.Chamadas);
    }

    // ---------------- Unitarios ----------------

    public static TheoryData<string[]?, string[]?, string?> Motivos => new()
    {
        { null, null, null },
        { null, new[] { "none" }, null },
        { null, new[] { " SAME-ORIGIN " }, null },
        { new[] { "" }, null, "cabecalho Origin presente" },
        { new[] { "null" }, new[] { "none" }, "cabecalho Origin presente" },
        { null, new[] { "none", "none" }, "Sec-Fetch-Site com mais de um valor" },
        { null, new[] { "none,none" }, "Sec-Fetch-Site com mais de um valor" },
        { null, Array.Empty<string>(), null }, // StringValues vazio = cabecalho ausente
        { null, new[] { "" }, "Sec-Fetch-Site diferente de none/same-origin" },
        { null, new[] { "cross-site" }, "Sec-Fetch-Site diferente de none/same-origin" },
        { null, new[] { "same-site" }, "Sec-Fetch-Site diferente de none/same-origin" },
    };

    [Theory]
    [MemberData(nameof(Motivos))]
    public void MotivoRecusa_Unitario(string[]? origin, string[]? secFetchSite, string? esperado)
    {
        var cabecalhos = new HeaderDictionary();
        if (origin is not null)
        {
            cabecalhos["Origin"] = new StringValues(origin);
        }

        if (secFetchSite is not null)
        {
            cabecalhos[OrigemEscritaMiddleware.CabecalhoSecFetchSite] = new StringValues(secFetchSite);
        }

        Assert.Equal(esperado, OrigemEscritaMiddleware.MotivoRecusa(cabecalhos));
    }

    [Fact]
    public void MotivoRecusa_ValorNulo_MaisDeUmValor()
    {
        var cabecalhos = new HeaderDictionary { [OrigemEscritaMiddleware.CabecalhoSecFetchSite] = new StringValues(new string?[] { null }) };
        Assert.Equal("Sec-Fetch-Site com mais de um valor", OrigemEscritaMiddleware.MotivoRecusa(cabecalhos));
    }

    [Fact]
    public async Task Middleware_ArgumentosNulos_Lancam()
    {
        Assert.Throws<ArgumentNullException>(() => new OrigemEscritaMiddleware(null!));
        Assert.Throws<ArgumentNullException>(() => OrigemEscritaMiddleware.MotivoRecusa(null!));
        var m = new OrigemEscritaMiddleware(_ => Task.CompletedTask);
        await Assert.ThrowsAsync<ArgumentNullException>(() => m.InvokeAsync(null!));
    }

    [Fact]
    public async Task Middleware_SemEndpoint_PassaSemOlharCabecalhos()
    {
        var chamou = false;
        var m = new OrigemEscritaMiddleware(_ =>
        {
            chamou = true;
            return Task.CompletedTask;
        });
        var contexto = new DefaultHttpContext();
        contexto.Request.Headers.Origin = "http://evil.example";

        await m.InvokeAsync(contexto);

        Assert.True(chamou);
        Assert.Equal(200, contexto.Response.StatusCode);
    }

    [Fact]
    public void MarcadorEscrita_Singleton()
    {
        Assert.Same(MarcadorEscritaMapas.Instancia, MarcadorEscritaMapas.Instancia);
    }
}

/// <summary>
/// PO6.6 / RS-6 - Kestrel real em 127.0.0.1: cabecalhos repetidos na linha do HTTP/1.1 (o
/// HttpClient junta valores; aqui cada cabecalho vai numa linha propria): Sec-Fetch-Site repetido e
/// Origin vazio respondem 403; Content-Type duplicado responde 415 no POST.
/// </summary>
[Collection(ColecaoKestrel.Nome)]
public class OrigemEContentTypeKestrelTests
{
    private static (ApiEscritaFactory Fabrica, RepositorioEscritaFalso Repo, int Porta) Subir()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var f = new ApiEscritaFactory { Repositorio = repo };
        f.UseKestrel(0);
        f.StartServer();
        var endereco = new Uri(f.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses.First());
        return (f, repo, endereco.Port);
    }

    private static async Task<string> EnviarCruAsync(int porta, string requisicao)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, porta);
        await socket.SendAsync(Encoding.UTF8.GetBytes(requisicao));
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        var resposta = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            int n;
            while ((n = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token)) > 0)
            {
                resposta.Write(buffer, 0, n);
                var texto = Encoding.UTF8.GetString(resposta.ToArray());
                var fim = texto.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (fim > 0 && (texto.EndsWith('}') || texto.StartsWith("HTTP/1.1 201", StringComparison.Ordinal)))
                {
                    break;
                }
            }
        }
        catch (Exception ex) when (ex is SocketException or OperationCanceledException)
        {
        }

        return Encoding.UTF8.GetString(resposta.ToArray());
    }

    private static string Requisicao(string metodo, string caminho, string cabecalhosExtras, string contentTypes = "Content-Type: application/json\r\n")
    {
        var corpo = CorposPut.Valido;
        return $"{metodo} {caminho} HTTP/1.1\r\nHost: localhost\r\n{contentTypes}{cabecalhosExtras}Content-Length: {Encoding.UTF8.GetByteCount(corpo)}\r\nConnection: close\r\n\r\n{corpo}";
    }

    [Theory]
    [InlineData("POST", "/api/mapas/graphy", "Sec-Fetch-Site: none\r\nSec-Fetch-Site: none\r\n")]
    [InlineData("PUT", "/api/mapas/1/metry", "Sec-Fetch-Site: same-origin\r\nSec-Fetch-Site: same-origin\r\n")]
    [InlineData("POST", "/api/mapas/metry", "Origin:\r\n")]
    [InlineData("PUT", "/api/mapas/1/graphy", "Origin: \r\n")]
    [InlineData("POST", "/api/mapas/graphy", "Origin: null\r\n")]
    [InlineData("POST", "/api/mapas/metry", "Sec-Fetch-Site:\r\n")]
    [InlineData("POST", "/api/mapas/metry", "Sec-Fetch-Site: none, same-origin\r\n")]
    public async Task CabecalhoRepetidoOuVazio_403_NaoGrava(string metodo, string caminho, string extras)
    {
        var (f, repo, porta) = Subir();
        await using var _ = f;

        var resposta = await EnviarCruAsync(porta, Requisicao(metodo, caminho, extras));

        Assert.True(resposta.StartsWith("HTTP/1.1 403", StringComparison.Ordinal), resposta);
        Assert.Contains("Origem não permitida", resposta, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
    }

    [Theory]
    [InlineData("/api/mapas/graphy")]
    [InlineData("/api/mapas/metry")]
    public async Task ContentTypeDuplicado_Post415_NaoGrava(string caminho)
    {
        var (f, repo, porta) = Subir();
        await using var _ = f;

        var resposta = await EnviarCruAsync(porta, Requisicao("POST", caminho, string.Empty,
            "Content-Type: application/json\r\nContent-Type: application/json\r\n"));

        Assert.True(resposta.StartsWith("HTTP/1.1 415", StringComparison.Ordinal), resposta);
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task Post201_PeloKestrel_ComSecFetchSiteNone()
    {
        var (f, repo, porta) = Subir();
        await using var _ = f;

        var resposta = await EnviarCruAsync(porta, Requisicao("POST", "/api/mapas/graphy", "Sec-Fetch-Site: none\r\n"));

        Assert.True(resposta.StartsWith("HTTP/1.1 201", StringComparison.Ordinal), resposta);
        Assert.Contains($"Location: /api/mapas/{RepositorioEscritaFalso.IdCriado}/graphy", resposta, StringComparison.Ordinal);
        Assert.Single(repo.Chamadas);
    }
}

/// <summary>Serializa os testes que sobem Kestrel real (evita corrida na partida do servidor).</summary>
[CollectionDefinition(Nome, DisableParallelization = true)]
public sealed class ColecaoKestrel
{
    public const string Nome = "Kestrel real";
}
