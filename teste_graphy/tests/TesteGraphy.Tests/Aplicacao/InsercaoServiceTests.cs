using TesteGraphy.Aplicacao;
using TesteGraphy.Comum;
using TesteGraphy.Dados;
using TesteGraphy.Tests.Fakes;
using TesteGraphy.Validacao;

namespace TesteGraphy.Tests.Aplicacao;

public class InsercaoServiceTests
{
    public enum Etapa
    {
        Validar,
        Inserir,
        Ler,
    }

    private const string Valido = "POINT(-46.6333 -23.5505)";

    private static InsercaoService Criar(RepositorioFalso repo, LogCaptura? log = null) =>
        new(repo, new WktValidator(log), log);

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(99)]
    [InlineData(InsercaoService.MaximoWktPorExecucao)]
    public async Task Executar_AteOLimite_InsereTodos(int quantidade)
    {
        var repo = new RepositorioFalso();
        var wkts = Enumerable.Repeat<string?>(Valido, quantidade).ToList();

        var resultado = await Criar(repo).ExecutarAsync(wkts);

        Assert.False(resultado.LimiteExcedido);
        Assert.Equal(CodigoSaida.Ok, resultado.Codigo);
        Assert.Equal(quantidade, resultado.Itens.Count);
        Assert.All(resultado.Itens, i => Assert.Equal(StatusItem.Inserido, i.Status));
        Assert.Equal(Enumerable.Range(1, quantidade), resultado.Itens.Select(i => i.Indice));
        Assert.All(resultado.Itens, i => Assert.Equal(InsercaoService.SridEsperado, i.Registro!.Srid));
        Assert.All(resultado.Itens, i => Assert.Equal(i.Registro!.Id, i.IdInserido));
        Assert.Equal(quantidade, repo.ChamadasInserir);
        Assert.Equal(quantidade, repo.ChamadasLer);
    }

    [Theory]
    [InlineData(InsercaoService.MaximoWktPorExecucao + 1)]
    [InlineData(500)]
    public async Task Executar_AcimaDoLimite_NaoChamaRepositorio(int quantidade)
    {
        var repo = new RepositorioFalso();
        var log = new LogCaptura();

        var resultado = await Criar(repo, log).ExecutarAsync(Enumerable.Repeat<string?>(Valido, quantidade).ToList());

        Assert.True(resultado.LimiteExcedido);
        Assert.Equal(CodigoSaida.WktInvalido, resultado.Codigo);
        Assert.Empty(resultado.Itens);
        Assert.Equal(0, repo.TotalChamadas);
        Assert.Single(log.Erros);
    }

    [Fact]
    public async Task Executar_ListaVazia_Ok()
    {
        var resultado = await Criar(new RepositorioFalso()).ExecutarAsync([]);

        Assert.Equal(CodigoSaida.Ok, resultado.Codigo);
        Assert.Empty(resultado.Itens);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("POINT(0 0")]
    [InlineData("CIRCULARSTRING(0 0, 1 1, 2 0)")]
    [InlineData("POINT(0 0)'); DROP TABLE public.mapas;--")]
    public async Task Executar_InvalidoLocal_NaoChamaRepositorio(string? wkt)
    {
        var repo = new RepositorioFalso();

        var resultado = await Criar(repo).ExecutarAsync([wkt]);

        var item = Assert.Single(resultado.Itens);
        Assert.Equal(StatusItem.InvalidoLocal, item.Status);
        Assert.NotNull(item.Erro);
        Assert.Equal(CodigoSaida.WktInvalido, resultado.Codigo);
        Assert.Equal(0, repo.TotalChamadas);
    }

    [Fact]
    public async Task Executar_StIsValidFalso_ImpedeInsert()
    {
        var repo = new RepositorioFalso { AoValidar = (_, _) => Task.FromResult(new ValidacaoBanco(false, "POLYGON")) };

        var resultado = await Criar(repo).ExecutarAsync([Valido]);

        var item = Assert.Single(resultado.Itens);
        Assert.Equal(StatusItem.InvalidoBanco, item.Status);
        Assert.Contains("ST_IsValid", item.Erro, StringComparison.Ordinal);
        Assert.Equal(CodigoSaida.WktInvalido, resultado.Codigo);
        Assert.Equal(1, repo.ChamadasValidar);
        Assert.Equal(0, repo.ChamadasInserir);
        Assert.Equal(0, repo.ChamadasLer);
    }

    [Fact]
    public async Task Executar_RegistroNaoEncontrado_ErroBancoComId()
    {
        var repo = new RepositorioFalso
        {
            AoInserir = (_, _) => Task.FromResult(42),
            AoLer = (_, _) => Task.FromResult<RegistroGraphy?>(null),
        };

        var resultado = await Criar(repo).ExecutarAsync([Valido]);

        var item = Assert.Single(resultado.Itens);
        Assert.Equal(StatusItem.ErroBanco, item.Status);
        Assert.Equal(42, item.IdInserido);
        Assert.Contains("id = 42", item.Erro, StringComparison.Ordinal);
        Assert.Equal(CodigoSaida.FalhaBanco, resultado.Codigo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4269)]
    [InlineData(3857)]
    public async Task Executar_SridDiferenteDe4326_ErroBancoComId(int srid)
    {
        var repo = new RepositorioFalso
        {
            AoInserir = (_, _) => Task.FromResult(7),
            AoLer = (id, _) => Task.FromResult<RegistroGraphy?>(new RegistroGraphy(id, Valido, srid)),
        };

        var resultado = await Criar(repo).ExecutarAsync([Valido]);

        var item = Assert.Single(resultado.Itens);
        Assert.Equal(StatusItem.ErroBanco, item.Status);
        Assert.Equal(7, item.IdInserido);
        Assert.NotNull(item.Registro);
        Assert.Contains($"SRID lido = {srid}", item.Erro, StringComparison.Ordinal);
        Assert.Equal(CodigoSaida.FalhaBanco, resultado.Codigo);
    }

    [Theory]
    [InlineData(Etapa.Validar, null)]
    [InlineData(Etapa.Inserir, null)]
    [InlineData(Etapa.Ler, 1)]
    public async Task Executar_FalhaBanco_MarcaErroEContinuaOsDemais(Etapa etapa, int? idEsperado)
    {
        var falhou = false;
        Task<T> FalharUmaVez<T>(Func<Task<T>> normal)
        {
            if (falhou)
            {
                return normal();
            }

            falhou = true;
            throw new FalhaBancoException("23505", "duplicate key\r\nvalue");
        }

        var repo = new RepositorioFalso();
        var padrao = new RepositorioFalso();
        switch (etapa)
        {
            case Etapa.Validar:
                repo.AoValidar = (w, ct) => FalharUmaVez(() => padrao.ValidarNoBancoAsync(w, ct));
                break;
            case Etapa.Inserir:
                repo.AoInserir = (w, ct) => FalharUmaVez(() => Task.FromResult(99));
                repo.AoLer = (id, _) => Task.FromResult<RegistroGraphy?>(new RegistroGraphy(id, Valido, 4326));
                break;
            default:
                repo.AoInserir = (_, _) => Task.FromResult(1);
                repo.AoLer = (id, ct) => FalharUmaVez(() => Task.FromResult<RegistroGraphy?>(new RegistroGraphy(id, Valido, 4326)));
                break;
        }

        var resultado = await Criar(repo).ExecutarAsync([Valido, Valido]);

        Assert.Equal(StatusItem.ErroBanco, resultado.Itens[0].Status);
        Assert.Equal(idEsperado, resultado.Itens[0].IdInserido);
        Assert.Equal("SqlState 23505: duplicate key??value", resultado.Itens[0].Erro);
        Assert.Equal(StatusItem.Inserido, resultado.Itens[1].Status);
        Assert.Equal(CodigoSaida.FalhaBanco, resultado.Codigo);
    }

    [Theory]
    [InlineData(Etapa.Validar, 3)]
    [InlineData(Etapa.Inserir, 3)]
    [InlineData(Etapa.Ler, 3)]
    [InlineData(Etapa.Validar, 100)]
    public async Task Executar_FalhaConexao_InterrompeEMarcaSeguintesComoNaoProcessado(Etapa etapa, int quantidade)
    {
        var repo = new RepositorioFalso();
        Func<Task> lancar = () => throw new FalhaConexaoException("SocketException");
        switch (etapa)
        {
            case Etapa.Validar:
                repo.AoValidar = async (_, _) => { await lancar(); return null!; };
                break;
            case Etapa.Inserir:
                repo.AoInserir = async (_, _) => { await lancar(); return 0; };
                break;
            default:
                repo.AoLer = async (_, _) => { await lancar(); return null; };
                break;
        }

        var resultado = await Criar(repo).ExecutarAsync(Enumerable.Repeat<string?>(Valido, quantidade).ToList());

        Assert.Equal(StatusItem.ErroConexao, resultado.Itens[0].Status);
        Assert.Equal("SocketException", resultado.Itens[0].Erro);
        Assert.All(resultado.Itens.Skip(1), i => Assert.Equal(StatusItem.NaoProcessado, i.Status));
        Assert.Equal(quantidade, resultado.Itens.Count);
        Assert.Equal(CodigoSaida.FalhaBanco, resultado.Codigo);
        var tentativas = etapa switch
        {
            Etapa.Validar => repo.ChamadasValidar,
            Etapa.Inserir => repo.ChamadasInserir,
            _ => repo.ChamadasLer,
        };
        Assert.Equal(1, tentativas);
        Assert.Equal(1, repo.ChamadasValidar);
    }

    [Fact]
    public async Task Executar_FalhaConexaoNoSegundo_PrimeiroInseridoTerceiroNaoProcessado()
    {
        var chamadas = 0;
        var repo = new RepositorioFalso();
        var padrao = new RepositorioFalso();
        repo.AoValidar = (w, ct) => ++chamadas == 2
            ? throw new FalhaConexaoException("PostgresException/28P01")
            : padrao.ValidarNoBancoAsync(w, ct);

        var resultado = await Criar(repo).ExecutarAsync([Valido, Valido, Valido]);

        Assert.Equal(
            [StatusItem.Inserido, StatusItem.ErroConexao, StatusItem.NaoProcessado],
            resultado.Itens.Select(i => i.Status));
        Assert.Equal(2, repo.ChamadasValidar);
    }

    [Theory]
    [InlineData(new[] { StatusItem.Inserido, StatusItem.InvalidoLocal }, CodigoSaida.WktInvalido)]
    [InlineData(new[] { StatusItem.InvalidoLocal, StatusItem.InvalidoBanco, StatusItem.Inserido }, CodigoSaida.WktInvalido)]
    [InlineData(new[] { StatusItem.Inserido, StatusItem.ErroBanco, StatusItem.InvalidoLocal }, CodigoSaida.FalhaBanco)]
    [InlineData(new[] { StatusItem.Inserido, StatusItem.Inserido }, CodigoSaida.Ok)]
    [InlineData(new[] { StatusItem.InvalidoBanco }, CodigoSaida.WktInvalido)]
    public async Task Executar_Mistura_CalculaCodigo(StatusItem[] cenarios, CodigoSaida codigo)
    {
        const string invalidoBanco = "POLYGON((0 0, 1 1, 1 0, 0 1, 0 0))";
        const string erroBanco = "POINT(1 1)";
        var repo = new RepositorioFalso
        {
            AoValidar = (w, _) => Task.FromResult(new ValidacaoBanco(w != invalidoBanco, null)),
        };
        repo.AoLer = (id, _) => Task.FromResult<RegistroGraphy?>(new RegistroGraphy(id, "x", 4326));
        repo.AoInserir = (w, _) => w == erroBanco ? throw new FalhaBancoException("XX000", "falha") : Task.FromResult(5);

        var wkts = cenarios.Select(s => s switch
        {
            StatusItem.InvalidoLocal => "POINT(0 0",
            StatusItem.InvalidoBanco => invalidoBanco,
            StatusItem.ErroBanco => erroBanco,
            _ => Valido,
        }).ToList<string?>();

        var resultado = await Criar(repo).ExecutarAsync(wkts);

        Assert.Equal(cenarios, resultado.Itens.Select(i => i.Status));
        Assert.Equal(codigo, resultado.Codigo);
    }

    [Theory]
    [InlineData("Host=x;Password=S3nh4-Sentinela-XYZ", TextoSeguro.EcoOmitido)]
    [InlineData("POINT(0 0\r\n\u001b[2J", "POINT(0 0???[2J")]
    public async Task Executar_EcoDoWkt_SanitizadoOuOmitido(string wkt, string eco)
    {
        var resultado = await Criar(new RepositorioFalso()).ExecutarAsync([wkt]);

        Assert.Equal(eco, resultado.Itens[0].WktEcoado);
        Assert.DoesNotContain(Ambiente.Sentinela, resultado.Itens[0].Erro ?? string.Empty, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Executar_LogNaoContemSentinelaNemWktComCredencial()
    {
        var log = new LogCaptura();
        var repo = new RepositorioFalso();

        await Criar(repo, log).ExecutarAsync([Valido, "pwd=" + Ambiente.Sentinela, "POINT(0 0"]);

        Assert.DoesNotContain(Ambiente.Sentinela, log.Tudo, StringComparison.Ordinal);
        Assert.Contains(log.Passos, p => p.Contains("inserido e conferido", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Executar_Cancelado_PropagaOperationCanceled()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Criar(new RepositorioFalso()).ExecutarAsync([Valido], cts.Token));
    }

    [Fact]
    public async Task Construtor_E_Executar_ArgumentosNulos()
    {
        Assert.Throws<ArgumentNullException>(() => new InsercaoService(null!, new WktValidator()));
        Assert.Throws<ArgumentNullException>(() => new InsercaoService(new RepositorioFalso(), null!));
        await Assert.ThrowsAsync<ArgumentNullException>(() => Criar(new RepositorioFalso()).ExecutarAsync(null!));
        Assert.Throws<ArgumentNullException>(() => InsercaoService.FormatarErroBanco(null!));
    }

    [Theory]
    [InlineData(null, "mensagem", "mensagem")]
    [InlineData("23505", "duplicate key", "SqlState 23505: duplicate key")]
    [InlineData("2\n3", "a\r\nb\u001bc", "SqlState 2?3: a??b?c")]
    [InlineData("XX000", "linha1\u2028linha2\u0085", "SqlState XX000: linha1?linha2?")]
    [InlineData("22023", "", "SqlState 22023: ")]
    public void FormatarErroBanco_SoSqlStateEMensagemSanitizados(string? sqlState, string mensagem, string esperado)
    {
        Assert.Equal(esperado, InsercaoService.FormatarErroBanco(new FalhaBancoException(sqlState, mensagem)));
    }

    [Fact]
    public void FormatarErroBanco_MensagemLonga_Truncada()
    {
        var formatado = InsercaoService.FormatarErroBanco(new FalhaBancoException(null, new string('m', 1000)));

        Assert.Equal(TextoSeguro.TamanhoMaximoEco + TextoSeguro.Reticencias.Length, formatado.Length);
    }

    [Fact]
    public void FormatarErroBanco_DePostgresException_NaoExibeDetailNemHint()
    {
        var pg = new Npgsql.PostgresException("msg do servidor", "ERROR", "ERROR", "23505", detail: "DETALHE-" + Ambiente.Sentinela, hint: "DICA-SECRETA");
        var traduzida = new FalhaBancoException(pg.SqlState, pg.MessageText);

        var formatado = InsercaoService.FormatarErroBanco(traduzida);

        Assert.Equal("SqlState 23505: msg do servidor", formatado);
        Assert.DoesNotContain("DETALHE", formatado, StringComparison.Ordinal);
        Assert.DoesNotContain("DICA", formatado, StringComparison.Ordinal);
        Assert.DoesNotContain(Ambiente.Sentinela, traduzida.ToString(), StringComparison.Ordinal);
    }
}
