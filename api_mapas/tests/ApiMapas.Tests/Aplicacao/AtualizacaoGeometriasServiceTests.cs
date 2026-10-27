using System.Text;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Tests.Aplicacao;

/// <summary>
/// P6.2 - AtualizacaoGeometriasService com repositorio e leitor falsos: todos os ramos de
/// ResultadoAtualizacao, ordem do contrato, RP-11 (JSON estrito), RP-13 (auditoria/Warning sem WKT),
/// cancelamento (499) e prazo de 30 s (D-P11).
/// </summary>
public class AtualizacaoGeometriasServiceTests
{
    private const string TraceId = "00-trace-teste-01";

    private static (AtualizacaoGeometriasService Servico, LoggerMemoria<AtualizacaoGeometriasService> Log) Criar(
        IMapasEscritaRepository repositorio, TimeSpan? prazo = null)
    {
        var log = new LoggerMemoria<AtualizacaoGeometriasService>();
        var opcoes = new OpcoesAtualizacao();
        if (prazo is not null)
        {
            opcoes.PrazoTotal = prazo.Value;
        }

        return (new AtualizacaoGeometriasService(repositorio, opcoes, log), log);
    }

    private static void AssertLogSemWkt(LoggerMemoria<AtualizacaoGeometriasService> log, params string[] proibidos)
    {
        var texto = log.TextoCompleto;
        foreach (var termo in proibidos.Concat(new[] { "POINT", "LINESTRING", "POLYGON", "JsonException", "wkt\":" }))
        {
            Assert.DoesNotContain(termo, texto, StringComparison.OrdinalIgnoreCase);
        }

        Assert.All(log.Entradas, e => Assert.Null(e.Erro));
    }

    // ---------------- Construtor ----------------

    [Fact]
    public void Construtor_ArgumentosNulos_Lanca()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var log = new LoggerMemoria<AtualizacaoGeometriasService>();
        Assert.Throws<ArgumentNullException>(() => new AtualizacaoGeometriasService(null!, new OpcoesAtualizacao(), log));
        Assert.Throws<ArgumentNullException>(() => new AtualizacaoGeometriasService(repo, null!, log));
        Assert.Throws<ArgumentNullException>(() => new AtualizacaoGeometriasService(repo, new OpcoesAtualizacao(), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Construtor_PrazoNaoPositivo_Lanca(int segundos)
    {
        var opcoes = new OpcoesAtualizacao { PrazoTotal = TimeSpan.FromSeconds(segundos) };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new AtualizacaoGeometriasService(RepositorioEscritaFalso.Sucesso(), opcoes, new LoggerMemoria<AtualizacaoGeometriasService>()));
    }

    [Fact]
    public async Task Atualizar_LeitorNulo_Lanca()
    {
        var (servico, _) = Criar(RepositorioEscritaFalso.Sucesso());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            servico.AtualizarAsync("1", ColunaMapa.Graphy, null!, TraceId, CancellationToken.None));
    }

    [Fact]
    public void Opcoes_Padroes()
    {
        var opcoes = new OpcoesAtualizacao();
        Assert.Equal(65_536, opcoes.LimiteBytesCorpo);
        Assert.Equal(TimeSpan.FromSeconds(30), opcoes.PrazoTotal);
        Assert.Equal(3, OpcoesAtualizacao.EscritasSimultaneas);
        Assert.Equal(10, OpcoesAtualizacao.LimiteEscritasPorMinutoPadrao);
        Assert.Equal(60, OpcoesAtualizacao.LimiteEscritasPorMinutoMaximo);
        Assert.Equal(1_048_576, OpcoesAtualizacao.LimiteBytesCorpoMaximo);
    }

    // ---------------- 2. Id (sem ler o corpo, sem banco) ----------------

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("abc")]
    [InlineData("2147483648")]
    [InlineData("1 ")]
    [InlineData("١")]
    public async Task IdInvalido_NaoLeCorpo_NaoTocaBanco(string? id)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var leitor = LeitorCorpoFalso.Json(CorposPut.Valido);
        var (servico, log) = Criar(repo);

        var r = await servico.AtualizarAsync(id, ColunaMapa.Graphy, leitor, TraceId, CancellationToken.None);

        Assert.Same(ResultadoAtualizacao.IdInvalido.Instancia, r);
        Assert.Equal(0, leitor.Leituras);
        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4102 && e.Nivel == LogLevel.Information);
    }

    // ---------------- 3/4. Content-Type, tamanho e leitura interrompida ----------------

    public static TheoryData<LeituraCorpo, ResultadoAtualizacao, int> Leituras => new()
    {
        { LeituraCorpo.TipoMidiaNaoSuportado.Instancia, ResultadoAtualizacao.TipoMidiaNaoSuportado.Instancia, 4104 },
        { new LeituraCorpo.MuitoGrande(1024), new ResultadoAtualizacao.CorpoMuitoGrande(1024), 4105 },
        { new LeituraCorpo.Invalida(400), ResultadoAtualizacao.CorpoInvalido.Instancia, 4115 },
        { new LeituraCorpo.Invalida(408), ResultadoAtualizacao.CorpoInvalido.Instancia, 4115 },
    };

    [Theory]
    [MemberData(nameof(Leituras))]
    public async Task LeituraRecusada_NaoTocaBanco(LeituraCorpo leitura, ResultadoAtualizacao esperado, int evento)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, log) = Criar(repo);

        var r = await servico.AtualizarAsync("10", ColunaMapa.Metry, new LeitorCorpoFalso(leitura), TraceId, CancellationToken.None);

        Assert.Equal(esperado, r);
        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == evento);
        AssertLogSemWkt(log);
    }

    // ---------------- 5. JSON (RP-11) ----------------

    public static TheoryData<string> CorposInvalidos => new()
    {
        string.Empty,
        " ",
        "{",
        "null",
        "[]",
        "\"wkt\"",
        "123",
        "true",
        "{}",
        "{\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\"}",
        "{\"wkt\":null,\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":null}",
        "{\"wkt\":123,\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":\"4326\"}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326.0}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326.5}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4.326e3}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":99999999999}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":true}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,\"extra\":1}",
        "{\"wkt\":\"POINT(0 0)\",\"wkt\":\"POINT(1 1)\",\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,\"srid\":4326}",
        "{\"WKT\":\"POINT(0 0)\",\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"Srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326/*c*/}",
        "// c\n{\"wkt\":\"POINT(0 0)\",\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326} {}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326}x",
        "{'wkt':'POINT(0 0)','srid':4326}",
        "{\"geometrias\":[{\"wkt\":\"POINT(0 0)\",\"srid\":4326}]}",
        "[{\"wkt\":\"POINT(0 0)\",\"srid\":4326}]",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,\"x\":{\"a\":{\"b\":{\"c\":{\"d\":{\"e\":{\"f\":{\"g\":1}}}}}}}}",
    };

    [Theory]
    [MemberData(nameof(CorposInvalidos))]
    public async Task CorpoInvalido_400_SemBanco_SemJsonExceptionNoLog(string json)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, log) = Criar(repo);

        var r = await servico.AtualizarAsync("7", ColunaMapa.Graphy, LeitorCorpoFalso.Json(json), TraceId, CancellationToken.None);

        Assert.Same(ResultadoAtualizacao.CorpoInvalido.Instancia, r);
        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4106);
        AssertLogSemWkt(log);
    }

    [Fact]
    public async Task CorpoInvalido_Utf8Invalido_400()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, _) = Criar(repo);
        var bytes = Encoding.UTF8.GetBytes("{\"wkt\":\"POINT(0 0)X\",\"srid\":4326}");
        bytes[Array.IndexOf(bytes, (byte)'X')] = 0xFF;

        var r = await servico.AtualizarAsync("7", ColunaMapa.Graphy, new LeitorCorpoFalso(new LeituraCorpo.Lido(bytes)), TraceId, CancellationToken.None);

        Assert.Same(ResultadoAtualizacao.CorpoInvalido.Instancia, r);
        Assert.Empty(repo.Chamadas);
    }

    [Theory]
    [InlineData("{\"wkt\":\"POINT(1 2)\",\"srid\":4326}", 4326)]
    [InlineData("  { \"srid\" : 3857 , \"wkt\" : \"POINT(1 2)\" }  ", 3857)]
    public void TentarDesserializar_Valido(string json, int srid)
    {
        Assert.True(EntradaGeometria.TentarDesserializar(Encoding.UTF8.GetBytes(json), out var corpo));
        Assert.Equal("POINT(1 2)", corpo!.Wkt);
        Assert.Equal(srid, corpo.Srid);
    }

    [Fact]
    public void OpcoesJson_ConformeRp11()
    {
        var o = EntradaGeometria.OpcoesJson;
        Assert.False(o.PropertyNameCaseInsensitive);
        Assert.Equal(8, o.MaxDepth);
        Assert.Equal(System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow, o.UnmappedMemberHandling);
        Assert.False(o.AllowDuplicateProperties);
        Assert.Equal(System.Text.Json.Serialization.JsonNumberHandling.Strict, o.NumberHandling);
        Assert.False(o.AllowTrailingCommas);
        Assert.Equal(System.Text.Json.JsonCommentHandling.Disallow, o.ReadCommentHandling);
    }

    // ---------------- 6. Geometria (sem indice: o corpo tem uma geometria so) ----------------

    public static TheoryData<ColunaMapa, string, string> ItensInvalidos => new()
    {
        { ColunaMapa.Graphy, CorposPut.Com(("POINT(0 0)", 3857)), "srid não permitido para a coluna 'graphy' (aceitos: 4326)" },
        { ColunaMapa.Graphy, CorposPut.Com(("POINT(181 0)", 4326)), "longitude fora da faixa [-180, 180] do SRID 4326" },
        { ColunaMapa.Graphy, CorposPut.Com(("POINT(0 -90.5)", 4326)), "latitude fora da faixa" },
        { ColunaMapa.Graphy, CorposPut.Com(("", 4326)), "WKT nulo ou vazio" },
        { ColunaMapa.Graphy, CorposPut.Com(("SRID=4326;POINT(0 0)", 4326)), "prefixo SRID= (EWKT) não permitido" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT(0 0)", 0)), "srid não permitido para a coluna 'metry' (aceitos: 4326, 4674, 3857, 31983)" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT(-1 0)", 31983)), "E fora da faixa [0, 1000000] do SRID 31983" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT Z (0 0 0)", 4326)), "apenas geometrias 2D" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT EMPTY", 4326)), "geometria EMPTY não é permitida" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT(0 0)" + new string(' ', 9_991), 4326)), "WKT excede o limite de 10000 caracteres" },
    };

    [Theory]
    [MemberData(nameof(ItensInvalidos))]
    public async Task GeometriaInvalida_SemBanco(ColunaMapa coluna, string json, string trecho)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, log) = Criar(repo);

        var r = await servico.AtualizarAsync("5", coluna, LeitorCorpoFalso.Json(json), TraceId, CancellationToken.None);

        var invalida = Assert.IsType<ResultadoAtualizacao.GeometriaInvalida>(r);
        Assert.Contains(trecho, invalida.Motivo, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4107);
        AssertLogSemWkt(log);
    }

    // ---------------- 7. Banco (resultados do repositorio) ----------------

    [Theory]
    [InlineData(ColunaMapa.Graphy, "graphy")]
    [InlineData(ColunaMapa.Metry, "metry")]
    public async Task Sucesso_PassaGeometria_AuditoriaInformation(ColunaMapa coluna, string nome)
    {
        var repo = new RepositorioEscritaFalso(ResultadoEscrita.Atualizado.Instancia);
        var (servico, log) = Criar(repo);
        var json = CorposPut.Com(("  " + CorposPut.LinhaGraphy + " ", 4326));

        var r = await servico.AtualizarAsync("2147483647", coluna, LeitorCorpoFalso.Json(json), TraceId, CancellationToken.None);

        Assert.Equal(new ResultadoAtualizacao.Sucesso(2147483647, nome), r);
        var chamada = Assert.Single(repo.Chamadas);
        Assert.Equal((2147483647, coluna), (chamada.Id!.Value.Valor, chamada.Coluna));
        Assert.Equal(CorposPut.LinhaGraphy, chamada.Geometria.Wkt);
        Assert.Equal("LINESTRING", chamada.Geometria.Tipo);

        var auditoria = Assert.Single(log.Entradas, e => e.Evento.Id == 4110);
        Assert.Equal(LogLevel.Information, auditoria.Nivel);
        Assert.Equal($"Auditoria PUT: id 2147483647, coluna {nome} atualizada (traceId {TraceId}).", auditoria.Mensagem);
        Assert.DoesNotContain(log.Entradas, e => e.Nivel >= LogLevel.Warning);
        AssertLogSemWkt(log, "-46.6", "-23.5");
    }

    public static TheoryData<ResultadoEscrita, ResultadoAtualizacao, int> ResultadosBanco => new()
    {
        { new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.InvalidaPostgis), new ResultadoAtualizacao.GeometriaInvalida(MotivosGeometria.InvalidaPostgis), 4107 },
        { new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.TipoDiverge), new ResultadoAtualizacao.GeometriaInvalida(MotivosGeometria.TipoDiverge), 4107 },
        { ResultadoEscrita.NaoEncontrado.Instancia, new ResultadoAtualizacao.NaoEncontrado(42, "graphy"), 4111 },
        { ResultadoEscrita.Conflito.Instancia, new ResultadoAtualizacao.Conflito(42, "graphy"), 4112 },
    };

    [Theory]
    [MemberData(nameof(ResultadosBanco))]
    public async Task ResultadosDoBanco_Traduzidos(ResultadoEscrita doBanco, ResultadoAtualizacao esperado, int evento)
    {
        var (servico, log) = Criar(new RepositorioEscritaFalso(doBanco));

        var r = await servico.AtualizarAsync("42", ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None);

        Assert.Equal(esperado, r);
        Assert.Contains(log.Entradas, e => e.Evento.Id == evento);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 4110);
        AssertLogSemWkt(log);
    }

    [Fact]
    public async Task ResultadoDesconhecido_InvalidOperation()
    {
        var (servico, _) = Criar(new RepositorioEscritaFalso((_, _) => Task.FromResult<ResultadoEscrita>(null!)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servico.AtualizarAsync("1", ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None));
    }

    [Fact]
    public async Task TraceIdNulo_AuditoriaComTextoVazio()
    {
        var (servico, log) = Criar(RepositorioEscritaFalso.Sucesso());

        var r = await servico.AtualizarAsync("1", ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), null!, CancellationToken.None);

        Assert.IsType<ResultadoAtualizacao.Sucesso>(r);
        Assert.EndsWith("(traceId ).", Assert.Single(log.Entradas, e => e.Evento.Id == 4110).Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExcecaoDoRepositorio_Propaga()
    {
        var erro = new InvalidOperationException("falha");
        var (servico, _) = Criar(RepositorioEscritaFalso.QueLanca(erro));

        var lancada = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servico.AtualizarAsync("1", ColunaMapa.Metry, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None));
        Assert.Same(erro, lancada);
    }

    // ---------------- Cancelamento (499) e prazo (D-P11) ----------------

    [Fact]
    public async Task ClienteCancela_DuranteBanco_PropagaOperationCanceled_E_Loga4113()
    {
        var repo = RepositorioEscritaFalso.Pendurado();
        var (servico, log) = Criar(repo);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            servico.AtualizarAsync("3", ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, cts.Token));

        Assert.Single(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4113);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 4114);
    }

    [Fact]
    public async Task ClienteCancela_DuranteLeitura_PropagaOperationCanceled_SemBanco()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, log) = Criar(repo);
        using var cts = new CancellationTokenSource();
        var leitor = new LeitorCorpoFalso(async ct =>
        {
            await cts.CancelAsync();
            ct.ThrowIfCancellationRequested();
            return new LeituraCorpo.Lido(Array.Empty<byte>());
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            servico.AtualizarAsync("3", ColunaMapa.Graphy, leitor, TraceId, cts.Token));

        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4113);
    }

    [Fact]
    public async Task PrazoEstourado_NoBanco_PrazoAtualizacaoEstouradoException()
    {
        var repo = RepositorioEscritaFalso.Pendurado();
        var (servico, log) = Criar(repo, TimeSpan.FromMilliseconds(150));

        var ex = await Assert.ThrowsAsync<PrazoAtualizacaoEstouradoException>(() =>
            servico.AtualizarAsync("3", ColunaMapa.Metry, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None));

        Assert.IsAssignableFrom<TimeoutException>(ex);
        Assert.Null(ex.InnerException);
        Assert.Contains("id 3, coluna metry", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("POINT", ex.Message, StringComparison.Ordinal);
        var erro = Assert.Single(log.Entradas, e => e.Evento.Id == 4114);
        Assert.Equal(LogLevel.Error, erro.Nivel);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 4113);
    }

    [Fact]
    public async Task PrazoEstourado_NaLeitura_PrazoAtualizacaoEstouradoException_SemBanco()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, _) = Criar(repo, TimeSpan.FromMilliseconds(100));
        var leitor = new LeitorCorpoFalso(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return LeituraCorpo.TipoMidiaNaoSuportado.Instancia;
        });

        await Assert.ThrowsAsync<PrazoAtualizacaoEstouradoException>(() =>
            servico.AtualizarAsync("3", ColunaMapa.Graphy, leitor, TraceId, CancellationToken.None));
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task Prazo_TokenRepassadoAoRepositorio_LigadoAoDoCliente()
    {
        CancellationToken recebido = default;
        var repo = new RepositorioEscritaFalso((_, ct) =>
        {
            recebido = ct;
            return Task.FromResult<ResultadoEscrita>(ResultadoEscrita.Atualizado.Instancia);
        });
        var (servico, _) = Criar(repo);
        using var cts = new CancellationTokenSource();

        await servico.AtualizarAsync("3", ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, cts.Token);

        Assert.True(recebido.CanBeCanceled);
        Assert.NotEqual(cts.Token, recebido);
    }

    [Fact]
    public void PrazoAtualizacaoEstouradoException_Construtores()
    {
        Assert.Equal("Prazo total da escrita (PUT e POST) estourado.", new PrazoAtualizacaoEstouradoException().Message);
        var interna = new InvalidOperationException();
        var ex = new PrazoAtualizacaoEstouradoException("m", interna);
        Assert.Same(interna, ex.InnerException);
    }

    [Fact]
    public void ResultadoAtualizacao_Singletons()
    {
        Assert.Same(ResultadoAtualizacao.IdInvalido.Instancia, ResultadoAtualizacao.IdInvalido.Instancia);
        Assert.NotEqual<ResultadoAtualizacao>(ResultadoAtualizacao.CorpoInvalido.Instancia, ResultadoAtualizacao.TipoMidiaNaoSuportado.Instancia);
        Assert.Equal(new LeituraCorpo.MuitoGrande(5), new LeituraCorpo.MuitoGrande(5));
    }
}
