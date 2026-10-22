using ApiMapas.Api;
using ApiMapas.Dominio;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
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
