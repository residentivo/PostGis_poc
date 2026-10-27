using System.Net;
using System.Text.Json;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Tests.Api;

/// <summary>
/// PO6.1 - GET com uma geometria por id (D-N7, RS-23, CN-5) pela API: objeto unico, 404 sem linha e
/// com a coluna NULL, 500 com 2 linhas (log Error 1004) e com teto de bytes (Warning 1005).
/// </summary>
public class ApiGetUmaGeometriaTests
{
    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Get404_ColunaNula_MesmosTextosDoSemLinha(string coluna)
    {
        await using var f = new ApiFactory(RepositorioFalso.ColunaVazia());
        using var cliente = f.CreateClient();

        var r = await cliente.GetAsync($"/api/mapas/9/{coluna}");
        var corpo = await r.Content.ReadAsStringAsync();

        Http.AssertProblem(r, corpo, HttpStatusCode.NotFound, "Geometria não encontrada",
            $"Nenhuma geometria encontrada na coluna '{coluna}' para o id 9.", $"/api/mapas/9/{coluna}");
    }

    [Theory]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Get500_DuasLinhas_LogError_SemVazamento(string coluna)
    {
        await using var f = new ApiFactory(new RepositorioFalso(Geometrias.Pontos(2)));
        using var cliente = f.CreateClient();

        var r = await cliente.GetAsync($"/api/mapas/4/{coluna}");
        var corpo = await r.Content.ReadAsStringAsync();

        Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno",
            "Não foi possível processar a requisição. Informe o traceId ao suporte.");
        Assert.DoesNotContain("quantidade", corpo, StringComparison.Ordinal);
        Assert.Contains(f.Logs.Mensagens, m => m.StartsWith("Error", StringComparison.Ordinal)
            && m.Contains("tem mais de uma linha em public.mapas", StringComparison.Ordinal)
            && m.Contains("migracao para PRIMARY KEY nao foi aplicada", StringComparison.Ordinal));
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("LeituraInconsistenteException", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("POINT(", StringComparison.Ordinal) && !m.Contains("Exception", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Get500_AcimaDoTetoDeBytes_Warning1005()
    {
        await using var f = new ApiFactory(new RepositorioFalso(new[] { Geometrias.ComTamanho(64) }));
        await using var limitada = f.WithWebHostBuilder(b => b.UseSetting("ApiMapas:LimiteBytesGeometrias", "10"));
        using var cliente = limitada.CreateClient();

        var r = await cliente.GetAsync("/api/mapas/3/graphy");
        var corpo = await r.Content.ReadAsStringAsync();

        Http.AssertProblem(r, corpo, HttpStatusCode.InternalServerError, "Erro interno");
        Assert.DoesNotContain("aaaa", corpo, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Get200_ObjetoUnico_SemQuantidadeTruncadoOuLista()
    {
        await using var f = new ApiFactory(new RepositorioFalso(new[] { Geometrias.Ponto(31983, 333250, 7394600) }));
        using var cliente = f.CreateClient();

        var r = await cliente.GetAsync("/api/mapas/5/metry");
        var json = JsonDocument.Parse(await r.Content.ReadAsStringAsync()).RootElement;

        Assert.Equal(HttpStatusCode.OK, r.StatusCode);
        Assert.Equal(JsonValueKind.Object, json.ValueKind);
        Assert.False(json.TryGetProperty("quantidade", out _));
        Assert.False(json.TryGetProperty("truncado", out _));
        Assert.False(json.TryGetProperty("geometrias", out _));
        Assert.Equal(31983, json.GetProperty("srid").GetInt32());
        Assert.Equal("POINT", json.GetProperty("tipo").GetString());
    }
}

/// <summary>PO6 - ramos restantes dos servicos (resultado fora do contrato).</summary>
public class ServicosRamosInesperadosTests
{
    private sealed class RepositorioNulo : IMapasRepository
    {
        public Task<LeituraMapa> ObterPorIdAsync(IdMapa id, ColunaMapa coluna, CancellationToken cancellationToken) => Task.FromResult<LeituraMapa>(null!);
    }

    [Fact]
    public async Task Consulta_LeituraNula_InvalidOperation()
    {
        var servico = new ConsultaGeometriasService(new RepositorioNulo(), new OpcoesConsulta(), new LoggerMemoria<ConsultaGeometriasService>());

        await Assert.ThrowsAsync<InvalidOperationException>(() => servico.ConsultarAsync("1", ColunaMapa.Graphy, CancellationToken.None));
    }

    [Fact]
    public async Task Atualizacao_ResultadoCriado_InvalidOperation_Log4116ComTipo()
    {
        var log = new LoggerMemoria<AtualizacaoGeometriasService>();
        var servico = new AtualizacaoGeometriasService(new RepositorioEscritaFalso(new ResultadoEscrita.Criado(5)), new OpcoesAtualizacao(), log);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            servico.AtualizarAsync("1", ColunaMapa.Metry, LeitorCorpoFalso.Json(CorposPut.Valido), "t", CancellationToken.None));

        var erro = Assert.Single(log.Entradas, e => e.Evento.Id == 4116);
        Assert.Equal(LogLevel.Error, erro.Nivel);
        Assert.Contains("(Criado)", erro.Mensagem, StringComparison.Ordinal);
    }
}
