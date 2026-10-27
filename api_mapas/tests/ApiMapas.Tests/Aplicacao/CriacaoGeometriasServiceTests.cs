using System.Text;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Tests.Aplicacao;

/// <summary>
/// PO6.2 - CriacaoGeometriasService (POST /api/mapas/graphy|metry, D-N5) com repositorio e leitor
/// falsos: todos os ramos de ResultadoCriacao, ordem do contrato (415 -> 413 -> JSON -> geometria ->
/// banco), EventIds 42xx (RS-4), auditoria sem WKT, cancelamento (499) e prazo de 30 s.
/// </summary>
public class CriacaoGeometriasServiceTests
{
    private const string TraceId = "00-trace-post-01";

    private static (CriacaoGeometriasService Servico, LoggerMemoria<CriacaoGeometriasService> Log) Criar(
        IMapasEscritaRepository repositorio, TimeSpan? prazo = null)
    {
        var log = new LoggerMemoria<CriacaoGeometriasService>();
        var opcoes = new OpcoesAtualizacao();
        if (prazo is not null)
        {
            opcoes.PrazoTotal = prazo.Value;
        }

        return (new CriacaoGeometriasService(repositorio, opcoes, log), log);
    }

    private static void AssertLogSemWkt(LoggerMemoria<CriacaoGeometriasService> log, params string[] proibidos)
    {
        var texto = log.TextoCompleto;
        foreach (var termo in proibidos.Concat(new[] { "POINT", "LINESTRING", "POLYGON", "JsonException", "wkt\":", "SEGREDO" }))
        {
            Assert.DoesNotContain(termo, texto, StringComparison.OrdinalIgnoreCase);
        }

        Assert.All(log.Entradas, e => Assert.Null(e.Erro));
        Assert.All(log.Entradas, e => Assert.InRange(e.Evento.Id, 4201, 4212));
    }

    // ---------------- Construtor ----------------

    [Fact]
    public void Construtor_ArgumentosNulos_Lanca()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var log = new LoggerMemoria<CriacaoGeometriasService>();
        Assert.Throws<ArgumentNullException>(() => new CriacaoGeometriasService(null!, new OpcoesAtualizacao(), log));
        Assert.Throws<ArgumentNullException>(() => new CriacaoGeometriasService(repo, null!, log));
        Assert.Throws<ArgumentNullException>(() => new CriacaoGeometriasService(repo, new OpcoesAtualizacao(), null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(-3600)]
    public void Construtor_PrazoNaoPositivo_Lanca(int segundos)
    {
        var opcoes = new OpcoesAtualizacao { PrazoTotal = TimeSpan.FromSeconds(segundos) };
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new CriacaoGeometriasService(RepositorioEscritaFalso.Sucesso(), opcoes, new LoggerMemoria<CriacaoGeometriasService>()));
    }

    [Fact]
    public async Task Criar_LeitorNulo_Lanca()
    {
        var (servico, _) = Criar(RepositorioEscritaFalso.Sucesso());
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            servico.CriarAsync(ColunaMapa.Graphy, null!, TraceId, CancellationToken.None));
    }

    // ---------------- 415 / 413 / leitura interrompida (sem banco) ----------------

    public static TheoryData<LeituraCorpo, ResultadoCriacao, int> Leituras => new()
    {
        { LeituraCorpo.TipoMidiaNaoSuportado.Instancia, ResultadoCriacao.TipoMidiaNaoSuportado.Instancia, 4202 },
        { new LeituraCorpo.MuitoGrande(65_536), new ResultadoCriacao.CorpoMuitoGrande(65_536), 4203 },
        { new LeituraCorpo.MuitoGrande(1), new ResultadoCriacao.CorpoMuitoGrande(1), 4203 },
        { new LeituraCorpo.Invalida(400), ResultadoCriacao.CorpoInvalido.Instancia, 4211 },
        { new LeituraCorpo.Invalida(408), ResultadoCriacao.CorpoInvalido.Instancia, 4211 },
    };

    [Theory]
    [MemberData(nameof(Leituras))]
    public async Task LeituraRecusada_NaoTocaBanco(LeituraCorpo leitura, ResultadoCriacao esperado, int evento)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, log) = Criar(repo);

        var r = await servico.CriarAsync(ColunaMapa.Metry, new LeitorCorpoFalso(leitura), TraceId, CancellationToken.None);

        Assert.Equal(esperado, r);
        Assert.Empty(repo.Chamadas);
        var entrada = Assert.Single(log.Entradas, e => e.Evento.Id == evento);
        Assert.Equal(LogLevel.Information, entrada.Nivel);
        Assert.Contains("POST", entrada.Mensagem, StringComparison.Ordinal);
        AssertLogSemWkt(log);
    }

    [Fact]
    public async Task LeituraNula_InvalidOperation_SemBanco()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, _) = Criar(repo);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servico.CriarAsync(ColunaMapa.Graphy, new LeitorCorpoFalso((LeituraCorpo)null!), TraceId, CancellationToken.None));
        Assert.Empty(repo.Chamadas);
    }

    // ---------------- 400 Corpo inválido (RP-11) ----------------

    public static TheoryData<string> CorposInvalidos => new()
    {
        string.Empty,
        "{",
        "null",
        "[]",
        "{}",
        "{\"wkt\":\"POINT(0 0)\"}",
        "{\"srid\":4326}",
        "{\"wkt\":null,\"srid\":4326}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":\"4326\"}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,\"id\":7}",
        "{\"wkt\":\"POINT(0 0)\",\"srid\":4326,\"extra\":\"SEGREDO\"}",
        "{\"wkt\":\"POINT(0 0)\",\"wkt\":\"POINT(1 1)\",\"srid\":4326}",
        "{\"WKT\":\"POINT(0 0)\",\"srid\":4326}",
        "[{\"wkt\":\"POINT(0 0)\",\"srid\":4326}]",
        "{\"geometrias\":[{\"wkt\":\"POINT(0 0)\",\"srid\":4326}]}",
    };

    [Theory]
    [MemberData(nameof(CorposInvalidos))]
    public async Task CorpoInvalido_400_SemBanco(string json)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, log) = Criar(repo);

        var r = await servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(json), TraceId, CancellationToken.None);

        Assert.Same(ResultadoCriacao.CorpoInvalido.Instancia, r);
        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4204);
        AssertLogSemWkt(log);
    }

    // ---------------- 400 Geometria inválida (local) ----------------

    public static TheoryData<ColunaMapa, string, string> GeometriasInvalidas => new()
    {
        { ColunaMapa.Graphy, CorposPut.Com(("POINT(0 0)", 3857)), "srid não permitido para a coluna 'graphy' (aceitos: 4326)" },
        { ColunaMapa.Graphy, CorposPut.Com(("POINT(181 0)", 4326)), "longitude fora da faixa" },
        { ColunaMapa.Graphy, CorposPut.Com(("", 4326)), "WKT nulo ou vazio" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT(0 0)", 0)), "srid não permitido para a coluna 'metry'" },
        { ColunaMapa.Metry, CorposPut.Com(("SRID=4326;POINT(0 0)", 4326)), "prefixo SRID= (EWKT) não permitido" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT(0 0)'; DROP TABLE mapas; --", 4326)), "caractere não permitido" },
        { ColunaMapa.Metry, CorposPut.Com(("POINT(0 0)" + new string(' ', 9_991), 4326)), "WKT excede o limite de 10000 caracteres" },
    };

    [Theory]
    [MemberData(nameof(GeometriasInvalidas))]
    public async Task GeometriaInvalida_400_SemBanco(ColunaMapa coluna, string json, string trecho)
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, log) = Criar(repo);

        var r = await servico.CriarAsync(coluna, LeitorCorpoFalso.Json(json), TraceId, CancellationToken.None);

        var invalida = Assert.IsType<ResultadoCriacao.GeometriaInvalida>(r);
        Assert.Contains(trecho, invalida.Motivo, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4205);
        AssertLogSemWkt(log, "DROP");
    }

    // ---------------- Banco ----------------

    [Theory]
    [InlineData(ColunaMapa.Graphy, "graphy", 1)]
    [InlineData(ColunaMapa.Metry, "metry", 2147483647)]
    public async Task Sucesso_IdDoRepositorio_AuditoriaInformation(ColunaMapa coluna, string nome, int idGerado)
    {
        var repo = new RepositorioEscritaFalso(new ResultadoEscrita.Criado(idGerado));
        var (servico, log) = Criar(repo);

        var r = await servico.CriarAsync(coluna, LeitorCorpoFalso.Json(CorposPut.Com(("  " + CorposPut.LinhaGraphy + " ", 4326))), TraceId, CancellationToken.None);

        Assert.Equal(new ResultadoCriacao.Sucesso(idGerado, nome), r);
        var chamada = Assert.Single(repo.Chamadas);
        Assert.Equal("POST", chamada.Metodo);
        Assert.Null(chamada.Id);
        Assert.Equal(coluna, chamada.Coluna);
        Assert.Equal(CorposPut.LinhaGraphy, chamada.Geometria.Wkt);
        Assert.Equal("LINESTRING", chamada.Geometria.Tipo);

        var auditoria = Assert.Single(log.Entradas, e => e.Evento.Id == 4207);
        Assert.Equal(LogLevel.Information, auditoria.Nivel);
        Assert.Equal($"Auditoria POST: id {idGerado} criado com a coluna {nome} preenchida (traceId {TraceId}).", auditoria.Mensagem);
        Assert.DoesNotContain(log.Entradas, e => e.Nivel >= LogLevel.Warning);
        Assert.All(log.Entradas.Where(e => e.Evento.Id is 4201 or 4206), e => Assert.Equal(LogLevel.Debug, e.Nivel));
        AssertLogSemWkt(log, "-46.6", "-23.5");
    }

    public static TheoryData<ResultadoEscrita, ResultadoCriacao, int, LogLevel> ResultadosBanco => new()
    {
        { new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.InvalidaPostgis), new ResultadoCriacao.GeometriaInvalida(MotivosGeometria.InvalidaPostgis), 4205, LogLevel.Information },
        { new ResultadoEscrita.GeometriaInvalida(MotivosGeometria.TipoDiverge), new ResultadoCriacao.GeometriaInvalida(MotivosGeometria.TipoDiverge), 4205, LogLevel.Information },
        { ResultadoEscrita.Conflito.Instancia, new ResultadoCriacao.Conflito("graphy"), 4208, LogLevel.Warning },
    };

    [Theory]
    [MemberData(nameof(ResultadosBanco))]
    public async Task ResultadosDoBanco_Traduzidos(ResultadoEscrita doBanco, ResultadoCriacao esperado, int evento, LogLevel nivel)
    {
        var (servico, log) = Criar(new RepositorioEscritaFalso(doBanco));

        var r = await servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None);

        Assert.Equal(esperado, r);
        Assert.Contains(log.Entradas, e => e.Evento.Id == evento && e.Nivel == nivel);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 4207);
        AssertLogSemWkt(log);
    }

    public static TheoryData<ResultadoEscrita?, string> ResultadosInesperados => new()
    {
        { ResultadoEscrita.Atualizado.Instancia, "Atualizado" },
        { ResultadoEscrita.NaoEncontrado.Instancia, "NaoEncontrado" },
        { null, "null" },
    };

    [Theory]
    [MemberData(nameof(ResultadosInesperados))]
    public async Task ResultadoInesperado_InvalidOperation_LogError4212(ResultadoEscrita? doBanco, string tipo)
    {
        var (servico, log) = Criar(new RepositorioEscritaFalso((_, _) => Task.FromResult(doBanco!)));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servico.CriarAsync(ColunaMapa.Metry, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None));

        var erro = Assert.Single(log.Entradas, e => e.Evento.Id == 4212);
        Assert.Equal(LogLevel.Error, erro.Nivel);
        Assert.Contains($"({tipo})", erro.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TraceIdNulo_AuditoriaComTextoVazio()
    {
        var (servico, log) = Criar(RepositorioEscritaFalso.Sucesso());

        var r = await servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), null!, CancellationToken.None);

        Assert.Equal(new ResultadoCriacao.Sucesso(RepositorioEscritaFalso.IdCriado, "graphy"), r);
        Assert.EndsWith("(traceId ).", Assert.Single(log.Entradas, e => e.Evento.Id == 4207).Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ExcecaoDoRepositorio_Propaga_SemRetry()
    {
        var erro = new InvalidOperationException("falha 23505");
        var repo = RepositorioEscritaFalso.QueLanca(erro);
        var (servico, _) = Criar(repo);

        var lancada = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servico.CriarAsync(ColunaMapa.Metry, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None));

        Assert.Same(erro, lancada);
        Assert.Single(repo.Chamadas); // RS-17: sem nova tentativa
    }

    [Fact]
    public async Task DoisPostIguais_DoisIdsDistintos_CN2()
    {
        var (servico, _) = Criar(RepositorioEscritaFalso.Sequencial(10));

        var a = await servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None);
        var b = await servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None);

        Assert.Equal(new ResultadoCriacao.Sucesso(10, "graphy"), a);
        Assert.Equal(new ResultadoCriacao.Sucesso(11, "graphy"), b);
    }

    // ---------------- Cancelamento (499) e prazo (D-P11) ----------------

    [Fact]
    public async Task ClienteCancela_DuranteBanco_Propaga_Loga4209()
    {
        var repo = RepositorioEscritaFalso.Pendurado();
        var (servico, log) = Criar(repo);
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, cts.Token));

        Assert.Single(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4209 && e.Nivel == LogLevel.Information);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 4210);
    }

    [Fact]
    public async Task ClienteCancela_DuranteLeitura_Propaga_SemBanco()
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
            servico.CriarAsync(ColunaMapa.Metry, leitor, TraceId, cts.Token));

        Assert.Empty(repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 4209);
    }

    [Fact]
    public async Task PrazoEstourado_NoBanco_PrazoAtualizacaoEstouradoException_Log4210()
    {
        var (servico, log) = Criar(RepositorioEscritaFalso.Pendurado(), TimeSpan.FromMilliseconds(150));

        var ex = await Assert.ThrowsAsync<PrazoAtualizacaoEstouradoException>(() =>
            servico.CriarAsync(ColunaMapa.Metry, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None));

        Assert.IsAssignableFrom<TimeoutException>(ex);
        Assert.Null(ex.InnerException);
        Assert.Contains("do POST estourado (coluna metry)", ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("POINT", ex.Message, StringComparison.Ordinal);
        Assert.Equal(LogLevel.Error, Assert.Single(log.Entradas, e => e.Evento.Id == 4210).Nivel);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 4209);
    }

    [Fact]
    public async Task PrazoEstourado_NaLeitura_SemBanco()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, _) = Criar(repo, TimeSpan.FromMilliseconds(100));
        var leitor = new LeitorCorpoFalso(async ct =>
        {
            await Task.Delay(Timeout.Infinite, ct);
            return LeituraCorpo.TipoMidiaNaoSuportado.Instancia;
        });

        await Assert.ThrowsAsync<PrazoAtualizacaoEstouradoException>(() =>
            servico.CriarAsync(ColunaMapa.Graphy, leitor, TraceId, CancellationToken.None));
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task OperationCanceledSemTokenCancelado_Propaga_SemLogDeCancelamento()
    {
        var (servico, log) = Criar(RepositorioEscritaFalso.QueLanca(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, CancellationToken.None));

        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id is 4209 or 4210);
    }

    [Fact]
    public async Task Prazo_TokenRepassadoAoRepositorio_LigadoAoDoCliente()
    {
        CancellationToken recebido = default;
        var repo = new RepositorioEscritaFalso((_, ct) =>
        {
            recebido = ct;
            return Task.FromResult<ResultadoEscrita>(new ResultadoEscrita.Criado(1));
        });
        var (servico, _) = Criar(repo);
        using var cts = new CancellationTokenSource();

        await servico.CriarAsync(ColunaMapa.Graphy, LeitorCorpoFalso.Json(CorposPut.Valido), TraceId, cts.Token);

        Assert.True(recebido.CanBeCanceled);
        Assert.NotEqual(cts.Token, recebido);
    }

    // ---------------- Registros ----------------

    [Fact]
    public void ResultadoCriacao_RegistrosEIgualdade()
    {
        Assert.Same(ResultadoCriacao.TipoMidiaNaoSuportado.Instancia, ResultadoCriacao.TipoMidiaNaoSuportado.Instancia);
        Assert.Same(ResultadoCriacao.CorpoInvalido.Instancia, ResultadoCriacao.CorpoInvalido.Instancia);
        Assert.Equal(new ResultadoCriacao.Sucesso(3, "metry"), new ResultadoCriacao.Sucesso(3, "metry"));
        Assert.NotEqual(new ResultadoCriacao.Sucesso(3, "metry"), new ResultadoCriacao.Sucesso(4, "metry"));
        Assert.Equal(7L, new ResultadoCriacao.CorpoMuitoGrande(7).Limite);
        Assert.Equal("graphy", new ResultadoCriacao.Conflito("graphy").Coluna);
        Assert.Equal("m", new ResultadoCriacao.GeometriaInvalida("m").Motivo);
        Assert.NotEqual<ResultadoCriacao>(ResultadoCriacao.CorpoInvalido.Instancia, ResultadoCriacao.TipoMidiaNaoSuportado.Instancia);
        Assert.Contains("Sucesso", new ResultadoCriacao.Sucesso(1, "graphy").ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public void LeituraInconsistenteException_Construtores()
    {
        Assert.Equal("Leitura inconsistente de public.mapas.", new LeituraInconsistenteException().Message);
        Assert.Equal("m", new LeituraInconsistenteException("m").Message);
        var interna = new InvalidOperationException();
        Assert.Same(interna, new LeituraInconsistenteException("m", interna).InnerException);
    }

    [Fact]
    public async Task Utf8Invalido_CorpoInvalido()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var (servico, _) = Criar(repo);
        var bytes = Encoding.UTF8.GetBytes("{\"wkt\":\"POINT(0 0)X\",\"srid\":4326}");
        bytes[Array.IndexOf(bytes, (byte)'X')] = 0xFF;

        var r = await servico.CriarAsync(ColunaMapa.Graphy, new LeitorCorpoFalso(new LeituraCorpo.Lido(bytes)), TraceId, CancellationToken.None);

        Assert.Same(ResultadoCriacao.CorpoInvalido.Instancia, r);
        Assert.Empty(repo.Chamadas);
    }
}
