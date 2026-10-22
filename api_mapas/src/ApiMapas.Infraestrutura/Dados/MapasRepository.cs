using System.Data.Common;
using System.Runtime.CompilerServices;
using ApiMapas.Dominio;
using Microsoft.Extensions.Logging;
using Npgsql;
using NpgsqlTypes;

namespace ApiMapas.Infraestrutura.Dados;

/// <summary>
/// Leitura de public.mapas com Npgsql (A4.3, CA-5, RA-3, RA-12).
/// SQL copiado EXATAMENTE de api_mapas/sql/consultas_referencia.sql (Q1 e Q2): duas constantes
/// escolhidas pelo enum <see cref="ColunaMapa"/>, unico parametro @id com NpgsqlDbType.Integer,
/// sem concatenacao. Cada chamada abre sua propria conexao do pool (uma por requisicao) e o
/// CancellationToken da requisicao chega ate OpenConnectionAsync/ExecuteReaderAsync/ReadAsync.
/// </summary>
public sealed partial class MapasRepository : IMapasRepository
{
    /// <summary>Q1 - GET /api/mapas/{id}/graphy.</summary>
    internal const string SqlGraphy =
        "SELECT GeometryType(t.g)                                        AS tipo,\n" +
        "       ST_SRID(t.g)                                             AS srid,\n" +
        "       ST_AsText(t.g)                                           AS wkt,\n" +
        "       CASE WHEN ST_SRID(t.g) <> 0\n" +
        "            THEN ST_AsGeoJSON(ST_Transform(t.g, 4326), 9)\n" +
        "            ELSE ST_AsGeoJSON(t.g, 9) END                       AS geojson,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT'\n" +
        "            THEN ST_X(t.g) END                                  AS x,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT'\n" +
        "            THEN ST_Y(t.g) END                                  AS y,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0\n" +
        "            THEN ST_X(ST_Transform(t.g, 4326)) END              AS longitude,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0\n" +
        "            THEN ST_Y(ST_Transform(t.g, 4326)) END              AS latitude\n" +
        "FROM (SELECT m.ctid AS linha, m.graphy::geometry AS g\n" +
        "      FROM public.mapas AS m\n" +
        "      WHERE m.id = @id\n" +
        "        AND m.graphy IS NOT NULL\n" +
        "      ORDER BY m.ctid\n" +
        "      LIMIT 101) AS t\n" +
        "ORDER BY t.linha;";

    /// <summary>Q2 - GET /api/mapas/{id}/metry.</summary>
    internal const string SqlMetry =
        "SELECT GeometryType(t.g)                                        AS tipo,\n" +
        "       ST_SRID(t.g)                                             AS srid,\n" +
        "       ST_AsText(t.g)                                           AS wkt,\n" +
        "       CASE WHEN ST_SRID(t.g) <> 0\n" +
        "            THEN ST_AsGeoJSON(ST_Transform(t.g, 4326), 9)\n" +
        "            ELSE ST_AsGeoJSON(t.g, 9) END                       AS geojson,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT'\n" +
        "            THEN ST_X(t.g) END                                  AS x,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT'\n" +
        "            THEN ST_Y(t.g) END                                  AS y,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0\n" +
        "            THEN ST_X(ST_Transform(t.g, 4326)) END              AS longitude,\n" +
        "       CASE WHEN GeometryType(t.g) = 'POINT' AND ST_SRID(t.g) <> 0\n" +
        "            THEN ST_Y(ST_Transform(t.g, 4326)) END              AS latitude\n" +
        "FROM (SELECT m.ctid AS linha, m.metry AS g\n" +
        "      FROM public.mapas AS m\n" +
        "      WHERE m.id = @id\n" +
        "        AND m.metry IS NOT NULL\n" +
        "      ORDER BY m.ctid\n" +
        "      LIMIT 101) AS t\n" +
        "ORDER BY t.linha;";

    internal const string ParametroId = "id";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILogger<MapasRepository> _log;

    public MapasRepository(NpgsqlDataSource dataSource, ILogger<MapasRepository> log)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        ArgumentNullException.ThrowIfNull(log);
        _dataSource = dataSource;
        _log = log;
    }

    /// <summary>RA-3: consulta constante por coluna; qualquer outro valor do enum e rejeitado.</summary>
    internal static string SqlPara(ColunaMapa coluna) => coluna switch
    {
        ColunaMapa.Graphy => SqlGraphy,
        ColunaMapa.Metry => SqlMetry,
        _ => throw new ArgumentOutOfRangeException(nameof(coluna), "coluna nao suportada"),
    };

    /// <summary>
    /// Cria o comando (sem conexao) com o SQL constante e o parametro @id Integer (RA-3/RA-4).
    /// Separado para permitir testar o NpgsqlDbType sem servidor (licao MF-4 do teste_metry).
    /// </summary>
    internal static NpgsqlCommand CriarComando(ColunaMapa coluna, IdMapa id)
    {
        var comando = new NpgsqlCommand(SqlPara(coluna));
        comando.Parameters.Add(new NpgsqlParameter(ParametroId, NpgsqlDbType.Integer) { Value = id.Valor });
        return comando;
    }

    public async IAsyncEnumerable<GeometriaMapa> ListarPorIdAsync(
        IdMapa id,
        ColunaMapa coluna,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var nomeColuna = coluna.Nome();
        NpgsqlConnection? conexao = null;
        NpgsqlCommand? comando = null;
        NpgsqlDataReader? leitor = null;

        try
        {
            comando = CriarComando(coluna, id);

            try
            {
                LogAbrindoConexao(nomeColuna);
                conexao = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
                comando.Connection = conexao;

                LogExecutando(nomeColuna, id.Valor);
                leitor = await comando.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is NpgsqlException or TimeoutException or DbException)
            {
                LogarFalha(ex, "abrir conexao/executar consulta", nomeColuna, id);
                throw;
            }

            var linhas = 0;
            while (true)
            {
                bool temLinha;
                GeometriaMapa? geometria = null;
                try
                {
                    temLinha = await leitor.ReadAsync(cancellationToken).ConfigureAwait(false);
                    if (temLinha)
                    {
                        geometria = Ler(leitor);
                    }
                }
                catch (Exception ex) when (ex is NpgsqlException or TimeoutException or DbException or InvalidCastException)
                {
                    LogarFalha(ex, "ler linhas", nomeColuna, id);
                    throw;
                }

                if (!temLinha || geometria is null)
                {
                    break;
                }

                linhas++;
                yield return geometria;
            }

            LogLeituraConcluida(nomeColuna, linhas, id.Valor);
        }
        finally
        {
            if (leitor is not null)
            {
                await leitor.DisposeAsync().ConfigureAwait(false);
            }

            if (comando is not null)
            {
                await comando.DisposeAsync().ConfigureAwait(false);
            }

            if (conexao is not null)
            {
                await conexao.DisposeAsync().ConfigureAwait(false);
                LogConexaoDevolvida(nomeColuna);
            }
        }
    }

    /// <summary>Le a linha corrente na ordem das colunas de Q1/Q2.</summary>
    private static GeometriaMapa Ler(NpgsqlDataReader leitor) => new(
        Tipo: leitor.GetString(0),
        Srid: leitor.GetInt32(1),
        Wkt: leitor.GetString(2),
        GeoJson: leitor.GetString(3),
        X: leitor.IsDBNull(4) ? null : leitor.GetDouble(4),
        Y: leitor.IsDBNull(5) ? null : leitor.GetDouble(5),
        Longitude: leitor.IsDBNull(6) ? null : leitor.GetDouble(6),
        Latitude: leitor.IsDBNull(7) ? null : leitor.GetDouble(7));

    /// <summary>
    /// Loga a falha sem connection string/senha: tipo, SqlState (se houver) e a etapa.
    /// Sem a excecao/stack: o stack completo e logado uma unica vez no ManipuladorExcecoes (com traceId).
    /// </summary>
    private void LogarFalha(Exception ex, string etapa, string nomeColuna, IdMapa id)
    {
        var sqlState = (ex as PostgresException)?.SqlState ?? (ex as NpgsqlException)?.SqlState;
        LogFalhaBanco(etapa, nomeColuna, id.Valor, ex.GetType().Name, sqlState ?? "-");
    }

    // ---- Logs estruturados (RA-9/RA-10): nunca connection string nem senha. ----

    [LoggerMessage(EventId = 2001, Level = LogLevel.Debug, Message = "Abrindo conexao do pool para consultar a coluna {Coluna}.")]
    private partial void LogAbrindoConexao(string coluna);

    [LoggerMessage(EventId = 2002, Level = LogLevel.Debug, Message = "Executando a consulta constante da coluna {Coluna} com @id = {Id}.")]
    private partial void LogExecutando(string coluna, int id);

    [LoggerMessage(EventId = 2003, Level = LogLevel.Debug, Message = "Leitura da coluna {Coluna} concluida: {Linhas} linha(s) lida(s) para o id {Id}.")]
    private partial void LogLeituraConcluida(string coluna, int linhas, int id);

    [LoggerMessage(EventId = 2004, Level = LogLevel.Debug, Message = "Conexao devolvida ao pool (coluna {Coluna}).")]
    private partial void LogConexaoDevolvida(string coluna);

    [LoggerMessage(EventId = 2005, Level = LogLevel.Error, Message = "Falha no banco ao {Etapa} (coluna {Coluna}, id {Id}, tipo {TipoErro}, SqlState {SqlState}).")]
    private partial void LogFalhaBanco(string etapa, string coluna, int id, string tipoErro, string sqlState);
}
