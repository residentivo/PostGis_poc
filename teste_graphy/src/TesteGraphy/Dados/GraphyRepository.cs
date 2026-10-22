using System.Net.Sockets;
using Npgsql;
using NpgsqlTypes;
using TesteGraphy.Comum;
using TesteGraphy.Configuracao;
using TesteGraphy.Diagnostico;

namespace TesteGraphy.Dados;

/// <summary>
/// Repositorio Npgsql de public.mapas.graphy (R3, S5).
/// SQL exatamente como em teste_graphy/sql/insert_referencia.sql: constantes, somente
/// os parametros @wkt (Text) e @id (Integer), SRID 4326 literal, sem concatenacao.
/// Cada operacao abre sua propria conexao (pool do NpgsqlDataSource) com await using.
/// </summary>
public sealed class GraphyRepository : IGraphyRepository, IAsyncDisposable
{
    /// <summary>1) Validacao previa (insert_referencia.sql).</summary>
    internal const string SqlValidar =
        "SELECT ST_IsValid(ST_GeomFromText(@wkt, 4326)) AS valido,\n" +
        "       GeometryType(ST_GeomFromText(@wkt, 4326)) AS tipo;";

    /// <summary>2) Insercao sem informar id (identity) - insert_referencia.sql.</summary>
    internal const string SqlInserir =
        "INSERT INTO public.mapas (graphy)\n" +
        "VALUES (ST_GeomFromText(@wkt, 4326)::geography)\n" +
        "RETURNING id;";

    /// <summary>3) Leitura de conferencia (SRID esperado 4326) - insert_referencia.sql.</summary>
    internal const string SqlLer =
        "SELECT id,\n" +
        "       ST_AsText(graphy)       AS wkt,\n" +
        "       ST_SRID(graphy::geometry) AS srid\n" +
        "FROM public.mapas\n" +
        "WHERE id = @id;";

    /// <summary>
    /// 4) Limpeza - SOMENTE para testes de integracao (R7). Nao usado pelo programa.
    /// public.mapas nao tem PK/UNIQUE em id (S3): o chamador deve conferir rowcount = 1.
    /// </summary>
    internal const string SqlExcluir =
        "DELETE FROM public.mapas\n" +
        "WHERE id = @id;";

    private const string ParametroWkt = "wkt";
    private const string ParametroId = "id";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILog _log;

    public GraphyRepository(ConexaoConfig config, ILog? log = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _log = log ?? LogNulo.Instancia;
        _log.Passo($"Criando fonte de dados Npgsql para {config.Descricao()} (sem abrir conexao).");
        _dataSource = NpgsqlDataSource.Create(config.CriarConnectionString());
    }

    public Task<ValidacaoBanco> ValidarNoBancoAsync(string wkt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wkt);

        return ExecutarAsync("validacao ST_IsValid", async ct =>
        {
            await using var conexao = await AbrirConexaoAsync(ct).ConfigureAwait(false);
            await using var comando = new NpgsqlCommand(SqlValidar, conexao);
            comando.Parameters.Add(new NpgsqlParameter(ParametroWkt, NpgsqlDbType.Text) { Value = wkt });

            _log.Passo("Executando SELECT ST_IsValid/GeometryType.");
            await using var leitor = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await leitor.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new FalhaBancoException(null, "validacao no banco nao retornou linhas");
            }

            var valido = !leitor.IsDBNull(0) && leitor.GetBoolean(0);
            var tipo = leitor.IsDBNull(1) ? null : leitor.GetString(1);
            _log.Passo($"ST_IsValid = {valido}, GeometryType = {tipo ?? "(nulo)"}.");
            return new ValidacaoBanco(valido, tipo);
        }, cancellationToken);
    }

    public Task<int> InserirAsync(string wkt, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wkt);

        return ExecutarAsync("INSERT", async ct =>
        {
            await using var conexao = await AbrirConexaoAsync(ct).ConfigureAwait(false);
            await using var comando = new NpgsqlCommand(SqlInserir, conexao);
            comando.Parameters.Add(new NpgsqlParameter(ParametroWkt, NpgsqlDbType.Text) { Value = wkt });

            _log.Passo("Executando INSERT INTO public.mapas (graphy) ... RETURNING id.");
            var retorno = await comando.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (retorno is not int id)
            {
                throw new FalhaBancoException(null, "INSERT nao retornou um id inteiro");
            }

            _log.Passo($"INSERT concluido, id = {id}.");
            return id;
        }, cancellationToken);
    }

    public Task<RegistroGraphy?> LerAsync(int id, CancellationToken cancellationToken = default)
    {
        return ExecutarAsync<RegistroGraphy?>("leitura de conferencia", async ct =>
        {
            await using var conexao = await AbrirConexaoAsync(ct).ConfigureAwait(false);
            await using var comando = new NpgsqlCommand(SqlLer, conexao);
            comando.Parameters.Add(new NpgsqlParameter(ParametroId, NpgsqlDbType.Integer) { Value = id });

            _log.Passo($"Executando SELECT de conferencia para id = {id}.");
            await using var leitor = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await leitor.ReadAsync(ct).ConfigureAwait(false))
            {
                _log.Passo($"Nenhum registro encontrado para id = {id}.");
                return null;
            }

            if (leitor.IsDBNull(1) || leitor.IsDBNull(2))
            {
                throw new FalhaBancoException(null, $"registro id = {id} sem geografia em graphy");
            }

            var registro = new RegistroGraphy(leitor.GetInt32(0), leitor.GetString(1), leitor.GetInt32(2));
            _log.Passo($"Registro lido: id = {registro.Id}, SRID = {registro.Srid}.");
            return registro;
        }, cancellationToken);
    }

    public ValueTask DisposeAsync()
    {
        _log.Passo("Liberando fonte de dados Npgsql.");
        return _dataSource.DisposeAsync();
    }

    /// <summary>
    /// SqlStates tratados como falha de conexao (F5/A07): classe 28 (autenticacao),
    /// 08 (conexao), 3D (base inexistente), 57P03 (servidor iniciando/indisponivel)
    /// e 53300 (limite de conexoes).
    /// </summary>
    internal static bool EhFalhaDeConexaoOuAutenticacao(string? sqlState) =>
        sqlState is not null
        && (sqlState.StartsWith("28", StringComparison.Ordinal)
            || sqlState.StartsWith("08", StringComparison.Ordinal)
            || sqlState.StartsWith("3D", StringComparison.Ordinal)
            || sqlState == "57P03"
            || sqlState == "53300");

    private async Task<NpgsqlConnection> AbrirConexaoAsync(CancellationToken ct)
    {
        _log.Passo("Abrindo conexao com o banco.");
        return await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Traduz excecoes de infraestrutura para excecoes do dominio sem anexar a excecao
    /// original (evita vazar detalhes por ToString - R2).
    /// </summary>
    private async Task<T> ExecutarAsync<T>(string operacao, Func<CancellationToken, Task<T>> acao, CancellationToken ct)
    {
        _log.Passo($"Operacao no banco: {operacao}.");
        try
        {
            return await acao(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex) when (EhFalhaDeConexaoOuAutenticacao(ex.SqlState))
        {
            // F5 (A07): autenticacao/conexao/indisponibilidade interrompe os itens seguintes,
            // evitando rajadas de login falho com senha errada.
            var tipo = $"PostgresException/{TextoSeguro.Sanitizar(ex.SqlState, 5)}";
            _log.Passo($"Falha de conexao/autenticacao em {operacao}: {tipo}.");
            throw new FalhaConexaoException(tipo);
        }
        catch (PostgresException ex)
        {
            _log.Passo($"Falha do servidor em {operacao}: SqlState {TextoSeguro.Sanitizar(ex.SqlState, 5)}.");
            throw new FalhaBancoException(ex.SqlState, ex.MessageText);
        }
        catch (NpgsqlException ex)
        {
            var tipo = ex.InnerException is null
                ? ex.GetType().Name
                : $"{ex.GetType().Name}/{ex.InnerException.GetType().Name}";
            _log.Passo($"Falha de conexao em {operacao}: {tipo}.");
            throw new FalhaConexaoException(tipo);
        }
        catch (Exception ex) when (ex is SocketException or TimeoutException)
        {
            _log.Passo($"Falha de conexao em {operacao}: {ex.GetType().Name}.");
            throw new FalhaConexaoException(ex.GetType().Name);
        }
    }
}
