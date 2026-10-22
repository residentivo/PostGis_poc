using System.Globalization;
using System.Net.Sockets;
using Npgsql;
using NpgsqlTypes;
using TesteMetry.Comum;
using TesteMetry.Configuracao;
using TesteMetry.Diagnostico;
using TesteMetry.Validacao;

namespace TesteMetry.Dados;

/// <summary>
/// Repositorio Npgsql de public.mapas.metry (D-M5, RQ-1, RQ-6).
/// SQL exatamente como em teste_metry/sql/insert_referencia.sql (M3.5): constantes, somente
/// os parametros @wkt (Text), @srid (Integer) e @id (Integer), sem concatenacao/interpolacao
/// e sem cast para geography. O programa NAO executa DELETE (somente os testes de integracao).
/// Cada operacao abre sua propria conexao (pool do NpgsqlDataSource) com await using.
/// </summary>
public sealed class MetryRepository : IMetryRepository, IAsyncDisposable
{
    /// <summary>1) Validacao previa (insert_referencia.sql). @srid repetido = um unico parametro nomeado.</summary>
    internal const string SqlValidar =
        "SELECT ST_IsValid(ST_GeomFromText(@wkt, @srid)) AS valido,\n" +
        "       GeometryType(ST_GeomFromText(@wkt, @srid)) AS tipo;";

    /// <summary>2) Insercao sem informar id (identity) - insert_referencia.sql.</summary>
    internal const string SqlInserir =
        "INSERT INTO public.mapas (metry)\n" +
        "VALUES (ST_GeomFromText(@wkt, @srid))\n" +
        "RETURNING id;";

    /// <summary>3) Leitura de conferencia - insert_referencia.sql. O chamador compara srid com o configurado.</summary>
    internal const string SqlLer =
        "SELECT id,\n" +
        "       ST_AsText(metry) AS wkt,\n" +
        "       ST_SRID(metry)   AS srid\n" +
        "FROM public.mapas\n" +
        "WHERE id = @id;";

    internal const string ParametroWkt = "wkt";
    internal const string ParametroSrid = "srid";
    internal const string ParametroId = "id";

    private readonly NpgsqlDataSource _dataSource;
    private readonly ILog _log;

    public MetryRepository(ConexaoConfig config, ILog? log = null)
    {
        ArgumentNullException.ThrowIfNull(config);
        _log = log ?? LogNulo.Instancia;
        _log.Passo($"Criando fonte de dados Npgsql para {config.Descricao()} (sem abrir conexao).");
        _dataSource = NpgsqlDataSource.Create(config.CriarConnectionString());
    }

    public Task<ValidacaoBanco> ValidarNoBancoAsync(string wkt, int srid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        GarantirSridPermitido(srid);

        return ExecutarAsync("validacao ST_IsValid", async ct =>
        {
            await using var conexao = await AbrirConexaoAsync(ct).ConfigureAwait(false);
            await using var comando = CriarComandoValidar(wkt, srid);
            comando.Connection = conexao;

            _log.Passo($"Executando SELECT ST_IsValid/GeometryType (@srid = {Texto(srid)}).");
            await using var leitor = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await leitor.ReadAsync(ct).ConfigureAwait(false))
            {
                throw new FalhaBancoException(null, "validacao no banco nao retornou linhas");
            }

            var valido = !leitor.IsDBNull(0) && leitor.GetBoolean(0);
            var tipo = leitor.IsDBNull(1) ? null : leitor.GetString(1);
            _log.Passo($"ST_IsValid = {valido}, GeometryType = {TextoSeguro.Sanitizar(tipo ?? "(nulo)", 40)}.");
            return new ValidacaoBanco(valido, tipo);
        }, cancellationToken);
    }

    public Task<int> InserirAsync(string wkt, int srid, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        GarantirSridPermitido(srid);

        return ExecutarAsync("INSERT", async ct =>
        {
            await using var conexao = await AbrirConexaoAsync(ct).ConfigureAwait(false);
            await using var comando = CriarComandoInserir(wkt, srid);
            comando.Connection = conexao;

            _log.Passo($"Executando INSERT INTO public.mapas (metry) ... RETURNING id (@srid = {Texto(srid)}).");
            var retorno = await comando.ExecuteScalarAsync(ct).ConfigureAwait(false);
            if (retorno is not int id)
            {
                throw new FalhaBancoException(null, "INSERT nao retornou um id inteiro");
            }

            _log.Passo($"INSERT concluido, id = {Texto(id)}.");
            return id;
        }, cancellationToken);
    }

    public Task<RegistroMetry?> LerAsync(int id, CancellationToken cancellationToken = default)
    {
        return ExecutarAsync<RegistroMetry?>("leitura de conferencia", async ct =>
        {
            await using var conexao = await AbrirConexaoAsync(ct).ConfigureAwait(false);
            await using var comando = CriarComandoLer(id);
            comando.Connection = conexao;

            _log.Passo($"Executando SELECT de conferencia para id = {Texto(id)}.");
            await using var leitor = await comando.ExecuteReaderAsync(ct).ConfigureAwait(false);
            if (!await leitor.ReadAsync(ct).ConfigureAwait(false))
            {
                _log.Passo($"Nenhum registro encontrado para id = {Texto(id)}.");
                return null;
            }

            if (leitor.IsDBNull(1) || leitor.IsDBNull(2))
            {
                throw new FalhaBancoException(null, $"registro id = {Texto(id)} sem geometria em metry");
            }

            var registro = new RegistroMetry(leitor.GetInt32(0), leitor.GetString(1), leitor.GetInt32(2));
            _log.Passo($"Registro lido: id = {Texto(registro.Id)}, SRID = {Texto(registro.Srid)}.");
            return registro;
        }, cancellationToken);
    }

    /// <summary>
    /// MF-4: comando da validacao previa, SEM conexao (testavel sem servidor). CommandText so da
    /// constante <see cref="SqlValidar"/>; @wkt = Text e @srid = Integer (R-13, R-14, R-15).
    /// </summary>
    internal static NpgsqlCommand CriarComandoValidar(string wkt, int srid)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        GarantirSridPermitido(srid);

        var comando = new NpgsqlCommand(SqlValidar);
        comando.Parameters.Add(new NpgsqlParameter(ParametroWkt, NpgsqlDbType.Text) { Value = wkt });
        comando.Parameters.Add(new NpgsqlParameter(ParametroSrid, NpgsqlDbType.Integer) { Value = srid });
        return comando;
    }

    /// <summary>
    /// MF-4: comando do INSERT, SEM conexao. CommandText so da constante <see cref="SqlInserir"/>;
    /// @wkt = Text e @srid = Integer (R-13, R-14, R-15).
    /// </summary>
    internal static NpgsqlCommand CriarComandoInserir(string wkt, int srid)
    {
        ArgumentNullException.ThrowIfNull(wkt);
        GarantirSridPermitido(srid);

        var comando = new NpgsqlCommand(SqlInserir);
        comando.Parameters.Add(new NpgsqlParameter(ParametroWkt, NpgsqlDbType.Text) { Value = wkt });
        comando.Parameters.Add(new NpgsqlParameter(ParametroSrid, NpgsqlDbType.Integer) { Value = srid });
        return comando;
    }

    /// <summary>
    /// MF-4: comando da leitura de conferencia, SEM conexao. CommandText so da constante
    /// <see cref="SqlLer"/>; @id = Integer (R-13, R-14).
    /// </summary>
    internal static NpgsqlCommand CriarComandoLer(int id)
    {
        var comando = new NpgsqlCommand(SqlLer);
        comando.Parameters.Add(new NpgsqlParameter(ParametroId, NpgsqlDbType.Integer) { Value = id });
        return comando;
    }

    public ValueTask DisposeAsync()
    {
        _log.Passo("Liberando fonte de dados Npgsql.");
        return _dataSource.DisposeAsync();
    }

    /// <summary>
    /// SqlStates tratados como falha de conexao (F5/A07) quando vierem da EXECUCAO dos comandos
    /// (na ABERTURA, qualquer PostgresException ja vira falha de conexao - MF-2): classe 28 (autenticacao),
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

    /// <summary>Defesa em profundidade (RQ-1): so SRIDs da lista constante chegam ao banco.</summary>
    private static void GarantirSridPermitido(int srid)
    {
        if (!LimitesSrid.EhPermitido(srid))
        {
            throw new ArgumentOutOfRangeException(nameof(srid), $"SRID nao permitido (use {LimitesSrid.PermitidosTexto}).");
        }
    }

    private static string Texto(int valor) => valor.ToString(CultureInfo.InvariantCulture);

    private async Task<NpgsqlConnection> AbrirConexaoAsync(CancellationToken ct)
    {
        _log.Passo("Abrindo conexao com o banco.");
        NpgsqlConnection conexao;
        try
        {
            conexao = await _dataSource.OpenConnectionAsync(ct).ConfigureAwait(false);
        }
        catch (PostgresException ex)
        {
            // MF-2: QUALQUER erro do servidor na ABERTURA (inclusive XX000, classe 22, 42501,
            // 53xxx, 57Pxx) e falha de conexao (codigo 3, interrompe os itens seguintes).
            // So o SqlState sanitizado; sem InnerException, Message, MessageText, Detail,
            // Hint, Where ou ToString (R-4, R-5, R-6).
            var tipo = $"PostgresException/{TextoSeguro.Sanitizar(ex.SqlState, 5)}";
            // Detalhe so no verboso (padrao do repositorio); o erro e logado pelo servico/console.
            _log.Passo($"Falha ao abrir conexao: {tipo}");
            throw new FalhaConexaoException(tipo);
        }

        // R-7: handler ligado somente apos a abertura com sucesso.
        // NOTICE do servidor (ex.: motivo de ST_IsValid = false) so vai ao log verboso, sanitizado.
        conexao.Notice += (_, e) =>
            _log.Passo($"NOTICE do servidor: {TextoSeguro.Sanitizar(e.Notice.MessageText)}");
        return conexao;
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
