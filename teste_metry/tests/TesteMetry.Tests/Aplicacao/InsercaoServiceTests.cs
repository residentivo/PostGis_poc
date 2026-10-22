using TesteMetry.Aplicacao;
using TesteMetry.Configuracao;
using TesteMetry.Dados;
using TesteMetry.Exemplos;
using TesteMetry.Tests.Fakes;
using TesteMetry.Validacao;

namespace TesteMetry.Tests.Aplicacao;

/// <summary>M6.5 - InsercaoService com IMetryRepository falso (e, para A07, o repositorio real contra servidor falso).</summary>
public class InsercaoServiceTests
{
    public static readonly TheoryData<int> Srids = new() { 4326, 4674, 3857, 31983 };

    private static InsercaoService Criar(RepositorioFalso repo, int srid = 4326, LogCaptura? log = null) =>
        new(repo, new WktValidator(srid), log);

    [Theory]
    [MemberData(nameof(Srids))]
    public async Task Exemplos_InseridosComOSridDoValidador(int srid)
    {
        var repo = new RepositorioFalso();
        var servico = Criar(repo, srid);

        var resultado = await servico.ExecutarAsync(ExemplosFixos.Para(srid));

        Assert.Equal(CodigoSaida.Ok, resultado.Codigo);
        Assert.False(resultado.LimiteExcedido);
        Assert.Equal(srid, servico.Srid);
        Assert.Equal(3, resultado.Itens.Count);
        Assert.All(resultado.Itens, i =>
        {
            Assert.Equal(StatusItem.Inserido, i.Status);
            Assert.NotNull(i.Registro);
            Assert.Equal(srid, i.Registro!.Srid);
            Assert.Equal(i.IdInserido, i.Registro.Id);
            Assert.Null(i.Erro);
        });
        Assert.Equal(new[] { 1, 2, 3 }, resultado.Itens.Select(i => i.Indice));
        Assert.All(repo.SridsRecebidos, s => Assert.Equal(srid, s));
        Assert.Equal(6, repo.SridsRecebidos.Count);
        Assert.Equal(3, repo.ChamadasLer);
    }

    public enum Falha
    {
        StIsValidFalso,
        TipoNulo,
        TipoForaDaLista,
        TipoMinusculo,
        Xx000NaValidacao,
        Classe22NaValidacao,
        OutroSqlStateNaValidacao,
        SqlStateNuloNaValidacao,
        ErroBancoNoInsert,
        ErroBancoNaLeitura,
        LeituraNula,
        SridDivergente,
        ConexaoNaValidacao,
        ConexaoNoInsert,
        ConexaoNaLeitura,
        ConexaoXx000NaAbertura,
        ConexaoClasse22NaAbertura,
    }

    [Theory]
    [InlineData(Falha.StIsValidFalso, StatusItem.InvalidoBanco, CodigoSaida.WktInvalido, false)]
    [InlineData(Falha.TipoNulo, StatusItem.InvalidoBanco, CodigoSaida.WktInvalido, false)]
    [InlineData(Falha.TipoForaDaLista, StatusItem.InvalidoBanco, CodigoSaida.WktInvalido, false)]
    [InlineData(Falha.TipoMinusculo, StatusItem.Inserido, CodigoSaida.Ok, true)]
    [InlineData(Falha.Xx000NaValidacao, StatusItem.InvalidoBanco, CodigoSaida.WktInvalido, false)]
    [InlineData(Falha.Classe22NaValidacao, StatusItem.InvalidoBanco, CodigoSaida.WktInvalido, false)]
    [InlineData(Falha.OutroSqlStateNaValidacao, StatusItem.ErroBanco, CodigoSaida.FalhaBanco, false)]
    [InlineData(Falha.SqlStateNuloNaValidacao, StatusItem.ErroBanco, CodigoSaida.FalhaBanco, false)]
    [InlineData(Falha.ErroBancoNoInsert, StatusItem.ErroBanco, CodigoSaida.FalhaBanco, false)]
    [InlineData(Falha.ErroBancoNaLeitura, StatusItem.ErroBanco, CodigoSaida.FalhaBanco, true)]
    [InlineData(Falha.LeituraNula, StatusItem.ErroBanco, CodigoSaida.FalhaBanco, true)]
    [InlineData(Falha.SridDivergente, StatusItem.SridDivergente, CodigoSaida.WktInvalido, true)]
    [InlineData(Falha.ConexaoNaValidacao, StatusItem.ErroConexao, CodigoSaida.FalhaBanco, false)]
    [InlineData(Falha.ConexaoNoInsert, StatusItem.ErroConexao, CodigoSaida.FalhaBanco, false)]
    [InlineData(Falha.ConexaoNaLeitura, StatusItem.ErroConexao, CodigoSaida.FalhaBanco, true)]
    [InlineData(Falha.ConexaoXx000NaAbertura, StatusItem.ErroConexao, CodigoSaida.FalhaBanco, false)]
    [InlineData(Falha.ConexaoClasse22NaAbertura, StatusItem.ErroConexao, CodigoSaida.FalhaBanco, false)]
    public async Task FalhaNoPrimeiroItem(Falha falha, StatusItem status, CodigoSaida codigo, bool comId)
    {
        var repo = new RepositorioFalso();
        var primeiro = true;
        bool Primeiro()
        {
            var r = primeiro;
            primeiro = false;
            return r;
        }

        switch (falha)
        {
            case Falha.StIsValidFalso:
                repo.AoValidar = (_, _, _) => Task.FromResult(new ValidacaoBanco(!Primeiro(), "POINT"));
                break;
            case Falha.TipoNulo:
                repo.AoValidar = (_, _, _) => Task.FromResult(new ValidacaoBanco(true, Primeiro() ? null : "POINT"));
                break;
            case Falha.TipoForaDaLista:
                repo.AoValidar = (_, _, _) => Task.FromResult(new ValidacaoBanco(true, Primeiro() ? "CIRCULARSTRING\n" : "POINT"));
                break;
            case Falha.TipoMinusculo:
                repo.AoValidar = (_, _, _) => Task.FromResult(new ValidacaoBanco(true, "point"));
                break;
            case Falha.Xx000NaValidacao:
                repo.AoValidar = (_, _, _) => Primeiro() ? throw new FalhaBancoException("XX000", "parse error - invalid geometry") : Task.FromResult(new ValidacaoBanco(true, "POINT"));
                break;
            case Falha.Classe22NaValidacao:
                repo.AoValidar = (_, _, _) => Primeiro() ? throw new FalhaBancoException("22023", "invalid") : Task.FromResult(new ValidacaoBanco(true, "POINT"));
                break;
            case Falha.OutroSqlStateNaValidacao:
                repo.AoValidar = (_, _, _) => Primeiro() ? throw new FalhaBancoException("42501", "permission denied") : Task.FromResult(new ValidacaoBanco(true, "POINT"));
                break;
            case Falha.SqlStateNuloNaValidacao:
                repo.AoValidar = (_, _, _) => Primeiro() ? throw new FalhaBancoException(null, "sem linhas") : Task.FromResult(new ValidacaoBanco(true, "POINT"));
                break;
            case Falha.ErroBancoNoInsert:
                repo.AoInserir = (_, _, _) => Primeiro() ? throw new FalhaBancoException("23505", "dup") : Task.FromResult(9);
                repo.AoLer = (id, _) => Task.FromResult<RegistroMetry?>(new RegistroMetry(id, "POINT(0 0)", 4326));
                break;
            case Falha.ErroBancoNaLeitura:
                repo.AoInserir = (_, _, _) => Task.FromResult(77);
                repo.AoLer = (id, _) => Primeiro() ? throw new FalhaBancoException("XX000", "erro\ninterno") : Task.FromResult<RegistroMetry?>(new RegistroMetry(id, "POINT(0 0)", 4326));
                break;
            case Falha.LeituraNula:
                repo.AoInserir = (_, _, _) => Task.FromResult(77);
                repo.AoLer = (id, _) => Task.FromResult<RegistroMetry?>(Primeiro() ? null : new RegistroMetry(id, "POINT(0 0)", 4326));
                break;
            case Falha.SridDivergente:
                repo.AoInserir = (_, _, _) => Task.FromResult(77);
                repo.AoLer = (id, _) => Task.FromResult<RegistroMetry?>(new RegistroMetry(id, "POINT(0 0)", Primeiro() ? 3857 : 4326));
                break;
            case Falha.ConexaoNaValidacao:
                repo.AoValidar = (_, _, _) => throw new FalhaConexaoException("NpgsqlException/SocketException");
                break;
            case Falha.ConexaoNoInsert:
                repo.AoInserir = (_, _, _) => throw new FalhaConexaoException("TimeoutException");
                break;
            case Falha.ConexaoNaLeitura:
                repo.AoInserir = (_, _, _) => Task.FromResult(77);
                repo.AoLer = (_, _) => throw new FalhaConexaoException("PostgresException/57P03");
                break;
            case Falha.ConexaoXx000NaAbertura:
                // MF-2: XX000 na ABERTURA chega ao servico como FalhaConexaoException (nao e erro de dado).
                repo.AoValidar = (_, _, _) => Primeiro() ? throw new FalhaConexaoException("PostgresException/XX000") : Task.FromResult(new ValidacaoBanco(true, "POINT"));
                break;
            case Falha.ConexaoClasse22NaAbertura:
                repo.AoValidar = (_, _, _) => Primeiro() ? throw new FalhaConexaoException("PostgresException/22023") : Task.FromResult(new ValidacaoBanco(true, "POINT"));
                break;
        }

        var log = new LogCaptura();
        var resultado = await Criar(repo, log: log).ExecutarAsync(["POINT(0 0)", "POINT(1 1)", "POINT(2 2)"]);

        var item = resultado.Itens[0];
        Assert.Equal(status, item.Status);
        Assert.Equal(codigo, resultado.Codigo);
        Assert.Equal(comId, item.IdInserido is not null);
        Assert.Equal("POINT(0 0)", item.WktEcoado);

        if (status == StatusItem.ErroConexao)
        {
            // Conexao falhou: os itens seguintes nao sao processados e o repositorio nao e mais chamado.
            Assert.Equal(StatusItem.NaoProcessado, resultado.Itens[1].Status);
            Assert.Equal(StatusItem.NaoProcessado, resultado.Itens[2].Status);
            Assert.Equal("nao processado: falha de conexao em item anterior", resultado.Itens[2].Erro);
            Assert.Equal(1, repo.ChamadasValidar);
        }
        else
        {
            Assert.Equal(3, repo.ChamadasValidar);
            Assert.All(resultado.Itens.Skip(1), i => Assert.Equal(StatusItem.Inserido, i.Status));
        }

        if (status != StatusItem.Inserido)
        {
            Assert.False(string.IsNullOrEmpty(item.Erro));
            Assert.DoesNotContain('\n', item.Erro!);
            Assert.NotEmpty(log.Erros);
        }

        switch (falha)
        {
            case Falha.Xx000NaValidacao:
                Assert.Equal("WKT rejeitado pelo PostGIS: SqlState XX000: parse error - invalid geometry", item.Erro);
                Assert.Equal(0, repo.ChamadasInserir - 2);
                break;
            case Falha.SridDivergente:
                Assert.Equal("SRID lido = 3857, esperado 4326 (PG_SRID)", item.Erro);
                Assert.Equal(77, item.IdInserido);
                Assert.Equal(3857, item.Registro!.Srid);
                break;
            case Falha.TipoForaDaLista:
                Assert.Equal("GeometryType retornado (CIRCULARSTRING?) fora da lista permitida; nao inserida", item.Erro);
                break;
            case Falha.TipoNulo:
                Assert.Contains("(nulo)", item.Erro, StringComparison.Ordinal);
                break;
            case Falha.LeituraNula:
                Assert.Equal("registro id = 77 nao encontrado na leitura de conferencia", item.Erro);
                break;
            case Falha.ErroBancoNaLeitura:
                Assert.Equal("SqlState XX000: erro?interno", item.Erro);
                break;
            case Falha.ConexaoNaLeitura:
                Assert.Equal("PostgresException/57P03", item.Erro);
                Assert.Equal(77, item.IdInserido);
                break;
            case Falha.ConexaoXx000NaAbertura:
                Assert.Equal("PostgresException/XX000", item.Erro);
                Assert.Equal(0, repo.ChamadasInserir);
                Assert.All(resultado.Itens.Skip(1), i => Assert.Equal("nao processado: falha de conexao em item anterior", i.Erro));
                break;
            case Falha.ConexaoClasse22NaAbertura:
                Assert.Equal("PostgresException/22023", item.Erro);
                Assert.Equal(0, repo.ChamadasInserir);
                break;
        }
    }

    [Theory]
    [InlineData("SRID=3857;POINT(0 0)")]
    [InlineData("POINT(0 0);SRID=3857")]
    [InlineData("3857")]
    [InlineData("POINT(-5191195.21 -2698729.46)")]
    [InlineData(null)]
    public async Task InvalidoLocal_NaoChegaAoBanco_E_NaoAlteraSrid(string? wkt)
    {
        var repo = new RepositorioFalso();

        var resultado = await Criar(repo).ExecutarAsync([wkt, "POINT(1 1)"]);

        Assert.Equal(CodigoSaida.WktInvalido, resultado.Codigo);
        Assert.Equal(StatusItem.InvalidoLocal, resultado.Itens[0].Status);
        Assert.Equal(StatusItem.Inserido, resultado.Itens[1].Status);
        Assert.Equal(1, repo.ChamadasValidar);
        Assert.All(repo.SridsRecebidos, s => Assert.Equal(4326, s));
    }

    [Theory]
    [InlineData(StatusItem.InvalidoLocal, StatusItem.ErroBanco, CodigoSaida.FalhaBanco)]
    [InlineData(StatusItem.SridDivergente, StatusItem.InvalidoLocal, CodigoSaida.WktInvalido)]
    [InlineData(StatusItem.InvalidoBanco, StatusItem.Inserido, CodigoSaida.WktInvalido)]
    [InlineData(StatusItem.SridDivergente, StatusItem.ErroBanco, CodigoSaida.FalhaBanco)]
    public async Task Prioridade_FalhaBancoSobreWktInvalido(StatusItem a, StatusItem b, CodigoSaida esperado)
    {
        var repo = new RepositorioFalso();
        var wkts = new List<string?>();
        var n = 0;
        foreach (var s in new[] { a, b })
        {
            n++;
            var marca = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
            wkts.Add(s switch
            {
                StatusItem.InvalidoLocal => "POINT(0 0",
                _ => $"POINT({marca} {marca})",
            });
        }

        repo.AoValidar = (w, _, _) => Task.FromResult(new ValidacaoBanco(Status(w) != StatusItem.InvalidoBanco, "POINT"));
        repo.AoInserir = (w, _, _) => Status(w) == StatusItem.ErroBanco
            ? throw new FalhaBancoException("23505", "dup")
            : Task.FromResult(w.Contains('1', StringComparison.Ordinal) ? 1 : 2);
        repo.AoLer = (id, _) => Task.FromResult<RegistroMetry?>(
            new RegistroMetry(id, "POINT(0 0)", Status(id == 1 ? "1" : "2") == StatusItem.SridDivergente ? 3857 : 4326));

        var resultado = await Criar(repo).ExecutarAsync(wkts);

        Assert.Equal(esperado, resultado.Codigo);
        Assert.Equal(a, resultado.Itens[0].Status);
        Assert.Equal(b, resultado.Itens[1].Status);

        StatusItem Status(string w) => w.Contains('1', StringComparison.Ordinal) ? a : b;
    }

    [Theory]
    [InlineData(100, false)]
    [InlineData(101, true)]
    public async Task LimiteDe100Wkt(int quantidade, bool excedido)
    {
        var repo = new RepositorioFalso();

        var resultado = await Criar(repo).ExecutarAsync(Enumerable.Repeat<string?>("POINT(0 0)", quantidade).ToList());

        Assert.Equal(excedido, resultado.LimiteExcedido);
        if (excedido)
        {
            Assert.Empty(resultado.Itens);
            Assert.Equal(CodigoSaida.WktInvalido, resultado.Codigo);
            Assert.Equal(0, repo.TotalChamadas);
        }
        else
        {
            Assert.Equal(CodigoSaida.Ok, resultado.Codigo);
            Assert.Equal(quantidade, repo.ChamadasInserir);
        }
    }

    [Fact]
    public async Task ListaVazia_Ok()
    {
        var resultado = await Criar(new RepositorioFalso()).ExecutarAsync([]);

        Assert.Equal(CodigoSaida.Ok, resultado.Codigo);
        Assert.Empty(resultado.Itens);
    }

    [Fact]
    public async Task EcoDoWkt_SanitizadoEOmitidoComCredencial()
    {
        var repo = new RepositorioFalso();

        var resultado = await Criar(repo).ExecutarAsync(["POINT(0 0)\u001B[2J", "Host=x;Password=" + Ambiente.Sentinela]);

        Assert.Equal("POINT(0 0)?[2J", resultado.Itens[0].WktEcoado);
        Assert.Equal(TextoSeguro_EcoOmitido, resultado.Itens[1].WktEcoado);
        Assert.DoesNotContain(Ambiente.Sentinela, string.Concat(resultado.Itens.Select(i => i.WktEcoado + i.Erro)), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Cancelamento_Propaga()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Criar(new RepositorioFalso()).ExecutarAsync(["POINT(0 0)"], cts.Token));
    }

    [Fact]
    public async Task ArgumentosNulos()
    {
        Assert.Throws<ArgumentNullException>(() => new InsercaoService(null!, new WktValidator(4326)));
        Assert.Throws<ArgumentNullException>(() => new InsercaoService(new RepositorioFalso(), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Criar(new RepositorioFalso()).ExecutarAsync(null!));
        Assert.Throws<ArgumentNullException>(() => InsercaoService.FormatarErroBanco(null!));
    }

    [Theory]
    [InlineData("XX000", true)]
    [InlineData("22023", true)]
    [InlineData("22P02", true)]
    [InlineData("22", true)]
    [InlineData("XX001", false)]
    [InlineData("42501", false)]
    [InlineData("23505", false)]
    [InlineData("28P01", false)]
    [InlineData("2", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void EhErroDeDadoNaValidacao(string? sqlState, bool esperado)
    {
        Assert.Equal(esperado, InsercaoService.EhErroDeDadoNaValidacao(sqlState));
    }

    [Theory]
    [InlineData(null, "msg", "msg")]
    [InlineData("23505", "dup\r\nkey", "SqlState 23505: dup??key")]
    [InlineData("4\n2", "m", "SqlState 4?2: m")]
    public void FormatarErroBanco(string? sqlState, string mensagem, string esperado)
    {
        Assert.Equal(esperado, InsercaoService.FormatarErroBanco(new FalhaBancoException(sqlState, mensagem)));
    }

    /// <summary>
    /// A07 de ponta a ponta: repositorio REAL contra servidor Postgres falso em 127.0.0.1.
    /// SqlStates de conexao/autenticacao -> ErroConexao no 1o item, demais NaoProcessado, codigo 3,
    /// e uma unica tentativa de login (sem rajada).
    /// </summary>
    [Theory]
    [InlineData("28P01")]
    [InlineData("28000")]
    [InlineData("08006")]
    [InlineData("08001")]
    [InlineData("3D000")]
    [InlineData("57P03")]
    [InlineData("53300")]
    public async Task SqlStateDeConexao_InterrompeItens_Codigo3(string sqlState)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErro(sqlState, "falha " + Ambiente.Sentinela, "DETALHE", "DICA");
        await using var repo = new MetryRepository(
            Ambiente.Config(
                (ConexaoConfig.VarHost, "127.0.0.1"),
                (ConexaoConfig.VarPorta, servidor.Porta.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (ConexaoConfig.VarSslMode, "Disable")),
            log);
        var servico = new InsercaoService(repo, new WktValidator(4326), log);

        var resultado = await servico.ExecutarAsync(["POINT(0 0)", "POINT(1 1)", "POINT(2 2)"]);

        Assert.Equal(CodigoSaida.FalhaBanco, resultado.Codigo);
        Assert.Equal(StatusItem.ErroConexao, resultado.Itens[0].Status);
        Assert.Equal($"PostgresException/{sqlState}", resultado.Itens[0].Erro);
        Assert.Equal(StatusItem.NaoProcessado, resultado.Itens[1].Status);
        Assert.Equal(StatusItem.NaoProcessado, resultado.Itens[2].Status);
        Assert.InRange(servidor.Conexoes, 1, 2);
        Assert.DoesNotContain(Ambiente.Sentinela, log.Tudo, StringComparison.Ordinal);
    }

    /// <summary>
    /// MF-2 de ponta a ponta: XX000/classe 22/42xxx na ABERTURA (erro no startup) e falha de conexao:
    /// item 1 ErroConexao, seguintes NaoProcessado, codigo 3, sem MessageText no log.
    /// </summary>
    [Theory]
    [InlineData("XX000")]
    [InlineData("22023")]
    [InlineData("42501")]
    [InlineData("42P01")]
    public async Task ErroDoServidorNaAbertura_ErroConexao_Codigo3_InterrompeItens(string sqlState)
    {
        var log = new LogCaptura();
        await using var servidor = ServidorPostgresFalso.ComErro(sqlState, "parse error usuario_secreto", "DETALHE", "DICA");
        await using var repo = CriarRepositorioLocal(servidor.Porta, log);
        var servico = new InsercaoService(repo, new WktValidator(4326), log);

        var resultado = await servico.ExecutarAsync(["POINT(0 0)", "POINT(1 1)", "POINT(2 2)"]);

        Assert.Equal(CodigoSaida.FalhaBanco, resultado.Codigo);
        Assert.Equal(StatusItem.ErroConexao, resultado.Itens[0].Status);
        Assert.Equal($"PostgresException/{sqlState}", resultado.Itens[0].Erro);
        Assert.All(resultado.Itens.Skip(1), i => Assert.Equal(StatusItem.NaoProcessado, i.Status));
        Assert.InRange(servidor.Conexoes, 1, 2);
        Assert.DoesNotContain("usuario_secreto", log.Tudo, StringComparison.Ordinal);
        Assert.DoesNotContain("parse error", log.Tudo, StringComparison.Ordinal);
    }

    /// <summary>
    /// Codigo 2 com repositorio REAL: XX000/classe 22 vindos da EXECUCAO da validacao (startup ok)
    /// sao erro de dado -> InvalidoBanco, e os itens seguintes continuam sendo processados.
    /// </summary>
    [Theory]
    [InlineData("XX000")]
    [InlineData("22023")]
    [InlineData("22P02")]
    public async Task ErroDeDadoNaExecucaoDaValidacao_Codigo2_ContinuaItens(string sqlState)
    {
        await using var servidor = ServidorPostgresFalso.ComErroNaExecucao(sqlState, "parse error", "DETALHE", "DICA");
        await using var repo = CriarRepositorioLocal(servidor.Porta);
        var servico = new InsercaoService(repo, new WktValidator(4326));

        var resultado = await servico.ExecutarAsync(["POINT(0 0)", "POINT(1 1)"]);

        Assert.Equal(CodigoSaida.WktInvalido, resultado.Codigo);
        Assert.All(resultado.Itens, i => Assert.Equal(StatusItem.InvalidoBanco, i.Status));
        Assert.All(resultado.Itens, i => Assert.Equal($"WKT rejeitado pelo PostGIS: SqlState {sqlState}: parse error", i.Erro));
        Assert.Equal(2, servidor.ComandosRecebidos.Count);
    }

    /// <summary>Outro SqlState na EXECUCAO (ex.: 42501) -> ErroBanco (codigo 3), sem interromper os itens.</summary>
    [Fact]
    public async Task ErroDeBancoNaExecucaoDaValidacao_Codigo3_ContinuaItens()
    {
        await using var servidor = ServidorPostgresFalso.ComErroNaExecucao("42501", "permission denied", "DETALHE", "DICA");
        await using var repo = CriarRepositorioLocal(servidor.Porta);
        var servico = new InsercaoService(repo, new WktValidator(4326));

        var resultado = await servico.ExecutarAsync(["POINT(0 0)", "POINT(1 1)"]);

        Assert.Equal(CodigoSaida.FalhaBanco, resultado.Codigo);
        Assert.All(resultado.Itens, i => Assert.Equal(StatusItem.ErroBanco, i.Status));
        Assert.All(resultado.Itens, i => Assert.Equal("SqlState 42501: permission denied", i.Erro));
    }

    /// <summary>Caminho feliz com repositorio REAL contra o servidor falso (validar, inserir e ler).</summary>
    [Theory]
    [InlineData(4326, 4326, StatusItem.Inserido, CodigoSaida.Ok)]
    [InlineData(3857, 4326, StatusItem.SridDivergente, CodigoSaida.WktInvalido)]
    public async Task RepositorioReal_FluxoCompleto(int sridConfigurado, int sridLido, StatusItem status, CodigoSaida codigo)
    {
        await using var servidor = ServidorPostgresFalso.ComLinhas(new(Id: 42, Wkt: "POINT(0 0)", Srid: sridLido));
        await using var repo = CriarRepositorioLocal(servidor.Porta);
        var servico = new InsercaoService(repo, new WktValidator(sridConfigurado));

        var resultado = await servico.ExecutarAsync(["POINT(0 0)"]);

        Assert.Equal(codigo, resultado.Codigo);
        Assert.Equal(status, resultado.Itens[0].Status);
        Assert.Equal(42, resultado.Itens[0].IdInserido);
        Assert.Equal(3, servidor.ComandosRecebidos.Count);
    }

    private static MetryRepository CriarRepositorioLocal(int porta, LogCaptura? log = null) =>
        new(
            Ambiente.Config(
                (ConexaoConfig.VarHost, "127.0.0.1"),
                (ConexaoConfig.VarPorta, porta.ToString(System.Globalization.CultureInfo.InvariantCulture)),
                (ConexaoConfig.VarSslMode, "Disable")),
            log);

    private const string TextoSeguro_EcoOmitido = TesteMetry.Comum.TextoSeguro.EcoOmitido;
}
