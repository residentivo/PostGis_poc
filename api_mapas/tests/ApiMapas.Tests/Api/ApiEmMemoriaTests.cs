using System.Net;
using System.Text.Json;
using ApiMapas.Dominio;
using ApiMapas.Tests.Suporte;

namespace ApiMapas.Tests.Api;

/// <summary>API em memoria (WebApplicationFactory + repositorio falso). Nenhum acesso a banco.</summary>
public class ApiEmMemoriaTests
{
    private static readonly string[] TermosProibidos =
    {
        "Npgsql", "SqlState", "password", "Password", "senha", "SenhaFicticia", "usuario_ficticio", "StackTrace",
        "   at ", "Exception", "192.168.", "127.0.0.1", "SEGREDO",
    };

    private static async Task<(HttpResponseMessage Resposta, string Corpo)> GetAsync(ApiFactory f, string caminho, HttpMethod? metodo = null)
    {
        using var cliente = f.CreateClient();
        var resposta = await cliente.SendAsync(new HttpRequestMessage(metodo ?? HttpMethod.Get, caminho));
        return (resposta, await resposta.Content.ReadAsStringAsync());
    }

    private static void AssertCabecalhosSeguranca(HttpResponseMessage r)
    {
        Assert.Equal("nosniff", Assert.Single(r.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.False(r.Headers.Contains("Server"));
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
    }

    private static void AssertSemVazamento(string corpo)
    {
        foreach (var termo in TermosProibidos)
        {
            Assert.DoesNotContain(termo, corpo, StringComparison.Ordinal);
        }
    }

    private static JsonElement AssertProblem(HttpResponseMessage r, string corpo, HttpStatusCode status)
    {
        Assert.Equal(status, r.StatusCode);
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var json = JsonDocument.Parse(corpo).RootElement;
        Assert.Equal((int)status, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("title").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("traceId").GetString()));
        AssertCabecalhosSeguranca(r);
        AssertSemVazamento(corpo);
        return json;
    }

    // ---------------- 200 ----------------

    [Theory]
    [InlineData("graphy", ColunaMapa.Graphy)]
    [InlineData("metry", ColunaMapa.Metry)]
    public async Task Get200_JsonCamelCase_GeojsonObjeto(string coluna, ColunaMapa enumColuna)
    {
        var repo = new RepositorioFalso(new[] { Geometrias.Ponto() });
        await using var f = new ApiFactory(repo);

        var (r, corpo) = await GetAsync(f, $"/api/mapas/123/{coluna}");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal("application/json", r.Content.Headers.ContentType?.MediaType);
        AssertCabecalhosSeguranca(r);
        var json = JsonDocument.Parse(corpo).RootElement;

        // D-N7 / Q2: objeto unico, sem quantidade, truncado nem lista.
        Assert.Equal(new[] { "id", "coluna", "tipo", "srid", "wkt", "geojson", "x", "y", "longitude", "latitude" }, json.EnumerateObject().Select(p => p.Name));
        Assert.Equal(123, json.GetProperty("id").GetInt32());
        Assert.Equal(coluna, json.GetProperty("coluna").GetString());
        Assert.Equal(JsonValueKind.Object, json.GetProperty("geojson").ValueKind);
        Assert.Equal("Point", json.GetProperty("geojson").GetProperty("type").GetString());
        Assert.Equal(-46.633309, json.GetProperty("longitude").GetDouble());
        Assert.Equal((123, enumColuna), (repo.Pedidos.Single().Id.Valor, repo.Pedidos.Single().Coluna));
    }

    [Fact]
    public async Task Get200_Poligono_CoordenadasNulas()
    {
        await using var f = new ApiFactory(new RepositorioFalso(new[] { Geometrias.Poligono() }));

        var (r, corpo) = await GetAsync(f, "/api/mapas/2147483647/metry");

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        var json = JsonDocument.Parse(corpo).RootElement;
        Assert.Equal(2147483647, json.GetProperty("id").GetInt32());
        Assert.Equal(JsonValueKind.Null, json.GetProperty("x").ValueKind);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("latitude").ValueKind);
    }

    // ---------------- 400 ----------------

    [Theory]
    [InlineData("abc", "graphy")]
    [InlineData("0", "graphy")]
    [InlineData("-1", "metry")]
    [InlineData("1.5", "graphy")]
    [InlineData("%2B1", "metry")]
    [InlineData("2147483648", "graphy")]
    [InlineData("12345678901", "metry")]
    [InlineData("1%27", "graphy")]
    [InlineData("1%27%20OR%201=1--", "metry")]
    [InlineData("%20", "graphy")]
    [InlineData("%D9%A1", "metry")] // U+0661
    [InlineData("%3Cscript%3Ealert(1)%3C%2Fscript%3E", "graphy")]
    public async Task Get400_ProblemDetails_SemEco_InstanceFixo_SemConsultarBanco(string idUrl, string coluna)
    {
        var repo = new RepositorioFalso(Geometrias.Pontos(1));
        await using var f = new ApiFactory(repo);

        var (r, corpo) = await GetAsync(f, $"/api/mapas/{idUrl}/{coluna}");

        var json = AssertProblem(r, corpo, HttpStatusCode.BadRequest);
        Assert.Equal("Id inválido", json.GetProperty("title").GetString());
        Assert.Equal($"/api/mapas/{{id}}/{coluna}", json.GetProperty("instance").GetString());
        Assert.Equal("O id deve ser um inteiro entre 1 e 2147483647.", json.GetProperty("detail").GetString());
        var bruto = Uri.UnescapeDataString(idUrl);
        if (bruto.Trim().Length >= 3)
        {
            // O traceId e hexadecimal aleatorio (pode conter "abc" por acaso): fica fora da comparacao.
            var semTraceId = corpo.Replace(json.GetProperty("traceId").GetString()!, string.Empty, StringComparison.Ordinal);
            Assert.DoesNotContain(bruto, semTraceId, StringComparison.Ordinal);
            Assert.DoesNotContain(idUrl, semTraceId, StringComparison.Ordinal);
        }

        Assert.Equal(0, repo.Chamadas);
    }

    // ---------------- 404 ----------------

    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Get404_IdSemGeometria(string coluna)
    {
        await using var f = new ApiFactory(RepositorioFalso.Vazio());

        var (r, corpo) = await GetAsync(f, $"/api/mapas/0042/{coluna}");

        var json = AssertProblem(r, corpo, HttpStatusCode.NotFound);
        Assert.Equal("Geometria não encontrada", json.GetProperty("title").GetString());
        Assert.Equal($"/api/mapas/42/{coluna}", json.GetProperty("instance").GetString());
        Assert.Equal($"Nenhuma geometria encontrada na coluna '{coluna}' para o id 42.", json.GetProperty("detail").GetString());
    }

    [Theory]
    [InlineData("/api/mapas/1/outra")]
    [InlineData("/api/mapas/1")]
    [InlineData("/api/mapas//graphy")]
    [InlineData("/")]
    [InlineData("/api/mapas/1/graphy/extra")]
    public async Task RotaDesconhecida_404ProblemDetails_SemInstance(string caminho)
    {
        await using var f = new ApiFactory(RepositorioFalso.Vazio());

        var (r, corpo) = await GetAsync(f, caminho);

        var json = AssertProblem(r, corpo, HttpStatusCode.NotFound);
        Assert.False(json.TryGetProperty("instance", out _));
    }

    // ---------------- 405 ----------------

    [Theory]
    [InlineData("POST", "graphy")]
    [InlineData("PUT", "metry")]
    [InlineData("DELETE", "graphy")]
    [InlineData("PATCH", "metry")]
    public async Task MetodoDiferenteDeGet_405(string metodo, string coluna)
    {
        var repo = new RepositorioFalso(Geometrias.Pontos(1));
        await using var f = new ApiFactory(repo);

        var (r, corpo) = await GetAsync(f, $"/api/mapas/1/{coluna}", new HttpMethod(metodo));

        AssertProblem(r, corpo, HttpStatusCode.MethodNotAllowed);
        Assert.Equal(0, repo.Chamadas);
    }

    // ---------------- 500 ----------------

    public static TheoryData<string, string> CenariosErro => new()
    {
        { "Development", "excecao" },
        { "Production", "excecao" },
        { "Development", "timeout" },
        { "Production", "geojson" },
        { "Development", "geojson" },
    };

    [Theory]
    [MemberData(nameof(CenariosErro))]
    public async Task Get500_GenericoComTraceId_SemVazamento(string ambiente, string cenario)
    {
        IMapasRepository repo = cenario switch
        {
            "excecao" => RepositorioFalso.QueLanca(new InvalidOperationException("SEGREDO Npgsql password=SenhaFicticia host 192.168.168.106")),
            "timeout" => RepositorioFalso.QueLanca(new TimeoutException("SEGREDO timeout 127.0.0.1")),
            _ => new RepositorioFalso(new[] { new GeometriaMapa("POINT", 4326, "POINT(1 2)", "SEGREDO{nao-json", 1, 2, 1, 2) }),
        };
        await using var f = new ApiFactory(repo, ambiente);

        var (r, corpo) = await GetAsync(f, "/api/mapas/7/graphy");

        var json = AssertProblem(r, corpo, HttpStatusCode.InternalServerError);
        Assert.Equal("Erro interno", json.GetProperty("title").GetString());
        Assert.Equal("Não foi possível processar a requisição. Informe o traceId ao suporte.", json.GetProperty("detail").GetString());
        Assert.False(json.TryGetProperty("instance", out _));
        Assert.False(json.TryGetProperty("exception", out _));
        Assert.False(json.TryGetProperty("errors", out _));
        Assert.DoesNotContain("text/html", r.Content.Headers.ContentType?.ToString() ?? string.Empty, StringComparison.Ordinal);

        // A excecao completa fica so no log, com o traceId e sem a senha do ambiente.
        var traceId = json.GetProperty("traceId").GetString()!;
        var logErro = Assert.Single(f.Logs.Mensagens, m => m.Contains(traceId, StringComparison.Ordinal) && m.Contains("Erro nao tratado", StringComparison.Ordinal));
        Assert.Contains(cenario == "geojson" ? "GeoJSON invalido" : "SEGREDO", logErro, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("SenhaFicticia#123", StringComparison.Ordinal));
    }

    // ---------------- 429 ----------------

    [Fact]
    public async Task RateLimit_Excedido_429ComRetryAfter()
    {
        var repo = new RepositorioFalso(Geometrias.Pontos(1));
        await using var f = new ApiFactory(repo, limiteRequisicoes: 2);
        using var cliente = f.CreateClient();

        var r1 = await cliente.GetAsync("/api/mapas/1/graphy");
        var r2 = await cliente.GetAsync("/api/mapas/abc/metry");
        var r3 = await cliente.GetAsync("/api/mapas/1/graphy");
        var corpo = await r3.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        var json = AssertProblem(r3, corpo, HttpStatusCode.TooManyRequests);
        Assert.Equal("Muitas requisições", json.GetProperty("title").GetString());
        Assert.NotNull(r3.Headers.RetryAfter);
        Assert.Equal(1, repo.Chamadas);
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("rate limiting", StringComparison.Ordinal));
    }

    // ---------------- Host / logs ----------------

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("192.168.168.106")]
    public async Task HostNaoPermitido_400ComCabecalhos(string host)
    {
        await using var f = new ApiFactory(new RepositorioFalso(Geometrias.Pontos(1)));
        using var cliente = f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/mapas/1/graphy");
        req.Headers.Host = host;

        var r = await cliente.SendAsync(req);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        AssertCabecalhosSeguranca(r);
    }

    [Theory]
    [InlineData("localhost")]
    [InlineData("127.0.0.1")]
    [InlineData("[::1]")]
    public async Task HostLoopback_Aceito(string host)
    {
        await using var f = new ApiFactory(new RepositorioFalso(Geometrias.Pontos(1)));
        using var cliente = f.CreateClient();
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/mapas/1/graphy");
        req.Headers.Host = host;

        var r = await cliente.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
    }

    [Fact]
    public async Task Logs_NaoContemSenhaNemIdBruto()
    {
        await using var f = new ApiFactory(new RepositorioFalso(Geometrias.Pontos(1)));
        await GetAsync(f, "/api/mapas/MARCADOR_ID_BRUTO/graphy");
        await GetAsync(f, "/api/mapas/1/metry");

        var tudo = string.Join('\n', f.Logs.Mensagens);
        Assert.Contains("127.0.0.1:1/casaos ssl=Require", tudo, StringComparison.Ordinal);
        Assert.Contains("NAO tem autenticacao", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("SenhaFicticia#123", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("usuario_ficticio_teste", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("MARCADOR_ID_BRUTO", tudo.Replace("/api/mapas/MARCADOR_ID_BRUTO/graphy", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }
}
