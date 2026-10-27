using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Npgsql;

namespace ApiMapas.Tests.Api;

/// <summary>Conteudo sem Content-Length (Transfer-Encoding: chunked) para o 413 por streaming.</summary>
internal sealed class ConteudoSemTamanho(byte[] dados, string? contentType = "application/json") : HttpContent
{
    public ConteudoSemTamanho(string texto, string? contentType = "application/json")
        : this(Encoding.UTF8.GetBytes(texto), contentType)
    {
    }

    public byte[] Dados => dados;

    public string? TipoConteudo => contentType;

    protected override async Task SerializeToStreamAsync(Stream stream, TransportContext? context)
    {
        // Em blocos, como um cliente que envia aos poucos.
        for (var i = 0; i < dados.Length; i += 4096)
        {
            await stream.WriteAsync(dados.AsMemory(i, Math.Min(4096, dados.Length - i)));
        }
    }

    protected override bool TryComputeLength(out long length)
    {
        length = 0;
        return false;
    }

    public static ConteudoSemTamanho Criar(string texto, string? contentType = "application/json")
    {
        var c = new ConteudoSemTamanho(texto, contentType);
        if (contentType is not null)
        {
            c.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return c;
    }
}

/// <summary>Funcoes comuns dos testes HTTP do PUT.</summary>
internal static class Http
{
    public static readonly string[] TermosProibidos =
    {
        "Npgsql", "SqlState", "password", "Password", "senha", "SenhaFicticia", "SenhaEscritaFicticia", "usuario_ficticio",
        "escrita_ficticia", "StackTrace", "   at ", "Exception", "192.168.", "127.0.0.1", "SEGREDO", "POINT", "LINESTRING",
    };

    public static HttpContent Json(string corpo, string? contentType = "application/json")
    {
        var c = new ByteArrayContent(Encoding.UTF8.GetBytes(corpo));
        if (contentType is not null)
        {
            c.Headers.TryAddWithoutValidation("Content-Type", contentType);
        }

        return c;
    }

    public static async Task<(HttpResponseMessage Resposta, string Corpo)> EnviarAsync(
        HttpClient cliente, HttpMethod metodo, string caminho, HttpContent? conteudo = null, Action<HttpRequestMessage>? ajustar = null, CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(metodo, caminho) { Content = conteudo };
        ajustar?.Invoke(req);
        var r = await cliente.SendAsync(req, ct);
        return (r, await r.Content.ReadAsStringAsync(ct));
    }

    public static Task<(HttpResponseMessage Resposta, string Corpo)> PutAsync(HttpClient cliente, string caminho, string corpo, string? contentType = "application/json") =>
        EnviarAsync(cliente, HttpMethod.Put, caminho, Json(corpo, contentType));

    public static void AssertCabecalhosSeguranca(HttpResponseMessage r)
    {
        Assert.Equal("nosniff", Assert.Single(r.Headers.GetValues("X-Content-Type-Options")));
        Assert.Contains("no-store", r.Headers.CacheControl?.ToString() ?? string.Empty, StringComparison.Ordinal);
        Assert.False(r.Headers.Contains("Server"));
        Assert.False(r.Headers.Contains("Access-Control-Allow-Origin"));
        Assert.False(r.Headers.Contains("Access-Control-Allow-Methods"));
    }

    public static JsonElement AssertProblem(HttpResponseMessage r, string corpo, HttpStatusCode status, string? titulo = null, string? detalhe = null, string? instance = null)
    {
        Assert.True(status == r.StatusCode, $"esperado {(int)status}, obtido {(int)r.StatusCode}: {corpo}");
        Assert.Equal("application/problem+json", r.Content.Headers.ContentType?.MediaType);
        var json = JsonDocument.Parse(corpo).RootElement;
        Assert.Equal((int)status, json.GetProperty("status").GetInt32());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("traceId").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("type").GetString()));
        if (titulo is not null)
        {
            Assert.Equal(titulo, json.GetProperty("title").GetString());
        }

        if (detalhe is not null)
        {
            Assert.Equal(detalhe, json.GetProperty("detail").GetString());
        }

        if (instance is null)
        {
            Assert.False(json.TryGetProperty("instance", out _), corpo);
        }
        else
        {
            Assert.Equal(instance, json.GetProperty("instance").GetString());
        }

        Assert.False(json.TryGetProperty("exception", out _));
        Assert.False(json.TryGetProperty("errors", out _));
        AssertCabecalhosSeguranca(r);
        foreach (var termo in TermosProibidos)
        {
            Assert.DoesNotContain(termo, corpo, StringComparison.Ordinal);
        }

        return json;
    }

    public static string Tipo(int status) => status switch
    {
        400 => "https://tools.ietf.org/html/rfc9110#section-15.5.1",
        404 => "https://tools.ietf.org/html/rfc9110#section-15.5.5",
        409 => "https://tools.ietf.org/html/rfc9110#section-15.5.10",
        413 => "https://tools.ietf.org/html/rfc9110#section-15.5.14",
        415 => "https://tools.ietf.org/html/rfc9110#section-15.5.16",
        _ => throw new ArgumentOutOfRangeException(nameof(status)),
    };
}

/// <summary>
/// P6.3 - PUT /api/mapas/{id}/graphy|metry pela API em memoria (ApiEscritaFactory) com repositorio
/// falso: 204, 400 (id, corpo, geometria), 404, 409, 413, 415, 429, 500, instance/traceId,
/// cabecalhos e RP-12. Nenhum acesso a banco real.
/// </summary>
public class ApiEscritaTests
{
    private const string TituloId = "Id inválido";
    private const string DetalheId = "O id deve ser um inteiro entre 1 e 2147483647.";
    private const string TituloCorpo = "Corpo inválido";
    private const string DetalheCorpo = "O corpo deve ser um JSON no formato {\"wkt\":\"...\",\"srid\":4326}.";
    private const string TituloGeometria = "Geometria inválida";
    private const string Titulo415 = "Tipo de mídia não suportado";
    private const string Detalhe415 = "Use Content-Type: application/json (charset utf-8 opcional).";

    // ---------------- 204 ----------------

    [Theory]
    [InlineData("graphy", ColunaMapa.Graphy)]
    [InlineData("metry", ColunaMapa.Metry)]
    public async Task Put204_SemCorpo_RepositorioRecebeAGeometria(string coluna, ColunaMapa enumColuna)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/123/{coluna}", CorposPut.Valido);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(string.Empty, corpo);
        Http.AssertCabecalhosSeguranca(r);
        var chamada = Assert.Single(repo.Chamadas);
        Assert.Equal((123, enumColuna), (chamada.Id!.Value.Valor, chamada.Coluna));
        Assert.Equal(CorposPut.PontoGraphy, chamada.Geometria.Wkt);

        var auditoria = Assert.Single(f.Logs.Mensagens, m => m.Contains("Auditoria PUT", StringComparison.Ordinal));
        Assert.Contains($"id 123, coluna {coluna} atualizada", auditoria, StringComparison.Ordinal);
        Assert.Contains("traceId ", auditoria, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("POINT", StringComparison.Ordinal) || m.Contains("-46.63", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("application/json")]
    [InlineData("application/json; charset=utf-8")]
    [InlineData("application/json;charset=UTF-8")]
    [InlineData("Application/JSON")]
    [InlineData("APPLICATION/JSON; CHARSET=UTF-8")]
    [InlineData("application/json; charset=\"utf-8\"")]
    public async Task Put204_ContentTypeAceito_C1(string contentType)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.PutAsync(cliente, "/api/mapas/1/graphy", CorposPut.Valido, contentType);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Single(repo.Chamadas);
    }

    [Fact]
    public async Task Put204_Wkt10000()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.PutAsync(cliente, "/api/mapas/5/metry", CorposPut.Com(("POINT(0 0)" + new string(' ', 9_990), 4326)));

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Single(repo.Chamadas);
    }

    // ---------------- 400 Id (antes do Content-Type, do tamanho e do banco) ----------------

    [Theory]
    [InlineData("abc", "graphy")]
    [InlineData("0", "metry")]
    [InlineData("-1", "graphy")]
    [InlineData("2147483648", "metry")]
    [InlineData("1%27%20OR%201=1--", "graphy")]
    [InlineData("%20", "metry")]
    [InlineData("%D9%A1", "graphy")]
    [InlineData("MARCADOR_ID", "metry")]
    public async Task Put400_IdInvalido_InstanceFixo_SemBanco(string id, string coluna)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteBytesCorpoPut", "10");
        using var cliente = f.CreateClient();

        // Content-Type errado e corpo acima do limite: o id e verificado antes (ordem do contrato).
        var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/{id}/{coluna}", CorposPut.Valido, "text/plain");

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, TituloId, DetalheId, $"/api/mapas/{{id}}/{coluna}");
        Assert.Equal(Http.Tipo(400), json.GetProperty("type").GetString());
        Assert.DoesNotContain("MARCADOR_ID", corpo, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
    }

    // ---------------- 400 Corpo ----------------

    public static TheoryData<string> CorposInvalidos => new()
    {
        "{",
        "",
        "[]",
        "null",
        "{}",
        "{\"wkt\":null,\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,\"extra\":\"SEGREDO_CORPO\"}",
        "{\"wkt\":\"POINT(0 0)\",\"wkt\":\"POINT(0 0)\",\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,\"srid\":4326}",
        "{\"Wkt\":\"POINT(0 0)\",\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":\"4326\"}",
        "{\"wkt\":\"POINT(0 0)\"}",
        "{\"geometrias\":[{\"wkt\":\"POINT(0 0)\",\"srid\":4326}]}",
        "[{\"wkt\":\"POINT(0 0)\",\"srid\":4326}]",
        "{\"wkt\":\"SEGREDO_CORPO\",\"srid\":4326",
    };

    [Theory]
    [MemberData(nameof(CorposInvalidos))]
    public async Task Put400_CorpoInvalido_SemJsonException_SemEco(string json)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/7/graphy", json);

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, TituloCorpo, DetalheCorpo, "/api/mapas/{id}/graphy");
        Assert.Empty(repo.Chamadas);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("JsonException", StringComparison.Ordinal)
            || m.Contains("SEGREDO_CORPO", StringComparison.Ordinal)
            || m.Contains("Path:", StringComparison.Ordinal));
    }

    // ---------------- 400 Geometria ----------------

    public static TheoryData<string, string, string> GeometriasInvalidas => new()
    {
        { "graphy", CorposPut.Com(("POINT(181 0)", 4326)), "longitude fora da faixa [-180, 180] do SRID 4326 antes da posição 11" },
        { "graphy", CorposPut.Com(("POINT(0 0)", 3857)), "srid não permitido para a coluna 'graphy' (aceitos: 4326)" },
        { "metry", CorposPut.Com(("POINT(0 0)", 0)), "srid não permitido para a coluna 'metry' (aceitos: 4326, 4674, 3857, 31983)" },
        { "metry", CorposPut.Com(("SRID=4326;POINT(0 0)", 4326)), "prefixo SRID= (EWKT) não permitido; informe o SRID no campo srid" },
        { "metry", CorposPut.Com(("POINT(0 0)'; DROP TABLE mapas; --", 4326)), "caractere não permitido (U+0027) na posição 11" },
        { "graphy", CorposPut.Com(("POINT(0 0)" + new string(' ', 9_991), 4326)), "WKT excede o limite de 10000 caracteres" },
    };

    [Theory]
    [MemberData(nameof(GeometriasInvalidas))]
    public async Task Put400_GeometriaInvalida_MotivoFixo_SemBanco(string coluna, string json, string detalhe)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/7/{coluna}", json);

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, TituloGeometria, detalhe, $"/api/mapas/{{id}}/{coluna}");
        Assert.DoesNotContain("DROP", corpo, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task Put400_GeometriaInvalidaNoBanco_MotivoDoRepositorio()
    {
        var repo = new RepositorioEscritaFalso(new ResultadoEscrita.GeometriaInvalida("geometria inválida segundo o PostGIS (ST_IsValid)"));
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/7/metry", CorposPut.Valido);

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, TituloGeometria,
            "geometria inválida segundo o PostGIS (ST_IsValid)", "/api/mapas/{id}/metry");
    }

    // ---------------- 404 / 409 ----------------

    [Theory]
    [InlineData("42", "graphy", "/api/mapas/42/graphy", 42)]
    [InlineData("0042", "metry", "/api/mapas/42/metry", 42)]
    [InlineData("2147483647", "graphy", "/api/mapas/2147483647/graphy", 2147483647)]
    public async Task Put404_MapaNaoEncontrado_InstanceComIdValidado(string id, string coluna, string instance, int idValidado)
    {
        await using var f = new ApiEscritaFactory { Repositorio = new RepositorioEscritaFalso(ResultadoEscrita.NaoEncontrado.Instancia) };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/{id}/{coluna}", CorposPut.Valido);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.NotFound, "Mapa não encontrado",
            $"Nenhuma linha encontrada em public.mapas para o id {idValidado}.", instance);
        Assert.Equal(Http.Tipo(404), json.GetProperty("type").GetString());
    }

    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Put409_AtualizacaoConcorrente(string coluna)
    {
        await using var f = new ApiEscritaFactory { Repositorio = new RepositorioEscritaFalso(ResultadoEscrita.Conflito.Instancia) };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/9/{coluna}", CorposPut.Valido);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.Conflict, "Atualização concorrente",
            "Outra atualização do id 9 está em andamento. Tente novamente.", $"/api/mapas/9/{coluna}");
        Assert.Equal(Http.Tipo(409), json.GetProperty("type").GetString());
    }

    // ---------------- 413 ----------------

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Put413_AcimaDoLimite_ComESemContentLength(bool comContentLength)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteBytesCorpoPut", "100");
        using var cliente = f.CreateClient();
        var corpoGrande = CorposPut.Com((CorposPut.PontoGraphy + new string(' ', 100), 4326));
        Assert.True(Encoding.UTF8.GetByteCount(corpoGrande) > 100);
        HttpContent conteudo = comContentLength ? Http.Json(corpoGrande) : ConteudoSemTamanho.Criar(corpoGrande);

        var (r, corpo) = await Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/3/graphy", conteudo);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.RequestEntityTooLarge, "Corpo muito grande",
            "O corpo excede o limite de 100 bytes.", "/api/mapas/{id}/graphy");
        Assert.Equal(Http.Tipo(413), json.GetProperty("type").GetString());
        Assert.Empty(repo.Chamadas);
    }

    [Theory]
    [InlineData(100, true, HttpStatusCode.NoContent)]
    [InlineData(100, false, HttpStatusCode.NoContent)]
    [InlineData(101, true, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(101, false, HttpStatusCode.RequestEntityTooLarge)]
    public async Task Put413_LimiteExato(int tamanho, bool comContentLength, HttpStatusCode esperado)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteBytesCorpoPut", "100");
        using var cliente = f.CreateClient();
        var json = CorposPut.Com(("POINT(1 2)", 4326));
        json = json[..^1] + new string(' ', tamanho - json.Length) + "}";
        Assert.Equal(tamanho, Encoding.UTF8.GetByteCount(json));
        HttpContent conteudo = comContentLength ? Http.Json(json) : ConteudoSemTamanho.Criar(json);

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/3/metry", conteudo);

        Assert.Equal(esperado, r.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Put413_PadraoDe64KiB(bool comContentLength)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();
        var json = CorposPut.Valido[..^1] + new string(' ', 65_537 - CorposPut.Valido.Length) + "}";
        Assert.Equal(65_537, json.Length);
        HttpContent conteudo = comContentLength ? Http.Json(json) : ConteudoSemTamanho.Criar(json);

        var (r, corpo) = await Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/3/metry", conteudo);

        Http.AssertProblem(r, corpo, HttpStatusCode.RequestEntityTooLarge, "Corpo muito grande", "O corpo excede o limite de 65536 bytes.", "/api/mapas/{id}/metry");
        Assert.Empty(repo.Chamadas);
    }

    // ---------------- 415 (RP-12) ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("text/plain")]
    [InlineData("application/json; charset=latin1")]
    [InlineData("application/json; charset=iso-8859-1")]
    [InlineData("application/json; charset=utf-16")]
    [InlineData("application/json; charset=utf8")]
    [InlineData("application/x-www-form-urlencoded")]
    [InlineData("multipart/form-data; boundary=x")]
    [InlineData("application/merge-patch+json")]
    [InlineData("application/problem+json")]
    [InlineData("application/*")]
    [InlineData("*/*")]
    [InlineData("application/jsonx")]
    [InlineData("text/json")]
    [InlineData("application/json; charset=utf-8; foo=bar")]
    [InlineData("application/json; foo=bar")]
    [InlineData("application/json;")]
    [InlineData("application/json; charset=")]
    public async Task Put415_ContentTypeRecusado_SemBanco(string? contentType)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/1/graphy", CorposPut.Valido, contentType);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.UnsupportedMediaType, Titulo415, Detalhe415, "/api/mapas/{id}/graphy");
        Assert.Equal(Http.Tipo(415), json.GetProperty("type").GetString());
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task Put415_ContentTypeAntesDoTamanho()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteBytesCorpoPut", "10");
        using var cliente = f.CreateClient();

        var (r, _) = await Http.PutAsync(cliente, "/api/mapas/1/graphy", CorposPut.Valido, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
    }

    [Fact]
    public async Task Put415_SemCorpoESemContentType()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/1/graphy");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        Assert.Empty(repo.Chamadas);
    }

    // ---------------- 429 (D-P4 / R-2) ----------------

    [Fact]
    public async Task Put429_LimiteDeEscritasPorMinuto_NaoAfetaOGet()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteEscritasPorMinuto", "2");
        using var cliente = f.CreateClient();

        var (r1, _) = await Http.PutAsync(cliente, "/api/mapas/1/graphy", CorposPut.Valido);
        var (r2, _) = await Http.PutAsync(cliente, "/api/mapas/abc/metry", CorposPut.Valido);
        var (r3, corpo3) = await Http.PutAsync(cliente, "/api/mapas/1/graphy", CorposPut.Valido);
        var get = await cliente.GetAsync("/api/mapas/1/graphy");

        Assert.Equal(HttpStatusCode.NoContent, r1.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        var json = Http.AssertProblem(r3, corpo3, HttpStatusCode.TooManyRequests, "Muitas requisições");
        Assert.Equal("https://tools.ietf.org/html/rfc6585#section-4", json.GetProperty("type").GetString());
        Assert.NotNull(r3.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Single(repo.Chamadas);
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("Escrita (PUT e POST) recusada pelo rate limiting", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Put429_QuartoPutSimultaneo_SemRetryAfter()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrou = 0;
        var repo = new RepositorioEscritaFalso(async (c, ct) =>
        {
            Interlocked.Increment(ref entrou);
            await liberar.Task.WaitAsync(ct);
            return ResultadoEscrita.Atualizado.Instancia;
        });
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var tres = Enumerable.Range(1, 3).Select(i => Http.PutAsync(cliente, $"/api/mapas/{i}/graphy", CorposPut.Valido)).ToList();
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (Volatile.Read(ref entrou) < 3 && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        Assert.Equal(3, Volatile.Read(ref entrou));
        var (r4, corpo4) = await Http.PutAsync(cliente, "/api/mapas/4/metry", CorposPut.Valido);
        var get = await cliente.GetAsync("/api/mapas/1/metry");
        liberar.SetResult();
        var respostas = await Task.WhenAll(tres);

        Http.AssertProblem(r4, corpo4, HttpStatusCode.TooManyRequests, "Muitas requisições");
        Assert.Null(r4.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.All(respostas, x => Assert.Equal(HttpStatusCode.NoContent, x.Resposta.StatusCode));
        Assert.Equal(3, repo.Chamadas.Count);
        Assert.Equal(3, repo.MaximoSimultaneo);
    }

    // ---------------- 500 ----------------

    public static TheoryData<string> Erros500 => new() { "falha-banco", "timeout", "generica" };

    [Theory]
    [MemberData(nameof(Erros500))]
    public async Task Put500_Generico_SemVazamento(string cenario)
    {
        Exception erro = cenario switch
        {
            "falha-banco" => new FalhaBancoEscritaException("atualizar a coluna (UPDATE pela PK)", "57014", "PostgresException"),
            "timeout" => new TimeoutException("SEGREDO 127.0.0.1"),
            _ => new InvalidOperationException("SEGREDO POINT(1 2) password=SenhaEscritaFicticia#456"),
        };
        await using var f = new ApiEscritaFactory { Repositorio = RepositorioEscritaFalso.QueLanca(erro) };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/7/graphy", CorposPut.Valido);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno",
            "Não foi possível processar a requisição. Informe o traceId ao suporte.");
        var traceId = json.GetProperty("traceId").GetString()!;
        Assert.Contains(f.Logs.Mensagens, m => m.Contains(traceId, StringComparison.Ordinal) && m.Contains("PUT /api/mapas/{id}/graphy", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Put500_PrazoDe30sEstourado_D_P11()
    {
        await using var f = new ApiEscritaFactory
        {
            Repositorio = RepositorioEscritaFalso.Pendurado(),
            Opcoes = new OpcoesAtualizacao { PrazoTotal = TimeSpan.FromMilliseconds(200) },
        };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/7/metry", CorposPut.Valido);

        Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno");
        Assert.Null(r.Headers.RetryAfter);
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("prazo total de 0 s estourado", StringComparison.Ordinal)
            || m.Contains("prazo total", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("PrazoAtualizacaoEstouradoException", StringComparison.Ordinal));
    }

    // ---------------- RP-12: Host, CORS, POST, method override ----------------

    [Theory]
    [InlineData("evil.example.com")]
    [InlineData("192.168.168.106")]
    [InlineData("localhost.evil.com")]
    public async Task HostMalicioso_400_NaoGrava(string host)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/1/graphy", Http.Json(CorposPut.Valido), req => req.Headers.Host = host);

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Http.AssertCabecalhosSeguranca(r);
        Assert.Empty(repo.Chamadas);
    }

    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Options_ComOrigin_SemCabecalhosCors(string coluna)
    {
        await using var f = new ApiEscritaFactory { Repositorio = RepositorioEscritaFalso.Sucesso() };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Options, $"/api/mapas/1/{coluna}", ajustar: req =>
        {
            req.Headers.Add("Origin", "http://evil.example.com");
            req.Headers.Add("Access-Control-Request-Method", "PUT");
            req.Headers.Add("Access-Control-Request-Headers", "content-type");
        });

        Assert.False(r.IsSuccessStatusCode);
        Assert.DoesNotContain(r.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
    }

    public static TheoryData<string, string?> MetodosNaoMapeados => new()
    {
        { "POST", null },
        { "POST", "PUT" },
        { "PATCH", null },
        { "DELETE", null },
        { "POST", "put" },
    };

    [Theory]
    [MemberData(nameof(MetodosNaoMapeados))]
    public async Task PostPatchDeleteEMethodOverride_405_NaoGravam(string metodo, string? overrideMetodo)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, new HttpMethod(metodo), "/api/mapas/1/graphy", Http.Json(CorposPut.Valido), req =>
        {
            if (overrideMetodo is not null)
            {
                req.Headers.Add("X-HTTP-Method-Override", overrideMetodo);
                req.Headers.Add("X-HTTP-Method", overrideMetodo);
                req.Headers.Add("X-Method-Override", overrideMetodo);
            }
        });

        Assert.Equal(HttpStatusCode.MethodNotAllowed, r.StatusCode);
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task GetComMethodOverridePut_ContinuaGet_NaoGrava()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Get, "/api/mapas/1/graphy", ajustar: req => req.Headers.Add("X-HTTP-Method-Override", "PUT"));

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Empty(repo.Chamadas);
    }

    // ---------------- Logs de inicio (RP-13) ----------------

    [Fact]
    public async Task LogsDeInicio_EscritaHabilitada_AvisoSemAutenticacao_SemCredenciais()
    {
        await using var f = new ApiEscritaFactory { Repositorio = RepositorioEscritaFalso.Sucesso() }.ComConfiguracao("Verboso", "true");
        using var cliente = f.CreateClient();
        await Http.PutAsync(cliente, "/api/mapas/1/graphy", CorposPut.Valido);

        var tudo = f.TextoLogs;
        Assert.Contains("Escrita (PUT e POST) HABILITADA: banco 127.0.0.1:1/casaos ssl=Require (escrita); limite 65536 bytes por corpo; 60 escritas (PUT e POST) por minuto por IP e na API toda; no maximo 3 escritas simultaneas; prazo 30 s.", tudo, StringComparison.Ordinal);
        Assert.Contains("ESCRITA (PUT e POST) SEM autenticacao", tudo, StringComparison.Ordinal);
        Assert.Contains("rotas PUT /api/mapas/{id}/graphy e /api/mapas/{id}/metry e POST /api/mapas/graphy e /api/mapas/metry mapeadas", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiEscritaFactory.SenhaEscrita, tudo, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiEscritaFactory.UsuarioEscrita, tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("SenhaFicticia#123", tudo, StringComparison.Ordinal);
    }

    // ---------------- ContentTypeAceito (unitario, C1 / RP-12) ----------------

    [Theory]
    [InlineData("application/json", true)]
    [InlineData("Application/JSON", true)]
    [InlineData("APPLICATION/JSON; CHARSET=UTF-8", true)]
    [InlineData("application/json;charset=utf-8", true)]
    [InlineData(" application/json ;\tcharset=utf-8 ", true)]
    [InlineData("application/json; charset=\"UTF-8\"", true)]
    [InlineData("application/json; charset=\"utf-8", false)]
    [InlineData("application/json; =utf-8", false)]
    [InlineData("application/json; charset", false)]
    [InlineData("application/json; charset=latin1", false)]
    [InlineData("application/json; charset=utf-8;", false)]
    [InlineData("application/json, text/plain", false)]
    [InlineData("application/vnd.api+json", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void ContentTypeAceito_Unitario(string? valor, bool esperado)
    {
        Assert.Equal(esperado, ApiMapas.Api.Endpoints.LeitorCorpoHttp.ContentTypeAceito(new Microsoft.Extensions.Primitives.StringValues(valor)));
    }

    [Fact]
    public void ContentTypeAceito_CabecalhoDuplicado_Recusado()
    {
        var duplicado = new Microsoft.Extensions.Primitives.StringValues(new[] { "application/json", "application/json" });
        Assert.False(ApiMapas.Api.Endpoints.LeitorCorpoHttp.ContentTypeAceito(duplicado));
        Assert.False(ApiMapas.Api.Endpoints.LeitorCorpoHttp.ContentTypeAceito(Microsoft.Extensions.Primitives.StringValues.Empty));
    }
}

/// <summary>
/// P6.3 / P6.8 (C2, C3) - 405 com a escrita desligada por cada motivo de D-P9; a API continua de pe
/// e o GET responde. Nenhum PUT chega ao repositorio.
/// </summary>
public class ApiEscritaDesligadaTests
{
    private static async Task AssertPut405_GetOk(ApiEscritaFactory f, RepositorioEscritaFalso repo, string motivo)
    {
        using var cliente = f.CreateClient();

        var (graphy, _) = await Http.PutAsync(cliente, "/api/mapas/1/graphy", CorposPut.Valido);
        var (metry, _) = await Http.PutAsync(cliente, "/api/mapas/1/metry", CorposPut.Valido);
        var (postGraphy, _) = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/mapas/graphy", Http.Json(CorposPut.Valido));
        var (postMetry, corpoPostMetry) = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/mapas/metry", Http.Json(CorposPut.Valido));
        var get = await cliente.GetAsync("/api/mapas/1/graphy");
        var getMetry = await cliente.GetAsync("/api/mapas/1/metry");

        Assert.Equal(HttpStatusCode.MethodNotAllowed, graphy.StatusCode);
        Assert.Equal(HttpStatusCode.MethodNotAllowed, metry.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, postGraphy.StatusCode); // RS-21
        Http.AssertProblem(postMetry, corpoPostMetry, HttpStatusCode.NotFound); // RS-21: rota inexistente, sem instance
        Http.AssertCabecalhosSeguranca(graphy);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getMetry.StatusCode);
        Assert.Empty(repo.Chamadas);

        var aviso = Assert.Single(f.Logs.Mensagens, m => m.Contains("Escrita (PUT e POST) DESLIGADA", StringComparison.Ordinal));
        Assert.StartsWith("Warning", aviso, StringComparison.Ordinal);
        Assert.Contains(motivo, aviso, StringComparison.Ordinal);
        Assert.Contains("respondem 405", aviso, StringComparison.Ordinal);
        Assert.Contains("os POST respondem 404", aviso, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("HABILITADA", StringComparison.Ordinal));
        Assert.DoesNotContain(ApiEscritaFactory.SenhaEscrita, f.TextoLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(ApiEscritaFactory.UsuarioEscrita, f.TextoLogs, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("  ", null)]
    public async Task SemVariaveis_405(string? usuario, string? senha)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var verificador = new VerificadorRoleFalso(MotivoEscritaDesligada.Nenhum);
        await using var f = new ApiEscritaFactory { Repositorio = repo, Verificador = verificador }
            .ComVariavel("PG_ESCRITA_USER", usuario).ComVariavel("PG_ESCRITA_PASSWORD", senha);

        await AssertPut405_GetOk(f, repo, "PG_ESCRITA_USER e PG_ESCRITA_PASSWORD nao definidas");
        Assert.Equal(0, verificador.Chamadas);
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("Prefer")]
    public async Task SslInseguro_405(string sslMode)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComVariavel("PG_SSLMODE", sslMode);

        await AssertPut405_GetOk(f, repo, "PG_SSLMODE efetivo e Disable ou Prefer");
    }

    [Theory]
    [InlineData(MotivoEscritaDesligada.RolePrivilegiada, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE")]
    [InlineData(MotivoEscritaDesligada.VerificacaoFalhou, "nao foi possivel conferir os atributos da role")]
    [InlineData(MotivoEscritaDesligada.SemChavePrimaria, "nao tem a PRIMARY KEY mapas_pkey exata em id")]
    public async Task R1_RolePrivilegiadaOuFalha_405(MotivoEscritaDesligada motivo, string texto)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var verificador = new VerificadorRoleFalso(motivo);
        await using var f = new ApiEscritaFactory { Repositorio = repo, Verificador = verificador };

        await AssertPut405_GetOk(f, repo, texto);
        Assert.Equal(1, verificador.Chamadas);
    }

    /// <summary>R-1 real: o banco de escrita (127.0.0.1:1) nao responde -> VerificacaoFalhou, API de pe.</summary>
    [Fact]
    public async Task R1_Real_BancoNaoResponde_405()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo, Verificador = null };

        await AssertPut405_GetOk(f, repo, "nao foi possivel conferir os atributos da role");
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("R-1: falha ao conferir a role de escrita", StringComparison.Ordinal));
    }

    /// <summary>C2: excecao que nao e do Npgsql na checagem real (pool descartado) -> 405, API de pe.</summary>
    [Fact]
    public async Task R1_Real_ExcecaoQualquer_405()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=u;Password=p"));
        await fonte.DisposeAsync();
        await using var f = new ApiEscritaFactory { Repositorio = repo, Verificador = null, Fonte = fonte };

        await AssertPut405_GetOk(f, repo, "nao foi possivel conferir os atributos da role");
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("ObjectDisposedException", StringComparison.Ordinal));
    }

    /// <summary>O-1: verificador que LANCA (sincrono ou assincrono) -> API sobe, PUT 405, pool limpo, log 4011 so com o tipo.</summary>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task O1_VerificadorQueLanca_ApiSobe_405_PoolLimpo_Log4011SemMensagem(bool sincrono)
    {
        await using var servidor = new ServidorPostgresEscritaFalso(_ => null);
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await (await fonte.DataSource.OpenConnectionAsync()).DisposeAsync(); // conexao ociosa no pool
        var repo = RepositorioEscritaFalso.Sucesso();
        var verificador = new VerificadorRoleFalso(sincrono
            ? _ => throw new InvalidOperationException("SEGREDO_O1 detalhe interno")
            : async _ =>
            {
                await Task.Yield();
                throw new InvalidOperationException("SEGREDO_O1 detalhe interno");
            });
        await using var f = new ApiEscritaFactory { Repositorio = repo, Verificador = verificador, Fonte = fonte };

        await AssertPut405_GetOk(f, repo, "nao foi possivel conferir os atributos da role");

        Assert.Equal(1, verificador.Chamadas);
        var log = Assert.Single(f.Logs.Mensagens, m => m.Contains("a checagem da role de escrita lancou", StringComparison.Ordinal));
        Assert.StartsWith("Warning", log, StringComparison.Ordinal);
        Assert.Contains("lancou InvalidOperationException; escrita desligada.", log, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGREDO_O1", f.TextoLogs, StringComparison.Ordinal);
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (servidor.Terminacoes == 0 && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        Assert.True(servidor.Terminacoes > 0, "pool de escrita nao foi limpo (C3)");
    }

    public static TheoryData<int, string> CriteriosC4 => new()
    {
        { 0, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE" },
        { 1, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE" },
        { 2, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE" },
        { 3, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE" },
        { 4, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE" },
        { 5, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE" },
        { 6, "DELETE em public.mapas, INSERT ou UPDATE na coluna id" },
        { 7, "DELETE em public.mapas, INSERT ou UPDATE na coluna id" },
        { 8, "PRIMARY KEY mapas_pkey" },
        { 9, "PRIMARY KEY mapas_pkey" },
        { 10, "INSERT ou UPDATE na coluna id" },
        { 11, "CREATEDB, REPLICATION" },
        { 12, "CREATEDB, REPLICATION" },
        { 13, "membro de outra role" },
    };

    /// <summary>C4 / RS-20: cada criterio da V1 real (via protocolo falso) desliga a escrita.</summary>
    [Theory]
    [MemberData(nameof(CriteriosC4))]
    public async Task R1_Real_CadaCriterio_405(int indice, string texto)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var colunas = new[] { "rolsuper", "rolbypassrls", "rolcreaterole", "pode_truncate", "escreve_tudo", "dona_tabela", "pode_delete", "insere_id", "pk_exata", "id_always", "atualiza_id", "rolcreatedb", "rolreplication", "membro_de_role" }
            .Select(n => new ColunaPg(n, ColunaPg.Bool)).ToArray();

        // 0 a 7 e 10 a 13: so o criterio do indice e true; 8 e 9: true, exceto o do indice (falta da PK).
        var valores = Enumerable.Range(0, 14).Select(i => (object?)(i is 8 or 9 ? i != indice : i == indice)).ToArray();
        await using var servidor = new ServidorPostgresEscritaFalso(_ => new RespostaPg.Linhas(colunas, new[] { valores }));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await using var f = new ApiEscritaFactory { Repositorio = repo, Verificador = null, Fonte = fonte };

        await AssertPut405_GetOk(f, repo, texto);
    }

    /// <summary>C3: com a escrita desligada pela R-1, as conexoes ociosas do pool de escrita sao fechadas.</summary>
    [Theory]
    [InlineData(MotivoEscritaDesligada.RolePrivilegiada, true)]
    [InlineData(MotivoEscritaDesligada.VerificacaoFalhou, true)]
    [InlineData(MotivoEscritaDesligada.Nenhum, false)]
    public async Task C3_EscritaDesligada_LimpaOPool(MotivoEscritaDesligada motivo, bool deveFechar)
    {
        await using var servidor = new ServidorPostgresEscritaFalso(_ => null);
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await (await fonte.DataSource.OpenConnectionAsync()).DisposeAsync(); // fica ociosa no pool
        Assert.Equal(0, servidor.Terminacoes);

        await using var f = new ApiEscritaFactory { Repositorio = RepositorioEscritaFalso.Sucesso(), Verificador = new VerificadorRoleFalso(motivo), Fonte = fonte };
        using var cliente = f.CreateClient();
        var limite = DateTime.UtcNow.AddSeconds(5);
        while (deveFechar && servidor.Terminacoes == 0 && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        Assert.Equal(deveFechar, servidor.Terminacoes > 0);
    }
}

/// <summary>
/// P6.3 / R-5 / CP-7 - pilha completa (API -> servico -> MapasEscritaRepository real) contra o
/// ServidorPostgresEscritaFalso: 204 com a transacao completa, 409, 500 com ROLLBACK e o 499
/// (cliente desconecta no meio da transacao) com ROLLBACK confirmado.
/// </summary>
public class ApiEscritaPilhaCompletaTests
{
    private static Func<ComandoPg, RespostaPg?> Feliz(Func<ComandoPg, RespostaPg?>? antes = null) => c =>
    {
        var r = antes?.Invoke(c);
        if (r is not null)
        {
            return r;
        }

        if (c.Contem("ST_IsValid"))
        {
            return RespostaPg.Validacao(true, new string(((string)c.Parametros[0]!).TakeWhile(char.IsAsciiLetter).ToArray()));
        }

        if (c.Contem("UPDATE public.mapas"))
        {
            return new RespostaPg.Comando("UPDATE 1");
        }

        if (c.Contem("INSERT INTO public.mapas"))
        {
            return new RespostaPg.Linhas(new[] { new ColunaPg("id", ColunaPg.Int4) }, new[] { new object?[] { 77 } }, "INSERT 0 1");
        }

        return null;
    };

    private static async Task<bool> EsperarAsync(Func<bool> condicao, int ms = 5000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condicao() && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        return condicao();
    }

    [Fact]
    public async Task Put204_TransacaoCompleta_UsaSoAConexaoDeEscrita()
    {
        await using var servidor = new ServidorPostgresEscritaFalso(Feliz());
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        var leitura = new RepositorioFalso(Geometrias.Pontos(1));
        await using var f = new ApiEscritaFactory { Fonte = fonte, RepositorioLeitura = leitura };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.PutAsync(cliente, "/api/mapas/55/graphy", CorposPut.Valido);

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Equal(0, leitura.Chamadas);
        var sqls = servidor.Sqls;
        Assert.Equal(1, sqls.Count(s => s.Contains("ST_IsValid", StringComparison.Ordinal)));
        Assert.Equal("COMMIT", sqls[^1]);
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("Auditoria PUT: id 55, coluna graphy atualizada", StringComparison.Ordinal));
        Assert.Empty(servidor.ErrosInternos);
    }

    [Fact]
    public async Task Put409_LockTimeout_Rollback()
    {
        await using var servidor = new ServidorPostgresEscritaFalso(Feliz(c => c.Contem("UPDATE public.mapas") ? new RespostaPg.Erro("55P03") : null));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await using var f = new ApiEscritaFactory { Fonte = fonte };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/56/metry", CorposPut.Valido);

        Http.AssertProblem(r, corpo, HttpStatusCode.Conflict, "Atualização concorrente", instance: "/api/mapas/56/metry");
        Assert.Equal("ROLLBACK", servidor.Sqls[^1]);
    }

    [Theory]
    [InlineData("57014")]
    [InlineData("42501")]
    public async Task Put500_FalhaNoBanco_Rollback_LogSoComSqlState(string sqlState)
    {
        await using var servidor = new ServidorPostgresEscritaFalso(Feliz(c => c.Contem("UPDATE public.mapas") ? new RespostaPg.Erro(sqlState) : null));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await using var f = new ApiEscritaFactory { Fonte = fonte };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/57/graphy", CorposPut.Valido);

        Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno");
        Assert.Equal("ROLLBACK", servidor.Sqls[^1]);
        Assert.DoesNotContain("COMMIT", servidor.Sqls);
        var tudo = f.TextoLogs;
        Assert.Contains($"SqlState {sqlState}", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGREDO", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("POINT(", tudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Put400_PostgisRejeita_SemTransacao()
    {
        var n = 0;
        await using var servidor = new ServidorPostgresEscritaFalso(Feliz(c => c.Contem("ST_IsValid") && n++ == 0 ? new RespostaPg.Erro("XX000") : null));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await using var f = new ApiEscritaFactory { Fonte = fonte };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/58/graphy", CorposPut.Valido);

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, "Geometria inválida",
            "geometria inválida segundo o PostGIS (ST_IsValid)", "/api/mapas/{id}/graphy");
        Assert.DoesNotContain(servidor.Sqls, s => s.StartsWith("BEGIN", StringComparison.Ordinal));
    }

    /// <summary>R-5: cliente desconecta com o UPDATE em andamento -> 499 sem corpo e ROLLBACK, sem COMMIT.</summary>
    [Fact]
    public async Task R5_ClienteDesconectaNoMeioDaTransacao_499_Rollback()
    {
        await using var servidor = new ServidorPostgresEscritaFalso(Feliz(c => c.Contem("UPDATE public.mapas")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(15), new RespostaPg.Comando("UPDATE 1"))
            : null));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await using var f = new ApiEscritaFactory { Fonte = fonte };
        using var cliente = f.CreateClient();
        using var cts = new CancellationTokenSource();

        var envio = Http.EnviarAsync(cliente, HttpMethod.Put, "/api/mapas/59/graphy", Http.Json(CorposPut.Valido), ct: cts.Token);
        Assert.True(await EsperarAsync(() => servidor.Sqls.Any(s => s.StartsWith("UPDATE", StringComparison.Ordinal))));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => envio);
        Assert.True(await EsperarAsync(() => servidor.Sqls.Count > 0 && servidor.Sqls[^1] == "ROLLBACK"), string.Join(" | ", servidor.Sqls));
        Assert.DoesNotContain("COMMIT", servidor.Sqls);
        Assert.DoesNotContain(servidor.Sqls, s => s.StartsWith("INSERT", StringComparison.Ordinal));
        // O 499 sem corpo e dado pelo ExceptionHandlerMiddleware do ASP.NET (requisicao abortada);
        // o servico e o repositorio registram o cancelamento e nada vira "Erro nao tratado"/500.
        Assert.True(await EsperarAsync(() => f.Logs.Mensagens.Any(m => m.Contains("PUT do id 59 na coluna graphy cancelado pelo cliente", StringComparison.Ordinal))), f.TextoLogs);
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("Escrita PUT: cancelada ao atualizar a coluna (UPDATE pela PK)", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("Erro nao tratado", StringComparison.Ordinal));
    }

    /// <summary>D-P11: prazo estoura no meio da transacao -> 500 generico e ROLLBACK.</summary>
    [Fact]
    public async Task PrazoEstouradoNoMeioDaTransacao_500_Rollback()
    {
        await using var servidor = new ServidorPostgresEscritaFalso(Feliz(c => c.Contem("UPDATE public.mapas")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(15), new RespostaPg.Comando("UPDATE 1"))
            : null));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        await using var f = new ApiEscritaFactory { Fonte = fonte, Opcoes = new OpcoesAtualizacao { PrazoTotal = TimeSpan.FromMilliseconds(500) } };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/60/metry", CorposPut.Valido);

        Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno");
        Assert.Equal("ROLLBACK", servidor.Sqls[^1]);
        Assert.DoesNotContain("COMMIT", servidor.Sqls);
    }
}
