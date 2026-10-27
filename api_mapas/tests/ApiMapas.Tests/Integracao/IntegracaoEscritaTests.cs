using System.Net;
using System.Text.Json;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Api;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace ApiMapas.Tests.Integracao;

/// <summary>Fact que so roda com PG_RUN_INTEGRATION=1 e as roles de escrita e de teste definidas.</summary>
public sealed class FactIntegracaoEscritaAttribute : FactAttribute
{
    public FactIntegracaoEscritaAttribute()
    {
        if (!AmbienteOriginal.IntegracaoEscritaHabilitada)
        {
            Skip = "Integracao da escrita opt-in: PG_RUN_INTEGRATION=1, PG_USER/PG_PASSWORD (api_mapas_leitura), " +
                   "PG_ESCRITA_USER/PG_ESCRITA_PASSWORD (api_mapas_escrita) e PG_TESTE_USER/PG_TESTE_PASSWORD (mapas_teste).";
        }
    }
}

/// <summary>Theory equivalente a <see cref="FactIntegracaoEscritaAttribute"/>.</summary>
public sealed class TheoryIntegracaoEscritaAttribute : TheoryAttribute
{
    public TheoryIntegracaoEscritaAttribute()
    {
        if (!AmbienteOriginal.IntegracaoEscritaHabilitada)
        {
            Skip = "Integracao da escrita opt-in (ver FactIntegracaoEscrita).";
        }
    }
}

/// <summary>Linha de public.mapas de um id (WKT de graphy, WKT/SRID de metry).</summary>
public sealed record LinhaMapa(string? Graphy, string? Metry, int? SridMetry);

/// <summary>
/// P6.5 / PO6.4 - integracao opt-in do PUT e do POST com o banco e as roles reais (pulada sem
/// PG_RUN_INTEGRATION=1 e as roles). R-3 / RS-7: todo id criado (por mapas_teste ou pelo POST, lido
/// do Location) e apagado no finally do proprio teste com mapas_teste, conferindo rowcount == 1;
/// nunca com casaos. O PUT e o POST usam api_mapas_escrita (PG_ESCRITA_*) e o GET api_mapas_leitura
/// (PG_USER). Pre-requisito: migracao para PRIMARY KEY aplicada (CN-13 confere o estado).
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(ColecaoIntegracao))]
public sealed class IntegracaoEscritaTests : IAsyncLifetime
{
    private readonly List<int> _ids = new();
    private NpgsqlDataSource? _teste;
    private NpgsqlDataSource? _escrita;
    private NpgsqlDataSource? _leitura;

    private static string ConnectionString(string usuarioVar, string senhaVar, string app)
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = AmbienteOriginal.Ler("PG_HOST") ?? ConexaoConfig.HostPadrao,
            Port = int.Parse(AmbienteOriginal.Ler("PG_PORT") ?? "5432", System.Globalization.CultureInfo.InvariantCulture),
            Database = AmbienteOriginal.Ler("PG_DATABASE") ?? ConexaoConfig.BasePadrao,
            Username = AmbienteOriginal.Ler(usuarioVar),
            Password = AmbienteOriginal.Ler(senhaVar),
            SslMode = Enum.Parse<SslMode>(AmbienteOriginal.Ler("PG_SSLMODE") ?? "Require", ignoreCase: true),
            ApplicationName = app,
            Timeout = 10,
            CommandTimeout = 30,
            MaxPoolSize = 3,
        };
        return builder.ConnectionString;
    }

    private NpgsqlDataSource Teste => _teste!;

    private NpgsqlDataSource Escrita => _escrita!;

    private NpgsqlDataSource Leitura => _leitura!;

    public Task InitializeAsync()
    {
        if (AmbienteOriginal.IntegracaoEscritaHabilitada)
        {
            _teste = NpgsqlDataSource.Create(ConnectionString("PG_TESTE_USER", "PG_TESTE_PASSWORD", "ApiMapas.Tests.MapasTeste"));
            _escrita = NpgsqlDataSource.Create(ConnectionString("PG_ESCRITA_USER", "PG_ESCRITA_PASSWORD", "ApiMapas.Tests.Escrita"));
            _leitura = NpgsqlDataSource.Create(ConnectionString("PG_USER", "PG_PASSWORD", "ApiMapas.Tests.Leitura"));
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        if (_teste is not null)
        {
            // Rede de seguranca: os testes ja apagam no proprio finally (ComLimpezaAsync).
            await ApagarCriadosAsync();
            await _teste.DisposeAsync();
        }

        if (_escrita is not null)
        {
            await _escrita.DisposeAsync();
        }

        if (_leitura is not null)
        {
            await _leitura.DisposeAsync();
        }
    }

    /// <summary>R-3: cria um id novo com mapas_teste (INSERT sem id; a identity gera o valor).</summary>
    private async Task<int> CriarIdAsync(string? graphy, string? metry, int sridMetry = 4326)
    {
        await using var cmd = Teste.CreateCommand(
            "INSERT INTO public.mapas (graphy, metry) VALUES (ST_GeomFromText(@g, 4326)::geography, ST_GeomFromText(@m, @s)) RETURNING id");
        cmd.Parameters.Add(new NpgsqlParameter("g", NpgsqlDbType.Text) { Value = (object?)graphy ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("m", NpgsqlDbType.Text) { Value = (object?)metry ?? DBNull.Value });
        cmd.Parameters.Add(new NpgsqlParameter("s", NpgsqlDbType.Integer) { Value = sridMetry });
        var id = (int)(await cmd.ExecuteScalarAsync())!;
        _ids.Add(id);
        return id;
    }

    /// <summary>
    /// RS-7: apaga com mapas_teste cada id criado (por mapas_teste ou pelo POST) e devolve os que nao
    /// tiveram rowcount == 1. Remove da lista o que foi tratado.
    /// </summary>
    private async Task<List<string>> ApagarCriadosAsync()
    {
        var falhas = new List<string>();
        foreach (var id in _ids.ToList())
        {
            await using var cmd = Teste.CreateCommand("DELETE FROM public.mapas WHERE id = @id");
            cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id });
            var linhas = await cmd.ExecuteNonQueryAsync();
            if (linhas != 1)
            {
                falhas.Add($"id {id}: rowcount {linhas}");
            }

            _ids.Remove(id);
        }

        return falhas;
    }

    /// <summary>Executa o teste e, no finally, apaga todos os ids criados conferindo rowcount == 1.</summary>
    private async Task ComLimpezaAsync(Func<Task> teste)
    {
        List<string> falhas;
        try
        {
            await teste();
        }
        finally
        {
            falhas = await ApagarCriadosAsync();
        }

        Assert.Empty(falhas);
    }

    /// <summary>RS-7: le o id do Location do 201 (/api/mapas/{n}/{coluna}) e registra para a limpeza.</summary>
    private int RegistrarIdDoLocation(HttpResponseMessage resposta, string coluna)
    {
        Assert.Equal(HttpStatusCode.Created, resposta.StatusCode);
        var location = resposta.Headers.Location?.OriginalString ?? string.Empty;
        var m = System.Text.RegularExpressions.Regex.Match(location, "^/api/mapas/([1-9][0-9]{0,9})/" + coluna + "$");
        Assert.True(m.Success, location);
        var id = int.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        _ids.Add(id);
        return id;
    }

    private static Task<(HttpResponseMessage Resposta, string Corpo)> PostAsync(HttpClient cliente, string coluna, (string Wkt, int Srid) item) =>
        Http.EnviarAsync(cliente, HttpMethod.Post, $"/api/mapas/{coluna}", Http.Json(CorposPut.Com(item)));

    /// <summary>Estado do id (como multiconjunto; a ordem por ctid nao e garantida depois de um PUT).</summary>
    private async Task<List<LinhaMapa>> EstadoAsync(int id)
    {
        await using var cmd = Teste.CreateCommand(
            "SELECT ST_AsText(graphy::geometry), ST_AsText(metry), ST_SRID(metry) FROM public.mapas WHERE id = @id");
        cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id });
        var linhas = new List<LinhaMapa>();
        await using var leitor = await cmd.ExecuteReaderAsync();
        while (await leitor.ReadAsync())
        {
            linhas.Add(new LinhaMapa(
                leitor.IsDBNull(0) ? null : leitor.GetString(0),
                leitor.IsDBNull(1) ? null : leitor.GetString(1),
                leitor.IsDBNull(2) ? null : leitor.GetInt32(2)));
        }

        return linhas.OrderBy(l => l.Graphy, StringComparer.Ordinal).ThenBy(l => l.Metry, StringComparer.Ordinal).ToList();
    }

    private static ApiEscritaFactory FabricaReal()
    {
        var f = new ApiEscritaFactory { Verificador = null, LeituraReal = true };
        foreach (var nome in new[] { "PG_HOST", "PG_PORT", "PG_DATABASE", "PG_USER", "PG_PASSWORD", "PG_SSLMODE", "PG_ESCRITA_USER", "PG_ESCRITA_PASSWORD" })
        {
            f.ComVariavel(nome, AmbienteOriginal.Ler(nome));
        }

        return f;
    }

    /// <summary>GET de uma geometria (D-N7): o wkt, ou null no 404.</summary>
    private static async Task<string?> GetWktAsync(HttpClient cliente, int id, string coluna)
    {
        var r = await cliente.GetAsync($"/api/mapas/{id}/{coluna}");
        if (r.StatusCode == HttpStatusCode.NotFound)
        {
            return null;
        }

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        using var doc = JsonDocument.Parse(await r.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("wkt").GetString();
    }

    /// <summary>Segura o lock de linha do id numa transacao da role de escrita (o PUT fica parado no UPDATE).</summary>
    private async Task<(NpgsqlConnection Conexao, NpgsqlTransaction Transacao)> TravarLinhaAsync(int id)
    {
        var conexao = await Escrita.OpenConnectionAsync();
        var tx = await conexao.BeginTransactionAsync();
        await using var cmd = new NpgsqlCommand("SELECT 1 FROM public.mapas WHERE id = @id FOR UPDATE", conexao, tx);
        cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id });
        await cmd.ExecuteNonQueryAsync();
        return (conexao, tx);
    }

    private const string P1 = "POINT(-46.5 -23.5)";
    private const string L1 = "LINESTRING(-46.6 -23.6,-46.5 -23.5)";
    private const string Pol1 = "POLYGON((-46.7 -23.7,-46.6 -23.7,-46.6 -23.6,-46.7 -23.7))";

    // ---------------- CP-1 / CP-2 / CP-3 (uma geometria por id) ----------------

    [FactIntegracaoEscrita]
    public async Task Cp1_Cp2_Cp3_PutGraphy_SubstituiAGeometria_PreservaMetry_Idempotente()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(1 1)", "POINT(10 10)");
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();
            var metryAntes = await GetWktAsync(cliente, id, "metry");

            var corpo = CorposPut.Com((L1, 4326));
            var (r1, c1) = await Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", corpo);
            Assert.True(r1.StatusCode == HttpStatusCode.NoContent, c1);

            // CP-1: o GET devolve a geometria enviada; a antiga (POINT(1 1)) sumiu.
            Assert.Equal(L1, await GetWktAsync(cliente, id, "graphy"));

            // CP-2: a outra coluna do mesmo id nao muda.
            Assert.Equal(metryAntes, await GetWktAsync(cliente, id, "metry"));
            var estado1 = await EstadoAsync(id);
            Assert.Single(estado1);

            // CP-3: repetir o mesmo PUT gera o mesmo estado.
            var (r2, _) = await Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", corpo);
            Assert.Equal(HttpStatusCode.NoContent, r2.StatusCode);
            Assert.Equal(estado1, await EstadoAsync(id));

            var auditorias = f.Logs.Mensagens.Where(m => m.Contains("Auditoria PUT", StringComparison.Ordinal)).ToList();
            Assert.Equal(2, auditorias.Count);
            Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("-46.6", StringComparison.Ordinal));
        });
    }

    [FactIntegracaoEscrita]
    public async Task Cp1_PutMetry_SridsDaColuna_PreencheColunaNula()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(2 2)", null);
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();
            var itens = new[] { ("POINT(-46.63 -23.55)", 4326), ("POINT(-46.63 -23.55)", 4674), ("POINT(-5191195.21 -2698729.46)", 3857), ("POINT(333250 7394600)", 31983) };

            foreach (var item in itens)
            {
                var (r, c) = await Http.PutAsync(cliente, $"/api/mapas/{id}/metry", CorposPut.Com(item));
                Assert.True(r.StatusCode == HttpStatusCode.NoContent, c);
                var linha = Assert.Single(await EstadoAsync(id));
                Assert.Equal(item.Item2, linha.SridMetry);
                Assert.Equal("POINT(2 2)", linha.Graphy);
            }
        });
    }

    // ---------------- CP-5 (id sem linhas) ----------------

    [FactIntegracaoEscrita]
    public async Task Cp5_IdSemLinhas_404_NadaInserido()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(6 6)", null);
            await using (var cmd = Teste.CreateCommand("DELETE FROM public.mapas WHERE id = @id"))
            {
                cmd.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id });
                Assert.Equal(1, await cmd.ExecuteNonQueryAsync());
            }

            _ids.Remove(id);
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();

            var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", CorposPut.Com((P1, 4326)));

            Http.AssertProblem(r, corpo, HttpStatusCode.NotFound, "Mapa não encontrado", instance: $"/api/mapas/{id}/graphy");
            Assert.Empty(await EstadoAsync(id));
        });
    }

    // ---------------- CP-7 (rollback) ----------------

    [FactIntegracaoEscrita]
    public async Task Cp7_ItemInvalidoNoPostgis_400_EstadoIntacto()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(7 7)", "POINT(70 70)");
            var antes = await EstadoAsync(id);
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();

            // Gravata borboleta: passa no validador local, ST_IsValid = false.
            var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/{id}/metry",
                CorposPut.Com(("POLYGON((0 0,1 1,1 0,0 1,0 0))", 4326)));

            Http.AssertProblem(r, corpo, HttpStatusCode.BadRequest, "Geometria inválida",
                "geometria inválida segundo o PostGIS (ST_IsValid)", "/api/mapas/{id}/metry");
            Assert.Equal(antes, await EstadoAsync(id));
        });
    }

    [FactIntegracaoEscrita]
    public async Task Cp7_TipoDivergente_E_LinestringDegenerada_400()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(8 8)", null);
            var antes = await EstadoAsync(id);
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();

            var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", CorposPut.Com(("LINESTRING(0 0, 0 0)", 4326)));

            Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
            Assert.Contains("Geometria inválida", corpo, StringComparison.Ordinal);
            Assert.Equal(antes, await EstadoAsync(id));
        });
    }

    [FactIntegracaoEscrita]
    public async Task Cp7_CancelamentoNoMeioDaTransacao_Rollback_EstadoIntacto()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(9 9)", "POINT(90 90)");
            var antes = await EstadoAsync(id);
            var repositorio = new MapasEscritaRepository(new FonteDadosEscrita(Escrita), NullLogger<MapasEscritaRepository>.Instance);
            ApiMapas.Dominio.Geometrias.GeometriaEntrada.TentarCriar("POINT(1 2)", 4326, ApiMapas.Dominio.ColunaMapa.Graphy, out var geometria, out _);
            ApiMapas.Dominio.IdMapa.TentarCriar(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var idMapa);

            // Outra sessao de escrita segura o lock de linha do id: o UPDATE fica parado e e cancelado.
            var (bloqueio, tx) = await TravarLinhaAsync(id);
            await using (bloqueio)
            await using (tx)
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(1500));
                await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repositorio.AtualizarAsync(idMapa, ApiMapas.Dominio.ColunaMapa.Graphy, geometria!, cts.Token));
                await tx.RollbackAsync();
            }

            Assert.Equal(antes, await EstadoAsync(id));
        });
    }

    // ---------------- CP-9 (concorrencia) ----------------

    [FactIntegracaoEscrita]
    public async Task Cp9_LockAcimaDe5s_409_EstadoIntacto()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(11 11)", null);
            var antes = await EstadoAsync(id);
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();
            var (bloqueio, tx) = await TravarLinhaAsync(id);
            await using var _b = bloqueio;
            await using var _t = tx;

            var inicio = DateTime.UtcNow;
            var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/{id}/metry", CorposPut.Com(("POINT(1 1)", 4326)));
            var duracao = DateTime.UtcNow - inicio;
            await tx.RollbackAsync();

            Http.AssertProblem(r, corpo, HttpStatusCode.Conflict, "Atualização concorrente", instance: $"/api/mapas/{id}/metry");
            Assert.InRange(duracao.TotalSeconds, 4.5, 20);
            Assert.Equal(antes, await EstadoAsync(id));
        });
    }

    [FactIntegracaoEscrita]
    public async Task Cp9_DoisPutsSimultaneos_EstadoIgualAUmDeles_NuncaMisturado()
    {
        await ComLimpezaAsync(async () =>
        {
            var id = await CriarIdAsync("POINT(12 12)", "POINT(120 120)");
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();
            var a = ("POINT(1 1)", 4326);
            var b = ("POINT(2 2)", 4326);

            var respostas = await Task.WhenAll(
                Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", CorposPut.Com(a)),
                Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", CorposPut.Com(b)));

            Assert.All(respostas, x => Assert.Equal(HttpStatusCode.NoContent, x.Resposta.StatusCode));
            var linha = Assert.Single(await EstadoAsync(id));
            Assert.Contains(linha.Graphy, new[] { a.Item1, b.Item1 });
            Assert.Equal("POINT(120 120)", linha.Metry);
        });
    }

    // ---------------- Faixa do graphy: a validacao local e a unica protecao ----------------

    [FactIntegracaoEscrita]
    public async Task Graphy_CastParaGeographyNaoFalhaForaDaFaixa_ApiRecusaAntes()
    {
        await ComLimpezaAsync(async () =>
        {
            // Evidencia (W1.V direto no banco, sem gravar): o cast nao falha com longitude 200.
            await using (var cmd = Escrita.CreateCommand(MapasEscritaRepository.SqlW1Validacao))
            {
                cmd.Parameters.Add(new NpgsqlParameter("wkt", NpgsqlDbType.Text) { Value = "POINT(200 0)" });
                cmd.Parameters.Add(new NpgsqlParameter("srid", NpgsqlDbType.Integer) { Value = 4326 });
                await using var leitor = await cmd.ExecuteReaderAsync();
                Assert.True(await leitor.ReadAsync());
                Assert.True(leitor.GetBoolean(0));
            }

            var id = await CriarIdAsync("POINT(13 13)", null);
            var antes = await EstadoAsync(id);
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();

            foreach (var wkt in new[] { "POINT(200 0)", "POINT(0 -95)", "LINESTRING(0 0, 180.5 0)", "GEOMETRYCOLLECTION(POINT(0 0), POINT(-181 0))" })
            {
                var (r, corpo) = await Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", CorposPut.Com((wkt, 4326)));
                Assert.Equal(HttpStatusCode.BadRequest, r.StatusCode);
                Assert.Contains("fora da faixa", corpo, StringComparison.Ordinal);
            }

            Assert.Equal(antes, await EstadoAsync(id));
        });
    }

    // ---------------- CP-14 / R-3 / RP-3: privilegios e configuracao ----------------

    private static async Task<object?> EscalarAsync(NpgsqlDataSource ds, string sql)
    {
        await using var cmd = ds.CreateCommand(sql);
        return await cmd.ExecuteScalarAsync();
    }

    /// <summary>Executa dentro de BEGIN ... ROLLBACK (nada e gravado mesmo se o privilegio existir) e devolve o SqlState.</summary>
    private static async Task<string?> SqlStateEmTransacaoAsync(NpgsqlDataSource ds, string sql)
    {
        await using var conexao = await ds.OpenConnectionAsync();
        await using var tx = await conexao.BeginTransactionAsync();
        try
        {
            await using var cmd = new NpgsqlCommand(sql, conexao, tx);
            await cmd.ExecuteNonQueryAsync();
            return null;
        }
        catch (PostgresException ex)
        {
            return ex.SqlState;
        }
        finally
        {
            await tx.RollbackAsync();
        }
    }

    public static TheoryData<string, string, bool> MatrizColunas => new()
    {
        { "id", "SELECT", true }, { "graphy", "SELECT", true }, { "metry", "SELECT", true },
        { "id", "INSERT", false }, { "graphy", "INSERT", true }, { "metry", "INSERT", true },
        { "id", "UPDATE", false }, { "graphy", "UPDATE", true }, { "metry", "UPDATE", true },
        { "id", "REFERENCES", false }, { "graphy", "REFERENCES", false }, { "metry", "REFERENCES", false },
    };

    [TheoryIntegracaoEscrita]
    [MemberData(nameof(MatrizColunas))]
    public async Task Cp14_Escrita_PrivilegiosPorColuna(string coluna, string privilegio, bool esperado)
    {
        var usuario = AmbienteOriginal.Ler("PG_ESCRITA_USER")!;
        var valor = await EscalarAsync(Escrita,
            $"SELECT has_column_privilege('{usuario.Replace("'", "''", StringComparison.Ordinal)}', 'public.mapas', '{coluna}', '{privilegio}')");
        Assert.Equal(esperado, valor);
    }

    [FactIntegracaoEscrita]
    public async Task R3_Escrita_SemTruncate_SemSequencia_SemUpdateDoId_SemPrivilegiosDeTabela()
    {
        // Privilegios de tabela (nao de coluna): nenhum (DELETE revogado - Q4, D-N4).
        foreach (var (privilegio, esperado) in new[] { ("DELETE", false), ("TRUNCATE", false), ("REFERENCES", false), ("TRIGGER", false), ("INSERT", false), ("UPDATE", false), ("SELECT", false) })
        {
            Assert.True(esperado.Equals(await EscalarAsync(Escrita, $"SELECT has_table_privilege(current_user, 'public.mapas', '{privilegio}')")), privilegio);
        }

        foreach (var privilegio in new[] { "USAGE", "SELECT", "UPDATE" })
        {
            Assert.Equal(false, await EscalarAsync(Escrita,
                $"SELECT has_sequence_privilege(current_user, pg_get_serial_sequence('public.mapas', 'id'), '{privilegio}')"));
        }

        Assert.Equal(false, await EscalarAsync(Escrita, "SELECT has_schema_privilege(current_user, 'public', 'CREATE')"));

        // Tentativas reais, sempre dentro de BEGIN ... ROLLBACK.
        Assert.Equal("42501", await SqlStateEmTransacaoAsync(Escrita, "TRUNCATE public.mapas"));
        Assert.Equal("42501", await SqlStateEmTransacaoAsync(Escrita, "SELECT nextval(pg_get_serial_sequence('public.mapas', 'id'))"));
        Assert.Equal("42501", await SqlStateEmTransacaoAsync(Escrita, "SELECT setval(pg_get_serial_sequence('public.mapas', 'id'), 1)"));
        Assert.Equal("42501", await SqlStateEmTransacaoAsync(Escrita, "UPDATE public.mapas SET id = id WHERE false"));
        Assert.Equal("42501", await SqlStateEmTransacaoAsync(Escrita, "CREATE TABLE public.qa_nao_deve_existir (x int)"));
        Assert.Equal("42501", await SqlStateEmTransacaoAsync(Escrita, "ALTER TABLE public.mapas ADD COLUMN qa_extra int"));
    }

    [FactIntegracaoEscrita]
    public async Task Cp14_Leitura_SemNenhumaEscrita()
    {
        foreach (var privilegio in new[] { "INSERT", "UPDATE" })
        {
            Assert.Equal(false, await EscalarAsync(Leitura, $"SELECT has_any_column_privilege(current_user, 'public.mapas', '{privilegio}')"));
        }

        foreach (var privilegio in new[] { "DELETE", "TRUNCATE" })
        {
            Assert.Equal(false, await EscalarAsync(Leitura, $"SELECT has_table_privilege(current_user, 'public.mapas', '{privilegio}')"));
        }

        foreach (var sql in new[] { "DELETE FROM public.mapas WHERE false", "TRUNCATE public.mapas", "UPDATE public.mapas SET graphy = graphy WHERE false", "INSERT INTO public.mapas (graphy) SELECT NULL WHERE false" })
        {
            Assert.Contains(await SqlStateEmTransacaoAsync(Leitura, sql), new[] { "42501", "25006" });
        }
    }

    [FactIntegracaoEscrita]
    public async Task Rp3_Escrita_ConfiguracaoDaSessao()
    {
        Assert.Equal("15s", await EscalarAsync(Escrita, "SHOW statement_timeout"));
        Assert.Equal("5s", await EscalarAsync(Escrita, "SHOW lock_timeout"));
        Assert.Equal("30s", await EscalarAsync(Escrita, "SHOW idle_in_transaction_session_timeout"));
        Assert.Equal("warning", await EscalarAsync(Escrita, "SHOW client_min_messages"));
        Assert.Equal("public", await EscalarAsync(Escrita, "SHOW search_path"));
        var versao = int.Parse((string)(await EscalarAsync(Escrita, "SHOW server_version_num"))!, System.Globalization.CultureInfo.InvariantCulture);
        if (versao >= 170000)
        {
            Assert.Equal("30s", await EscalarAsync(Escrita, "SHOW transaction_timeout"));
        }

        Assert.Equal(true, await EscalarAsync(Escrita, "SELECT has_table_privilege(current_user, 'public.spatial_ref_sys', 'SELECT')"));
    }

    [FactIntegracaoEscrita]
    public async Task R1_RoleDeEscritaReal_PassaNaChecagem()
    {
        var motivo = await new VerificadorRoleEscrita(new FonteDadosEscrita(Escrita), NullLogger<VerificadorRoleEscrita>.Instance)
            .VerificarAsync(CancellationToken.None);
        Assert.Equal(MotivoEscritaDesligada.Nenhum, motivo);
    }

    [FactIntegracaoEscrita]
    public async Task R3_IdsDeTesteCriadosPorMapasTeste_ENaoPorCasaos()
    {
        await ComLimpezaAsync(async () =>
        {
            Assert.NotEqual("casaos", AmbienteOriginal.Ler("PG_TESTE_USER"));
            Assert.NotEqual("casaos", AmbienteOriginal.Ler("PG_ESCRITA_USER"));
            Assert.Equal(AmbienteOriginal.Ler("PG_TESTE_USER"), await EscalarAsync(Teste, "SELECT current_user"));
            Assert.Equal(false, await EscalarAsync(Teste, "SELECT rolsuper FROM pg_roles WHERE rolname = current_user"));
            var id = await CriarIdAsync("POINT(14 14)", null);
            Assert.True(id > 0);
        });
    }

    // ---------------- PO6.4: POST (D-N5) ----------------

    /// <summary>Contagem total de linhas (para provar que nenhuma outra linha mudou de quantidade).</summary>
    private async Task<long> ContarLinhasAsync() => (long)(await EscalarAsync(Teste, "SELECT count(*) FROM public.mapas"))!;

    /// <summary>CN-1: POST cria uma linha nova so com a coluna da rota (a outra NULL); id lido do Location.</summary>
    [TheoryIntegracaoEscrita]
    [InlineData("graphy", "POINT(-46.61 -23.51)", 4326)]
    [InlineData("metry", "POINT(333250 7394600)", 31983)]
    public async Task Cn1_Post_LinhaNova_SoAColunaDaRota_IdDoLocation(string coluna, string wkt, int srid)
    {
        await ComLimpezaAsync(async () =>
        {
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();

            var (r, corpo) = await PostAsync(cliente, coluna, (wkt, srid));

            Assert.Equal(string.Empty, corpo);
            var id = RegistrarIdDoLocation(r, coluna);
            var linha = Assert.Single(await EstadoAsync(id));
            if (coluna == "graphy")
            {
                Assert.Equal(wkt, linha.Graphy);
                Assert.Null(linha.Metry);
            }
            else
            {
                Assert.Equal(wkt, linha.Metry);
                Assert.Equal(srid, linha.SridMetry);
                Assert.Null(linha.Graphy);
            }

            Assert.Equal(wkt, await GetWktAsync(cliente, id, coluna));
            Assert.Null(await GetWktAsync(cliente, id, coluna == "graphy" ? "metry" : "graphy")); // coluna NULL = 404
            Assert.Contains(f.Logs.Mensagens, m => m.Contains($"Auditoria POST: id {id} criado", StringComparison.Ordinal));
        });
    }

    /// <summary>CN-2: dois POST iguais (e tres simultaneos) criam ids distintos.</summary>
    [FactIntegracaoEscrita]
    public async Task Cn2_PostIguais_EPostSimultaneos_IdsDistintos()
    {
        await ComLimpezaAsync(async () =>
        {
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();
            var antes = await ContarLinhasAsync();

            var (a, _) = await PostAsync(cliente, "graphy", (P1, 4326));
            var idA = RegistrarIdDoLocation(a, "graphy");
            var (b, _) = await PostAsync(cliente, "graphy", (P1, 4326));
            var idB = RegistrarIdDoLocation(b, "graphy");
            var simultaneos = await Task.WhenAll(Enumerable.Range(0, 3).Select(_ => PostAsync(cliente, "metry", (P1, 4326))));
            var ids = simultaneos.Select(x => RegistrarIdDoLocation(x.Resposta, "metry")).Append(idA).Append(idB).ToList();

            Assert.NotEqual(idA, idB);
            Assert.Equal(5, ids.Distinct().Count());
            Assert.Equal(antes + 5, await ContarLinhasAsync());
        });
    }

    /// <summary>CN-3: POST de metry e PUT de graphy no mesmo id deixam uma linha com as duas colunas.</summary>
    [FactIntegracaoEscrita]
    public async Task Cn3_PostMetry_DepoisPutGraphy_UmaLinhaComAsDuasColunas()
    {
        await ComLimpezaAsync(async () =>
        {
            await using var f = FabricaReal();
            using var cliente = f.CreateClient();

            var (post, _) = await PostAsync(cliente, "metry", ("POINT(-46.62 -23.52)", 4674));
            var id = RegistrarIdDoLocation(post, "metry");
            var (put, corpoPut) = await Http.PutAsync(cliente, $"/api/mapas/{id}/graphy", CorposPut.Com((L1, 4326)));

            Assert.True(put.StatusCode == HttpStatusCode.NoContent, corpoPut);
            var linha = Assert.Single(await EstadoAsync(id));
            Assert.Equal(L1, linha.Graphy);
            Assert.Equal("POINT(-46.62 -23.52)", linha.Metry);
            Assert.Equal(4674, linha.SridMetry);
            Assert.Equal(L1, await GetWktAsync(cliente, id, "graphy"));
            Assert.Equal("POINT(-46.62 -23.52)", await GetWktAsync(cliente, id, "metry"));
        });
    }

    /// <summary>CN-4: PUT em id inexistente responde 404 e nao cria linha.</summary>
    [FactIntegracaoEscrita]
    public async Task Cn4_PutIdInexistente_404_NadaCriado()
    {
        await using var f = FabricaReal();
        using var cliente = f.CreateClient();
        var antes = await ContarLinhasAsync();

        var (r, corpo) = await Http.PutAsync(cliente, "/api/mapas/2147483647/metry", CorposPut.Com((P1, 4326)));

        Http.AssertProblem(r, corpo, HttpStatusCode.NotFound, "Mapa não encontrado", instance: "/api/mapas/2147483647/metry");
        Assert.Equal(antes, await ContarLinhasAsync());
        Assert.Empty(await EstadoAsync(2147483647));
    }

    /// <summary>CN-6 no banco real: geometria invalida no POST responde 400 e nada e gravado.</summary>
    [FactIntegracaoEscrita]
    public async Task Cn6_PostInvalido_400_NadaGravado()
    {
        await using var f = FabricaReal();
        using var cliente = f.CreateClient();
        var antes = await ContarLinhasAsync();

        var (r1, _) = await PostAsync(cliente, "metry", ("POLYGON((0 0,1 1,1 0,0 1,0 0))", 4326));
        var (r2, _) = await PostAsync(cliente, "graphy", ("POINT(200 0)", 4326));

        Assert.Equal(HttpStatusCode.BadRequest, r1.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, r2.StatusCode);
        Assert.Null(r1.Headers.Location);
        Assert.Equal(antes, await ContarLinhasAsync());
    }

    /// <summary>CN-13 (somente leitura): estado depois da migracao - PK exata, GENERATED ALWAYS, CHECK, sem duplicados, 3 colunas.</summary>
    [FactIntegracaoEscrita]
    public async Task Cn13_EstadoPosMigracao_PkExata_Always_Check_SemDuplicados()
    {
        Assert.Equal(true, await EscalarAsync(Teste,
            "SELECT EXISTS (SELECT 1 FROM pg_constraint k JOIN pg_attribute a ON a.attrelid = k.conrelid AND a.attname = 'id' " +
            "WHERE k.conrelid = 'public.mapas'::regclass AND k.contype = 'p' AND k.conname = 'mapas_pkey' AND k.conkey = ARRAY[a.attnum])"));
        Assert.Equal("a", (await EscalarAsync(Teste,
            "SELECT attidentity::text FROM pg_attribute WHERE attrelid = 'public.mapas'::regclass AND attname = 'id'"))?.ToString());
        Assert.Equal(true, await EscalarAsync(Teste,
            "SELECT EXISTS (SELECT 1 FROM pg_constraint WHERE conrelid = 'public.mapas'::regclass AND contype = 'c' AND conname = 'mapas_nao_vazia')"));
        Assert.Equal(0L, await EscalarAsync(Teste,
            "SELECT count(*) FROM pg_constraint WHERE conrelid = 'public.mapas'::regclass AND contype IN ('p','u','x') AND conname <> 'mapas_pkey'"));
        Assert.Equal(0L, await EscalarAsync(Teste, "SELECT count(*) FROM (SELECT id FROM public.mapas GROUP BY id HAVING count(*) > 1) d"));
        Assert.Equal(new[] { "graphy", "id", "metry" }, await ColunasAsync());
    }

    private async Task<string[]> ColunasAsync()
    {
        await using var cmd = Teste.CreateCommand(
            "SELECT attname::text FROM pg_attribute WHERE attrelid = 'public.mapas'::regclass AND attnum > 0 AND NOT attisdropped ORDER BY attname");
        var nomes = new List<string>();
        await using var leitor = await cmd.ExecuteReaderAsync();
        while (await leitor.ReadAsync())
        {
            nomes.Add(leitor.GetString(0));
        }

        return nomes.ToArray();
    }

    /// <summary>CN-14: a api_mapas_escrita nao tem privilegio na sequencia nem DELETE, e mesmo assim o POST funciona.</summary>
    [FactIntegracaoEscrita]
    public async Task Cn14_SemPrivilegioNaSequencia_SemDelete_PostFunciona()
    {
        await ComLimpezaAsync(async () =>
        {
            foreach (var privilegio in new[] { "USAGE", "SELECT", "UPDATE" })
            {
                Assert.Equal(false, await EscalarAsync(Escrita,
                    $"SELECT has_sequence_privilege(current_user, pg_get_serial_sequence('public.mapas', 'id'), '{privilegio}')"));
            }

            Assert.Equal(false, await EscalarAsync(Escrita, "SELECT has_table_privilege(current_user, 'public.mapas', 'DELETE')"));
            Assert.Equal("42501", await SqlStateEmTransacaoAsync(Escrita, "DELETE FROM public.mapas WHERE false"));
            Assert.Contains(await SqlStateEmTransacaoAsync(Escrita, "INSERT INTO public.mapas (id, graphy) OVERRIDING SYSTEM VALUE VALUES (1, NULL)"), new[] { "42501", "428C9" });

            await using var f = FabricaReal();
            using var cliente = f.CreateClient();
            var (r, _) = await PostAsync(cliente, "graphy", (P1, 4326));
            RegistrarIdDoLocation(r, "graphy");
        });
    }
}
