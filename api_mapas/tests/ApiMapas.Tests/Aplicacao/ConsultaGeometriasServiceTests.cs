using System.Text.Json;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiMapas.Tests.Aplicacao;

public class ConsultaGeometriasServiceTests
{
    private static (ConsultaGeometriasService Servico, LoggerMemoria<ConsultaGeometriasService> Log) Criar(
        IMapasRepository repo, long limiteBytes = OpcoesConsulta.LimiteBytesPadrao)
    {
        var log = new LoggerMemoria<ConsultaGeometriasService>();
        return (new ConsultaGeometriasService(repo, new OpcoesConsulta { LimiteBytesGeometrias = limiteBytes }, log), log);
    }

    // ---------- 400: id invalido nao consulta o banco ----------

    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("+1")]
    [InlineData("2147483648")]
    [InlineData("12345678901")]
    [InlineData("1'")]
    [InlineData("")]
    [InlineData(null)]
    public async Task IdInvalido_NaoConsultaRepositorio_ENaoLogaValorBruto(string? id)
    {
        var repo = new RepositorioFalso(Geometrias.Pontos(1));
        var (servico, log) = Criar(repo);

        var resultado = await servico.ConsultarAsync(id, ColunaMapa.Graphy, CancellationToken.None);

        Assert.Same(ResultadoConsulta.IdInvalido.Instancia, resultado);
        Assert.Equal(0, repo.Chamadas);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 1002 && e.Nivel == LogLevel.Information);
        if (!string.IsNullOrEmpty(id) && id.Length >= 3)
        {
            Assert.DoesNotContain(id, log.TextoCompleto, StringComparison.Ordinal);
        }
    }

    // ---------- 404 / 200 / 500 (uma geometria por id - D-N7, RS-23) ----------

    [Theory]
    [InlineData(ColunaMapa.Graphy, "graphy")]
    [InlineData(ColunaMapa.Metry, "metry")]
    public async Task ZeroItens_DevolveNaoEncontrado(ColunaMapa coluna, string nome)
    {
        var repo = RepositorioFalso.Vazio();
        var (servico, _) = Criar(repo);

        var resultado = await servico.ConsultarAsync("15", coluna, CancellationToken.None);

        var nao = Assert.IsType<ResultadoConsulta.NaoEncontrado>(resultado);
        Assert.Equal(15, nao.Id);
        Assert.Equal(nome, nao.Coluna);
        Assert.Equal(1, repo.Chamadas);
        Assert.Equal((15, coluna), (repo.Pedidos[0].Id.Valor, repo.Pedidos[0].Coluna));
    }

    [Fact]
    public async Task ColunaVazia_DevolveNaoEncontrado()
    {
        var (servico, log) = Criar(RepositorioFalso.ColunaVazia());

        var resultado = await servico.ConsultarAsync("15", ColunaMapa.Metry, CancellationToken.None);

        Assert.Equal(new ResultadoConsulta.NaoEncontrado(15, "metry"), resultado);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 1010 && e.Nivel == LogLevel.Debug);
    }

    [Fact]
    public async Task UmaLinha_DevolveObjetoUnico()
    {
        var (servico, _) = Criar(new RepositorioFalso(Geometrias.Pontos(1)));

        var resultado = await servico.ConsultarAsync("1", ColunaMapa.Metry, CancellationToken.None);

        var resposta = Assert.IsType<ResultadoConsulta.Sucesso>(resultado).Resposta;
        Assert.Equal(1, resposta.Id);
        Assert.Equal("metry", resposta.Coluna);
        Assert.Equal("POINT", resposta.Tipo);
    }

    [Fact]
    public async Task DuasLinhas_LeituraInconsistente_LogError()
    {
        var (servico, log) = Criar(new RepositorioFalso(Geometrias.Pontos(2)));

        await Assert.ThrowsAsync<LeituraInconsistenteException>(
            () => servico.ConsultarAsync("4", ColunaMapa.Graphy, CancellationToken.None));

        Assert.Contains(log.Entradas, e => e.Evento.Id == 1004 && e.Nivel == LogLevel.Error);
    }

    // ---------- teto de bytes (item) ----------

    [Theory]
    // limite, bytes de wkt (geojson "{}" soma 2), cabe
    [InlineData(10, 8, true)]
    [InlineData(10, 9, false)]
    public async Task TetoDeBytes_AcimaDoTeto_LeituraInconsistente_Warning(long limite, int tamanho, bool cabe)
    {
        var (servico, log) = Criar(new RepositorioFalso(new[] { Geometrias.ComTamanho(tamanho) }), limite);

        if (cabe)
        {
            Assert.IsType<ResultadoConsulta.Sucesso>(await servico.ConsultarAsync("3", ColunaMapa.Graphy, CancellationToken.None));
        }
        else
        {
            await Assert.ThrowsAsync<LeituraInconsistenteException>(
                () => servico.ConsultarAsync("3", ColunaMapa.Graphy, CancellationToken.None));
        }

        Assert.Equal(!cabe, log.Entradas.Any(e => e.Evento.Id == 1005 && e.Nivel == LogLevel.Warning));
    }

    [Fact]
    public async Task TetoDeBytes_ContaBytesUtf8()
    {
        // "é" = 2 bytes em UTF-8: 4 chars "éééé" = 8 bytes + "{}" = 10.
        var g = new GeometriaMapa("POINT", 4326, "éééé", "{}", null, null, null, null);
        var (servico, _) = Criar(new RepositorioFalso(new[] { g }), 9);

        await Assert.ThrowsAsync<LeituraInconsistenteException>(
            () => servico.ConsultarAsync("1", ColunaMapa.Graphy, CancellationToken.None));
    }

    // ---------- mapeamento ----------

    [Fact]
    public async Task Mapeamento_Ponto_GeojsonObjetoECoordenadas()
    {
        var (servico, _) = Criar(new RepositorioFalso(new[] { Geometrias.Ponto() }));

        var resposta = Assert.IsType<ResultadoConsulta.Sucesso>(
            await servico.ConsultarAsync("1", ColunaMapa.Graphy, CancellationToken.None)).Resposta;

        var item = resposta;
        Assert.Equal("POINT", item.Tipo);
        Assert.Equal(4326, item.Srid);
        Assert.Equal(JsonValueKind.Object, item.Geojson.ValueKind);
        Assert.Equal("Point", item.Geojson.GetProperty("type").GetString());
        Assert.Equal(-46.633309, item.X);
        Assert.Equal(-23.55052, item.Y);
        Assert.Equal(-46.633309, item.Longitude);
        Assert.Equal(-23.55052, item.Latitude);
    }

    [Theory]
    [InlineData("POLYGON", 31983)]
    [InlineData("LINESTRING", 3857)]
    [InlineData("MULTIPOLYGON", 4674)]
    public void Mapear_NaoPonto_CoordenadasNulas(string tipo, int srid)
    {
        IdMapa.TentarCriar("1", out var id);
        var g = new GeometriaMapa(tipo, srid, "X", "{\"type\":\"Polygon\",\"coordinates\":[]}", null, null, null, null);

        var dto = ConsultaGeometriasService.Mapear(g, id, "metry");

        Assert.Equal(tipo, dto.Tipo);
        Assert.Null(dto.X);
        Assert.Null(dto.Y);
        Assert.Null(dto.Longitude);
        Assert.Null(dto.Latitude);
        Assert.Equal(JsonValueKind.Object, dto.Geojson.ValueKind);
    }

    [Fact]
    public void Mapear_PontoSrid0_LongitudeLatitudeNulas()
    {
        IdMapa.TentarCriar("1", out var id);

        var dto = ConsultaGeometriasService.Mapear(Geometrias.Ponto(0, 10, 20), id, "metry");

        Assert.Equal(0, dto.Srid);
        Assert.Equal(10, dto.X);
        Assert.Equal(20, dto.Y);
        Assert.Null(dto.Longitude);
        Assert.Null(dto.Latitude);
    }

    [Fact]
    public void Mapear_GeojsonSobreviveAoDisposeDoDocumento()
    {
        IdMapa.TentarCriar("1", out var id);
        var dto = ConsultaGeometriasService.Mapear(Geometrias.Ponto(), id, "graphy");

        GC.Collect();
        Assert.Equal("{\"type\":\"Point\",\"coordinates\":[-46.633309,-23.55052]}", dto.Geojson.GetRawText());
    }

    [Theory]
    [InlineData("nao e json")]
    [InlineData("{\"type\":")]
    [InlineData("")]
    public async Task GeojsonInvalido_LancaInvalidOperation_SemConteudoDoBanco(string geojson)
    {
        var g = new GeometriaMapa("POINT", 4326, "POINT(1 2)", geojson, 1, 2, 1, 2);
        var (servico, log) = Criar(new RepositorioFalso(new[] { g }));

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => servico.ConsultarAsync("8", ColunaMapa.Graphy, CancellationToken.None));

        Assert.Contains("id 8", ex.Message, StringComparison.Ordinal);
        Assert.Contains("graphy", ex.Message, StringComparison.Ordinal);
        Assert.IsAssignableFrom<JsonException>(ex.InnerException);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 1007 && e.Nivel == LogLevel.Error);
    }

    // ---------- erros e cancelamento ----------

    [Fact]
    public async Task ErroDoRepositorio_Propaga_ELogaSoOTipo()
    {
        var erro = new TimeoutException("detalhe interno host=10.0.0.1 password=xyz");
        var (servico, log) = Criar(RepositorioFalso.QueLanca(erro));

        var ex = await Assert.ThrowsAsync<TimeoutException>(
            () => servico.ConsultarAsync("2", ColunaMapa.Metry, CancellationToken.None));

        Assert.Same(erro, ex);
        var falha = Assert.Single(log.Entradas, e => e.Evento.Id == 1007);
        Assert.Contains("TimeoutException", falha.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("password", log.TextoCompleto, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Cancelamento_Propaga_ELogaCancelada()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var (servico, log) = Criar(new RepositorioFalso(Geometrias.Pontos(1)));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => servico.ConsultarAsync("2", ColunaMapa.Graphy, cts.Token));

        Assert.Contains(log.Entradas, e => e.Evento.Id == 1006);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 1007);
    }

    [Fact]
    public async Task OperationCanceledSemTokenCancelado_TratadaComoFalha()
    {
        var (servico, log) = Criar(RepositorioFalso.QueLanca(new OperationCanceledException()));

        await Assert.ThrowsAsync<OperationCanceledException>(
            () => servico.ConsultarAsync("2", ColunaMapa.Graphy, CancellationToken.None));

        Assert.Contains(log.Entradas, e => e.Evento.Id == 1007);
    }

    [Fact]
    public async Task LogsDePasso_EmDebug()
    {
        var (servico, log) = Criar(new RepositorioFalso(Geometrias.Pontos(1)));

        await servico.ConsultarAsync("1", ColunaMapa.Graphy, CancellationToken.None);

        Assert.All(log.Entradas.Where(e => e.Evento.Id is 1001 or 1003 or 1009), e => Assert.Equal(LogLevel.Debug, e.Nivel));
        Assert.Contains(log.Entradas, e => e.Evento.Id == 1009);
    }

    // ---------- construtor ----------

    [Fact]
    public void Construtor_ArgumentosNulos_Lanca()
    {
        var repo = RepositorioFalso.Vazio();
        var opcoes = new OpcoesConsulta();
        var log = NullLogger<ConsultaGeometriasService>.Instance;

        Assert.Throws<ArgumentNullException>(() => new ConsultaGeometriasService(null!, opcoes, log));
        Assert.Throws<ArgumentNullException>(() => new ConsultaGeometriasService(repo, null!, log));
        Assert.Throws<ArgumentNullException>(() => new ConsultaGeometriasService(repo, opcoes, null!));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(long.MinValue)]
    public void Construtor_LimiteBytesNaoPositivo_Lanca(long limite)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new ConsultaGeometriasService(
            RepositorioFalso.Vazio(), new OpcoesConsulta { LimiteBytesGeometrias = limite }, NullLogger<ConsultaGeometriasService>.Instance));
    }

    [Fact]
    public void OpcoesConsulta_Padroes()
    {
        Assert.Equal("ApiMapas", OpcoesConsulta.Secao);
        Assert.Equal(5L * 1024 * 1024, new OpcoesConsulta().LimiteBytesGeometrias);
    }

    [Fact]
    public async Task ServicoComNullLogger_Funciona()
    {
        var servico = new ConsultaGeometriasService(new RepositorioFalso(Geometrias.Pontos(1)), new OpcoesConsulta(), NullLogger<ConsultaGeometriasService>.Instance);

        var r = await servico.ConsultarAsync("1", ColunaMapa.Graphy, CancellationToken.None);

        Assert.IsType<ResultadoConsulta.Sucesso>(r);
        Assert.IsType<ResultadoConsulta.IdInvalido>(await servico.ConsultarAsync("x", ColunaMapa.Graphy, CancellationToken.None));
    }
}
