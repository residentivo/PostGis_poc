using ApiMapas.Api.Erros;
using ApiMapas.Api.Seguranca;
using ApiMapas.Tests.Suporte;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;

namespace ApiMapas.Tests.Api;

public class ProblemasApiTests
{
    [Fact]
    public void Personalizar_ComInstanceDoEndpoint_ETraceId_RemoveExtensoesInternas()
    {
        var http = new DefaultHttpContext { TraceIdentifier = "trace-1" };
        http.Items[ProblemasApi.ChaveInstance] = "/api/mapas/{id}/graphy";
        var pd = new ProblemDetails { Instance = "/api/mapas/abc/graphy" };
        pd.Extensions["exception"] = "stack";
        pd.Extensions["errors"] = "x";

        ProblemasApi.Personalizar(new ProblemDetailsContext { HttpContext = http, ProblemDetails = pd });

        Assert.Equal("/api/mapas/{id}/graphy", pd.Instance);
        Assert.NotNull(pd.Extensions["traceId"]);
        Assert.False(pd.Extensions.ContainsKey("exception"));
        Assert.False(pd.Extensions.ContainsKey("errors"));
    }

    [Fact]
    public void Personalizar_SemInstanceDoEndpoint_OmiteInstance()
    {
        var http = new DefaultHttpContext();
        var pd = new ProblemDetails { Instance = "/caminho/bruto?x=<script>" };

        ProblemasApi.Personalizar(new ProblemDetailsContext { HttpContext = http, ProblemDetails = pd });

        Assert.Null(pd.Instance);
    }

    [Fact]
    public void Personalizar_ContextoNulo_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => ProblemasApi.Personalizar(null!));
    }

    [Theory]
    [InlineData("graphy", 1, "Nenhuma geometria encontrada na coluna 'graphy' para o id 1.")]
    [InlineData("metry", 2147483647, "Nenhuma geometria encontrada na coluna 'metry' para o id 2147483647.")]
    public void Detalhe404(string coluna, int id, string esperado)
    {
        Assert.Equal(esperado, ProblemasApi.Detalhe404(coluna, id));
    }
}

public class ManipuladorExcecoesTests
{
    private static (ManipuladorExcecoes Manipulador, LoggerMemoria<ManipuladorExcecoes> Log, DefaultHttpContext Http) Criar(bool abortada)
    {
        var servicos = new ServiceCollection().AddLogging().AddProblemDetails().BuildServiceProvider();
        var log = new LoggerMemoria<ManipuladorExcecoes>();
        var http = new DefaultHttpContext { RequestServices = servicos };
        http.Response.Body = new MemoryStream();
        if (abortada)
        {
            var cts = new CancellationTokenSource();
            cts.Cancel();
            http.RequestAborted = cts.Token;
        }

        return (new ManipuladorExcecoes(servicos.GetRequiredService<IProblemDetailsService>(), log), log, http);
    }

    [Fact]
    public async Task ClienteDesconectou_499SemCorpo_LogInformation()
    {
        var (m, log, http) = Criar(abortada: true);

        var tratado = await m.TryHandleAsync(http, new OperationCanceledException(), CancellationToken.None);

        Assert.True(tratado);
        Assert.Equal(499, http.Response.StatusCode);
        Assert.Equal(0, http.Response.Body.Length);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 3001);
    }

    [Fact]
    public async Task OperationCanceledSemAbort_Vira500()
    {
        var (m, log, http) = Criar(abortada: false);

        await m.TryHandleAsync(http, new TaskCanceledException("timeout interno"), CancellationToken.None);

        Assert.Equal(500, http.Response.StatusCode);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 3002);
    }

    [Fact]
    public async Task Excecao_500Generico_SemDetalheNoCorpo_ComRotaSemRota()
    {
        var (m, log, http) = Criar(abortada: false);

        var tratado = await m.TryHandleAsync(http, new InvalidOperationException("SEGREDO-INTERNO"), CancellationToken.None);

        Assert.True(tratado);
        Assert.Equal(500, http.Response.StatusCode);
        http.Response.Body.Position = 0;
        var corpo = await new StreamReader(http.Response.Body).ReadToEndAsync();
        Assert.Contains("Erro interno", corpo, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGREDO-INTERNO", corpo, StringComparison.Ordinal);
        var entrada = Assert.Single(log.Entradas, e => e.Evento.Id == 3002);
        Assert.Contains("(sem rota)", entrada.Mensagem, StringComparison.Ordinal);
        Assert.NotNull(entrada.Erro); // excecao completa so no log
    }

    [Fact]
    public async Task ArgumentosNulos_Lancam()
    {
        var (m, _, http) = Criar(abortada: false);
        await Assert.ThrowsAsync<ArgumentNullException>(() => m.TryHandleAsync(null!, new Exception(), CancellationToken.None).AsTask());
        await Assert.ThrowsAsync<ArgumentNullException>(() => m.TryHandleAsync(http, null!, CancellationToken.None).AsTask());
    }
}

public class CabecalhosSegurancaStartupFilterTests
{
    [Fact]
    public void Configure_NextNulo_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => new CabecalhosSegurancaStartupFilter().Configure(null!));
    }

    [Fact]
    public void Constantes()
    {
        Assert.Equal("nosniff", CabecalhosSegurancaStartupFilter.ValorNoSniff);
        Assert.Equal("no-store", CabecalhosSegurancaStartupFilter.ValorNoStore);
    }
}
