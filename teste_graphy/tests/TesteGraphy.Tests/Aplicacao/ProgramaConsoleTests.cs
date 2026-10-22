using TesteGraphy.Aplicacao;
using TesteGraphy.Configuracao;
using TesteGraphy.Dados;
using TesteGraphy.Diagnostico;
using TesteGraphy.Tests.Fakes;

namespace TesteGraphy.Tests.Aplicacao;

public class ProgramaConsoleTests
{
    public enum Cenario
    {
        Sucesso,
        WktInvalido,
        StIsValidFalso,
        LimiteExcedido,
        ErroBancoNoItem,
        SridDivergente,
        FalhaConexaoNoItem,
        CanceladoPeloRepositorio,
        CanceladoPeloToken,
        ExcecaoInesperada,
        PostgresExceptionNaoTraduzida,
        FabricaFalhaConexao,
        FabricaFalhaBanco,
        FabricaExcecaoInesperada,
        ConfiguracaoInvalida,
        PortaInvalida,
    }

    private const string Sentinela = Ambiente.Sentinela;

    private sealed class Execucao
    {
        public StringWriter Saida { get; } = new();

        public StringWriter Erro { get; } = new();

        public RepositorioFalso Repositorio { get; } = new();

        public int ChamadasFabrica { get; set; }

        public ConexaoConfig? ConfigRecebida { get; set; }

        public string Tudo => Saida + "\n" + Erro;
    }

    private static async Task<(int Codigo, Execucao Exec)> ExecutarAsync(
        Cenario cenario,
        bool verboso = false,
        IReadOnlyList<string>? args = null)
    {
        var exec = new Execucao();
        var variaveis = new List<(string, string?)>
        {
            (ConexaoConfig.VarHost, "host-teste"),
            (ConexaoConfig.VarPorta, "5544"),
        };
        if (cenario == Cenario.ConfiguracaoInvalida)
        {
            variaveis.Add((ConexaoConfig.VarUsuario, null));
        }

        if (cenario == Cenario.PortaInvalida)
        {
            variaveis.Add((ConexaoConfig.VarPorta, "porta" + Sentinela));
        }

        var repo = exec.Repositorio;
        var mensagemSecreta = "conexao Password=" + Sentinela + " Host=x";
        using var cts = new CancellationTokenSource();
        switch (cenario)
        {
            case Cenario.StIsValidFalso:
                repo.AoValidar = (_, _) => Task.FromResult(new ValidacaoBanco(false, null));
                break;
            case Cenario.ErroBancoNoItem:
                repo.AoInserir = (_, _) => Task.FromResult(77);
                repo.AoLer = (_, _) => throw new FalhaBancoException("XX000", "erro\ninterno");
                break;
            case Cenario.SridDivergente:
                repo.AoLer = (id, _) => Task.FromResult<RegistroGraphy?>(new RegistroGraphy(id, "POINT(0 0)", 4269));
                break;
            case Cenario.FalhaConexaoNoItem:
                repo.AoValidar = (_, _) => throw new FalhaConexaoException("NpgsqlException/SocketException");
                break;
            case Cenario.CanceladoPeloRepositorio:
                repo.AoValidar = (_, _) => throw new OperationCanceledException(mensagemSecreta);
                break;
            case Cenario.CanceladoPeloToken:
                await cts.CancelAsync();
                break;
            case Cenario.ExcecaoInesperada:
                repo.AoInserir = (_, _) => throw new InvalidOperationException(mensagemSecreta);
                break;
            case Cenario.PostgresExceptionNaoTraduzida:
                repo.AoValidar = (_, _) => throw new Npgsql.PostgresException(
                    mensagemSecreta, "ERROR", "ERROR", "XX000", detail: "DETALHE " + Sentinela, hint: "DICA " + Sentinela);
                break;
        }

        args ??= cenario switch
        {
            Cenario.WktInvalido => ["POINT(0 0", "POINT(1 1)"],
            Cenario.LimiteExcedido => Enumerable.Repeat("POINT(0 0)", InsercaoService.MaximoWktPorExecucao + 1).ToList(),
            _ => [],
        };

        Func<ConexaoConfig, ILog, IGraphyRepository> fabrica = (config, _) =>
        {
            exec.ChamadasFabrica++;
            exec.ConfigRecebida = config;
            return cenario switch
            {
                Cenario.FabricaFalhaConexao => throw new FalhaConexaoException("TimeoutException"),
                Cenario.FabricaFalhaBanco => throw new FalhaBancoException("3F000", "schema\r\ninexistente"),
                Cenario.FabricaExcecaoInesperada => throw new NotSupportedException(mensagemSecreta),
                _ => repo,
            };
        };

        var log = new ConsoleLog(verboso, exec.Erro);
        var programa = new ProgramaConsole(exec.Saida, exec.Erro, Ambiente.Variaveis([.. variaveis]), fabrica, log);
        var codigo = await programa.ExecutarAsync(args, cts.Token);
        return (codigo, exec);
    }

    public static TheoryData<Cenario, bool> TodosOsCenarios()
    {
        var dados = new TheoryData<Cenario, bool>();
        foreach (var cenario in Enum.GetValues<Cenario>())
        {
            dados.Add(cenario, false);
            dados.Add(cenario, true);
        }

        return dados;
    }

    [Theory]
    [MemberData(nameof(TodosOsCenarios))]
    public async Task Sentinela_NuncaApareceNoStdoutNemStderr(Cenario cenario, bool verboso)
    {
        var (_, exec) = await ExecutarAsync(cenario, verboso);

        Assert.DoesNotContain(Sentinela, exec.Tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("Password=", exec.Tudo, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DETALHE", exec.Tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("DICA", exec.Tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", exec.Tudo, StringComparison.Ordinal);
        if (verboso && cenario is not Cenario.ConfiguracaoInvalida and not Cenario.PortaInvalida)
        {
            Assert.Contains("[verboso ", exec.Erro.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(Cenario.Sucesso, CodigoSaida.Ok)]
    [InlineData(Cenario.WktInvalido, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.StIsValidFalso, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.LimiteExcedido, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.ErroBancoNoItem, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.SridDivergente, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.FalhaConexaoNoItem, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.CanceladoPeloRepositorio, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.CanceladoPeloToken, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.ExcecaoInesperada, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.PostgresExceptionNaoTraduzida, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.FabricaFalhaConexao, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.FabricaFalhaBanco, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.FabricaExcecaoInesperada, CodigoSaida.FalhaBanco)]
    [InlineData(Cenario.ConfiguracaoInvalida, CodigoSaida.ConfiguracaoInvalida)]
    [InlineData(Cenario.PortaInvalida, CodigoSaida.ConfiguracaoInvalida)]
    public async Task CodigoDeSaida(Cenario cenario, CodigoSaida esperado)
    {
        var (codigo, _) = await ExecutarAsync(cenario);

        Assert.Equal((int)esperado, codigo);
    }

    [Theory]
    [InlineData(Cenario.Sucesso)]
    [InlineData(Cenario.WktInvalido)]
    [InlineData(Cenario.StIsValidFalso)]
    [InlineData(Cenario.LimiteExcedido)]
    [InlineData(Cenario.ErroBancoNoItem)]
    [InlineData(Cenario.SridDivergente)]
    [InlineData(Cenario.FalhaConexaoNoItem)]
    [InlineData(Cenario.CanceladoPeloRepositorio)]
    [InlineData(Cenario.CanceladoPeloToken)]
    [InlineData(Cenario.ExcecaoInesperada)]
    [InlineData(Cenario.PostgresExceptionNaoTraduzida)]
    public async Task DisposeAsyncDoRepositorio_ChamadoUmaVezEmTodosOsCaminhos(Cenario cenario)
    {
        var (_, exec) = await ExecutarAsync(cenario);

        Assert.Equal(1, exec.ChamadasFabrica);
        Assert.Equal(1, exec.Repositorio.ChamadasDispose);
    }

    [Theory]
    [InlineData(Cenario.ConfiguracaoInvalida)]
    [InlineData(Cenario.PortaInvalida)]
    public async Task ConfiguracaoInvalida_NaoCriaRepositorio_MensagemSoComONome(Cenario cenario)
    {
        var (_, exec) = await ExecutarAsync(cenario);

        Assert.Equal(0, exec.ChamadasFabrica);
        Assert.Equal(0, exec.Repositorio.ChamadasDispose);
        Assert.Contains("Configuracao invalida: Variavel de ambiente PG_", exec.Erro.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(Cenario.FabricaFalhaConexao, "falha de conexao com host-teste:5544 (TimeoutException)")]
    [InlineData(Cenario.FabricaFalhaBanco, "Erro do banco: SqlState 3F000: schema??inexistente")]
    [InlineData(Cenario.FabricaExcecaoInesperada, "Erro inesperado (NotSupportedException).")]
    [InlineData(Cenario.ExcecaoInesperada, "Erro inesperado (InvalidOperationException).")]
    [InlineData(Cenario.PostgresExceptionNaoTraduzida, "Erro inesperado (PostgresException).")]
    [InlineData(Cenario.CanceladoPeloRepositorio, "Execucao cancelada.")]
    [InlineData(Cenario.CanceladoPeloToken, "Execucao cancelada.")]
    [InlineData(Cenario.FalhaConexaoNoItem, "[1] ERRO: falha de conexao com host-teste:5544 (NpgsqlException/SocketException)")]
    [InlineData(Cenario.ErroBancoNoItem, "[1] ERRO DO BANCO (id inserido=77): SqlState XX000: erro?interno | WKT: POINT(-46.6333 -23.5505)")]
    [InlineData(Cenario.SridDivergente, "[1] ERRO DO BANCO (id inserido=1): SRID lido = 4269, esperado 4326")]
    [InlineData(Cenario.StIsValidFalso, "[1] WKT INVALIDO (ST_IsValid)")]
    [InlineData(Cenario.WktInvalido, "[1] WKT INVALIDO (validacao local)")]
    [InlineData(Cenario.LimiteExcedido, "excede o maximo de 100 por execucao; nada foi inserido.")]
    public async Task MensagensDeErro_NoStderr(Cenario cenario, string trecho)
    {
        var (_, exec) = await ExecutarAsync(cenario);

        Assert.Contains(trecho, exec.Erro.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task FalhaConexaoNoItem_DemaisNaoProcessados()
    {
        var (_, exec) = await ExecutarAsync(Cenario.FalhaConexaoNoItem);

        var erro = exec.Erro.ToString();
        Assert.Contains("[2] NAO PROCESSADO", erro, StringComparison.Ordinal);
        Assert.Contains("[3] NAO PROCESSADO", erro, StringComparison.Ordinal);
        Assert.Equal(1, exec.Repositorio.ChamadasValidar);
    }

    [Fact]
    public async Task Sucesso_SemArgumentos_UsaExemplosFixosEImprimeResumo()
    {
        var (codigo, exec) = await ExecutarAsync(Cenario.Sucesso);

        var saida = exec.Saida.ToString();
        Assert.Equal(0, codigo);
        Assert.Contains("Destino: host-teste:5544/casaos (usuario: usuario_teste)", saida, StringComparison.Ordinal);
        Assert.Contains("Fonte: 3 exemplos fixos", saida, StringComparison.Ordinal);
        Assert.Contains("[1] OK id=1 SRID=4326 ST_AsText=POINT(-46.6333 -23.5505)", saida, StringComparison.Ordinal);
        Assert.Contains("[3] OK id=3 SRID=4326 ST_AsText=POLYGON", saida, StringComparison.Ordinal);
        Assert.Contains("Resumo: 3 de 3 inserido(s). Codigo de saida 0.", saida, StringComparison.Ordinal);
        Assert.Equal(3, exec.Repositorio.ChamadasInserir);
    }

    [Fact]
    public async Task Argumentos_SaoUsadosNoLugarDosExemplos()
    {
        var (codigo, exec) = await ExecutarAsync(Cenario.Sucesso, args: ["POINT(1 1)"]);

        Assert.Equal(0, codigo);
        Assert.Contains("Fonte: 1 WKT(s) informado(s) por argumento", exec.Saida.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, exec.Repositorio.ChamadasInserir);
    }

    [Fact]
    public async Task ArgumentoComCredencial_NaoEEcoado()
    {
        var (codigo, exec) = await ExecutarAsync(Cenario.Sucesso, args: ["Host=x;Password=" + Sentinela]);

        Assert.Equal(2, codigo);
        Assert.Contains("argumento omitido", exec.Erro.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, exec.Tudo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Disable", true)]
    [InlineData("Prefer", true)]
    [InlineData("Require", false)]
    [InlineData("VerifyFull", false)]
    public async Task AvisoSsl_SoParaDisableOuPrefer(string ssl, bool aviso)
    {
        using var erro = new StringWriter();
        var programa = new ProgramaConsole(
            TextWriter.Null,
            erro,
            Ambiente.Variaveis((ConexaoConfig.VarSslMode, ssl)),
            (_, _) => new RepositorioFalso(),
            LogNulo.Instancia);

        await programa.ExecutarAsync(["POINT(1 1)"]);

        Assert.Equal(aviso, erro.ToString().Contains("Aviso: PG_SSLMODE", StringComparison.Ordinal));
    }

    [Fact]
    public async Task FabricaRecebeConfigCarregada()
    {
        var (_, exec) = await ExecutarAsync(Cenario.Sucesso);

        Assert.NotNull(exec.ConfigRecebida);
        Assert.Equal("host-teste", exec.ConfigRecebida!.Host);
        Assert.Equal(5544, exec.ConfigRecebida.Porta);
    }

    [Fact]
    public void MensagemConexao_Generica()
    {
        var config = Ambiente.Config((ConexaoConfig.VarHost, "h\r\nx"),(ConexaoConfig.VarPorta, "1"));

        Assert.Equal("falha de conexao com h??x:1 (SocketException)", ProgramaConsole.MensagemConexao(config, "SocketException"));
        Assert.Throws<ArgumentNullException>(() => ProgramaConsole.MensagemConexao(null!, "x"));
    }

    [Fact]
    public async Task Construtor_E_Executar_ArgumentosNulos()
    {
        var w = TextWriter.Null;
        Func<string, string?> v = _ => null;
        Func<ConexaoConfig, ILog, IGraphyRepository> f = (_, _) => new RepositorioFalso();
        var l = LogNulo.Instancia;

        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(null!, w, v, f, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, null!, v, f, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, w, null!, f, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, w, v, null!, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, w, v, f, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ProgramaConsole(w, w, v, f, l).ExecutarAsync(null!));
    }
}
