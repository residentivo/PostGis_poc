using System.Net;
using ApiMapas.Dominio;
using ApiMapas.Tests.Suporte;

namespace ApiMapas.Tests.Api;

/// <summary>
/// PO6.2 / PO6.6 - limitadores encadeados CreateChained(escritaSimultanea, global, escritaPorIp,
/// escritaGlobal) (D-N9, RS-25, CN-8; achado 2 do PO5): PUT e POST dividem as cotas; a 4a escrita
/// simultanea responde 429 sem gastar as cotas por minuto; a 11a escrita no minuto responde 429 no
/// mesmo IP e somando todos os IPs.
/// </summary>
public class LimitesEscritaTests
{
    private static HttpContent Corpo() => Http.Json(CorposPut.Valido);

    private static Task<(HttpResponseMessage Resposta, string Corpo)> Escrever(HttpClient cliente, string tipo, string? ip = null)
    {
        var (metodo, caminho) = tipo switch
        {
            "POST-graphy" => (HttpMethod.Post, "/api/mapas/graphy"),
            "POST-metry" => (HttpMethod.Post, "/api/mapas/metry"),
            "PUT-graphy" => (HttpMethod.Put, "/api/mapas/1/graphy"),
            _ => (HttpMethod.Put, "/api/mapas/2/metry"),
        };
        return Http.EnviarAsync(cliente, metodo, caminho, Corpo(), req =>
        {
            if (ip is not null)
            {
                req.Headers.Add(ApiEscritaFactory.CabecalhoIpTeste, ip);
            }
        });
    }

    private static async Task EsperarAsync(Func<bool> condicao)
    {
        var limite = DateTime.UtcNow.AddSeconds(10);
        while (!condicao() && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        Assert.True(condicao());
    }

    /// <summary>
    /// Achado 2 do PO5 (definitivo): 4 escritas/min e 5 req/min. 3 escritas presas e 3 recusas por
    /// simultaneidade (429 sem Retry-After). Depois, a 4a escrita passa (se as recusas tivessem gasto a
    /// cota de escrita, seriam 7 de 4) e um GET ainda passa (global: 3 + 1 + 1 = 5; com as recusas
    /// seriam 8 de 5). So o GET seguinte recebe 429.
    /// </summary>
    [Fact]
    public async Task Recusa429PorSimultaneidade_NaoGastaAsCotasPorMinuto()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrou = 0;
        var repo = new RepositorioEscritaFalso(async (c, ct) =>
        {
            if (Interlocked.Increment(ref entrou) <= 3)
            {
                await liberar.Task.WaitAsync(ct);
            }

            return c.Id is null ? new ResultadoEscrita.Criado(9) : ResultadoEscrita.Atualizado.Instancia;
        });
        await using var f = new ApiEscritaFactory { Repositorio = repo }
            .ComConfiguracao("ApiMapas:LimiteEscritasPorMinuto", "4")
            .ComConfiguracao("ApiMapas:LimiteRequisicoesPorMinuto", "5");
        using var cliente = f.CreateClient();

        var presas = new[] { "POST-graphy", "PUT-graphy", "POST-metry" }.Select(t => Escrever(cliente, t)).ToList();
        await EsperarAsync(() => Volatile.Read(ref entrou) == 3);

        foreach (var tipo in new[] { "POST-metry", "PUT-metry", "POST-graphy" })
        {
            var (r, corpo) = await Escrever(cliente, tipo);
            Http.AssertProblem(r, corpo, HttpStatusCode.TooManyRequests, "Muitas requisições");
            Assert.Null(r.Headers.RetryAfter);
        }

        liberar.SetResult();
        var respostas = await Task.WhenAll(presas);
        Assert.All(respostas, x => Assert.True(x.Resposta.IsSuccessStatusCode));

        var (quarta, _) = await Escrever(cliente, "PUT-metry");
        var get = await cliente.GetAsync("/api/mapas/1/graphy");
        var getExcedente = await cliente.GetAsync("/api/mapas/1/graphy");

        Assert.Equal(HttpStatusCode.NoContent, quarta.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, getExcedente.StatusCode);
        Assert.Equal(4, repo.Chamadas.Count);
        Assert.Equal(3, repo.MaximoSimultaneo);
    }

    [Fact]
    public async Task QuintaEscritaNoMinuto_429ComRetryAfter_DepoisDasSimultaneas()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteEscritasPorMinuto", "4");
        using var cliente = f.CreateClient();

        foreach (var tipo in new[] { "POST-graphy", "PUT-graphy", "POST-metry", "PUT-metry" })
        {
            Assert.True((await Escrever(cliente, tipo)).Resposta.IsSuccessStatusCode);
        }

        var (quinta, _) = await Escrever(cliente, "POST-graphy");
        Assert.Equal(HttpStatusCode.TooManyRequests, quinta.StatusCode);
        Assert.NotNull(quinta.Headers.RetryAfter);
        Assert.Equal(4, repo.Chamadas.Count);
    }

    [Fact]
    public async Task QuartaEscritaSimultanea_Mista_429_GetContinua()
    {
        var liberar = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var entrou = 0;
        var repo = new RepositorioEscritaFalso(async (c, ct) =>
        {
            Interlocked.Increment(ref entrou);
            await liberar.Task.WaitAsync(ct);
            return c.Id is null ? new ResultadoEscrita.Criado(1) : ResultadoEscrita.Atualizado.Instancia;
        });
        await using var f = new ApiEscritaFactory { Repositorio = repo };
        using var cliente = f.CreateClient();

        var presas = new[] { "POST-graphy", "POST-metry", "POST-graphy" }.Select(t => Escrever(cliente, t)).ToList();
        await EsperarAsync(() => Volatile.Read(ref entrou) == 3);
        var (quartaPut, _) = await Escrever(cliente, "PUT-graphy");
        var (quartaPost, _) = await Escrever(cliente, "POST-metry");
        var get = await cliente.GetAsync("/api/mapas/1/metry");
        liberar.SetResult();
        await Task.WhenAll(presas);

        Assert.Equal(HttpStatusCode.TooManyRequests, quartaPut.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, quartaPost.StatusCode);
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        Assert.Equal(3, repo.Chamadas.Count);
    }

    /// <summary>CN-8 / RS-25: limitador global de escrita, sem particao, somando PUT e POST de IPs diferentes.</summary>
    [Fact]
    public async Task LimitadorGlobalDeEscrita_SomaPutEPostDeTodosOsIps()
    {
        var repo = RepositorioEscritaFalso.Sequencial(1);
        await using var f = new ApiEscritaFactory { Repositorio = repo, IpPorCabecalho = true }
            .ComConfiguracao("ApiMapas:LimiteEscritasPorMinuto", "3");
        using var cliente = f.CreateClient();

        var (a1, _) = await Escrever(cliente, "POST-graphy", "10.0.0.1");
        var (a2, _) = await Escrever(cliente, "PUT-metry", "10.0.0.1");
        var (b1, _) = await Escrever(cliente, "POST-metry", "10.0.0.2");
        var (c1, corpoC1) = await Escrever(cliente, "PUT-graphy", "10.0.0.3");
        var (c2, _) = await Escrever(cliente, "POST-graphy", "10.0.0.4");
        var getC = await Http.EnviarAsync(cliente, HttpMethod.Get, "/api/mapas/1/graphy", ajustar: r => r.Headers.Add(ApiEscritaFactory.CabecalhoIpTeste, "10.0.0.3"));

        Assert.Equal(HttpStatusCode.Created, a1.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, a2.StatusCode);
        Assert.Equal(HttpStatusCode.Created, b1.StatusCode);
        Http.AssertProblem(c1, corpoC1, HttpStatusCode.TooManyRequests, "Muitas requisições");
        Assert.NotNull(c1.Headers.RetryAfter);
        Assert.Equal(HttpStatusCode.TooManyRequests, c2.StatusCode);
        Assert.Equal(HttpStatusCode.OK, getC.Resposta.StatusCode);
        Assert.Equal(3, repo.Chamadas.Count);
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("Escrita (PUT e POST) recusada pelo rate limiting", StringComparison.Ordinal));
    }

    /// <summary>CN-8: a 11a escrita no mesmo minuto e no mesmo IP responde 429 (padrao 10/min), PUT e POST juntos.</summary>
    [Fact]
    public async Task OnzeEscritasNoMesmoIp_PadraoDe10_PutEPostJuntos()
    {
        var repo = RepositorioEscritaFalso.Sequencial(1);
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteEscritasPorMinuto", null);
        using var cliente = f.CreateClient();

        var status = new List<HttpStatusCode>();
        for (var i = 0; i < 11; i++)
        {
            var (r, _) = await Escrever(cliente, i % 2 == 0 ? "POST-graphy" : "PUT-metry");
            status.Add(r.StatusCode);
        }

        Assert.All(status.Take(10), s => Assert.True(s is HttpStatusCode.Created or HttpStatusCode.NoContent));
        Assert.Equal(HttpStatusCode.TooManyRequests, status[10]);
        Assert.Equal(10, repo.Chamadas.Count);
        Assert.Equal(5, repo.Chamadas.Count(c => c.Metodo == "POST"));
    }

    /// <summary>Escritas recusadas por 429 (cota por minuto) nunca chegam ao repositorio, misturando PUT e POST.</summary>
    [Fact]
    public async Task EscritaRecusadaPor429_NaoChegaAoRepositorio()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        await using var f = new ApiEscritaFactory { Repositorio = repo }.ComConfiguracao("ApiMapas:LimiteEscritasPorMinuto", "1");
        using var cliente = f.CreateClient();

        var (r1, _) = await Escrever(cliente, "POST-metry");
        var (r2, _) = await Escrever(cliente, "PUT-graphy");
        var (r3, _) = await Escrever(cliente, "POST-graphy");

        Assert.Equal(HttpStatusCode.Created, r1.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r2.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, r3.StatusCode);
        Assert.Single(repo.Chamadas);
    }
}
