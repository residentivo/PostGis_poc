using System.Net;
using System.Text.Json;
using ApiMapas.Api;
using ApiMapas.Dominio;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiMapas.Tests.Integracao;

/// <summary>Fact que so roda com PG_RUN_INTEGRATION=1 (credenciais do usuario via PG_*).</summary>
public sealed class FactIntegracaoAttribute : FactAttribute
{
    public FactIntegracaoAttribute()
    {
        if (!AmbienteOriginal.IntegracaoHabilitada)
        {
            Skip = "Integracao opt-in: defina PG_RUN_INTEGRATION=1 e PG_USER/PG_PASSWORD (role somente leitura).";
        }
    }
}

/// <summary>Theory que so roda com PG_RUN_INTEGRATION=1.</summary>
public sealed class TheoryIntegracaoAttribute : TheoryAttribute
{
    public TheoryIntegracaoAttribute()
    {
        if (!AmbienteOriginal.IntegracaoHabilitada)
        {
            Skip = "Integracao opt-in: defina PG_RUN_INTEGRATION=1 e PG_USER/PG_PASSWORD (role somente leitura).";
        }
    }
}

/// <summary>
/// Integracao SOMENTE LEITURA contra o PostgreSQL configurado pelo usuario (A7.3). Executa apenas
/// as consultas Q1/Q2 (SELECT). PG_TEST_ID define o id existente (padrao 1).
/// </summary>
[Trait("Category", "Integration")]
[Collection(nameof(ColecaoIntegracao))]
public class IntegracaoBancoTests
{
    private static MapasRepository CriarRepositorio(out IAsyncDisposable dataSource)
    {
        var config = ConexaoConfig.Carregar(AmbienteOriginal.Ler);
        var ds = config.CriarDataSource();
        dataSource = ds;
        return new MapasRepository(ds, NullLogger<MapasRepository>.Instance);
    }

    private static string IdTeste => AmbienteOriginal.Ler("PG_TEST_ID") ?? "1";

    [TheoryIntegracao]
    [InlineData(ColunaMapa.Graphy)]
    [InlineData(ColunaMapa.Metry)]
    public async Task Repositorio_LeGeometriasDoId(ColunaMapa coluna)
    {
        var repo = CriarRepositorio(out var ds);
        await using var _ = ds;
        Assert.True(IdMapa.TentarCriar(IdTeste, out var id));

        var itens = new List<GeometriaMapa>();
        await foreach (var g in repo.ListarPorIdAsync(id, coluna, CancellationToken.None))
        {
            itens.Add(g);
        }

        Assert.True(itens.Count <= 101);
        foreach (var g in itens)
        {
            Assert.False(string.IsNullOrWhiteSpace(g.Tipo));
            Assert.False(string.IsNullOrWhiteSpace(g.Wkt));
            using var doc = JsonDocument.Parse(g.GeoJson);
            Assert.Equal(JsonValueKind.Object, doc.RootElement.ValueKind);
            Assert.Equal(g.Tipo == "POINT", g.X.HasValue);
            if (g.Tipo == "POINT")
            {
                Assert.Equal(g.Srid != 0, g.Longitude.HasValue);
            }
        }
    }

    [TheoryIntegracao]
    [InlineData(ColunaMapa.Graphy)]
    [InlineData(ColunaMapa.Metry)]
    public async Task Repositorio_IdInexistente_Vazio(ColunaMapa coluna)
    {
        var repo = CriarRepositorio(out var ds);
        await using var _ = ds;
        IdMapa.TentarCriar("2147483647", out var id);

        var quantidade = 0;
        await foreach (var _g in repo.ListarPorIdAsync(id, coluna, CancellationToken.None))
        {
            quantidade++;
        }

        Assert.Equal(0, quantidade);
    }

    [TheoryIntegracao]
    [InlineData("graphy")]
    [InlineData("metry")]
    public async Task Api_ComBancoReal_200ou404_Nunca500(string coluna)
    {
        // Usa as credenciais originais do usuario apenas neste processo de teste.
        foreach (var nome in new[] { "PG_HOST", "PG_PORT", "PG_DATABASE", "PG_USER", "PG_PASSWORD", "PG_SSLMODE" })
        {
            Environment.SetEnvironmentVariable(nome, AmbienteOriginal.Ler(nome));
        }

        try
        {
            await using var f = new FabricaBancoReal();
            using var cliente = f.CreateClient();

            var r = await cliente.GetAsync($"/api/mapas/{IdTeste}/{coluna}");

            Assert.True(r.StatusCode is HttpStatusCode.OK or HttpStatusCode.NotFound, $"status {(int)r.StatusCode}");
        }
        finally
        {
            ApiFactory.DefinirVariaveisFicticias();
        }
    }

    private sealed class FabricaBancoReal : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
    {
    }
}

/// <summary>Integracao altera variaveis do processo: roda sem paralelismo.</summary>
[CollectionDefinition(nameof(ColecaoIntegracao), DisableParallelization = true)]
public sealed class ColecaoIntegracao
{
}
