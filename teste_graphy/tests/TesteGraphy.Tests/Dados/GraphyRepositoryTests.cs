using TesteGraphy.Configuracao;
using TesteGraphy.Dados;
using TesteGraphy.Tests.Fakes;

namespace TesteGraphy.Tests.Dados;

/// <summary>
/// Testes unitarios do repositorio SEM banco real: constantes SQL, classificacao de SqlState e
/// traducao de excecoes contra um servidor falso em 127.0.0.1 (nunca acessa 192.168.168.106).
/// </summary>
public class GraphyRepositoryTests
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
        Assert.Equal(esperado, GraphyRepository.EhFalhaDeConexaoOuAutenticacao(sqlState));
    }

    [Fact]
    public void Sql_ConstantesParametrizadasComSrid4326()
    {
        Assert.Contains("ST_GeomFromText(@wkt, 4326)", GraphyRepository.SqlValidar, StringComparison.Ordinal);
        Assert.Contains("ST_IsValid", GraphyRepository.SqlValidar, StringComparison.Ordinal);
        Assert.Contains("INSERT INTO public.mapas (graphy)", GraphyRepository.SqlInserir, StringComparison.Ordinal);
        Assert.Contains("ST_GeomFromText(@wkt, 4326)::geography", GraphyRepository.SqlInserir, StringComparison.Ordinal);
        Assert.Contains("RETURNING id", GraphyRepository.SqlInserir, StringComparison.Ordinal);
        Assert.Contains("WHERE id = @id", GraphyRepository.SqlLer, StringComparison.Ordinal);
        Assert.Contains("ST_SRID(graphy::geometry)", GraphyRepository.SqlLer, StringComparison.Ordinal);
        Assert.Equal("DELETE FROM public.mapas\nWHERE id = @id;", GraphyRepository.SqlExcluir);
        Assert.DoesNotContain("{", GraphyRepository.SqlInserir + GraphyRepository.SqlValidar + GraphyRepository.SqlLer, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Construtor_NaoConecta_ELogNaoContemSentinela()
    {
        var log = new LogCaptura();
        var config = Ambiente.Config((ConexaoConfig.VarHost, "127.0.0.1"), (ConexaoConfig.VarPorta, ServidorPostgresFalso.PortaFechada().ToString()));

        var repo = new GraphyRepository(config, log);
        await repo.DisposeAsync();

        Assert.DoesNotContain(Sentinela, log.Tudo, StringComparison.Ordinal);
        Assert.Contains(log.Passos, p => p.Contains("sem abrir conexao", StringComparison.Ordinal));
        Assert.Contains(log.Passos, p => p.Contains("Liberando", StringComparison.Ordinal));
    }

    [Fact]
    public async Task ArgumentosNulos()
    {
        Assert.Throws<ArgumentNullException>(() => new GraphyRepository(null!));
        await using var repo = new GraphyRepository(Ambiente.Config((ConexaoConfig.VarHost, "127.0.0.1")));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.ValidarNoBancoAsync(null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => repo.InserirAsync(null!));
    }

    [Theory]
    [InlineData(Operacao.Validar)]
    [InlineData(Operacao.Inserir)]
    [InlineData(Operacao.Ler)]
    public async Task PortaFechada_FalhaConexaoComSoONomeDoTipo(Operacao operacao)
    {
        var log = new LogCaptura();
        await using var repo = CriarLocal(ServidorPostgresFalso.PortaFechada(), log);

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => Executar(repo, operacao));

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

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => repo.ValidarNoBancoAsync("POINT(0 0)"));

        Assert.StartsWith("Npgsql", ex.TipoExcecao, StringComparison.Ordinal);
        Assert.Null(ex.InnerException);
    }

    [Theory]
    [InlineData("28P01", Operacao.Validar)]
    [InlineData("28000", Operacao.Inserir)]
    [InlineData("3D000", Operacao.Ler)]
    [InlineData("57P03", Operacao.Validar)]
    [InlineData("53300", Operacao.Validar)]
    [InlineData("08006", Operacao.Validar)]
    public async Task PostgresExceptionDeConexaoOuAutenticacao_ViraFalhaConexao(string sqlState, Operacao operacao)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErro(sqlState, "autenticacao " + Sentinela, "DETALHE " + Sentinela, "DICA " + Sentinela);
        await using var repo = CriarLocal(servidor.Porta, log);

        var ex = await Assert.ThrowsAsync<FalhaConexaoException>(() => Executar(repo, operacao));

        Assert.Equal($"PostgresException/{sqlState}", ex.TipoExcecao);
        Assert.Null(ex.InnerException);
        Assert.DoesNotContain(Sentinela, ex.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, log.Tudo, StringComparison.Ordinal);
        // Observado: o Npgsql 10 abre ate 2 conexoes TCP por tentativa de abertura com erro no startup
        // (nova tentativa interna). O repositorio nao repete a operacao.
        Assert.InRange(servidor.Conexoes, 1, 2);
    }

    [Theory]
    [InlineData("XX000", Operacao.Validar)]
    [InlineData("42P01", Operacao.Inserir)]
    [InlineData("22023", Operacao.Ler)]
    [InlineData("42501", Operacao.Validar)]
    public async Task PostgresExceptionDoServidor_ViraFalhaBanco_SemDetailNemHint(string sqlState, Operacao operacao)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErro(sqlState, "mensagem\r\ndo servidor", "DETALHE " + Sentinela, "DICA SECRETA");
        await using var repo = CriarLocal(servidor.Porta, log);

        var ex = await Assert.ThrowsAsync<FalhaBancoException>(() => Executar(repo, operacao));

        Assert.Equal(sqlState, ex.SqlState);
        Assert.Equal("mensagem\r\ndo servidor", ex.MensagemServidor);
        Assert.Null(ex.InnerException);
        var formatado = TesteGraphy.Aplicacao.InsercaoService.FormatarErroBanco(ex);
        Assert.Equal($"SqlState {sqlState}: mensagem??do servidor", formatado);
        foreach (var texto in new[] { ex.ToString(), formatado, log.Tudo })
        {
            Assert.DoesNotContain("DETALHE", texto, StringComparison.Ordinal);
            Assert.DoesNotContain("DICA", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(Sentinela, texto, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task TokenCancelado_PropagaOperationCanceled()
    {
        await using var servidor = ServidorPostgresFalso.QueFecha();
        await using var repo = CriarLocal(servidor.Porta);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => repo.ValidarNoBancoAsync("POINT(0 0)", cts.Token));
    }

    [Fact]
    public void Excecoes_MensagensSemSentinela()
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

    private static GraphyRepository CriarLocal(int porta, LogCaptura? log = null) =>
        new(
            Ambiente.Config(
                (ConexaoConfig.VarHost, "127.0.0.1"),
                (ConexaoConfig.VarPorta, porta.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (ConexaoConfig.VarSslMode, "Disable")),
            log);

    private static Task Executar(GraphyRepository repo, Operacao operacao) => operacao switch
    {
        Operacao.Validar => repo.ValidarNoBancoAsync("POINT(0 0)"),
        Operacao.Inserir => repo.InserirAsync("POINT(0 0)"),
        _ => repo.LerAsync(1),
    };
}
