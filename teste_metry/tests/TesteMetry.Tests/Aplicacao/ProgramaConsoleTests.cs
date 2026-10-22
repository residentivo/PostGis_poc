using System.Globalization;
using TesteMetry.Aplicacao;
using TesteMetry.Configuracao;
using TesteMetry.Dados;
using TesteMetry.Diagnostico;
using TesteMetry.Exemplos;
using TesteMetry.Tests.Fakes;

namespace TesteMetry.Tests.Aplicacao;

/// <summary>M6.5 - ProgramaConsole: codigos 0/1/2/3, PG_SRID (RQ-1/RQ-2) e S6 (senha sentinela).</summary>
public class ProgramaConsoleTests
{
    public enum Cenario
    {
        Sucesso,
        WktInvalido,
        StIsValidFalso,
        Xx000NaValidacao,
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
        SridInvalido,
        SridComInjecao,
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
        IReadOnlyList<string>? args = null,
        int srid = 4326)
    {
        var exec = new Execucao();
        var variaveis = new List<(string, string?)>
        {
            (ConexaoConfig.VarHost, "host-teste"),
            (ConexaoConfig.VarPorta, "5544"),
            (ConexaoConfig.VarSrid, srid.ToString(CultureInfo.InvariantCulture)),
        };
        switch (cenario)
        {
            case Cenario.ConfiguracaoInvalida:
                variaveis.Add((ConexaoConfig.VarUsuario, null));
                break;
            case Cenario.PortaInvalida:
                variaveis.Add((ConexaoConfig.VarPorta, "porta" + Sentinela));
                break;
            case Cenario.SridInvalido:
                variaveis.Add((ConexaoConfig.VarSrid, "04326"));
                break;
            case Cenario.SridComInjecao:
                variaveis.Add((ConexaoConfig.VarSrid, "4326;DROP TABLE mapas;" + Sentinela));
                break;
        }

        var repo = exec.Repositorio;
        var mensagemSecreta = "conexao Password=" + Sentinela + " Host=x";
        using var cts = new CancellationTokenSource();
        switch (cenario)
        {
            case Cenario.StIsValidFalso:
                repo.AoValidar = (_, _, _) => Task.FromResult(new ValidacaoBanco(false, null));
                break;
            case Cenario.Xx000NaValidacao:
                repo.AoValidar = (_, _, _) => throw new FalhaBancoException("XX000", "parse error - invalid geometry");
                break;
            case Cenario.ErroBancoNoItem:
                repo.AoInserir = (_, _, _) => Task.FromResult(77);
                repo.AoLer = (_, _) => throw new FalhaBancoException("42501", "erro\ninterno");
                break;
            case Cenario.SridDivergente:
                repo.AoLer = (id, _) => Task.FromResult<RegistroMetry?>(new RegistroMetry(id, "POINT(0 0)", 4269));
                break;
            case Cenario.FalhaConexaoNoItem:
                repo.AoValidar = (_, _, _) => throw new FalhaConexaoException("NpgsqlException/SocketException");
                break;
            case Cenario.CanceladoPeloRepositorio:
                repo.AoValidar = (_, _, _) => throw new OperationCanceledException(mensagemSecreta);
                break;
            case Cenario.CanceladoPeloToken:
                await cts.CancelAsync();
                break;
            case Cenario.ExcecaoInesperada:
                repo.AoInserir = (_, _, _) => throw new InvalidOperationException(mensagemSecreta);
                break;
            case Cenario.PostgresExceptionNaoTraduzida:
                repo.AoValidar = (_, _, _) => throw new Npgsql.PostgresException(
                    mensagemSecreta, "ERROR", "ERROR", "XX000", detail: "DETALHE " + Sentinela, hint: "DICA " + Sentinela);
                break;
        }

        args ??= cenario switch
        {
            Cenario.WktInvalido => ["POINT(0 0", "POINT(1 1)"],
            Cenario.LimiteExcedido => Enumerable.Repeat("POINT(0 0)", InsercaoService.MaximoWktPorExecucao + 1).ToList(),
            _ => [],
        };

        Func<ConexaoConfig, ILog, IMetryRepository> fabrica = (config, _) =>
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
        Assert.DoesNotContain("DROP", exec.Tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("   at ", exec.Tudo, StringComparison.Ordinal);
        if (verboso && cenario is not (Cenario.ConfiguracaoInvalida or Cenario.PortaInvalida or Cenario.SridInvalido or Cenario.SridComInjecao))
        {
            Assert.Contains("[verboso ", exec.Erro.ToString(), StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(Cenario.Sucesso, CodigoSaida.Ok)]
    [InlineData(Cenario.WktInvalido, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.StIsValidFalso, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.Xx000NaValidacao, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.LimiteExcedido, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.SridDivergente, CodigoSaida.WktInvalido)]
    [InlineData(Cenario.ErroBancoNoItem, CodigoSaida.FalhaBanco)]
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
    [InlineData(Cenario.SridInvalido, CodigoSaida.ConfiguracaoInvalida)]
    [InlineData(Cenario.SridComInjecao, CodigoSaida.ConfiguracaoInvalida)]
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
    [InlineData(Cenario.ConfiguracaoInvalida, "PG_USER: obrigatoria e nao definida.")]
    [InlineData(Cenario.PortaInvalida, "PG_PORT: deve ser um numero inteiro entre 1 e 65535.")]
    [InlineData(Cenario.SridInvalido, "PG_SRID: zeros a esquerda nao sao aceitos; valor nao permitido (use 4326, 4674, 3857 ou 31983).")]
    [InlineData(Cenario.SridComInjecao, "PG_SRID: valor nao permitido (use 4326, 4674, 3857 ou 31983).")]
    public async Task ConfiguracaoInvalida_NaoCriaRepositorio_MensagemSoComONome(Cenario cenario, string mensagem)
    {
        var (_, exec) = await ExecutarAsync(cenario, verboso: true);

        Assert.Equal(0, exec.ChamadasFabrica);
        Assert.Equal(0, exec.Repositorio.ChamadasDispose);
        Assert.Contains("Configuracao invalida: Variavel de ambiente " + mensagem, exec.Erro.ToString(), StringComparison.Ordinal);
        Assert.DoesNotContain("04326", exec.Tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("porta", exec.Tudo, StringComparison.Ordinal);
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
    [InlineData(Cenario.ErroBancoNoItem, "[1] ERRO DO BANCO (id inserido=77): SqlState 42501: erro?interno | WKT: POINT(-46.6333 -23.5505)")]
    [InlineData(Cenario.SridDivergente, "[1] SRID DIVERGENTE (id inserido=1): SRID lido = 4269, esperado 4326 (PG_SRID) | WKT: POINT(-46.6333 -23.5505)")]
    [InlineData(Cenario.StIsValidFalso, "[1] WKT INVALIDO (ST_IsValid): geometria invalida segundo ST_IsValid; nao inserida")]
    [InlineData(Cenario.Xx000NaValidacao, "[1] WKT INVALIDO (ST_IsValid): WKT rejeitado pelo PostGIS: SqlState XX000: parse error - invalid geometry")]
    [InlineData(Cenario.WktInvalido, "[1] WKT INVALIDO (validacao local): parenteses desbalanceados (falta ')') | WKT: POINT(0 0")]
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
        Assert.Contains("[2] NAO PROCESSADO: nao processado: falha de conexao em item anterior", erro, StringComparison.Ordinal);
        Assert.Contains("[3] NAO PROCESSADO", erro, StringComparison.Ordinal);
        Assert.Equal(1, exec.Repositorio.ChamadasValidar);
    }

    [Theory]
    [InlineData(4326, "POINT(-46.6333 -23.5505)")]
    [InlineData(4674, "POINT(-46.6333 -23.5505)")]
    [InlineData(3857, "POINT(-5191195.21 -2698729.46)")]
    [InlineData(31983, "POINT(333250 7394600)")]
    public async Task Sucesso_SemArgumentos_UsaExemplosDoSrid(int srid, string primeiro)
    {
        var (codigo, exec) = await ExecutarAsync(Cenario.Sucesso, srid: srid);

        var saida = exec.Saida.ToString();
        Assert.Equal(0, codigo);
        Assert.Contains("TesteMetry - insercao em public.mapas.metry", saida, StringComparison.Ordinal);
        Assert.Contains($"Destino: host-teste:5544/casaos usuario=usuario_teste srid={srid}", saida, StringComparison.Ordinal);
        Assert.Contains($"Fonte: 3 exemplos fixos (Sao Paulo, SRID {srid})", saida, StringComparison.Ordinal);
        Assert.Contains($"[1] OK id=1 SRID={srid} ST_AsText={primeiro}", saida, StringComparison.Ordinal);
        Assert.Contains($"[3] OK id=3 SRID={srid} ST_AsText=POLYGON", saida, StringComparison.Ordinal);
        Assert.Contains("Resumo: 3 de 3 inserido(s). Codigo de saida 0.", saida, StringComparison.Ordinal);
        Assert.Equal(ExemplosFixos.Para(srid), exec.Repositorio.WktsValidados);
        Assert.All(exec.Repositorio.SridsRecebidos, s => Assert.Equal(srid, s));
        Assert.Equal(srid, exec.ConfigRecebida!.Srid);
    }

    /// <summary>RQ-2: os argumentos sao so WKT; nada neles altera o SRID enviado ao banco.</summary>
    [Theory]
    [InlineData(4326, "SRID=3857;POINT(0 0)")]
    [InlineData(4326, "3857")]
    [InlineData(4326, "--srid=3857")]
    [InlineData(4326, "PG_SRID=3857")]
    [InlineData(3857, "SRID=4326;POINT(0 0)")]
    [InlineData(31983, "srid=4326;POINT(500000 7000000)")]
    public async Task Argumento_NaoAlteraSrid(int srid, string argumento)
    {
        var (codigo, exec) = await ExecutarAsync(Cenario.Sucesso, args: [argumento, srid == 31983 ? "POINT(500000 7000000)" : "POINT(1 1)"], srid: srid);

        Assert.Equal((int)CodigoSaida.WktInvalido, codigo);
        Assert.Contains("[1] WKT INVALIDO (validacao local)", exec.Erro.ToString(), StringComparison.Ordinal);
        Assert.Contains($"[2] OK id=1 SRID={srid}", exec.Saida.ToString(), StringComparison.Ordinal);
        Assert.Contains($"(SRID {srid})", exec.Saida.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, exec.Repositorio.ChamadasValidar);
        Assert.All(exec.Repositorio.SridsRecebidos, s => Assert.Equal(srid, s));
    }

    [Fact]
    public async Task InjecaoDeSqlNoWkt_RejeitadaLocalmente()
    {
        var (codigo, exec) = await ExecutarAsync(Cenario.Sucesso, args: ["POINT(0 0)'); DELETE FROM public.mapas; --"]);

        Assert.Equal((int)CodigoSaida.WktInvalido, codigo);
        Assert.Equal(0, exec.Repositorio.TotalChamadas);
        Assert.Contains("caractere nao permitido (U+0027)", exec.Erro.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4326, "POINT(-5191195.21 -2698729.46)", CodigoSaida.WktInvalido)]
    [InlineData(3857, "POINT(-5191195.21 -2698729.46)", CodigoSaida.Ok)]
    [InlineData(31983, "POINT(333250 7394600)", CodigoSaida.Ok)]
    [InlineData(4326, "POINT(333250 7394600)", CodigoSaida.WktInvalido)]
    public async Task FaixaDoSrid_AplicadaAosArgumentos(int srid, string wkt, CodigoSaida esperado)
    {
        var (codigo, _) = await ExecutarAsync(Cenario.Sucesso, args: [wkt], srid: srid);

        Assert.Equal((int)esperado, codigo);
    }

    [Fact]
    public async Task Argumentos_SaoUsadosNoLugarDosExemplos()
    {
        var (codigo, exec) = await ExecutarAsync(Cenario.Sucesso, args: ["POINT(1 1)"]);

        Assert.Equal(0, codigo);
        Assert.Contains("Fonte: 1 WKT(s) informado(s) por argumento (SRID 4326)", exec.Saida.ToString(), StringComparison.Ordinal);
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

    /// <summary>
    /// Repositorio REAL (MetryRepository) contra servidor Postgres falso: SqlState de conexao ->
    /// codigo 3, itens seguintes nao processados, sem detalhe/sentinela na saida.
    /// </summary>
    [Theory]
    [InlineData("28P01")]
    [InlineData("3D000")]
    [InlineData("53300")]
    [InlineData("XX000")] // MF-2: qualquer erro do servidor na ABERTURA
    [InlineData("22023")]
    [InlineData("42501")]
    public async Task RepositorioReal_ServidorFalsoComErroDeConexao_Codigo3(string sqlState)
    {
        await using var servidor = ServidorPostgresFalso.ComErro(sqlState, "falha " + Sentinela, "DETALHE " + Sentinela, "DICA");
        using var saida = new StringWriter();
        using var erro = new StringWriter();
        var programa = new ProgramaConsole(
            saida,
            erro,
            Ambiente.Variaveis(
                (ConexaoConfig.VarHost, "127.0.0.1"),
                (ConexaoConfig.VarPorta, servidor.Porta.ToString(CultureInfo.InvariantCulture)),
                (ConexaoConfig.VarSslMode, "Disable"),
                (ConexaoConfig.VarSrid, "31983")),
            static (config, log) => new MetryRepository(config, log),
            new ConsoleLog(true, erro));

        var codigo = await programa.ExecutarAsync([]);

        var tudo = saida + "\n" + erro;
        Assert.Equal((int)CodigoSaida.FalhaBanco, codigo);
        Assert.Contains($"[1] ERRO: falha de conexao com 127.0.0.1:{servidor.Porta} (PostgresException/{sqlState})", tudo, StringComparison.Ordinal);
        Assert.Contains("[2] NAO PROCESSADO", tudo, StringComparison.Ordinal);
        Assert.Contains("[3] NAO PROCESSADO", tudo, StringComparison.Ordinal);
        Assert.DoesNotContain(Sentinela, tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("DETALHE", tudo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task FabricaRecebeConfigCarregada()
    {
        var (_, exec) = await ExecutarAsync(Cenario.Sucesso, srid: 3857);

        Assert.NotNull(exec.ConfigRecebida);
        Assert.Equal("host-teste", exec.ConfigRecebida!.Host);
        Assert.Equal(5544, exec.ConfigRecebida.Porta);
        Assert.Equal(3857, exec.ConfigRecebida.Srid);
    }

    [Fact]
    public void MensagemConexao_Generica()
    {
        var config = Ambiente.Config((ConexaoConfig.VarHost, "h\r\nx"), (ConexaoConfig.VarPorta, "1"));

        Assert.Equal("falha de conexao com h??x:1 (SocketException)", ProgramaConsole.MensagemConexao(config, "SocketException"));
        Assert.Throws<ArgumentNullException>(() => ProgramaConsole.MensagemConexao(null!, "x"));
    }

    [Fact]
    public async Task Construtor_E_Executar_ArgumentosNulos()
    {
        var w = TextWriter.Null;
        Func<string, string?> v = _ => null;
        Func<ConexaoConfig, ILog, IMetryRepository> f = (_, _) => new RepositorioFalso();
        var l = LogNulo.Instancia;

        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(null!, w, v, f, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, null!, v, f, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, w, null!, f, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, w, v, null!, l));
        Assert.Throws<ArgumentNullException>(() => new ProgramaConsole(w, w, v, f, null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => new ProgramaConsole(w, w, v, f, l).ExecutarAsync(null!));
    }
}

/// <summary>
/// MF-3 (R-10, R-11): SRID sem exemplos fixos/faixa chegando ao ProgramaConsole = erro de CONFIGURACAO
/// (codigo 1), mensagem citando PG_SRID sem o valor e SEM chamar a fabrica do repositorio.
/// Pela configuracao o cenario e IMPOSSIVEL: ConexaoConfig.Carregar so aceita SRIDs de LimitesSrid e
/// todo SRID permitido tem exemplos e faixa (o catch e defesa em profundidade). Para cobrir o ramo,
/// o teste troca TEMPORARIAMENTE uma entrada do array privado LimitesSrid.Faixas por reflexao
/// (colecao sem paralelismo, restaurada no finally). Nenhum codigo de producao e alterado.
/// </summary>
[Collection(ColecaoConsole.Nome)]
public class ProgramaConsoleSridSemExemplosTests
{
    private const int SridFalso = 99999;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SridPermitidoSemExemplos_Codigo1_SemValor_SemFabrica(bool verboso)
    {
        var campo = typeof(TesteMetry.Validacao.LimitesSrid).GetField("Faixas", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        var faixas = (TesteMetry.Validacao.FaixaSrid[])campo!.GetValue(null)!;
        var indice = Array.FindIndex(faixas, f => f.Srid == TesteMetry.Validacao.LimitesSrid.Sirgas2000Utm23S);
        var original = faixas[indice];
        faixas[indice] = original with { Srid = SridFalso };
        try
        {
            using var saida = new StringWriter();
            using var erro = new StringWriter();
            var chamadasFabrica = 0;
            var programa = new ProgramaConsole(
                saida,
                erro,
                Ambiente.Variaveis((ConexaoConfig.VarHost, "host-teste"), (ConexaoConfig.VarSrid, SridFalso.ToString(CultureInfo.InvariantCulture))),
                (_, _) =>
                {
                    chamadasFabrica++;
                    return new RepositorioFalso();
                },
                new ConsoleLog(verboso, erro));

            var codigo = await programa.ExecutarAsync([]);

            var textoErro = erro.ToString();
            Assert.Equal((int)CodigoSaida.ConfiguracaoInvalida, codigo);
            Assert.Equal(0, chamadasFabrica);
            Assert.Contains("Configuracao invalida: PG_SRID nao permitido (use 4326, 4674, 3857 ou 31983).", textoErro, StringComparison.Ordinal);
            Assert.DoesNotContain("Fonte:", saida.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain("ArgumentOutOfRange", textoErro, StringComparison.Ordinal);
            Assert.DoesNotContain("   at ", textoErro, StringComparison.Ordinal);
            Assert.DoesNotContain(Ambiente.Sentinela, saida + textoErro, StringComparison.Ordinal);

            // O valor so aparece onde ja aparecia antes do MF-3 (Destino/SRID efetivo, ja validado
            // pela configuracao); a mensagem de erro do MF-3 nao o repete.
            var linhaErro = textoErro.Split('\n').Single(l => l.StartsWith("Configuracao invalida:", StringComparison.Ordinal));
            Assert.DoesNotContain(SridFalso.ToString(CultureInfo.InvariantCulture), linhaErro, StringComparison.Ordinal);
            if (verboso)
            {
                Assert.Contains("Configuracao invalida: PG_SRID", textoErro, StringComparison.Ordinal);
            }
        }
        finally
        {
            faixas[indice] = original;
        }

        Assert.True(TesteMetry.Validacao.LimitesSrid.EhPermitido(TesteMetry.Validacao.LimitesSrid.Sirgas2000Utm23S));
        Assert.False(TesteMetry.Validacao.LimitesSrid.EhPermitido(SridFalso));
    }
}
