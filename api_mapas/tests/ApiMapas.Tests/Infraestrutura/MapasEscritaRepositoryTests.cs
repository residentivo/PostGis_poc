using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace ApiMapas.Tests.Infraestrutura;

/// <summary>
/// MapasEscritaRepository sem servidor: SQL W1 a W4 e V1 constantes e identicos ao
/// api_mapas/sql/consultas_referencia.sql (RP-15/CP-10/CN-12), tipos de parametro.
/// Ajustado no PO4 ao modelo de uma geometria por id; a ampliacao fica com o PO6.
/// </summary>
public class MapasEscritaRepositorySqlTests
{
    private static string ReferenciaSql()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "sql", "consultas_referencia.sql")))
        {
            dir = dir.Parent;
        }

        Assert.NotNull(dir);
        return File.ReadAllText(Path.Combine(dir!.FullName, "sql", "consultas_referencia.sql"));
    }

    public static TheoryData<string, string> Constantes => new()
    {
        { "W1.V", MapasEscritaRepository.SqlW1Validacao },
        { "W1.1", MapasEscritaRepository.SqlW1LockTimeout },
        { "W1.2", MapasEscritaRepository.SqlW1Update },
        { "W2.V", MapasEscritaRepository.SqlW2Validacao },
        { "W2.1", MapasEscritaRepository.SqlW2LockTimeout },
        { "W2.2", MapasEscritaRepository.SqlW2Update },
        { "W3.1", MapasEscritaRepository.SqlW3LockTimeout },
        { "W3.2", MapasEscritaRepository.SqlW3Insert },
        { "W4.1", MapasEscritaRepository.SqlW4LockTimeout },
        { "W4.2", MapasEscritaRepository.SqlW4Insert },
        { "V1", VerificadorRoleEscrita.SqlAtributosRole },
    };

    [Theory]
    [MemberData(nameof(Constantes))]
    public void Sql_CopiadoExatamenteDaReferencia(string bloco, string sql)
    {
        var linhas = ReferenciaSql().Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.True(linhas.Contains(sql), $"{bloco} nao aparece literalmente em consultas_referencia.sql: {sql}");
    }

    [Theory]
    [InlineData(ColunaMapa.Graphy, "graphy", "metry")]
    [InlineData(ColunaMapa.Metry, "metry", "graphy")]
    public void Sql_PorColuna_SemDelete_SemIdNoInsert_SemAdvisoryLock(ColunaMapa coluna, string propria, string outra)
    {
        var sql = MapasEscritaRepository.SqlPara(coluna);

        Assert.Equal("SET LOCAL lock_timeout = '5s';", sql.LockTimeoutUpdate);
        Assert.Equal("SET LOCAL lock_timeout = '5s';", sql.LockTimeoutInsert);
        Assert.StartsWith($"UPDATE public.mapas SET {propria} = ST_GeomFromText(@wkt, @srid)", sql.Update, StringComparison.Ordinal);
        Assert.EndsWith(" WHERE id = @id;", sql.Update, StringComparison.Ordinal);
        Assert.StartsWith($"INSERT INTO public.mapas ({propria}) VALUES (ST_GeomFromText(@wkt, @srid)", sql.Insert, StringComparison.Ordinal);
        Assert.EndsWith(" RETURNING id;", sql.Insert, StringComparison.Ordinal);
        Assert.Equal(coluna == ColunaMapa.Graphy, sql.Insert.Contains("::geography", StringComparison.Ordinal));
        Assert.Equal(coluna == ColunaMapa.Graphy, sql.Update.Contains("::geography", StringComparison.Ordinal));
        Assert.Equal(coluna == ColunaMapa.Graphy, sql.Validacao.Contains("::geography::geometry", StringComparison.Ordinal));
        foreach (var texto in new[] { sql.Validacao, sql.Update, sql.Insert })
        {
            Assert.DoesNotContain("DELETE", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("pg_advisory", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("nextval", texto, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain($"SET {outra} ", texto, StringComparison.Ordinal);
            Assert.DoesNotContain($"({outra})", texto, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("(id", sql.Insert, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlPara_Constante_MesmaInstancia()
    {
        Assert.Same(MapasEscritaRepository.SqlPara(ColunaMapa.Graphy), MapasEscritaRepository.SqlPara(ColunaMapa.Graphy));
        Assert.NotSame(MapasEscritaRepository.SqlPara(ColunaMapa.Graphy), MapasEscritaRepository.SqlPara(ColunaMapa.Metry));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SqlPara_ColunaForaDoEnum_Lanca(int coluna)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapasEscritaRepository.SqlPara((ColunaMapa)coluna));
    }

    private static GeometriaEntrada Geometria(ColunaMapa coluna, string wkt, int srid = 4326)
    {
        Assert.True(GeometriaEntrada.TentarCriar(wkt, srid, coluna, out var g, out var m), m);
        return g!;
    }

    [Theory]
    [InlineData(ColunaMapa.Graphy)]
    [InlineData(ColunaMapa.Metry)]
    public void Comandos_ParametrosTipados(ColunaMapa coluna)
    {
        IdMapa.TentarCriar("77", out var id);
        var geometria = Geometria(coluna, "POINT(1 2)");

        using var validacao = MapasEscritaRepository.CriarComandoValidacao(coluna, geometria);
        using var update = MapasEscritaRepository.CriarComandoUpdate(coluna, id, geometria);
        using var insert = MapasEscritaRepository.CriarComandoInsert(coluna, geometria);
        using var lockTimeout = MapasEscritaRepository.CriarComandoLockTimeout(MapasEscritaRepository.SqlW1LockTimeout);

        Assert.Equal(MapasEscritaRepository.SqlPara(coluna).Validacao, validacao.CommandText);
        Assert.Equal(new[] { ("wkt", NpgsqlDbType.Text), ("srid", NpgsqlDbType.Integer) },
            validacao.Parameters.Select(p => (p.ParameterName, p.NpgsqlDbType)));
        Assert.Equal(new[] { ("wkt", NpgsqlDbType.Text), ("srid", NpgsqlDbType.Integer), ("id", NpgsqlDbType.Integer) },
            update.Parameters.Select(p => (p.ParameterName, p.NpgsqlDbType)));
        Assert.Equal(new object[] { "POINT(1 2)", 4326, 77 }, update.Parameters.Select(p => p.Value!));
        Assert.Equal(new[] { "wkt", "srid" }, insert.Parameters.Select(p => p.ParameterName));
        Assert.Empty(lockTimeout.Parameters);
    }

    [Fact]
    public void Comandos_GeometriaNula_Lanca()
    {
        IdMapa.TentarCriar("5", out var id);
        Assert.Throws<ArgumentNullException>(() => MapasEscritaRepository.CriarComandoInsert(ColunaMapa.Graphy, null!));
        Assert.Throws<ArgumentNullException>(() => MapasEscritaRepository.CriarComandoUpdate(ColunaMapa.Graphy, id, null!));
        Assert.Throws<ArgumentNullException>(() => MapasEscritaRepository.CriarComandoValidacao(ColunaMapa.Graphy, null!));
    }

    [Theory]
    [InlineData("XX000", true)]
    [InlineData("22023", true)]
    [InlineData("22P02", true)]
    [InlineData("2200F", true)]
    [InlineData("42501", false)]
    [InlineData("55P03", false)]
    [InlineData("57014", false)]
    [InlineData("XX001", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EhErroDeGeometria(string? sqlState, bool esperado)
    {
        Assert.Equal(esperado, MapasEscritaRepository.EhErroDeGeometria(sqlState));
    }

    [Fact]
    public void FalhaBancoEscritaException_SoEtapaSqlStateETipo()
    {
        var ex = new FalhaBancoEscritaException("validar a geometria", "XX000", "PostgresException");
        Assert.Equal("Falha no banco de escrita ao validar a geometria (SqlState XX000, tipo PostgresException).", ex.Message);
        Assert.Equal(("validar a geometria", "XX000"), (ex.Etapa, ex.SqlState));
        Assert.Null(ex.InnerException);
        Assert.Null(new FalhaBancoEscritaException().Etapa);
        Assert.Equal("m", new FalhaBancoEscritaException("m").Message);
        Assert.NotNull(new FalhaBancoEscritaException("m", new InvalidOperationException()).InnerException);
    }

    [Fact]
    public void Construtor_ArgumentosNulos_Lanca()
    {
        using var ds = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=u;Password=p");
        Assert.Throws<ArgumentNullException>(() => new MapasEscritaRepository(null!, new LoggerMemoria<MapasEscritaRepository>()));
        Assert.Throws<ArgumentNullException>(() => new MapasEscritaRepository(new FonteDadosEscrita(ds), null!));
        Assert.Throws<ArgumentNullException>(() => new FonteDadosEscrita(null!));
    }
}

/// <summary>
/// Protocolo com o ServidorPostgresEscritaFalso (ajustado no PO4 ao modelo de uma geometria por id):
/// PUT = validacao -> BEGIN READ COMMITTED -> SET LOCAL lock_timeout -> UPDATE pela PK -> COMMIT;
/// POST = validacao -> BEGIN -> SET LOCAL -> INSERT ... RETURNING id -> COMMIT. ROLLBACK em toda
/// falha, 55P03 -> Conflito, 57014/pool esgotado -> 500, cancelamento antes do COMMIT -> ROLLBACK,
/// COMMIT com CancellationToken.None e logs so com SqlState.
/// </summary>
public class MapasEscritaRepositoryProtocoloTests
{
    private static readonly string[] Proibidos =
        { "SEGREDO", "POINT", "LINESTRING", "p_escrita_falsa", "u_escrita_falso", "canceling statement", "erro simulado" };

    private const int IdGerado = 4242;

    private static IdMapa Id(int valor)
    {
        Assert.True(IdMapa.TentarCriar(valor.ToString(System.Globalization.CultureInfo.InvariantCulture), out var id));
        return id;
    }

    private static GeometriaEntrada Geometria(ColunaMapa coluna, string wkt)
    {
        Assert.True(GeometriaEntrada.TentarCriar(wkt, 4326, coluna, out var g, out var m), m);
        return g!;
    }

    private static string TipoDe(object? wkt) =>
        new(((string)wkt!).TakeWhile(char.IsAsciiLetter).ToArray());

    private static RespostaPg.Linhas Retorno(object? id) =>
        new(new[] { new ColunaPg("id", ColunaPg.Int4) }, new[] { new object?[] { id } }, "INSERT 0 1");

    /// <summary>Caminho feliz configuravel; devolve null para BEGIN/COMMIT/ROLLBACK/SET (padrao do servidor).</summary>
    private static Func<ComandoPg, RespostaPg?> Feliz(int atualizadas = 1, Func<ComandoPg, RespostaPg?>? antes = null) => c =>
    {
        var especial = antes?.Invoke(c);
        if (especial is not null)
        {
            return especial;
        }

        if (c.Contem("ST_IsValid"))
        {
            return RespostaPg.Validacao(true, TipoDe(c.Parametros[0]));
        }

        if (c.Contem("UPDATE public.mapas"))
        {
            return new RespostaPg.Comando($"UPDATE {atualizadas}");
        }

        if (c.Contem("INSERT INTO public.mapas"))
        {
            return Retorno(IdGerado);
        }

        return null;
    };

    private sealed class Cenario : IAsyncDisposable
    {
        public Cenario(Func<ComandoPg, RespostaPg?> responder, int maxPool = 5, int timeout = 5)
        {
            Servidor = new ServidorPostgresEscritaFalso(responder);
            Fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(Servidor.ConnectionString(maxPool, timeout)));
            Repositorio = new MapasEscritaRepository(Fonte, Log);
        }

        public ServidorPostgresEscritaFalso Servidor { get; }

        public FonteDadosEscrita Fonte { get; }

        public LoggerMemoria<MapasEscritaRepository> Log { get; } = new();

        public MapasEscritaRepository Repositorio { get; }

        public List<string> Sqls => Servidor.Sqls.ToList();

        public bool Recebeu(string inicio) => Sqls.Any(s => s.StartsWith(inicio, StringComparison.OrdinalIgnoreCase));

        public void AssertLogLimpo()
        {
            var texto = Log.TextoCompleto;
            foreach (var termo in Proibidos)
            {
                Assert.DoesNotContain(termo, texto, StringComparison.Ordinal);
            }

            Assert.All(Log.Entradas, e => Assert.Null(e.Erro));
            Assert.Empty(Servidor.ErrosInternos);
        }

        public async ValueTask DisposeAsync()
        {
            await Fonte.DisposeAsync();
            await Servidor.DisposeAsync();
        }
    }

    /// <summary>SQL como o Npgsql envia (@nome -> $n, sem ';' final).</summary>
    private static string Posicional(string sql, params string[] nomes)
    {
        var resultado = sql;
        foreach (var (nome, i) in nomes.Select((n, i) => (n, i)).OrderByDescending(p => p.n.Length))
        {
            resultado = resultado.Replace("@" + nome, "$" + (i + 1).ToString(System.Globalization.CultureInfo.InvariantCulture), StringComparison.Ordinal);
        }

        return resultado.TrimEnd(';');
    }

    // ---------------- PUT: sucesso e ordem exata ----------------

    [Theory]
    [InlineData(ColunaMapa.Graphy)]
    [InlineData(ColunaMapa.Metry)]
    public async Task Put_Sucesso_OrdemExata_ParametrosTipados_UmaConexao(ColunaMapa coluna)
    {
        await using var c = new Cenario(Feliz());

        var r = await c.Repositorio.AtualizarAsync(Id(321), coluna, Geometria(coluna, "LINESTRING(0 0, 1 1)"), CancellationToken.None);

        Assert.Same(ResultadoEscrita.Atualizado.Instancia, r);
        var sql = MapasEscritaRepository.SqlPara(coluna);
        var esperado = new[]
        {
            Posicional(sql.Validacao, "wkt", "srid"),
            "BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED",
            Posicional(sql.LockTimeoutUpdate),
            Posicional(sql.Update, "wkt", "srid", "id"),
            "COMMIT",
        };
        Assert.Equal(esperado, c.Sqls);

        var comandos = c.Servidor.Comandos.ToList();
        Assert.Single(comandos.Select(x => x.Conexao).Distinct());
        Assert.Equal(new[] { 25, 23 }, comandos[0].Oids);
        Assert.Equal(new object?[] { "LINESTRING(0 0, 1 1)", 4326 }, comandos[0].Parametros);
        Assert.Equal(new[] { 25, 23, 23 }, comandos[3].Oids);
        Assert.Equal(new object?[] { "LINESTRING(0 0, 1 1)", 4326, 321 }, comandos[3].Parametros);
        Assert.True(comandos[1].Simples);
        Assert.True(comandos[4].Simples);

        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2110);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task Put_IdInexistente_Rowcount0_NaoEncontrado_Rollback()
    {
        await using var c = new Cenario(Feliz(atualizadas: 0));

        var r = await c.Repositorio.AtualizarAsync(Id(9), ColunaMapa.Metry, Geometria(ColunaMapa.Metry, "POINT(1 1)"), CancellationToken.None);

        Assert.Same(ResultadoEscrita.NaoEncontrado.Instancia, r);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.False(c.Recebeu("COMMIT") || c.Recebeu("INSERT") || c.Recebeu("DELETE"));
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2109);
    }

    // ---------------- Validacao (antes da transacao) ----------------

    public static TheoryData<RespostaPg, string> Rejeicoes => new()
    {
        { RespostaPg.Validacao(false, "POINT"), MotivosGeometria.InvalidaPostgis },
        { RespostaPg.Validacao(null, "POINT"), MotivosGeometria.InvalidaPostgis },
        { RespostaPg.Validacao(true, "LINESTRING"), MotivosGeometria.TipoDiverge },
        { RespostaPg.Validacao(true, null), MotivosGeometria.TipoDiverge },
        { RespostaPg.Validacao(true, "point"), MotivosGeometria.TipoDiverge },
        { new RespostaPg.Linhas(new[] { new ColunaPg("valida", ColunaPg.Bool), new ColunaPg("tipo", ColunaPg.Text) }, Array.Empty<object?[]>()), MotivosGeometria.InvalidaPostgis },
        { new RespostaPg.Erro("XX000"), MotivosGeometria.InvalidaPostgis },
        { new RespostaPg.Erro("22023"), MotivosGeometria.InvalidaPostgis },
    };

    [Theory]
    [MemberData(nameof(Rejeicoes))]
    public async Task Validacao_Rejeitada_SemTransacao(RespostaPg resposta, string motivo)
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("ST_IsValid") ? resposta : null));

        var r = await c.Repositorio.AtualizarAsync(Id(4), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None);

        Assert.Equal(new ResultadoEscrita.GeometriaInvalida(motivo), r);
        Assert.False(c.Recebeu("BEGIN"), string.Join(" | ", c.Sqls));
        Assert.False(c.Recebeu("COMMIT"));
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2105);
        c.AssertLogLimpo();
    }

    [Theory]
    [InlineData("42501")]
    [InlineData("42883")]
    [InlineData("53300")]
    public async Task Validacao_ErroQueNaoEDeGeometria_Falha500_SemTransacao(string sqlState)
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("ST_IsValid") ? new RespostaPg.Erro(sqlState) : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.AtualizarAsync(Id(4), ColunaMapa.Metry, Geometria(ColunaMapa.Metry, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal(("validar a geometria", sqlState), (ex.Etapa, ex.SqlState));
        Assert.Null(ex.InnerException);
        Assert.False(c.Recebeu("BEGIN"));
        var log = Assert.Single(c.Log.Entradas, e => e.Evento.Id == 2115);
        Assert.Contains($"SqlState {sqlState}", log.Mensagem, StringComparison.Ordinal);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task Validacao_Cancelamento_PropagaOperationCanceled_SemTransacao()
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("ST_IsValid")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(10), RespostaPg.Validacao(true, "POINT"))
            : null));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.Repositorio.AtualizarAsync(Id(4), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), cts.Token));

        Assert.False(c.Recebeu("BEGIN"));
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2112 && e.Mensagem.Contains("validar a geometria", StringComparison.Ordinal));
    }

    // ---------------- 55P03 -> Conflito (409) ----------------

    [Fact]
    public async Task Put_LockIndisponivel_55P03_Conflito_Rollback()
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("UPDATE public.mapas") ? new RespostaPg.Erro("55P03", "canceling statement due to lock timeout") : null));

        var r = await c.Repositorio.AtualizarAsync(Id(11), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None);

        Assert.Same(ResultadoEscrita.Conflito.Instancia, r);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.False(c.Recebeu("COMMIT"));
        var log = Assert.Single(c.Log.Entradas, e => e.Evento.Id == 2111);
        Assert.Equal(LogLevel.Warning, log.Nivel);
        Assert.Contains("atualizar a coluna (UPDATE pela PK)", log.Mensagem, StringComparison.Ordinal);
        Assert.Contains("55P03", log.Mensagem, StringComparison.Ordinal);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task SetLocalLockTimeout_LogoDepoisDoBegin_AntesDoUpdate()
    {
        await using var c = new Cenario(Feliz());

        await c.Repositorio.AtualizarAsync(Id(1), ColunaMapa.Metry, Geometria(ColunaMapa.Metry, "POINT(1 2)"), CancellationToken.None);

        var sqls = c.Sqls;
        var begin = sqls.FindIndex(s => s.StartsWith("BEGIN", StringComparison.Ordinal));
        Assert.Equal("SET LOCAL lock_timeout = '5s'", sqls[begin + 1]);
        Assert.StartsWith("UPDATE public.mapas SET metry", sqls[begin + 2], StringComparison.Ordinal);
    }

    // ---------------- Falhas -> 500 (RP-10) e ROLLBACK (CP-7) ----------------

    [Theory]
    [InlineData("SET LOCAL", "57014", "definir lock_timeout")]
    [InlineData("UPDATE public.mapas", "57014", "atualizar a coluna (UPDATE pela PK)")]
    [InlineData("UPDATE public.mapas", "42501", "atualizar a coluna (UPDATE pela PK)")]
    [InlineData("UPDATE public.mapas", "XX000", "atualizar a coluna (UPDATE pela PK)")]
    public async Task Put_FalhaNaTransacao_Rollback_Falha500_LogSoComSqlState(string onde, string sqlState, string etapa)
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem(onde) ? new RespostaPg.Erro(sqlState) : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.AtualizarAsync(Id(12), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal((etapa, sqlState), (ex.Etapa, ex.SqlState));
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain("SEGREDO", ex.Message, StringComparison.Ordinal);
        if (sqlState.StartsWith("XX", StringComparison.Ordinal))
        {
            // O Npgsql descarta a conexao em erro interno (classe XX): o ROLLBACK nao e enviado e o
            // servidor desfaz ao fechar a sessao; o repositorio registra 2114 (so tipo/SqlState).
            Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2114);
        }
        else
        {
            Assert.Equal("ROLLBACK", c.Sqls[^1]);
        }

        Assert.False(c.Recebeu("COMMIT"));
        var log = Assert.Single(c.Log.Entradas, e => e.Evento.Id == 2115);
        Assert.Equal(LogLevel.Error, log.Nivel);
        Assert.Contains($"SqlState {sqlState}", log.Mensagem, StringComparison.Ordinal);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task Put_RowcountAcimaDe1_Rollback_Falha500_LogError()
    {
        await using var c = new Cenario(Feliz(atualizadas: 2));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.AtualizarAsync(Id(13), ColunaMapa.Metry, Geometria(ColunaMapa.Metry, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal("atualizar a coluna (UPDATE pela PK)", ex.Etapa);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.False(c.Recebeu("COMMIT"));
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2116 && e.Nivel == LogLevel.Error);
    }

    [Fact]
    public async Task FalhaNoBegin_Falha500_SemRollback()
    {
        await using var c = new Cenario(Feliz(antes: x => x.Simples && x.Sql.StartsWith("BEGIN", StringComparison.Ordinal) ? new RespostaPg.Erro("08P01") : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.AtualizarAsync(Id(12), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal("08P01", ex.SqlState);
        Assert.False(c.Recebeu("COMMIT"));
    }

    [Fact]
    public async Task ConexaoCaiNoMeio_Falha500_RollbackNaoConcluidoSoNoLog()
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("UPDATE public.mapas") ? new RespostaPg.Derrubar() : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.AtualizarAsync(Id(14), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal("atualizar a coluna (UPDATE pela PK)", ex.Etapa);
        Assert.False(c.Recebeu("COMMIT"));
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task FalhaNoCommit_Falha500()
    {
        await using var c = new Cenario(Feliz(antes: x => x.Simples && x.Sql.StartsWith("COMMIT", StringComparison.Ordinal) ? new RespostaPg.Erro("40001") : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.AtualizarAsync(Id(15), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal(("confirmar (COMMIT)", "40001"), (ex.Etapa, ex.SqlState));
    }

    // ---------------- Pool esgotado e banco fora do ar (RP-10) ----------------

    [Fact]
    public async Task PoolEsgotado_Falha500_AoAbrirConexao()
    {
        await using var c = new Cenario(Feliz(), maxPool: 1, timeout: 1);
        await using var ocupada = await c.Fonte.DataSource.OpenConnectionAsync();

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.AtualizarAsync(Id(16), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal("abrir conexao", ex.Etapa);
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2115 && e.Mensagem.Contains("abrir conexao", StringComparison.Ordinal));
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task BancoForaDoAr_Falha500_AoAbrirConexao()
    {
        var ouvinte = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        ouvinte.Start();
        var porta = ((System.Net.IPEndPoint)ouvinte.LocalEndpoint).Port;
        ouvinte.Stop();
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(
            $"Host=127.0.0.1;Port={porta};Username=u;Password=SENHA_NAO_VAZA;SSL Mode=Disable;Timeout=2;Server Compatibility Mode=NoTypeLoading"));
        var log = new LoggerMemoria<MapasEscritaRepository>();
        var repositorio = new MapasEscritaRepository(fonte, log);

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal("abrir conexao", ex.Etapa);
        Assert.DoesNotContain("SENHA_NAO_VAZA", log.TextoCompleto, StringComparison.Ordinal);
        Assert.DoesNotContain("SENHA_NAO_VAZA", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TokenJaCancelado_AoAbrir_PropagaOperationCanceled()
    {
        await using var c = new Cenario(Feliz());
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.Repositorio.AtualizarAsync(Id(16), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), cts.Token));

        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2112 && e.Mensagem.Contains("abrir conexao", StringComparison.Ordinal));
        Assert.Empty(c.Sqls);
    }

    // ---------------- R-5 / CP-7: cancelamento e COMMIT ----------------

    [Fact]
    public async Task CancelamentoNoUpdate_Rollback_SemCommit()
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("UPDATE public.mapas")
            ? new RespostaPg.Pendurar(TimeSpan.FromSeconds(10), new RespostaPg.Comando("UPDATE 1"))
            : null));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.Repositorio.AtualizarAsync(Id(17), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), cts.Token));

        Assert.True(c.Servidor.CancelRequests >= 1);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.False(c.Recebeu("COMMIT"));
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2112);
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2113);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task CancelamentoDepoisDoUpdate_AntesDoCommit_Rollback()
    {
        using var cts = new CancellationTokenSource();
        await using var c = new Cenario(Feliz(antes: x =>
        {
            if (x.Contem("UPDATE public.mapas"))
            {
                cts.Cancel();
                return new RespostaPg.Comando("UPDATE 1");
            }

            return null;
        }));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            c.Repositorio.AtualizarAsync(Id(18), ColunaMapa.Metry, Geometria(ColunaMapa.Metry, "POINT(1 2)"), cts.Token));

        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.False(c.Recebeu("COMMIT"));
    }

    [Fact]
    public async Task CancelamentoDuranteOCommit_NaoInterrompe_CommitComTokenNone()
    {
        using var cts = new CancellationTokenSource();
        await using var c = new Cenario(Feliz(antes: x =>
        {
            if (x.Simples && x.Sql.StartsWith("COMMIT", StringComparison.Ordinal))
            {
                cts.Cancel();
                return new RespostaPg.Comando("COMMIT");
            }

            return null;
        }));

        var r = await c.Repositorio.AtualizarAsync(Id(19), ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), cts.Token);

        Assert.Same(ResultadoEscrita.Atualizado.Instancia, r);
        Assert.Equal("COMMIT", c.Sqls[^1]);
        Assert.False(c.Recebeu("ROLLBACK"));
        Assert.Equal(0, c.Servidor.CancelRequests);
    }

    // ---------------- POST: INSERT ... RETURNING id (smoke do PO4; a suite do POST e do PO6) ----------------

    [Theory]
    [InlineData(ColunaMapa.Graphy)]
    [InlineData(ColunaMapa.Metry)]
    public async Task Post_Sucesso_OrdemExata_IdDoReturning(ColunaMapa coluna)
    {
        await using var c = new Cenario(Feliz());

        var r = await c.Repositorio.CriarAsync(coluna, Geometria(coluna, "POINT(1 2)"), CancellationToken.None);

        Assert.Equal(new ResultadoEscrita.Criado(IdGerado), r);
        var sql = MapasEscritaRepository.SqlPara(coluna);
        Assert.Equal(new[]
        {
            Posicional(sql.Validacao, "wkt", "srid"),
            "BEGIN TRANSACTION ISOLATION LEVEL READ COMMITTED",
            Posicional(sql.LockTimeoutInsert),
            Posicional(sql.Insert, "wkt", "srid"),
            "COMMIT",
        }, c.Sqls);
        Assert.Equal(new[] { 25, 23 }, c.Servidor.Comandos.ToList()[3].Oids);
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2131);
        c.AssertLogLimpo();
    }

    [Theory]
    [InlineData("55P03")]
    [InlineData("40P01")]
    public async Task Post_LockOuDeadlock_Conflito_Rollback(string sqlState)
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("INSERT INTO") ? new RespostaPg.Erro(sqlState) : null));

        var r = await c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None);

        Assert.Same(ResultadoEscrita.Conflito.Instancia, r);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2111 && e.Nivel == LogLevel.Warning);
        c.AssertLogLimpo();
    }

    [Theory]
    [InlineData("23505", "mapas_pkey", 2132)]
    [InlineData("23505", "outra_unica", 2133)]
    [InlineData("23514", "mapas_nao_vazia", 2133)]
    public async Task Post_ViolacaoDeRestricao_Rollback_Falha500_SemRetry_SemDetail(string sqlState, string constraint, int evento)
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("INSERT INTO") ? new RespostaPg.Erro(sqlState, Constraint: constraint) : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Metry, Geometria(ColunaMapa.Metry, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal(sqlState, ex.SqlState);
        Assert.Equal(1, c.Sqls.Count(s => s.StartsWith("INSERT", StringComparison.Ordinal)));
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        var log = Assert.Single(c.Log.Entradas, e => e.Evento.Id == evento);
        Assert.Equal(LogLevel.Error, log.Nivel);
        Assert.Contains(constraint, log.Mensagem, StringComparison.Ordinal);
        c.AssertLogLimpo();
    }

    [Fact]
    public async Task Post_ReturningNulo_Rollback_Falha500()
    {
        await using var c = new Cenario(Feliz(antes: x => x.Contem("INSERT INTO") ? Retorno(null) : null));

        var ex = await Assert.ThrowsAsync<FalhaBancoEscritaException>(() =>
            c.Repositorio.CriarAsync(ColunaMapa.Graphy, Geometria(ColunaMapa.Graphy, "POINT(1 2)"), CancellationToken.None));

        Assert.Equal("inserir a geometria (INSERT ... RETURNING id)", ex.Etapa);
        Assert.Equal("ROLLBACK", c.Sqls[^1]);
        Assert.Contains(c.Log.Entradas, e => e.Evento.Id == 2134);
    }

    // ---------------- Argumentos ----------------

    [Fact]
    public async Task Argumentos_GeometriaNula_Lanca_SemConectar()
    {
        await using var c = new Cenario(Feliz());

        await Assert.ThrowsAsync<ArgumentNullException>(() => c.Repositorio.AtualizarAsync(Id(1), ColunaMapa.Graphy, null!, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentNullException>(() => c.Repositorio.CriarAsync(ColunaMapa.Graphy, null!, CancellationToken.None));
        Assert.Equal(0, c.Servidor.Conexoes);
    }
}
