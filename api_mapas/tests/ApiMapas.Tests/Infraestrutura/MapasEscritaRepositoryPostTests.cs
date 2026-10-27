using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ApiMapas.Tests.Infraestrutura;

/// <summary>
/// PO6.2 - POST (W3/W4) no MapasEscritaRepository contra o ServidorPostgresEscritaFalso: ramos que a
/// suite de protocolo do PO4 nao cobria (validacao rejeitada, cancelamento, falha de banco no
/// INSERT, RETURNING de tipo errado ou fora da faixa, constraint sem nome, falha no BEGIN) e
/// a estrutura da V1 de 14 colunas.
/// </summary>
public class MapasEscritaRepositoryPostTests
{
    private static readonly string[] Proibidos =
        { "SEGREDO", "POINT", "p_escrita_falsa", "u_escrita_falso", "erro simulado", "detalhe", "hint" };

    private static GeometriaEntrada Geometria(ColunaMapa coluna, string wkt = "POINT(1 2)")
    {
        Assert.True(GeometriaEntrada.TentarCriar(wkt, 4326, coluna, out var g, out var m), m);
        return g!;
    }

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

        return c.Contem("INSERT INTO public.mapas") ? Retorno(ColunaPg.Int4, 5) : null;
    };

    private static RespostaPg.Linhas Retorno(int oid, object? valor) =>
        new(new[] { new ColunaPg("id", oid) }, new[] { new object?[] { valor } }, "INSERT 0 1");

    private sealed class Cenario : IAsyncDisposable
    {
        public Cenario(Func<ComandoPg, RespostaPg?> responder)
        {
            Servidor = new ServidorPostgresEscritaFalso(responder);
            Fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(Servidor.ConnectionString()));
            Repositorio = new MapasEscritaRepository(Fonte, Log);
        }

        public ServidorPostgresEscritaFalso Servidor { get; }

        public FonteDadosEscrita Fonte { get; }

        public LoggerMemoria<MapasEscritaRepository> Log { get; } = new();

        public MapasEscritaRepository Repositorio { get; }

        public List<string> Sqls => Servidor.Sqls.Where(x => x != "DISCARD ALL").ToList();

        public void AssertLogLimpo()
        {
            foreach (var termo in Proibidos)
            {
                Assert.DoesNotContain(termo, Log.TextoCompleto, StringComparison.OrdinalIgnoreCase);
            }

            Assert.All(Log.Entradas, e => Assert.Null(e.Erro));
            Assert.All(Log.Entradas.Where(e => e.Mensagem.StartsWith("Escrita POST", StringComparison.Ordinal) && e.Evento.Id < 2131),
                e => Assert.Contains("id novo", e.Mensagem, StringComparison.Ordinal));
        }

        public async ValueTask DisposeAsync()
        {
            await Fonte.DisposeAsync();
            await Servidor.DisposeAsync();
        }
    }

    public static TheoryData<RespostaPg, string> Rejeicoes => new()
    {
        { RespostaPg.Validacao(false, "POINT"), MotivosGeometria.InvalidaPostgis },
        { RespostaPg.Validacao(true, "LINESTRING"), MotivosGeometria.TipoDiverge },
        { new RespostaPg.Erro("XX000"), MotivosGeometria.InvalidaPostgis },
        { new RespostaPg.Erro("22023"), MotivosGeometria.InvalidaPostgis },
    };

    [Theory]
    [MemberData(nameof(Rejeicoes))]
    public async Task Post_ValidacaoRejeitada_SemTransacao_SemInsert(RespostaPg resposta, string motivo)
    {
        await using var c = new Cenario(Feliz(x => x.Contem("ST_IsValid") ? resposta : null));

        var r = await c.Repositorio.CriarAsync(ColunaMapa.Metry, Geometria(ColunaMapa.Metry), CancellationToken.None);

        Assert.Equal(new ResultadoEscrita.GeometriaInvalida(motivo), r);
        Assert.DoesNotContain(c.Sqls, s => s.StartsWith("BEGIN", StringComparison.Ordinal) || s.StartsWith("INSERT", StringComparison.Ordinal));
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2105);
        c.AssertLogLimpo();
    }

    [Theory]
    [InlineData("57014", "inserir a geometria (INSERT ... RETURNING id)", "INSERT INTO")]
    [InlineData("42501", "inserir a geometria (INSERT ... RETURNING id)", "INSERT INTO")]
    [InlineData("57014", "definir lock_timeout", "SET LOCAL")]
    public async Task Post_FalhaDeBanco_Rollback_Falha500_SemRetry(string sqlState, string etapa, string onde)
    {
        await using var c = new Cenario(Feliz(x => x.Contem(onde) ? new RespostaPg.Erro(sqlState) : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), CancellationToken.None));

        Assert.Equal((etapa, sqlState), (ex.Etapa, ex.SqlState));
        Assert.Null(ex.InnerException);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.True(c.Sqls.Count(s => s.StartsWith("INSERT", StringComparison.Ordinal)) <= 1);
        var log = Assert.Single(c.Log.Entradas, e => e.Evento.Id == 2115);
        Assert.Equal(LogLevel.Error, log.Nivel);
        Assert.Contains("Escrita POST", log.Mensagem, StringComparison.Ordinal);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task Post_ConstraintSemNome_LogComTraco()
    {
        await using var c = new Cenario(Feliz(x => x.Contem("INSERT INTO") ? new RespostaPg.Erro("23514") : null));

        await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), CancellationToken.None));

        var log = Assert.Single(c.Log.Entradas, e => e.Evento.Id == 2133);
        Assert.Contains("constraint -,", log.Mensagem, StringComparison.Ordinal);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
    }

    public static TheoryData<int, object?, string> RetornosInvalidos => new()
    {
        { ColunaPg.Int4, 0, "Int32" },
        { ColunaPg.Int4, -7, "Int32" },
        { ColunaPg.Text, "12", "String" },
        { ColunaPg.Bool, true, "Boolean" },
    };

    [Theory]
    [MemberData(nameof(RetornosInvalidos))]
    public async Task Post_ReturningInvalido_Rollback_Falha500_Log2134(int oid, object? valor, string tipo)
    {
        await using var c = new Cenario(Feliz(x => x.Contem("INSERT INTO") ? Retorno(oid, valor) : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Metry, Geometria(ColunaMapa.Metry), CancellationToken.None));

        Assert.Equal("inserir a geometria (INSERT ... RETURNING id)", ex.Etapa);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.DoesNotContain("COMMIT", c.Sqls);
        Assert.Contains($"tipo {tipo}", Assert.Single(c.Log.Entradas, e => e.Evento.Id == 2134).Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_ReturningSemLinha_Rollback_Falha500_TipoNull()
    {
        await using var c = new Cenario(Feliz(x => x.Contem("INSERT INTO")
            ? new RespostaPg.Linhas(new[] { new ColunaPg("id", ColunaPg.Int4) }, Array.Empty<object?[]>(), "INSERT 0 0")
            : null));

        await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), CancellationToken.None));

        Assert.Contains("tipo null", Assert.Single(c.Log.Entradas, e => e.Evento.Id == 2134).Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Post_CancelamentoNoInsert_Rollback_SemCommit()
    {
        await using var c = new Cenario(Feliz(x => x.Contem("INSERT INTO")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(10), Retorno(ColunaPg.Int4, 5))
            : null));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), cts.Token));

        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.DoesNotContain("COMMIT", c.Sqls);
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2112 && e.Mensagem.Contains("Escrita POST: cancelada ao inserir", StringComparison.Ordinal));
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task Post_CanceladoDepoisDoInsert_AntesDoCommit_Rollback()
    {
        using var cts = new CancellationTokenSource();
        await using var c = new Cenario(Feliz(x =>
        {
            if (x.Contem("INSERT INTO"))
            {
                cts.Cancel();
                return Retorno(ColunaPg.Int4, 5);
            }

            return null;
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Metry, Geometria(ColunaMapa.Metry), cts.Token));

        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.DoesNotContain("COMMIT", c.Sqls);
    }

    [Fact]
    public async Task Post_CancelamentoNaValidacao_SemTransacao()
    {
        await using var c = new Cenario(Feliz(x => x.Contem("ST_IsValid")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(10), RespostaPg.Validacao(true, "POINT"))
            : null));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), cts.Token));

        Assert.DoesNotContain(c.Sqls, s => s.StartsWith("BEGIN", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_FalhaNoBegin_SemRollback_Falha500()
    {
        await using var c = new Cenario(Feliz(x => x.Simples && x.Sql.StartsWith("BEGIN", StringComparison.Ordinal) ? new RespostaPg.Erro("08P01") : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), CancellationToken.None));

        // O Npgsql envia o BEGIN junto com o primeiro comando da transacao (SET LOCAL).
        Assert.Equal("08P01", ex.SqlState);
        Assert.Contains(ex.Etapa, new[] { "iniciar a transacao", "definir lock_timeout" });
        Assert.DoesNotContain(c.Sqls, s => s.StartsWith("INSERT", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Post_ConexaoCaiNoInsert_Falha500_RollbackSoNoLog()
    {
        await using var c = new Cenario(Feliz(x => x.Contem("INSERT INTO") ? new RespostaPg.Derrubar() : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Metry, Geometria(ColunaMapa.Metry), CancellationToken.None));

        Assert.Equal("inserir a geometria (INSERT ... RETURNING id)", ex.Etapa);
        Assert.DoesNotContain("COMMIT", c.Sqls);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task Post_FalhaNoCommit_Falha500()
    {
        await using var c = new Cenario(Feliz(x => x.Simples && x.Sql.StartsWith("COMMIT", StringComparison.Ordinal) ? new RespostaPg.Erro("40001") : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), CancellationToken.None));

        Assert.Equal(("confirmar (COMMIT)", "40001"), (ex.Etapa, ex.SqlState));
    }

    [Fact]
    public async Task Post_DoisInserts_DoisIdsDoReturning()
    {
        var proximo = 40;
        await using var c = new Cenario(Feliz(x => x.Contem("INSERT INTO") ? Retorno(ColunaPg.Int4, Interlocked.Increment(ref proximo)) : null));

        var a = await c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), CancellationToken.None);
        var b = await c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy), CancellationToken.None);

        Assert.Equal(new ResultadoEscrita.Criado(41), a);
        Assert.Equal(new ResultadoEscrita.Criado(42), b);
        Assert.Equal(2, c.Sqls.Count(s => s.StartsWith("INSERT", StringComparison.Ordinal)));
    }

    // ---------------- V1 (14 colunas, PO5.C1/PO5.C2) ----------------

    [Fact]
    public void V1_QuatorzeColunas_NaOrdemDosIndices()
    {
        var sql = VerificadorRoleEscrita.SqlAtributosRole;
        var marcos = new[]
        {
            "r.rolsuper,", "r.rolbypassrls,", "r.rolcreaterole,", "AS pode_truncate,", "AS escreve_tudo,", "AS dona_tabela,",
            "AS pode_delete,", "AS insere_id,", "AS pk_exata,", "AS id_always,", "AS atualiza_id,", "r.rolcreatedb,",
            "r.rolreplication,", "AS membro_de_role FROM",
        };

        Assert.Equal(14, VerificadorRoleEscrita.ColunasV1);
        Assert.Equal(VerificadorRoleEscrita.ColunasV1, marcos.Length);
        var posicoes = marcos.Select(m => sql.IndexOf(m, StringComparison.Ordinal)).ToList();
        Assert.All(posicoes, p => Assert.True(p >= 0));
        Assert.Equal(posicoes.OrderBy(p => p), posicoes);
        Assert.Contains("'id', 'UPDATE'", sql, StringComparison.Ordinal);
        Assert.Contains("pg_auth_members AS m WHERE m.member = r.oid", sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    [Fact]
    public async Task V1_SemLinha_VerificacaoFalhou()
    {
        var colunas = Enumerable.Range(0, 14).Select(i => new ColunaPg("c" + i, ColunaPg.Bool)).ToArray();
        await using var servidor = new ServidorPostgresEscritaFalso(_ => new RespostaPg.Linhas(colunas, Array.Empty<object?[]>()));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        var log = new LoggerMemoria<VerificadorRoleEscrita>();

        var motivo = await new VerificadorRoleEscrita(fonte, log).VerificarAsync(CancellationToken.None);

        Assert.Equal(MotivoEscritaDesligada.VerificacaoFalhou, motivo);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2203 && e.Nivel == LogLevel.Warning);
    }

    public static TheoryData<int, MotivoEscritaDesligada> CriteriosV1()
    {
        var dados = new TheoryData<int, MotivoEscritaDesligada>();
        for (var i = 0; i < 14; i++)
        {
            dados.Add(i, i is 8 or 9 ? MotivoEscritaDesligada.SemChavePrimaria : MotivoEscritaDesligada.RolePrivilegiada);
        }

        dados.Add(-1, MotivoEscritaDesligada.Nenhum);
        return dados;
    }

    /// <summary>RS-20 / achado 3: cada coluna isolada (0 a 7 e 10 a 13 true; 8 ou 9 false) desliga a escrita; nenhuma = habilitada.</summary>
    [Theory]
    [MemberData(nameof(CriteriosV1))]
    public async Task V1_CadaColunaIsolada_Motivo_E_Warning(int indice, MotivoEscritaDesligada esperado)
    {
        var colunas = Enumerable.Range(0, 14).Select(i => new ColunaPg("c" + i, ColunaPg.Bool)).ToArray();
        var valores = Enumerable.Range(0, 14).Select(i => (object?)(i is 8 or 9 ? i != indice : i == indice)).ToArray();
        await using var servidor = new ServidorPostgresEscritaFalso(_ => new RespostaPg.Linhas(colunas, new[] { valores }));
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        var log = new LoggerMemoria<VerificadorRoleEscrita>();

        var motivo = await new VerificadorRoleEscrita(fonte, log).VerificarAsync(CancellationToken.None);

        Assert.Equal(esperado, motivo);
        var evento = esperado switch
        {
            MotivoEscritaDesligada.RolePrivilegiada => 2205,
            MotivoEscritaDesligada.SemChavePrimaria => 2206,
            _ => 2207,
        };
        Assert.Contains(log.Entradas, e => e.Evento.Id == evento);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2208);
        Assert.DoesNotContain("p_escrita_falsa", log.TextoCompleto, StringComparison.Ordinal);
    }
}
