using System.Globalization;
using TesteMetry.Aplicacao;
using TesteMetry.Configuracao;
using TesteMetry.Dados;
using TesteMetry.Tests.Fakes;

namespace TesteMetry.Tests.Dados;

/// <summary>
/// M6.4 - MetryRepository SEM banco real: constantes SQL (RQ-1), defesa do SRID, classificacao de
/// SqlState (A07) e traducao de excecoes contra servidor falso em 127.0.0.1 (nunca acessa 192.168.168.106).
/// </summary>
public class MetryRepositoryTests
{
    private const string Sentinela = Ambiente.Sentinela;

    public enum Operacao
    {
        Validar,
        Inserir,
        Ler,
    }

    [Theory]
    [InlineData("28P01", true)]
    [InlineData("28000", true)]
    [InlineData("08006", true)]
    [InlineData("08001", true)]
    [InlineData("08P01", true)]
    [InlineData("3D000", true)]
    [InlineData("57P03", true)]
    [InlineData("53300", true)]
    [InlineData("23505", false)]
    [InlineData("22023", false)]
    [InlineData("XX000", false)]
    [InlineData("42P01", false)]
    [InlineData("42501", false)]
    [InlineData("57P01", false)]
    [InlineData("53000", false)]
    [InlineData("3F000", false)]
    [InlineData("2", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void EhFalhaDeConexaoOuAutenticacao(string? sqlState, bool esperado)
    {
        Assert.Equal(esperado, MetryRepository.EhFalhaDeConexaoOuAutenticacao(sqlState));
    }

    [Fact]
    public void Sql_ConstantesParametrizadas_SemSridNoTexto()
    {
        Assert.Equal(
            "SELECT ST_IsValid(ST_GeomFromText(@wkt, @srid)) AS valido,\n       GeometryType(ST_GeomFromText(@wkt, @srid)) AS tipo;",
            MetryRepository.SqlValidar);
        Assert.Equal("INSERT INTO public.mapas (metry)\nVALUES (ST_GeomFromText(@wkt, @srid))\nRETURNING id;", MetryRepository.SqlInserir);
        Assert.Equal(
            "SELECT id,\n       ST_AsText(metry) AS wkt,\n       ST_SRID(metry)   AS srid\nFROM public.mapas\nWHERE id = @id;",
            MetryRepository.SqlLer);

        var todos = MetryRepository.SqlValidar + MetryRepository.SqlInserir + MetryRepository.SqlLer;
        Assert.DoesNotContain(todos, char.IsAsciiDigit);           // nenhum SRID literal (RQ-1)
        Assert.DoesNotContain("{", todos, StringComparison.Ordinal);
        Assert.DoesNotContain("geography", todos, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", todos, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SRID=", todos, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(("wkt", "srid", "id"), (MetryRepository.ParametroWkt, MetryRepository.ParametroSrid, MetryRepository.ParametroId));
    }

    [Fact]
    public void Interface_SemOperacaoDeExclusao_Rq6()
    {
        var nomes = typeof(IMetryRepository).GetMethods().Select(m => m.Name).ToArray();

        Assert.Equal(new[] { "InserirAsync", "LerAsync", "ValidarNoBancoAsync" }, nomes.Order());
        Assert.DoesNotContain(typeof(MetryRepository).GetMethods(), m => m.Name.Contains("Excluir", StringComparison.OrdinalIgnoreCase)
            || m.Name.Contains("Delete", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(2154)]
    [InlineData(4269)]
    [InlineData(int.MaxValue)]
    public async Task SridForaDaLista_RejeitadoAntesDeConectar(int srid)
    {
        await using var servidor = ServidorPostgresFalso.QueFecha();
        await using var repo = CriarLocal(servidor.Porta);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.ValidarNoBancoAsync("POINT(0 0)", srid));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => repo.InserirAsync("POINT(0 0)", srid));
        Assert.Equal(0, servidor.Conexoes);
    }

    [Fact]
    public async Task Construtor_NaoConecta_ELogNaoContemSentinela()
    {
        var log = new LogCaptura();
        var config = Ambiente.Config((ConexaoConfig.VarHost, "127.0.0.1"), (ConexaoConfig.VarPorta, ServidorPostgresFalso.PortaFechada().ToString(CultureInfo.InvariantCulture)));

        var repo = new MetryRepository(config, log);
        await repo.DisposeAsync();

        Assert.DoesNotContain(Sentinela, log.Tudo, StringComparison.Ordinal);
        Assert.Contains(log.Passos, p => p.Contains("sem abrir conexao", StringComparison.Ordinal));
        Assert.Contains(log.Passos, p => p.Contains("Liberando", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArgumentosNulos()
    {
        Assert.Throws<ArgumentNullException>(() => new MetryRepository(null!));
        await using var repo = new MetryRepository(Ambiente.Config((ConexaoConfig.VarHost, "127.0.0.1")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.ValidarNoBancoAsync(null!, 4326));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.InserirAsync(null!, 4326));
    }

    [Theory]
    [InlineData(Operacao.Validar)]
    [InlineData(Operacao.Inserir)]
    [InlineData(Operacao.Ler)]
    public async Task PortaFechada_FalhaConexaoComSoONomeDoTipo(Operacao operacao)
    {
        var log = new LogCaptura();
        await using var repo = CriarLocal(ServidorPostgresFalso.PortaFechada(), log);

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => Executar(repo, operacao, 4326));

        Assert.StartsWith("Npgsql", ex.TipoExcecao, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain(Sentinela, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, log.Tudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ServidorQueFecha_FalhaConexao()
    {
        await using var servidor = ServidorPostgresFalso.QueFecha();
        await using var repo = CriarLocal(servidor.Porta);

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => repo.ValidarNoBancoAsync("POINT(0 0)", 3857));

        Assert.StartsWith("Npgsql", ex.TipoExcecao, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    [Theory]
    [InlineData("28P01", Operacao.Validar, 4326)]
    [InlineData("28000", Operacao.Inserir, 4674)]
    [InlineData("3D000", Operacao.Ler, 3857)]
    [InlineData("57P03", Operacao.Validar, 31983)]
    [InlineData("53300", Operacao.Inserir, 4326)]
    [InlineData("08006", Operacao.Validar, 3857)]
    [InlineData("08P01", Operacao.Ler, 4326)]
    public async Task PostgresExceptionDeConexaoOuAutenticacao_ViraFalhaConexao(string sqlState, Operacao operacao, int srid)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErro(sqlState, "autenticacao " + Sentinela, "DETALHE " + Sentinela, "DICA " + Sentinela);
        await using var repo = CriarLocal(servidor.Porta, log);

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => Executar(repo, operacao, srid));

        Assert.Equal($"PostgresException/{sqlState}", ex.TipoExcecao);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain(Sentinela, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, log.Tudo, StringComparison.Ordinal);
        Assert.InRange(servidor.Conexoes, 1, 2);
    }

    /// <summary>
    /// MF-2: QUALQUER PostgresException na ABERTURA (inclusive XX000, classe 22, 42501, 42P01) vira
    /// FalhaConexaoException("PostgresException/SqlState"), sem MessageText, Detail nem Hint.
    /// </summary>
    [Theory]
    [InlineData("XX000", Operacao.Validar)]
    [InlineData("42P01", Operacao.Inserir)]
    [InlineData("22023", Operacao.Ler)]
    [InlineData("42501", Operacao.Validar)]
    [InlineData("22P02", Operacao.Inserir)]
    [InlineData("57P01", Operacao.Ler)]
    [InlineData("53000", Operacao.Validar)]
    public async Task PostgresExceptionNaAbertura_ViraFalhaConexao_SemMessageTextDetailNemHint(string sqlState, Operacao operacao)
    {
        const string Marcador = "usuario_secreto";
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErro(sqlState, "mensagem\r\ndo servidor " + Marcador, "DETALHE " + Sentinela, "DICA SECRETA");
        await using var repo = CriarLocal(servidor.Porta, log);

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => Executar(repo, operacao, 4326));

        Assert.Equal($"PostgresException/{sqlState}", ex.TipoExcecao);
        Assert.Equal($"falha de conexao (PostgresException/{sqlState})", ex.Message);
        Assert.Null(ex.InnerException);
        Assert.Contains(log.Passos, p => p.Contains($"Falha ao abrir conexao: PostgresException/{sqlState}", StringComparison.Ordinal));
        foreach (var texto in new[] { ex.ToString(), log.Tudo })
        {
            Assert.DoesNotContain(Marcador, texto, StringComparison.Ordinal);
            Assert.DoesNotContain("mensagem", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("DETALHE", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("DICA", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentinela, texto, StringComparison.Ordinal);
        }

        Assert.InRange(servidor.Conexoes, 1, 2);
    }

    /// <summary>
    /// Erro do servidor na EXECUCAO (startup concluido; ErrorResponse no lote Parse/Bind/Execute/Sync):
    /// SqlState que nao e de conexao vira FalhaBancoException(SqlState, MessageText), sem Detail/Hint.
    /// </summary>
    [Theory]
    [InlineData("XX000", Operacao.Validar)]
    [InlineData("42P01", Operacao.Inserir)]
    [InlineData("22023", Operacao.Ler)]
    [InlineData("42501", Operacao.Validar)]
    [InlineData("23505", Operacao.Inserir)]
    [InlineData("57P01", Operacao.Ler)]
    public async Task PostgresExceptionNaExecucao_ViraFalhaBanco_SemDetailNemHint(string sqlState, Operacao operacao)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErroNaExecucao(sqlState, "mensagem\r\ndo servidor", "DETALHE " + Sentinela, "DICA SECRETA");
        await using var repo = CriarLocal(servidor.Porta, log);

        var ex = await Assert.ThrowsAsync<FalhaBancoException>(() => Executar(repo, operacao, 4326));

        Assert.Equal(sqlState, ex.SqlState);
        Assert.Equal("mensagem\r\ndo servidor", ex.MensagemServidor);
        Assert.Null(ex.InnerException);
        var formatado = InsercaoService.FormatarErroBanco(ex);
        Assert.Equal($"SqlState {sqlState}: mensagem??do servidor", formatado);
        Assert.Contains(log.Passos, p => p.Contains($"SqlState {sqlState}", StringComparison.Ordinal));
        foreach (var texto in new[] { ex.ToString(), formatado, log.Tudo })
        {
            Assert.DoesNotContain("DETALHE", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("DICA", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentinela, texto, StringComparison.Ordinal);
        }

        Assert.Single(servidor.ComandosRecebidos);
    }

    /// <summary>F5/A07 na EXECUCAO: SqlState de conexao/autenticacao vira FalhaConexaoException.</summary>
    [Theory]
    [InlineData("28P01", Operacao.Validar)]
    [InlineData("08006", Operacao.Inserir)]
    [InlineData("3D000", Operacao.Ler)]
    [InlineData("57P03", Operacao.Validar)]
    [InlineData("53300", Operacao.Inserir)]
    public async Task PostgresExceptionDeConexaoNaExecucao_ViraFalhaConexao(string sqlState, Operacao operacao)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErroNaExecucao(sqlState, "falha " + Sentinela, "DETALHE " + Sentinela, "DICA");
        await using var repo = CriarLocal(servidor.Porta, log);

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => Executar(repo, operacao, 4326));

        Assert.Equal($"PostgresException/{sqlState}", ex.TipoExcecao);
        Assert.Null(ex.InnerException);
        Assert.Contains(log.Passos, p => p.Contains("Falha de conexao/autenticacao em", StringComparison.Ordinal));
        Assert.DoesNotContain(Sentinela, ex.ToString() + log.Tudo, StringComparison.Ordinal);
    }

    /// <summary>R-13/R-14 de ponta a ponta: o SQL que chega ao servidor e a constante com $1/$2 (sem SRID literal).</summary>
    [Theory]
    [InlineData(Operacao.Validar, 3857)]
    [InlineData(Operacao.Inserir, 31983)]
    [InlineData(Operacao.Ler, 4674)]
    public async Task SqlEnviadoAoServidor_SoComParametros(Operacao operacao, int srid)
    {
        await using var servidor = ServidorPostgresFalso.ComErroNaExecucao("42P01", "x", "d", "h");
        await using var repo = CriarLocal(servidor.Porta);

        await Assert.ThrowsAsync<FalhaBancoException>(() => Executar(repo, operacao, srid));

        var sql = Assert.Single(servidor.ComandosRecebidos);
        var esperado = operacao switch
        {
            Operacao.Validar => MetryRepository.SqlValidar.Replace("@wkt", "$1", StringComparison.Ordinal).Replace("@srid", "$2", StringComparison.Ordinal),
            Operacao.Inserir => MetryRepository.SqlInserir.Replace("@wkt", "$1", StringComparison.Ordinal).Replace("@srid", "$2", StringComparison.Ordinal),
            _ => MetryRepository.SqlLer.Replace("@id", "$1", StringComparison.Ordinal),
        };
        Assert.Equal(esperado.TrimEnd(';'), sql); // o Npgsql remove o ';' final ao reescrever @nome -> $n
        Assert.DoesNotContain(srid.ToString(CultureInfo.InvariantCulture), sql, StringComparison.Ordinal);
    }

    /// <summary>Zero linhas: validacao sem linhas e INSERT sem id viram FalhaBancoException(null); leitura devolve null.</summary>
    [Theory]
    [InlineData(Operacao.Validar, "validacao no banco nao retornou linhas")]
    [InlineData(Operacao.Inserir, "INSERT nao retornou um id inteiro")]
    [InlineData(Operacao.Ler, null)]
    public async Task ServidorSemLinhas(Operacao operacao, string? mensagem)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.SemLinhas();
        await using var repo = CriarLocal(servidor.Porta, log);

        if (mensagem is null)
        {
            Assert.Null(await repo.LerAsync(5));
            Assert.Contains(log.Passos, p => p.Contains("Nenhum registro encontrado para id = 5", StringComparison.Ordinal));
            return;
        }

        var ex = await Assert.ThrowsAsync<FalhaBancoException>(() => Executar(repo, operacao, 4326));
        Assert.Null(ex.SqlState);
        Assert.Equal(mensagem, ex.MensagemServidor);
    }

    /// <summary>Caminho feliz contra o servidor falso: valores lidos e NOTICE sanitizado so no log.</summary>
    [Theory]
    [InlineData(true, "POINT", true, "POINT")]
    [InlineData(false, "POLYGON", false, "POLYGON")]
    [InlineData(null, null, false, null)]
    [InlineData(true, null, true, null)]
    public async Task ValidarNoBanco_LeValidoETipo(bool? valido, string? tipo, bool validoEsperado, string? tipoEsperado)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComLinhas(new(Valido: valido, Tipo: tipo, Aviso: "Self-intersection\u001B[2J"));
        await using var repo = CriarLocal(servidor.Porta, log);

        var resultado = await repo.ValidarNoBancoAsync("POINT(0 0)", 4326);

        Assert.Equal(new ValidacaoBanco(validoEsperado, tipoEsperado), resultado);
        Assert.Contains(log.Passos, p => p.Contains($"ST_IsValid = {validoEsperado}", StringComparison.Ordinal));
        Assert.Contains(log.Passos, p => p == "NOTICE do servidor: Self-intersection?[2J");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(int.MaxValue)]
    public async Task Inserir_DevolveId(int id)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComLinhas(new(Id: id));
        await using var repo = CriarLocal(servidor.Porta, log);

        Assert.Equal(id, await repo.InserirAsync("POINT(0 0)", 3857));
        Assert.Contains(log.Passos, p => p.Contains($"INSERT concluido, id = {id.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Inserir_IdNulo_FalhaBanco()
    {
        await using var servidor = ServidorPostgresFalso.ComLinhas(new(Id: null));
        await using var repo = CriarLocal(servidor.Porta);

        var ex = await Assert.ThrowsAsync<FalhaBancoException>(() => repo.InserirAsync("POINT(0 0)", 4326));

        Assert.Equal("INSERT nao retornou um id inteiro", ex.MensagemServidor);
    }

    [Theory]
    [InlineData(7, "POINT(1 2)", 4326)]
    [InlineData(8, "LINESTRING(0 0,1 1)", 31983)]
    public async Task Ler_DevolveRegistro(int id, string wkt, int srid)
    {
        await using var servidor = ServidorPostgresFalso.ComLinhas(new(Id: id, Wkt: wkt, Srid: srid));
        await using var repo = CriarLocal(servidor.Porta);

        Assert.Equal(new RegistroMetry(id, wkt, srid), await repo.LerAsync(id));
    }

    [Theory]
    [InlineData(null, 4326)]
    [InlineData("POINT(0 0)", null)]
    [InlineData(null, null)]
    public async Task Ler_SemGeometria_FalhaBanco(string? wkt, int? srid)
    {
        await using var servidor = ServidorPostgresFalso.ComLinhas(new(Id: 9, Wkt: wkt, Srid: srid));
        await using var repo = CriarLocal(servidor.Porta);

        var ex = await Assert.ThrowsAsync<FalhaBancoException>(() => repo.LerAsync(9));

        Assert.Null(ex.SqlState);
        Assert.Equal("registro id = 9 sem geometria em metry", ex.MensagemServidor);
    }

    public static readonly TheoryData<int> SridsPermitidos = new() { 4326, 4674, 3857, 31983 };

    public static readonly TheoryData<int> SridsForaDaLista = new() { 0, -1, 2154, 4269, 900913, int.MaxValue, int.MinValue };

    /// <summary>MF-4: comando da validacao sem conexao, so a constante e parametros tipados.</summary>
    [Theory]
    [MemberData(nameof(SridsPermitidos))]
    public void CriarComandoValidar_ConstanteEParametrosTipados(int srid)
    {
        const string Wkt = "POINT(1 2)'); DROP TABLE x; --";
        using var comando = MetryRepository.CriarComandoValidar(Wkt, srid);

        Assert.Equal(MetryRepository.SqlValidar, comando.CommandText);
        Assert.Null(comando.Connection);
        AssertParametrosWktSrid(comando, Wkt, srid);
    }

    /// <summary>MF-4: comando do INSERT sem conexao, so a constante e parametros tipados.</summary>
    [Theory]
    [MemberData(nameof(SridsPermitidos))]
    public void CriarComandoInserir_ConstanteEParametrosTipados(int srid)
    {
        const string Wkt = "POLYGON((0 0, 1 0, 1 1, 0 0))";
        using var comando = MetryRepository.CriarComandoInserir(Wkt, srid);

        Assert.Equal(MetryRepository.SqlInserir, comando.CommandText);
        Assert.Null(comando.Connection);
        AssertParametrosWktSrid(comando, Wkt, srid);
    }

    /// <summary>MF-4: comando da leitura sem conexao; @id Integer.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-7)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void CriarComandoLer_ConstanteEParametroInteger(int id)
    {
        using var comando = MetryRepository.CriarComandoLer(id);

        Assert.Equal(MetryRepository.SqlLer, comando.CommandText);
        Assert.Null(comando.Connection);
        var parametro = Assert.Single(comando.Parameters.Cast<Npgsql.NpgsqlParameter>());
        Assert.Equal(MetryRepository.ParametroId, parametro.ParameterName);
        Assert.Equal(NpgsqlTypes.NpgsqlDbType.Integer, parametro.NpgsqlDbType);
        Assert.Equal(id, Assert.IsType<int>(parametro.Value));
    }

    [Theory]
    [MemberData(nameof(SridsForaDaLista))]
    public void CriarComandos_SridForaDaLista_ArgumentOutOfRange(int srid)
    {
        var ex1 = Assert.Throws<ArgumentOutOfRangeException>(() => MetryRepository.CriarComandoValidar("POINT(0 0)", srid));
        var ex2 = Assert.Throws<ArgumentOutOfRangeException>(() => MetryRepository.CriarComandoInserir("POINT(0 0)", srid));

        foreach (var ex in new[] { ex1, ex2 })
        {
            Assert.Equal("srid", ex.ParamName);
            Assert.Contains("SRID nao permitido (use 4326, 4674, 3857 ou 31983)", ex.Message, StringComparison.Ordinal);
        }
    }

    [Theory]
    [MemberData(nameof(SridsPermitidos))]
    [InlineData(0)]
    public void CriarComandos_WktNulo_ArgumentNull(int srid)
    {
        // wkt nulo e verificado antes do SRID (inclusive com SRID fora da lista).
        Assert.Equal("wkt", Assert.Throws<ArgumentNullException>(() => MetryRepository.CriarComandoValidar(null!, srid)).ParamName);
        Assert.Equal("wkt", Assert.Throws<ArgumentNullException>(() => MetryRepository.CriarComandoInserir(null!, srid)).ParamName);
    }

    private static void AssertParametrosWktSrid(Npgsql.NpgsqlCommand comando, string wkt, int srid)
    {
        Assert.Equal(2, comando.Parameters.Count);
        var pWkt = comando.Parameters[0];
        var pSrid = comando.Parameters[1];
        Assert.Equal(MetryRepository.ParametroWkt, pWkt.ParameterName);
        Assert.Equal(NpgsqlTypes.NpgsqlDbType.Text, pWkt.NpgsqlDbType);
        Assert.Equal(wkt, Assert.IsType<string>(pWkt.Value));
        Assert.Equal(MetryRepository.ParametroSrid, pSrid.ParameterName);
        Assert.Equal(NpgsqlTypes.NpgsqlDbType.Integer, pSrid.NpgsqlDbType);
        Assert.Equal(srid, Assert.IsType<int>(pSrid.Value));
    }

    [Fact]
    public async Task TokenCancelado_PropagaOperationCanceled()
    {
        await using var servidor = ServidorPostgresFalso.QueFecha();
        await using var repo = CriarLocal(servidor.Porta);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.ValidarNoBancoAsync("POINT(0 0)", 4326, cts.Token));
    }

    [Fact]
    public void Excecoes_Mensagens()
    {
        var banco = new FalhaBancoException(null, "m");
        var bancoComState = new FalhaBancoException("23505", "dup");
        var conexao = new FalhaConexaoException("SocketException");

        Assert.Equal("m", banco.Message);
        Assert.Equal("23505: dup", bancoComState.Message);
        Assert.Equal("falha de conexao (SocketException)", conexao.Message);
        Assert.Equal("SocketException", conexao.TipoExcecao);
        Assert.Null(banco.SqlState);
    }

    private static MetryRepository CriarLocal(int porta, LogCaptura? log = null) =>
        new(
            Ambiente.Config(
                (ConexaoConfig.VarHost, "127.0.0.1"),
                (ConexaoConfig.VarPorta, porta.ToString(CultureInfo.InvariantCulture)),
                (ConexaoConfig.VarSslMode, "Disable")),
            log);

    private static Task Executar(MetryRepository repo, Operacao operacao, int srid) => operacao switch
    {
        Operacao.Validar => repo.ValidarNoBancoAsync("POINT(0 0)", srid),
        Operacao.Inserir => repo.InserirAsync("POINT(0 0)", srid),
        _ => repo.LerAsync(1),
    };
}
