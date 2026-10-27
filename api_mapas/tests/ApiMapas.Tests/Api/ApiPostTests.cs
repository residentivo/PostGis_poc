using System.Net;
using System.Text;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Npgsql;

namespace ApiMapas.Tests.Api;

/// <summary>
/// PO6.2 / PO6.3 - POST /api/mapas/graphy|metry pela API em memoria com repositorio falso (CN-1, CN-2,
/// CN-6, CN-9, CN-11): 201 com Location e sem corpo, 400, 409, 413, 415 (RS-6), 500 sem retry,
/// method override (RS-3) e metodos nao mapeados. Nenhum acesso a banco real.
/// </summary>
public class ApiPostTests
{
    private const string TituloCorpo = "Corpo inválido";
    private const string DetalheCorpo = "O corpo deve ser um JSON no formato {\"wkt\":\"...\",\"srid\":4326}.";
    private const string Titulo415 = "Tipo de mídia não suportado";
    private const string Detalhe415 = "Use Content-Type: application/json (charset utf-8 opcional).";

    private static Task<(HttpResponseMessage Resposta, string Corpo)> PostAsync(HttpClient cliente, string coluna, string corpo, string? contentType = "application/json") =>
        Http.EnviarAsync(cliente, HttpMethod.Post, $"/api/mapas/{coluna}", Http.Json(corpo, contentType));

    // ---------------- 201 (CN-1) ----------------

    [Theory]
    [InlineData("graphy", ColunaMapa.Graphy)]
    [InlineData("metry", ColunaMapa.Metry)]
    public async Task Post201_SemCorpo_LocationComIdGerado_SoAColunaDaRota(string coluna, ColunaMapa enumColuna)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, coluna, CorposPut.Valido);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(string.Empty, corpo);
        Assert.Equal(0, r.Content.Headers.ContentLength ?? 0);
        Assert.Equal($"/api/mapas/{RepositorioEscritaFalso.IdCriado}/{coluna}", r.Headers.Location?.OriginalString);
        Assert.False(r.Headers.Location!.IsAbsoluteUri);
        Http.AssertCabecalhosSeguranca(r);
        var chamada = Assert.Single(repo.Chamadas);
        Assert.Equal(("POST", enumColuna), (chamada.Metodo, chamada.Coluna));
        Assert.Null(chamada.Id);
        Assert.Equal(CorposPut.PontoGraphy, chamada.Geometria.Wkt);
        Assert.Equal(4326, chamada.Geometria.Srid);

        var auditoria = Assert.Single(f.Logs.Mensagens, m => m.Contains("Auditoria POST", StringComparison.Ordinal));
        Assert.Contains($"id {RepositorioEscritaFalso.IdCriado} criado com a coluna {coluna} preenchida", auditoria, StringComparison.Ordinal);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("POINT", StringComparison.Ordinal) || m.Contains("-46.63", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(1, "graphy")]
    [InlineData(42, "metry")]
    [InlineData(2147483647, "graphy")]
    public async Task Post201_LocationMontadoDoIdRetornado(int idGerado, string coluna)
    {
        await using var f = new ApiEscritaFactory { Repositorio = new RepositorioEscritaFalso(new ResultadoEscrita.Criado(idGerado)) };
        using var cliente = f.CreateClient();

        var (r, _) = await PostAsync(cliente, coluna, CorposPut.Valido);

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal($"/api/mapas/{idGerado.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{coluna}", r.Headers.Location?.OriginalString);
    }

    [Theory]
    [InlineData("metry", 4326)]
    [InlineData("metry", 4674)]
    [InlineData("metry", 3857)]
    [InlineData("metry", 31983)]
    [InlineData("graphy", 4326)]
    public async Task Post201_SridsDaColuna(string coluna, int srid)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();
        var wkt = srid switch { 3857 => "POINT(-5191195.21 -2698729.46)", 31983 => "POINT(333250 7394600)", _ => "POINT(-46.63 -23.55)" };

        var (r, _) = await PostAsync(cliente, coluna, CorposPut.Com((wkt, srid)));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(srid, Assert.Single(repo.Chamadas).Geometria.Srid);
    }

    [Fact]
    public async Task Post201_Wkt10000()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await PostAsync(cliente, "metry", CorposPut.Com(("POINT(0 0)" + new string(' ', 9_990), 4326)));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
    }

    // ---------------- CN-2: nao idempotente ----------------

    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task DoisPostIguais_DoisIdsDistintos(string coluna)
    {
        var repo = RepositorioEscritaFalso.Sequencial(700);
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (a, _) = await PostAsync(cliente, coluna, CorposPut.Valido);
        var (b, _) = await PostAsync(cliente, coluna, CorposPut.Valido);

        Assert.Equal(HttpStatusCode.Created, a.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b.StatusCode);
        Assert.Equal($"/api/mapas/700/{coluna}", a.Headers.Location!.OriginalString);
        Assert.Equal($"/api/mapas/701/{coluna}", b.Headers.Location!.OriginalString);
        Assert.Equal(2, repo.Chamadas.Count);
        Assert.Equal(2, f.Logs.Mensagens.Count(m => m.Contains("Auditoria POST", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task TresPostSimultaneos_IdsDistintos()
    {
        var repo = RepositorioEscritaFalso.Sequencial(1);
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var respostas = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PostAsync(cliente, "graphy", CorposPut.Valido)));

        Assert.All(respostas, x => Assert.Equal(HttpStatusCode.Created, x.Resposta.StatusCode));
        Assert.Equal(3, respostas.Select(x => x.Resposta.Headers.Location!.OriginalString).Distinct().Count());
    }

    // ---------------- 400 (CN-6) ----------------

    [Theory]
    [MemberData(nameof(ApiEscritaTests.CorposInvalidos), MemberType = typeof(ApiEscritaTests))]
    public async Task Post400_CorpoInvalido_InstanceDaRota_NadaGravado(string json)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, "graphy", json);

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, TituloCorpo, DetalheCorpo, "/api/mapas/graphy");
        Assert.Null(r.Headers.Location);
        Assert.Empty(repo.Chamadas);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("JsonException", StringComparison.Ordinal) || m.Contains("SEGREDO_CORPO", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post400_CorpoComId_Recusado()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, "metry", "{\"id\":5,\"wkt\":\"POINT(0 0)\",\"srid\":4326}");

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, TituloCorpo, DetalheCorpo, "/api/mapas/metry");
        Assert.Empty(repo.Chamadas);
    }

    [Theory]
    [MemberData(nameof(ApiEscritaTests.GeometriasInvalidas), MemberType = typeof(ApiEscritaTests))]
    public async Task Post400_GeometriaInvalida_MotivoFixo_NadaGravado(string coluna, string json, string detalhe)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, coluna, json);

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, "Geometria inválida", detalhe, $"/api/mapas/{coluna}");
        Assert.DoesNotContain("DROP", corpo, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
    }

    [Theory]
    [InlineData(MotivosGeometria.InvalidaPostgis)]
    [InlineData(MotivosGeometria.TipoDiverge)]
    public async Task Post400_GeometriaInvalidaNoBanco(string motivo)
    {
        await using var f = new ApiEscritaFactory { Repositorio = new RepositorioEscritaFalso(new ResultadoEscrita.GeometriaInvalida(motivo)) };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, "metry", CorposPut.Valido);

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, "Geometria inválida", motivo, "/api/mapas/metry");
        Assert.Null(r.Headers.Location);
    }

    // ---------------- 409 (RS-18) ----------------

    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Post409_Conflito_TextosDoPut_SemId(string coluna)
    {
        await using var f = new ApiEscritaFactory { Repositorio = new RepositorioEscritaFalso(ResultadoEscrita.Conflito.Instancia) };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, coluna, CorposPut.Valido);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.Conflict, "Atualização concorrente",
            "Outra atualização de public.mapas está em andamento. Tente novamente.", $"/api/mapas/{coluna}");
        Assert.Equal(Http.Tipo(409), json.GetProperty("type").GetString());
        Assert.Null(r.Headers.Location);
        Assert.Contains(f.Logs.Mensagens, m => m.StartsWith("Warning", StringComparison.Ordinal) && m.Contains("POST: escrita concorrente", StringComparison.Ordinal));
    }

    // ---------------- 413 (64 KiB) ----------------

    [Theory]
    [InlineData(65_536, true, HttpStatusCode.Created)]
    [InlineData(65_536, false, HttpStatusCode.Created)]
    [InlineData(65_537, true, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(65_537, false, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(200_000, true, HttpStatusCode.RequestEntityTooLarge)]
    [InlineData(200_000, false, HttpStatusCode.RequestEntityTooLarge)]
    public async Task Post413_ApartirDe64KiB_ComESemContentLength(int tamanho, bool comContentLength, HttpStatusCode esperado)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();
        var json = CorposPut.Valido[..^1] + new string(' ', tamanho - CorposPut.Valido.Length) + "}";
        Assert.Equal(tamanho, Encoding.UTF8.GetByteCount(json));
        HttpContent conteudo = comContentLength ? Http.Json(json) : ConteudoSemTamanho.Criar(json);

        var (r, corpo) = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/mapas/graphy", conteudo);

        Assert.Equal(esperado, r.StatusCode);
        if (esperado == HttpStatusCode.RequestEntityTooLarge)
        {
            Http.AssertProblem(r, corpo, HttpStatusCode.RequestEntityTooLarge, "Corpo muito grande", "O corpo excede o limite de 65536 bytes.", "/api/mapas/graphy");
            Assert.Empty(repo.Chamadas);
        }
        else
        {
            Assert.Single(repo.Chamadas);
        }
    }

    [Fact]
    public async Task Post413_LimiteConfigurado()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteBytesCorpoPut", "100");
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, "metry", CorposPut.Com((CorposPut.PontoGraphy + new string(' ', 100), 4326)));

        Http.AssertProblem(r, corpo, HttpStatusCode.RequestEntityTooLarge, "Corpo muito grande", "O corpo excede o limite de 100 bytes.", "/api/mapas/metry");
        Assert.Empty(repo.Chamadas);
    }

    // ---------------- 415 (RS-1 / RS-6 nas rotas novas) ----------------

    [Theory]
    [InlineData("graphy", null)]
    [InlineData("metry", null)]
    [InlineData("graphy", "text/plain")]
    [InlineData("metry", "text/plain")]
    [InlineData("graphy", "application/x-www-form-urlencoded")]
    [InlineData("metry", "application/x-www-form-urlencoded")]
    [InlineData("graphy", "multipart/form-data; boundary=x")]
    [InlineData("metry", "multipart/form-data; boundary=x")]
    [InlineData("graphy", "application/json, text/plain")]
    [InlineData("metry", "application/json, text/plain")]
    [InlineData("graphy", "application/json; charset=latin1")]
    [InlineData("metry", "application/json; charset=latin1")]
    [InlineData("graphy", "application/json; charset=iso-8859-1")]
    [InlineData("metry", "text/json")]
    [InlineData("graphy", "application/merge-patch+json")]
    [InlineData("metry", "*/*")]
    public async Task Post415_ContentTypeRecusado_NadaGravado(string coluna, string? contentType)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, coluna, CorposPut.Valido, contentType);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.UnsupportedMediaType, Titulo415, Detalhe415, $"/api/mapas/{coluna}");
        Assert.Equal(Http.Tipo(415), json.GetProperty("type").GetString());
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task Post415_SemCorpoESemContentType()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/mapas/graphy");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task Post415_ContentTypeAntesDoTamanho()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteBytesCorpoPut", "10");
        using var cliente = f.CreateClient();

        var (r, _) = await PostAsync(cliente, "graphy", CorposPut.Valido, "text/plain");

        Assert.Equal(HttpStatusCode.UnsupportedMediaType, r.StatusCode);
    }

    // ---------------- 500 sem retry (RS-17) ----------------

    public static TheoryData<string> Erros500 => new() { "23505-pkey", "23505-outra", "23514", "57014", "timeout", "generica", "prazo" };

    [Theory]
    [MemberData(nameof(Erros500))]
    public async Task Post500_Generico_SemRetry_SemVazamento(string cenario)
    {
        var repo = cenario switch
        {
            "23505-pkey" => RepositorioEscritaFalso.QueLanca(new FalhaBancoEscritaException("inserir a geometria (INSERT ... RETURNING id)", "23505", "PostgresException")),
            "23505-outra" => RepositorioEscritaFalso.QueLanca(new FalhaBancoEscritaException("inserir a geometria (INSERT ... RETURNING id)", "23505", "PostgresException")),
            "23514" => RepositorioEscritaFalso.QueLanca(new FalhaBancoEscritaException("inserir a geometria (INSERT ... RETURNING id)", "23514", "PostgresException")),
            "57014" => RepositorioEscritaFalso.QueLanca(new FalhaBancoEscritaException("inserir a geometria (INSERT ... RETURNING id)", "57014", "PostgresException")),
            "timeout" => RepositorioEscritaFalso.QueLanca(new TimeoutException("SEGREDO 127.0.0.1")),
            "generica" => RepositorioEscritaFalso.QueLanca(new InvalidOperationException("SEGREDO POINT(1 2) password=SenhaEscritaFicticia#456")),
            _ => RepositorioEscritaFalso.Pendurado(),
        };
        await using var f = new ApiEscritaFactory
        {
            Repositorio = repo,
            Opcoes = cenario == "prazo" ? new OpcoesAtualizacao { PrazoTotal = TimeSpan.FromMilliseconds(200) } : null,
        };
        using var cliente = f.CreateClient();

        var (r, corpo) = await PostAsync(cliente, "graphy", CorposPut.Valido);

        var json = Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno",
            "Não foi possível processar a requisição. Informe o traceId ao suporte.");
        Assert.Null(r.Headers.Location);
        Assert.Single(repo.Chamadas); // sem nova tentativa
        var traceId = json.GetProperty("traceId").GetString()!;
        Assert.Contains(f.Logs.Mensagens, m => m.Contains(traceId, StringComparison.Ordinal) && m.Contains("POST /api/mapas/graphy", StringComparison.Ordinal));
    }

    // ---------------- RS-3: method override; CN-11: metodos nao mapeados ----------------

    [Theory]
    [InlineData("graphy", "PUT")]
    [InlineData("graphy", "DELETE")]
    [InlineData("metry", "PUT")]
    [InlineData("metry", "DELETE")]
    [InlineData("metry", "patch")]
    public async Task PostComMethodOverride_SoInsere(string coluna, string overrideMetodo)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Post, $"/api/mapas/{coluna}?_method={overrideMetodo}", Http.Json(CorposPut.Valido), req =>
        {
            req.Headers.Add("X-HTTP-Method-Override", overrideMetodo);
            req.Headers.Add("X-HTTP-Method", overrideMetodo);
            req.Headers.Add("X-Method-Override", overrideMetodo);
        });

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        var chamada = Assert.Single(repo.Chamadas);
        Assert.Equal("POST", chamada.Metodo);
    }

    public static TheoryData<string, string?> PostNaRotaComId => new()
    {
        { "graphy", null },
        { "metry", null },
        { "graphy", "PUT" },
        { "metry", "PUT" },
    };

    [Theory]
    [MemberData(nameof(PostNaRotaComId))]
    public async Task PostNaRotaComId_405_NaoGrava(string coluna, string? overrideMetodo)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, corpo) = await Http.EnviarAsync(cliente, HttpMethod.Post, $"/api/mapas/1/{coluna}", Http.Json(CorposPut.Valido), req =>
        {
            if (overrideMetodo is not null)
            {
                req.Headers.Add("X-HTTP-Method-Override", overrideMetodo);
            }
        });

        Http.AssertProblem(r, corpo, HttpStatusCode.MethodNotAllowed);
        Assert.Empty(repo.Chamadas);
    }

    public static TheoryData<string, string> MetodosQueNaoGravam => new()
    {
        { "PATCH", "/api/mapas/graphy" },
        { "DELETE", "/api/mapas/graphy" },
        { "PUT", "/api/mapas/graphy" },
        { "GET", "/api/mapas/metry" },
        { "HEAD", "/api/mapas/metry" },
        { "OPTIONS", "/api/mapas/graphy" },
        { "PATCH", "/api/mapas/1/metry" },
        { "DELETE", "/api/mapas/1/graphy" },
        { "HEAD", "/api/mapas/1/graphy" },
        { "OPTIONS", "/api/mapas/1/metry" },
    };

    [Theory]
    [MemberData(nameof(MetodosQueNaoGravam))]
    public async Task PatchDeleteHeadOptions_NaoGravam(string metodo, string caminho)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();
        HttpContent? conteudo = metodo is "GET" or "HEAD" ? null : Http.Json(CorposPut.Valido);

        var (r, _) = await Http.EnviarAsync(cliente, new HttpMethod(metodo), caminho, conteudo, req => req.Headers.Add("Origin", "http://localhost"));

        Assert.False(r.IsSuccessStatusCode, $"{metodo} {caminho}: {(int)r.StatusCode}");
        Assert.DoesNotContain(r.Headers, h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase));
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task HostMalicioso_Post400_NaoGrava()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var (r, _) = await Http.EnviarAsync(cliente, HttpMethod.Post, "/api/mapas/graphy", Http.Json(CorposPut.Valido), req => req.Headers.Host = "evil.example.com");

        Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
        Assert.Empty(repo.Chamadas);
    }
}

/// <summary>
/// PO6.2 - pilha completa (API -> servicos -> MapasEscritaRepository real) contra o
/// ServidorPostgresEscritaFalso: SQL e fluxo do POST (CN-1, CN-3, CN-9, CN-12), PUT novo
/// (rowcount 0/1/&gt;1) e logs sem WKT, corpo, Detail, Hint ou credenciais.
/// </summary>
public class ApiPostPilhaCompletaTests
{
    private const int IdGerado = 77;

    // Sensivel a maiusculas: "POINT(" e nao "point" (que aparece em "MapasEndpoints" no stack trace).
    private static readonly string[] ProibidosNoLog =
    {
        "POINT(", "LINESTRING(", "-46.633309", "-23.55052", "-46.64", "SEGREDO", "detalhe", "hint ", "erro simulado",
        "p_escrita_falsa", "u_escrita_falso", ApiEscritaFactory.SenhaEscrita, ApiEscritaFactory.UsuarioEscrita, "SenhaFicticia#123",
        "\"wkt\"", "Password=",
    };

    private static Func<ComandoPg, RespostaPg?> Feliz(Func<ComandoPg, RespostaPg?>? antes = null, int atualizadas = 1) => c =>
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
            return new RespostaPg.Comando($"UPDATE {atualizadas}");
        }

        if (c.Contem("INSERT INTO public.mapas"))
        {
            return new RespostaPg.Linhas(new[] { new ColunaPg("id", ColunaPg.Int4) }, new[] { new object?[] { IdGerado } }, "INSERT 0 1");
        }

        return null;
    };

    private sealed class Pilha : IAsyncDisposable
    {
        private Pilha(ServidorPostgresEscritaFalso servidor, FonteDadosEscrita fonte, ApiEscritaFactory fabrica)
        {
            Servidor = servidor;
            Fonte = fonte;
            Fabrica = fabrica;
            Cliente = fabrica.CreateClient();
        }

        public ServidorPostgresEscritaFalso Servidor { get; }

        public FonteDadosEscrita Fonte { get; }

        public ApiEscritaFactory Fabrica { get; }

        public HttpClient Cliente { get; }

        /// <summary>SQLs da API (sem o DISCARD ALL que o pool do Npgsql envia ao reaproveitar a conexao).</summary>
        public List<string> Sqls => Servidor.Sqls.Where(x => x != "DISCARD ALL").ToList();

        public static Pilha Criar(Func<ComandoPg, RespostaPg?> responder, OpcoesAtualizacao? opcoes = null)
        {
            var servidor = new ServidorPostgresEscritaFalso(responder);
            var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
            var f = new ApiEscritaFactory { Fonte = fonte, Opcoes = opcoes }.ComConfiguracao("Verboso", "true");
            return new Pilha(servidor, fonte, f);
        }

        public void AssertLogsLimpos()
        {
            var tudo = Fabrica.TextoLogs;
            foreach (var termo in ProibidosNoLog)
            {
                Assert.DoesNotContain(termo, tudo, StringComparison.Ordinal);
            }

            Assert.Empty(Servidor.ErrosInternos);
        }

        public async ValueTask DisposeAsync()
        {
            Cliente.Dispose();
            await Fabrica.DisposeAsync();
            await Fonte.DisposeAsync();
            await Servidor.DisposeAsync();
        }
    }

    private static async Task<bool> EsperarAsync(Func<bool> condicao, int ms = 5000)
    {
        var limite = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condicao() && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        return condicao();
    }

    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Post201_SqlConstante_SemIdNoInsert_LocationDoReturning(string coluna)
    {
        await using var p = Pilha.Criar(Feliz());

        var (r, corpo) = await Http.EnviarAsync(p.Cliente, HttpMethod.Post, $"/api/mapas/{coluna}", Http.Json(CorposPut.Valido));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Equal(string.Empty, corpo);
        Assert.Equal($"/api/mapas/{IdGerado}/{coluna}", r.Headers.Location!.OriginalString);
        var sqls = p.Sqls;
        Assert.Equal(5, sqls.Count);
        Assert.StartsWith("SELECT ST_IsValid", sqls[0], StringComparison.Ordinal);
        Assert.Equal("BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED", sqls[1]);
        Assert.Equal("SET LOCAL lock_timeout = '5s'", sqls[2]);
        Assert.Equal($"INSERT INTO public.mapas ({coluna}) VALUES (ST_GeomFromText($1, $2){(coluna == "graphy" ? "::geography" : string.Empty)}) RETURNING id", sqls[3]);
        Assert.Equal("COMMIT", sqls[4]);
        Assert.DoesNotContain(sqls, s => s.Contains("UPDATE", StringComparison.OrdinalIgnoreCase)
            || s.Contains("DELETE", StringComparison.OrdinalIgnoreCase)
            || s.Contains("nextval", StringComparison.OrdinalIgnoreCase)
            || s.Contains("pg_advisory", StringComparison.OrdinalIgnoreCase));
        var insert = p.Servidor.Comandos.ToList()[3];
        Assert.Equal(new[] { 25, 23 }, insert.Oids);
        Assert.Equal(new object?[] { CorposPut.PontoGraphy, 4326 }, insert.Parametros);
        Assert.Single(p.Servidor.Comandos.Select(c => c.Conexao).Distinct());
        Assert.Contains(p.Fabrica.Logs.Mensagens, m => m.Contains($"Auditoria POST: id {IdGerado} criado com a coluna {coluna}", StringComparison.Ordinal));
        p.AssertLogsLimpos();
    }

    [Theory]
    [InlineData("PUT")]
    [InlineData("DELETE")]
    public async Task PostComOverride_SoInsert(string overrideMetodo)
    {
        await using var p = Pilha.Criar(Feliz());

        var (r, _) = await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/graphy", Http.Json(CorposPut.Valido),
            req => req.Headers.Add("X-HTTP-Method-Override", overrideMetodo));

        Assert.Equal(HttpStatusCode.Created, r.StatusCode);
        Assert.Single(p.Sqls, s => s.StartsWith("INSERT INTO public.mapas", StringComparison.Ordinal));
        Assert.DoesNotContain(p.Sqls, s => s.StartsWith("UPDATE", StringComparison.Ordinal) || s.StartsWith("DELETE", StringComparison.Ordinal));
    }

    /// <summary>CN-3 no falso: POST de metry e depois PUT de graphy no id devolvido; o PUT so faz UPDATE de graphy pela PK.</summary>
    [Fact]
    public async Task PostMetry_DepoisPutGraphy_MesmoId_UpdateSoDaOutraColuna()
    {
        await using var p = Pilha.Criar(Feliz());

        var (post, _) = await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/metry", Http.Json(CorposPut.Valido));
        Assert.Equal(HttpStatusCode.Created, post.StatusCode);
        var location = post.Headers.Location!.OriginalString;
        Assert.Equal($"/api/mapas/{IdGerado}/metry", location);
        var caminhoPut = location.Replace("/metry", "/graphy", StringComparison.Ordinal);

        var (put, _) = await Http.PutAsync(p.Cliente, caminhoPut, CorposPut.Com((CorposPut.LinhaGraphy, 4326)));

        Assert.Equal(HttpStatusCode.NoContent, put.StatusCode);
        var sqls = p.Sqls;
        Assert.True(10 == sqls.Count, string.Join(" | ", sqls));
        Assert.StartsWith("INSERT INTO public.mapas (metry)", sqls[3], StringComparison.Ordinal);
        Assert.Equal("COMMIT", sqls[4]);
        Assert.Contains("::geography::geometry", sqls[5], StringComparison.Ordinal); // W1.V (graphy)
        Assert.Equal("UPDATE public.mapas SET graphy = ST_GeomFromText($1, $2)::geography WHERE id = $3", sqls[8]);
        Assert.Equal("COMMIT", sqls[9]);
        Assert.Single(sqls, s => s.StartsWith("INSERT", StringComparison.Ordinal));
        Assert.DoesNotContain(sqls, s => s.Contains("SET metry", StringComparison.Ordinal));
        var update = p.Servidor.Comandos.Single(c => c.Sql.StartsWith("UPDATE", StringComparison.Ordinal));
        Assert.Equal(new object?[] { CorposPut.LinhaGraphy, 4326, IdGerado }, update.Parametros);
        p.AssertLogsLimpos();
    }

    [Theory]
    [InlineData("55P03")]
    [InlineData("40P01")]
    public async Task Post409_LockOuDeadlock_Rollback(string sqlState)
    {
        await using var p = Pilha.Criar(Feliz(c => c.Contem("INSERT INTO") ? new RespostaPg.Erro(sqlState) : null));

        var (r, corpo) = await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/metry", Http.Json(CorposPut.Valido));

        Http.AssertProblem(r, corpo, HttpStatusCode.Conflict, "Atualização concorrente",
            "Outra atualização de public.mapas está em andamento. Tente novamente.", "/api/mapas/metry");
        Assert.Equal("ROLLBACK", p.Sqls[^1]);
        Assert.DoesNotContain("COMMIT", p.Sqls);
        Assert.Contains(p.Fabrica.Logs.Mensagens, m => m.StartsWith("Warning", StringComparison.Ordinal) && m.Contains($"SqlState {sqlState}", StringComparison.Ordinal));
        p.AssertLogsLimpos();
    }

    [Theory]
    [InlineData("23505", "mapas_pkey", "id gerado ja existe; conferir a sequencia")]
    [InlineData("23505", "outra_unica", "restricao violada")]
    [InlineData("23514", "mapas_nao_vazia", "restricao violada")]
    public async Task Post500_Restricao_SemRetry_Rollback_LogErrorSoComSqlStateEConstraint(string sqlState, string constraint, string texto)
    {
        await using var p = Pilha.Criar(Feliz(c => c.Contem("INSERT INTO") ? new RespostaPg.Erro(sqlState, Constraint: constraint) : null));

        var (r, corpo) = await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/graphy", Http.Json(CorposPut.Valido));

        Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno");
        Assert.DoesNotContain(constraint, corpo, StringComparison.Ordinal);
        Assert.Single(p.Sqls, s => s.StartsWith("INSERT", StringComparison.Ordinal));
        Assert.Equal("ROLLBACK", p.Sqls[^1]);
        var erro = Assert.Single(p.Fabrica.Logs.Mensagens, m => m.Contains(texto, StringComparison.Ordinal));
        Assert.StartsWith("Error", erro, StringComparison.Ordinal);
        Assert.Contains($"SqlState {sqlState}, constraint {constraint}", erro, StringComparison.Ordinal);
        p.AssertLogsLimpos();
    }

    [Fact]
    public async Task Post400_PostgisRejeita_SemTransacao()
    {
        await using var p = Pilha.Criar(Feliz(c => c.Contem("ST_IsValid") ? RespostaPg.Validacao(false, "POLYGON") : null));

        var (r, corpo) = await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/metry", Http.Json(CorposPut.Valido));

        Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, "Geometria inválida", MotivosGeometria.InvalidaPostgis, "/api/mapas/metry");
        Assert.DoesNotContain(p.Sqls, s => s.StartsWith("BEGIN", StringComparison.Ordinal) || s.StartsWith("INSERT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_ClienteDesconecta_Rollback_SemCommit()
    {
        await using var p = Pilha.Criar(Feliz(c => c.Contem("INSERT INTO")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(15), new RespostaPg.Comando("INSERT 0 1"))
            : null));
        using var cts = new CancellationTokenSource();

        var envio = Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/graphy", Http.Json(CorposPut.Valido), ct: cts.Token);
        Assert.True(await EsperarAsync(() => p.Sqls.Any(s => s.StartsWith("INSERT", StringComparison.Ordinal))));
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => envio);
        Assert.True(await EsperarAsync(() => p.Sqls[^1] == "ROLLBACK"), string.Join(" | ", p.Sqls));
        Assert.DoesNotContain("COMMIT", p.Sqls);
        Assert.True(await EsperarAsync(() => p.Fabrica.Logs.Mensagens.Any(m => m.Contains("POST na coluna graphy cancelado pelo cliente", StringComparison.Ordinal))));
        Assert.DoesNotContain(p.Fabrica.Logs.Mensagens, m => m.Contains("Erro nao tratado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_PrazoEstourado_500_Rollback()
    {
        await using var p = Pilha.Criar(Feliz(c => c.Contem("INSERT INTO")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(15), new RespostaPg.Comando("INSERT 0 1"))
            : null), new OpcoesAtualizacao { PrazoTotal = TimeSpan.FromMilliseconds(500) });

        var (r, corpo) = await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/metry", Http.Json(CorposPut.Valido));

        Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno");
        Assert.Equal("ROLLBACK", p.Sqls[^1]);
        Assert.DoesNotContain("COMMIT", p.Sqls);
        Assert.Contains(p.Fabrica.Logs.Mensagens, m => m.Contains("POST na coluna metry: prazo total", StringComparison.Ordinal));
    }

    // ---------------- PUT novo (D-N6): rowcount 0 / 1 / >1 ----------------

    [Theory]
    [InlineData(0, HttpStatusCode.NotFound, "ROLLBACK")]
    [InlineData(1, HttpStatusCode.NoContent, "COMMIT")]
    [InlineData(2, HttpStatusCode.InternalServerError, "ROLLBACK")]
    [InlineData(5, HttpStatusCode.InternalServerError, "ROLLBACK")]
    public async Task Put_Rowcount_404_204_500(int linhas, HttpStatusCode esperado, string fim)
    {
        await using var p = Pilha.Criar(Feliz(atualizadas: linhas));

        var (r, corpo) = await Http.PutAsync(p.Cliente, "/api/mapas/31/metry", CorposPut.Valido);

        Assert.True(esperado == r.StatusCode, corpo);
        Assert.Equal(fim, p.Sqls[^1]);
        Assert.DoesNotContain(p.Sqls, s => s.StartsWith("INSERT", StringComparison.Ordinal) || s.StartsWith("DELETE", StringComparison.Ordinal));
        Assert.Equal("UPDATE public.mapas SET metry = ST_GeomFromText($1, $2) WHERE id = $3", p.Sqls[3]);
        if (linhas == 0)
        {
            Http.AssertProblem(r, corpo, HttpStatusCode.NotFound, "Mapa não encontrado", "Nenhuma linha encontrada em public.mapas para o id 31.", "/api/mapas/31/metry");
        }

        if (linhas > 1)
        {
            Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno");
            Assert.Contains(p.Fabrica.Logs.Mensagens, m => m.StartsWith("Error", StringComparison.Ordinal)
                && m.Contains($"alterou {linhas} linhas do id 31", StringComparison.Ordinal));
        }

        p.AssertLogsLimpos();
    }

    [Fact]
    public async Task Put_Repetido_MesmoEstado_Idempotente()
    {
        await using var p = Pilha.Criar(Feliz());

        var (r1, _) = await Http.PutAsync(p.Cliente, "/api/mapas/8/graphy", CorposPut.Valido);
        var (r2, _) = await Http.PutAsync(p.Cliente, "/api/mapas/8/graphy", CorposPut.Valido);

        Assert.Equal(HttpStatusCode.NoContent, r1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, r2.StatusCode);
        var updates = p.Servidor.Comandos.Where(c => c.Sql.StartsWith("UPDATE", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, updates.Count);
        Assert.Equal(updates[0].Sql, updates[1].Sql);
        Assert.Equal(updates[0].Parametros, updates[1].Parametros);
    }

    /// <summary>Logs: nem WKT, nem corpo, nem MessageText/Detail/Hint do banco, nem credenciais, em nenhum caminho.</summary>
    [Fact]
    public async Task Logs_NuncaContemWktCorpoDetailOuCredenciais_EmTodosOsCaminhos()
    {
        var n = 0;
        await using var p = Pilha.Criar(Feliz(c =>
        {
            if (!c.Contem("INSERT INTO") && !c.Contem("UPDATE public.mapas"))
            {
                return null;
            }

            return (Interlocked.Increment(ref n) % 4) switch
            {
                1 => new RespostaPg.Erro("23505", Constraint: "mapas_pkey"),
                2 => new RespostaPg.Erro("57014"),
                3 => new RespostaPg.Erro("55P03"),
                _ => null,
            };
        }));

        var corpos = new[] { CorposPut.Valido, CorposPut.Com((CorposPut.LinhaGraphy, 4326)), "{\"wkt\":\"POINT(1 2)\",\"srid\":4326,\"x\":\"SEGREDO_CORPO\"}", CorposPut.Com(("POINT(999 0)", 4326)) };
        foreach (var corpo in corpos)
        {
            await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/graphy", Http.Json(corpo));
            await Http.PutAsync(p.Cliente, "/api/mapas/3/metry", corpo);
            await Http.EnviarAsync(p.Cliente, HttpMethod.Post, "/api/mapas/metry", Http.Json(corpo), req => req.Headers.Add("Origin", "http://SEGREDO-ORIGEM.example"));
        }

        Assert.True(n >= 4);
        p.AssertLogsLimpos();
    }
}
