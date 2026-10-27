using ApiMapas.Api;
using ApiMapas.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Tests.Suporte;

/// <summary>
/// API em memoria (TestServer) com repositorio falso. Define PG_USER/PG_PASSWORD FICTICIOS
/// no processo e aponta PG_HOST para 127.0.0.1:1 (nada escuta), garantindo que nenhum teste
/// alcance o servidor real.
/// </summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    private readonly string _ambiente;
    private readonly int _limiteRequisicoes;

    static ApiFactory() => DefinirVariaveisFicticias();

    public ApiFactory(IMapasRepository repositorio, string ambiente = "Development", int limiteRequisicoes = 100_000)
    {
        Repositorio = repositorio;
        _ambiente = ambiente;
        _limiteRequisicoes = limiteRequisicoes;
    }

    public IMapasRepository Repositorio { get; }

    public ProvedorLogMemoria Logs { get; } = new();

    public static void DefinirVariaveisFicticias()
    {
        Environment.SetEnvironmentVariable("PG_USER", "usuario_ficticio_teste");
        Environment.SetEnvironmentVariable("PG_PASSWORD", "SenhaFicticia#123");
        Environment.SetEnvironmentVariable("PG_HOST", "127.0.0.1");
        Environment.SetEnvironmentVariable("PG_PORT", "1");
        Environment.SetEnvironmentVariable("PG_SSLMODE", null);

        // RA-5: HTTP_PORTS/HTTPS_PORTS do ambiente (ex.: imagens .NET definem 8080) impediriam a subida.
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTP_PORTS", null);
        Environment.SetEnvironmentVariable("ASPNETCORE_HTTPS_PORTS", null);
        Environment.SetEnvironmentVariable("DOTNET_HTTP_PORTS", null);
        Environment.SetEnvironmentVariable("DOTNET_HTTPS_PORTS", null);

        // Escrita (PUT) desligada por padrao nos testes, independente do ambiente da maquina.
        Environment.SetEnvironmentVariable("PG_ESCRITA_USER", null);
        Environment.SetEnvironmentVariable("PG_ESCRITA_PASSWORD", null);
    }

    /// <summary>
    /// O Program le as variaveis PG_* do processo durante a montagem do host. A trava impede que
    /// uma fabrica com escrita (<see cref="ApiEscritaFactory"/>) troque as variaveis no meio da
    /// montagem desta.
    /// </summary>
    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (TravaAmbiente.Trava)
        {
            DefinirVariaveisFicticias();
            return base.CreateHost(builder);
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(_ambiente);
        builder.UseSetting("ApiMapas:LimiteRequisicoesPorMinuto", _limiteRequisicoes.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(servicos =>
        {
            servicos.AddScoped(_ => Repositorio);
        });
    }
}

/// <summary>Trava unica para trocar as variaveis PG_* do processo durante a montagem do host.</summary>
public static class TravaAmbiente
{
    public static readonly object Trava = new();
}
