using System.Data;
using System.Data.Common;
using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;
using ApiMapas.Infraestrutura.Configuracao;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace ApiMapas.Infraestrutura.Dados;

/// <summary>
/// Escrita em public.mapas, uma geometria por id (PO4; D-N5, D-N6; RS-17, RS-18, RS-19; RP-3, RP-4,
/// RP-10, RP-15; CN-9, CN-12). SQL copiado EXATAMENTE de api_mapas/sql/consultas_referencia.sql
/// (W1.V/W2.V, W1 a W4): um conjunto constante por coluna, escolhido por switch no enum, com
/// parametros tipados (@id Integer, @wkt Text, @srid Integer) e sem concatenacao. Sem advisory
/// lock, sem DELETE, sem id explicito em INSERT e sem acesso a sequencia. Usa SOMENTE a conexao de
/// escrita (<see cref="FonteDadosEscrita"/>); uma conexao por requisicao. Qualquer falha,
/// cancelamento ou prazo estourado faz ROLLBACK. Nunca registra WKT, MessageText, Detail, Hint ou
/// Where: so etapa, coluna, id, tipo, SqlState e nome de constraint.
/// </summary>
public sealed partial class MapasEscritaRepository : IMapasEscritaRepository
{
    /// <summary>SqlState do lock_timeout estourado (lock_not_available) - 409.</summary>
    internal const string SqlStateLockIndisponivel = "55P03";

    /// <summary>SqlState de deadlock (deadlock_detected) - 409 no POST (RS-18).</summary>
    internal const string SqlStateDeadlock = "40P01";

    /// <summary>SqlState de violacao de unicidade (unique_violation) - 500 sem retry (RS-17).</summary>
    internal const string SqlStateUnicidade = "23505";

    /// <summary>SqlState de violacao de CHECK (check_violation) - 500 (RS-17).</summary>
    internal const string SqlStateCheck = "23514";

    /// <summary>Nome da PK de public.mapas (migracao PO3).</summary>
    internal const string ConstraintPk = "mapas_pkey";

    // ---------------- W1 - PUT /api/mapas/{id}/graphy ; W3 - POST /api/mapas/graphy ----------------

    /// <summary>W1.V - validacao (usada pelo PUT W1 e pelo POST W3), antes da transacao.</summary>
    internal const string SqlW1Validacao =
        "SELECT ST_IsValid(v.g) AS valida, GeometryType(v.g) AS tipo FROM (SELECT ST_GeomFromText(@wkt, @srid)::geography::geometry AS g) AS v;";

    /// <summary>W1.1</summary>
    internal const string SqlW1LockTimeout = "SET LOCAL lock_timeout = '5s';";

    /// <summary>W1.2</summary>
    internal const string SqlW1Update =
        "UPDATE public.mapas SET graphy = ST_GeomFromText(@wkt, @srid)::geography WHERE id = @id;";

    /// <summary>W3.1</summary>
    internal const string SqlW3LockTimeout = "SET LOCAL lock_timeout = '5s';";

    /// <summary>W3.2</summary>
    internal const string SqlW3Insert =
        "INSERT INTO public.mapas (graphy) VALUES (ST_GeomFromText(@wkt, @srid)::geography) RETURNING id;";

    // ---------------- W2 - PUT /api/mapas/{id}/metry ; W4 - POST /api/mapas/metry -----------------

    /// <summary>W2.V - validacao (usada pelo PUT W2 e pelo POST W4), antes da transacao.</summary>
    internal const string SqlW2Validacao =
        "SELECT ST_IsValid(v.g) AS valida, GeometryType(v.g) AS tipo FROM (SELECT ST_GeomFromText(@wkt, @srid) AS g) AS v;";

    /// <summary>W2.1</summary>
    internal const string SqlW2LockTimeout = "SET LOCAL lock_timeout = '5s';";

    /// <summary>W2.2</summary>
    internal const string SqlW2Update =
        "UPDATE public.mapas SET metry = ST_GeomFromText(@wkt, @srid) WHERE id = @id;";

    /// <summary>W4.1</summary>
    internal const string SqlW4LockTimeout = "SET LOCAL lock_timeout = '5s';";

    /// <summary>W4.2</summary>
    internal const string SqlW4Insert =
        "INSERT INTO public.mapas (metry) VALUES (ST_GeomFromText(@wkt, @srid)) RETURNING id;";

    internal const string ParametroId = "id";
    internal const string ParametroWkt = "wkt";
    internal const string ParametroSrid = "srid";

    internal const string MetodoPut = "PUT";
    internal const string MetodoPost = "POST";

    /// <summary>Alvo dos logs do POST, antes de o id existir.</summary>
    private const string AlvoNovo = "novo";

    private static readonly SqlEscrita SqlGraphy = new(SqlW1Validacao, SqlW1LockTimeout, SqlW1Update, SqlW3LockTimeout, SqlW3Insert);

    private static readonly SqlEscrita SqlMetry = new(SqlW2Validacao, SqlW2LockTimeout, SqlW2Update, SqlW4LockTimeout, SqlW4Insert);

    private readonly FonteDadosEscrita _fonte;
    private readonly ILogger<MapasEscritaRepository> _log;

    public MapasEscritaRepository(FonteDadosEscrita fonte, ILogger<MapasEscritaRepository> log)
    {
        ArgumentNullException.ThrowIfNull(fonte);
        ArgumentNullException.ThrowIfNull(log);
        _fonte = fonte;
        _log = log;
    }

    /// <summary>Conjunto constante de comandos de uma coluna (W1+W3 ou W2+W4).</summary>
    internal sealed record SqlEscrita(
        string Validacao,
        string LockTimeoutUpdate,
        string Update,
        string LockTimeoutInsert,
        string Insert);

    /// <summary>RP-15 / CP-10: SQL constante por coluna; qualquer outro valor do enum e rejeitado.</summary>
    internal static SqlEscrita SqlPara(ColunaMapa coluna) => coluna switch
    {
        ColunaMapa.Graphy => SqlGraphy,
        ColunaMapa.Metry => SqlMetry,
        _ => throw new ArgumentOutOfRangeException(nameof(coluna), "coluna nao suportada"),
    };

    /// <summary>W*.V: @wkt Text e @srid Integer.</summary>
    internal static NpgsqlCommand CriarComandoValidacao(ColunaMapa coluna, GeometriaEntrada geometria)
    {
        ArgumentNullException.ThrowIfNull(geometria);
        var comando = new NpgsqlCommand(SqlPara(coluna).Validacao);
        AdicionarGeometria(comando, geometria);
        return comando;
    }

    /// <summary>W*.1: SET LOCAL lock_timeout, sem parametros.</summary>
    internal static NpgsqlCommand CriarComandoLockTimeout(string sql) => new(sql);

    /// <summary>W1.2/W2.2: @wkt Text, @srid Integer e @id Integer.</summary>
    internal static NpgsqlCommand CriarComandoUpdate(ColunaMapa coluna, IdMapa id, GeometriaEntrada geometria)
    {
        ArgumentNullException.ThrowIfNull(geometria);
        var comando = new NpgsqlCommand(SqlPara(coluna).Update);
        AdicionarGeometria(comando, geometria);
        comando.Parameters.Add(new NpgsqlParameter(ParametroId, NpgsqlDbType.Integer) { Value = id.Valor });
        return comando;
    }

    /// <summary>W3.2/W4.2: @wkt Text e @srid Integer; o id vem da identity (RETURNING id).</summary>
    internal static NpgsqlCommand CriarComandoInsert(ColunaMapa coluna, GeometriaEntrada geometria)
    {
        ArgumentNullException.ThrowIfNull(geometria);
        var comando = new NpgsqlCommand(SqlPara(coluna).Insert);
        AdicionarGeometria(comando, geometria);
        return comando;
    }

    private static void AdicionarGeometria(NpgsqlCommand comando, GeometriaEntrada geometria)
    {
        comando.Parameters.Add(new NpgsqlParameter(ParametroWkt, NpgsqlDbType.Text) { Value = geometria.Wkt });
        comando.Parameters.Add(new NpgsqlParameter(ParametroSrid, NpgsqlDbType.Integer) { Value = geometria.Srid });
    }

    public async Task<ResultadoEscrita> AtualizarAsync(IdMapa id, ColunaMapa coluna, GeometriaEntrada geometria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geometria);
        var nomeColuna = coluna.Nome();
        var alvo = id.ToString();

        var conexao = await AbrirConexaoAsync(MetodoPut, nomeColuna, alvo, cancellationToken).ConfigureAwait(false);
        await using (conexao.ConfigureAwait(false))
        {
            var rejeicao = await ValidarAsync(conexao, coluna, geometria, MetodoPut, nomeColuna, alvo, cancellationToken).ConfigureAwait(false);
            if (rejeicao is not null)
            {
                return rejeicao;
            }

            var resultado = await GravarUpdateAsync(conexao, coluna, nomeColuna, id, geometria, cancellationToken).ConfigureAwait(false);
            LogConexaoDevolvida(MetodoPut, nomeColuna);
            return resultado;
        }
    }

    public async Task<ResultadoEscrita> CriarAsync(ColunaMapa coluna, GeometriaEntrada geometria, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(geometria);
        var nomeColuna = coluna.Nome();

        var conexao = await AbrirConexaoAsync(MetodoPost, nomeColuna, AlvoNovo, cancellationToken).ConfigureAwait(false);
        await using (conexao.ConfigureAwait(false))
        {
            var rejeicao = await ValidarAsync(conexao, coluna, geometria, MetodoPost, nomeColuna, AlvoNovo, cancellationToken).ConfigureAwait(false);
            if (rejeicao is not null)
            {
                return rejeicao;
            }

            var resultado = await GravarInsertAsync(conexao, coluna, nomeColuna, geometria, cancellationToken).ConfigureAwait(false);
            LogConexaoDevolvida(MetodoPost, nomeColuna);
            return resultado;
        }
    }

    private async Task<NpgsqlConnection> AbrirConexaoAsync(string metodo, string nomeColuna, string alvo, CancellationToken cancellationToken)
    {
        try
        {
            LogAbrindoConexao(metodo, nomeColuna, alvo);
            return await _fonte.DataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            LogCancelado(metodo, "abrir conexao", nomeColuna, alvo);
            throw;
        }
        catch (Exception ex) when (EhFalhaBanco(ex))
        {
            // Inclui pool esgotado (NpgsqlException com TimeoutException interna) - 500 (RP-10).
            throw Falha(ex, metodo, "abrir conexao", nomeColuna, alvo);
        }
    }

    /// <summary>
    /// Etapa 1 (D-P6): W*.V num comando simples, sem transacao explicita. Devolve a rejeicao (motivo
    /// fixo) ou null quando a geometria e valida e do mesmo tipo lido localmente.
    /// </summary>
    private async Task<ResultadoEscrita.GeometriaInvalida?> ValidarAsync(
        NpgsqlConnection conexao,
        ColunaMapa coluna,
        GeometriaEntrada geometria,
        string metodo,
        string nomeColuna,
        string alvo,
        CancellationToken cancellationToken)
    {
        LogValidando(metodo, nomeColuna, alvo);
        ResultadoEscrita.GeometriaInvalida? rejeicao;

        var comando = CriarComandoValidacao(coluna, geometria);
        await using (comando.ConfigureAwait(false))
        {
            comando.Connection = conexao;
            try
            {
                var leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
                await using (leitor.ConfigureAwait(false))
                {
                    rejeicao = await LerValidacaoAsync(leitor, geometria, cancellationToken).ConfigureAwait(false);
                }
            }
            catch (PostgresException ex) when (EhErroDeGeometria(ex.SqlState))
            {
                // XX000 (parse do WKT) ou classe 22: 400; so o SqlState vai ao log.
                LogRejeitadaPostgis(metodo, ex.SqlState, nomeColuna, alvo);
                rejeicao = new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.InvalidaPostgis);
            }
            catch (OperationCanceledException)
            {
                LogCancelado(metodo, "validar a geometria", nomeColuna, alvo);
                throw;
            }
            catch (Exception ex) when (EhFalhaBanco(ex))
            {
                throw Falha(ex, metodo, "validar a geometria", nomeColuna, alvo);
            }
        }

        if (rejeicao is not null)
        {
            LogRejeitada(metodo, nomeColuna, alvo);
            return rejeicao;
        }

        LogValida(metodo, nomeColuna, alvo);
        return null;
    }

    /// <summary>Le a linha (valida, tipo); devolve a rejeicao ou null.</summary>
    private static async Task<ResultadoEscrita.GeometriaInvalida?> LerValidacaoAsync(
        NpgsqlDataReader leitor,
        GeometriaEntrada geometria,
        CancellationToken cancellationToken)
    {
        if (!await leitor.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            return new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.InvalidaPostgis);
        }

        var valida = !leitor.IsDBNull(0) && leitor.GetBoolean(0);
        if (!valida)
        {
            return new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.InvalidaPostgis);
        }

        var tipo = leitor.IsDBNull(1) ? null : leitor.GetString(1);
        return string.Equals(tipo, geometria.Tipo, StringComparison.Ordinal)
            ? null
            : new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.TipoDiverge);
    }

    /// <summary>
    /// PUT, etapa 2 (D-N6): BEGIN (READ COMMITTED) -> W*.1 lock_timeout -> W*.2 UPDATE pela PK ->
    /// COMMIT. rowcount 0 = ROLLBACK e 404; mais de 1 = ROLLBACK, log Error e 500 (RS-19); 55P03 = 409.
    /// </summary>
    private async Task<ResultadoEscrita> GravarUpdateAsync(
        NpgsqlConnection conexao,
        ColunaMapa coluna,
        string nomeColuna,
        IdMapa id,
        GeometriaEntrada geometria,
        CancellationToken cancellationToken)
    {
        var sql = SqlPara(coluna);
        var alvo = id.ToString();
        NpgsqlTransaction? transacao = null;
        var etapa = "iniciar a transacao";

        try
        {
            LogIniciandoTransacao(MetodoPut, nomeColuna, alvo);
            transacao = await conexao.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

            etapa = "definir lock_timeout";
            LogEtapa(MetodoPut, etapa, nomeColuna, alvo);
            await ExecutarAsync(conexao, transacao, CriarComandoLockTimeout(sql.LockTimeoutUpdate), cancellationToken).ConfigureAwait(false);

            etapa = "atualizar a coluna (UPDATE pela PK)";
            LogEtapa(MetodoPut, etapa, nomeColuna, alvo);
            var linhas = await ExecutarAsync(conexao, transacao, CriarComandoUpdate(coluna, id, geometria), cancellationToken).ConfigureAwait(false);

            if (linhas == 0)
            {
                await DesfazerAsync(transacao, MetodoPut, nomeColuna, alvo).ConfigureAwait(false);
                LogIdInexistente(nomeColuna, id.Valor);
                return ResultadoEscrita.NaoEncontrado.Instancia;
            }

            if (linhas > 1)
            {
                // RS-19: so possivel sem a PK (migracao nao aplicada).
                await DesfazerAsync(transacao, MetodoPut, nomeColuna, alvo).ConfigureAwait(false);
                LogLinhasDemais(linhas, nomeColuna, id.Valor);
                throw new FalhaBancoEscritaException(etapa, "-", "rowcount acima de 1");
            }

            // Cancelado antes do COMMIT: desfaz. O COMMIT em si nao recebe o token para nao deixar
            // o resultado ambiguo (gravado no banco, mas respondido como falha).
            cancellationToken.ThrowIfCancellationRequested();

            etapa = "confirmar (COMMIT)";
            LogEtapa(MetodoPut, etapa, nomeColuna, alvo);
            await transacao.CommitAsync(CancellationToken.None).ConfigureAwait(false);

            LogAtualizado(nomeColuna, id.Valor);
            return ResultadoEscrita.Atualizado.Instancia;
        }
        catch (PostgresException ex) when (ex.SqlState == SqlStateLockIndisponivel)
        {
            // 55P03 no lock de linha do UPDATE: 409.
            await DesfazerAsync(transacao, MetodoPut, nomeColuna, alvo).ConfigureAwait(false);
            LogConflito(MetodoPut, etapa, ex.SqlState, nomeColuna, alvo);
            return ResultadoEscrita.Conflito.Instancia;
        }
        catch (OperationCanceledException)
        {
            // Cliente desconectou ou prazo de 30 s estourou (CP-7, R-5).
            await DesfazerAsync(transacao, MetodoPut, nomeColuna, alvo).ConfigureAwait(false);
            LogCancelado(MetodoPut, etapa, nomeColuna, alvo);
            throw;
        }
        catch (Exception ex) when (EhFalhaBanco(ex))
        {
            // 57014, pool esgotado, conexao perdida, permissao etc.: 500 (RP-10).
            await DesfazerAsync(transacao, MetodoPut, nomeColuna, alvo).ConfigureAwait(false);
            throw Falha(ex, MetodoPut, etapa, nomeColuna, alvo);
        }
        finally
        {
            if (transacao is not null)
            {
                await transacao.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// POST, etapa 2 (D-N5): BEGIN (READ COMMITTED) -> W*.1 lock_timeout -> W3.2/W4.2 INSERT ...
    /// RETURNING id -> COMMIT. 55P03/40P01 = ROLLBACK e 409 (RS-18); 23505/23514 = ROLLBACK, log Error
    /// so com SqlState e constraint, 500 e SEM retry (RS-17).
    /// </summary>
    private async Task<ResultadoEscrita> GravarInsertAsync(
        NpgsqlConnection conexao,
        ColunaMapa coluna,
        string nomeColuna,
        GeometriaEntrada geometria,
        CancellationToken cancellationToken)
    {
        var sql = SqlPara(coluna);
        NpgsqlTransaction? transacao = null;
        var etapa = "iniciar a transacao";

        try
        {
            LogIniciandoTransacao(MetodoPost, nomeColuna, AlvoNovo);
            transacao = await conexao.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken).ConfigureAwait(false);

            etapa = "definir lock_timeout";
            LogEtapa(MetodoPost, etapa, nomeColuna, AlvoNovo);
            await ExecutarAsync(conexao, transacao, CriarComandoLockTimeout(sql.LockTimeoutInsert), cancellationToken).ConfigureAwait(false);

            etapa = "inserir a geometria (INSERT ... RETURNING id)";
            LogEtapa(MetodoPost, etapa, nomeColuna, AlvoNovo);
            var valor = await ExecutarEscalarAsync(conexao, transacao, CriarComandoInsert(coluna, geometria), cancellationToken).ConfigureAwait(false);

            if (valor is not int idGerado || idGerado < IdMapa.Minimo)
            {
                await DesfazerAsync(transacao, MetodoPost, nomeColuna, AlvoNovo).ConfigureAwait(false);
                LogRetornoInvalido(nomeColuna, valor?.GetType().Name ?? "null");
                throw new FalhaBancoEscritaException(etapa, "-", "RETURNING id invalido");
            }

            cancellationToken.ThrowIfCancellationRequested();

            etapa = "confirmar (COMMIT)";
            LogEtapa(MetodoPost, etapa, nomeColuna, AlvoNovo);
            await transacao.CommitAsync(CancellationToken.None).ConfigureAwait(false);

            LogCriado(nomeColuna, idGerado);
            return new ResultadoEscrita.Criado(idGerado);
        }
        catch (PostgresException ex) when (ex.SqlState is SqlStateLockIndisponivel or SqlStateDeadlock)
        {
            // RS-18: lock nao obtido em 5 s ou deadlock: 409.
            await DesfazerAsync(transacao, MetodoPost, nomeColuna, AlvoNovo).ConfigureAwait(false);
            LogConflito(MetodoPost, etapa, ex.SqlState, nomeColuna, AlvoNovo);
            return ResultadoEscrita.Conflito.Instancia;
        }
        catch (PostgresException ex) when (ex.SqlState is SqlStateUnicidade or SqlStateCheck)
        {
            // RS-17: 500 sem retry. So SqlState e nome da constraint (nunca MessageText/Detail).
            await DesfazerAsync(transacao, MetodoPost, nomeColuna, AlvoNovo).ConfigureAwait(false);
            var constraint = string.IsNullOrEmpty(ex.ConstraintName) ? "-" : ex.ConstraintName;
            if (ex.SqlState == SqlStateUnicidade && string.Equals(ex.ConstraintName, ConstraintPk, StringComparison.Ordinal))
            {
                LogIdGeradoJaExiste(ex.SqlState, constraint, nomeColuna);
            }
            else
            {
                LogRestricaoViolada(ex.SqlState, constraint, nomeColuna);
            }

            throw new FalhaBancoEscritaException(etapa, ex.SqlState, nameof(PostgresException));
        }
        catch (OperationCanceledException)
        {
            await DesfazerAsync(transacao, MetodoPost, nomeColuna, AlvoNovo).ConfigureAwait(false);
            LogCancelado(MetodoPost, etapa, nomeColuna, AlvoNovo);
            throw;
        }
        catch (Exception ex) when (EhFalhaBanco(ex))
        {
            // 57014, pool esgotado, conexao perdida, permissao etc.: 500 (RP-10).
            await DesfazerAsync(transacao, MetodoPost, nomeColuna, AlvoNovo).ConfigureAwait(false);
            throw Falha(ex, MetodoPost, etapa, nomeColuna, AlvoNovo);
        }
        finally
        {
            if (transacao is not null)
            {
                await transacao.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    private static async Task<int> ExecutarAsync(NpgsqlConnection conexao, NpgsqlTransaction transacao, NpgsqlCommand comando, CancellationToken cancellationToken)
    {
        await using (comando.ConfigureAwait(false))
        {
            comando.Connection = conexao;
            comando.Transaction = transacao;
            return await comando.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task<object?> ExecutarEscalarAsync(NpgsqlConnection conexao, NpgsqlTransaction transacao, NpgsqlCommand comando, CancellationToken cancellationToken)
    {
        await using (comando.ConfigureAwait(false))
        {
            comando.Connection = conexao;
            comando.Transaction = transacao;
            return await comando.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>ROLLBACK sem token (precisa rodar mesmo com a requisicao cancelada); falha so vai ao log.</summary>
    private async Task DesfazerAsync(NpgsqlTransaction? transacao, string metodo, string nomeColuna, string alvo)
    {
        if (transacao is null)
        {
            return;
        }

        try
        {
            await transacao.RollbackAsync(CancellationToken.None).ConfigureAwait(false);
            LogRollback(metodo, nomeColuna, alvo);
        }
        catch (Exception ex) when (EhFalhaBanco(ex) || ex is InvalidOperationException)
        {
            // Conexao quebrada: o servidor desfaz a transacao ao fechar a sessao.
            LogRollbackFalhou(metodo, nomeColuna, alvo, ex.GetType().Name, SqlStateDe(ex));
        }
    }

    private static bool EhFalhaBanco(Exception ex) => ex is NpgsqlException or TimeoutException or DbException or InvalidCastException;

    /// <summary>XX000 (parse/erro interno do PostGIS) ou classe 22 (dados invalidos).</summary>
    internal static bool EhErroDeGeometria(string? sqlState) =>
        sqlState is not null && (sqlState == "XX000" || sqlState.StartsWith("22", StringComparison.Ordinal));

    private static string SqlStateDe(Exception ex) =>
        (ex as PostgresException)?.SqlState ?? (ex as NpgsqlException)?.SqlState ?? "-";

    /// <summary>
    /// Registra a falha (so metodo, etapa, coluna, alvo, tipo e SqlState - RP-10) e devolve a
    /// excecao sanitizada, sem a excecao original como interna.
    /// </summary>
    private FalhaBancoEscritaException Falha(Exception ex, string metodo, string etapa, string nomeColuna, string alvo)
    {
        var sqlState = SqlStateDe(ex);
        var tipo = ex.GetType().Name;
        LogFalhaBanco(metodo, etapa, nomeColuna, alvo, tipo, sqlState);
        return new FalhaBancoEscritaException(etapa, sqlState, tipo);
    }

    // ---- Logs estruturados (RP-10, RS-4): nunca connection string, senha, WKT, MessageText/Detail/Hint/Where. ----
    // {Alvo} e o id ja validado no PUT e "novo" no POST (o id so existe depois do RETURNING).

    [LoggerMessage(EventId = 2101, Level = LogLevel.Debug, Message = "Escrita {Metodo}: abrindo conexao do pool de escrita (coluna {Coluna}, id {Alvo}).")]
    private partial void LogAbrindoConexao(string metodo, string coluna, string alvo);

    [LoggerMessage(EventId = 2102, Level = LogLevel.Debug, Message = "Escrita {Metodo}: validando a geometria no banco (coluna {Coluna}, id {Alvo}).")]
    private partial void LogValidando(string metodo, string coluna, string alvo);

    [LoggerMessage(EventId = 2103, Level = LogLevel.Debug, Message = "Escrita {Metodo}: geometria valida no banco (coluna {Coluna}, id {Alvo}).")]
    private partial void LogValida(string metodo, string coluna, string alvo);

    [LoggerMessage(EventId = 2104, Level = LogLevel.Information, Message = "Escrita {Metodo}: PostGIS rejeitou a geometria (SqlState {SqlState}, coluna {Coluna}, id {Alvo}).")]
    private partial void LogRejeitadaPostgis(string metodo, string sqlState, string coluna, string alvo);

    [LoggerMessage(EventId = 2105, Level = LogLevel.Debug, Message = "Escrita {Metodo}: geometria rejeitada na validacao do banco; nada gravado (coluna {Coluna}, id {Alvo}).")]
    private partial void LogRejeitada(string metodo, string coluna, string alvo);

    [LoggerMessage(EventId = 2107, Level = LogLevel.Debug, Message = "Escrita {Metodo}: BEGIN (READ COMMITTED) (coluna {Coluna}, id {Alvo}).")]
    private partial void LogIniciandoTransacao(string metodo, string coluna, string alvo);

    [LoggerMessage(EventId = 2108, Level = LogLevel.Debug, Message = "Escrita {Metodo}: etapa '{Etapa}' (coluna {Coluna}, id {Alvo}).")]
    private partial void LogEtapa(string metodo, string etapa, string coluna, string alvo);

    [LoggerMessage(EventId = 2109, Level = LogLevel.Debug, Message = "Escrita PUT: id {Id} sem linha em public.mapas (rowcount 0); ROLLBACK (coluna {Coluna}).")]
    private partial void LogIdInexistente(string coluna, int id);

    [LoggerMessage(EventId = 2110, Level = LogLevel.Debug, Message = "Escrita PUT: COMMIT (coluna {Coluna}, id {Id}): 1 linha atualizada.")]
    private partial void LogAtualizado(string coluna, int id);

    [LoggerMessage(EventId = 2111, Level = LogLevel.Warning, Message = "Escrita {Metodo}: lock nao obtido ou deadlock ao {Etapa} (SqlState {SqlState}, coluna {Coluna}, id {Alvo}); ROLLBACK.")]
    private partial void LogConflito(string metodo, string etapa, string sqlState, string coluna, string alvo);

    [LoggerMessage(EventId = 2112, Level = LogLevel.Information, Message = "Escrita {Metodo}: cancelada ao {Etapa} (coluna {Coluna}, id {Alvo}).")]
    private partial void LogCancelado(string metodo, string etapa, string coluna, string alvo);

    [LoggerMessage(EventId = 2113, Level = LogLevel.Debug, Message = "Escrita {Metodo}: ROLLBACK executado (coluna {Coluna}, id {Alvo}).")]
    private partial void LogRollback(string metodo, string coluna, string alvo);

    [LoggerMessage(EventId = 2114, Level = LogLevel.Warning, Message = "Escrita {Metodo}: ROLLBACK nao concluido (coluna {Coluna}, id {Alvo}, tipo {TipoErro}, SqlState {SqlState}); o servidor desfaz ao fechar a sessao.")]
    private partial void LogRollbackFalhou(string metodo, string coluna, string alvo, string tipoErro, string sqlState);

    [LoggerMessage(EventId = 2115, Level = LogLevel.Error, Message = "Escrita {Metodo}: falha no banco ao {Etapa} (coluna {Coluna}, id {Alvo}, tipo {TipoErro}, SqlState {SqlState}).")]
    private partial void LogFalhaBanco(string metodo, string etapa, string coluna, string alvo, string tipoErro, string sqlState);

    [LoggerMessage(EventId = 2116, Level = LogLevel.Error, Message = "Escrita PUT: o UPDATE pela PK alterou {Linhas} linhas do id {Id} (coluna {Coluna}); ROLLBACK. A migracao para PRIMARY KEY nao foi aplicada.")]
    private partial void LogLinhasDemais(int linhas, string coluna, int id);

    [LoggerMessage(EventId = 2118, Level = LogLevel.Debug, Message = "Escrita {Metodo}: conexao devolvida ao pool de escrita (coluna {Coluna}).")]
    private partial void LogConexaoDevolvida(string metodo, string coluna);

    [LoggerMessage(EventId = 2131, Level = LogLevel.Debug, Message = "Escrita POST: COMMIT (coluna {Coluna}): id {Id} criado.")]
    private partial void LogCriado(string coluna, int id);

    [LoggerMessage(EventId = 2132, Level = LogLevel.Error, Message = "Escrita POST: id gerado ja existe; conferir a sequencia (SqlState {SqlState}, constraint {Constraint}, coluna {Coluna}). ROLLBACK, sem nova tentativa.")]
    private partial void LogIdGeradoJaExiste(string sqlState, string constraint, string coluna);

    [LoggerMessage(EventId = 2133, Level = LogLevel.Error, Message = "Escrita POST: restricao violada (SqlState {SqlState}, constraint {Constraint}, coluna {Coluna}). ROLLBACK, sem nova tentativa.")]
    private partial void LogRestricaoViolada(string sqlState, string constraint, string coluna);

    [LoggerMessage(EventId = 2134, Level = LogLevel.Error, Message = "Escrita POST: o INSERT nao devolveu um id valido (tipo {Tipo}, coluna {Coluna}); ROLLBACK.")]
    private partial void LogRetornoInvalido(string coluna, string tipo);
}
