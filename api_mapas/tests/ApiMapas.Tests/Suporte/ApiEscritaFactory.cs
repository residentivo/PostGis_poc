using ApiMapas.Api;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Tests.Suporte;

/// <summary>
/// API em memoria com a ESCRITA (PUT) configurada por variaveis PG_ESCRITA_* FICTICIAS. A checagem
/// R-1 usa <see cref="Verificador"/> (falso; padrao: role sem privilegios) e o PUT usa o repositorio
/// falso, ou o MapasEscritaRepository real contra o ServidorPostgresEscritaFalso quando
/// <see cref="Fonte"/> e informada. PG_HOST continua 127.0.0.1:1 (nada escuta): nenhum teste
/// alcanca um servidor real.
/// </summary>
public sealed class ApiEscritaFactory : WebApplicationFactory<Program>
{
    public const string UsuarioEscrita = "escrita_ficticia_teste";
    public const string SenhaEscrita = "SenhaEscritaFicticia#456";

    private readonly Dictionary<string, string?> _variaveis = new(StringComparer.Ordinal)
    {
        ["PG_ESCRITA_USER"] = UsuarioEscrita,
        ["PG_ESCRITA_PASSWORD"] = SenhaEscrita,
    };

    private readonly Dictionary<string, string?> _configuracoes = new(StringComparer.Ordinal)
    {
        ["ApiMapas:LimiteRequisicoesPorMinuto"] = "100000",
        ["ApiMapas:LimiteEscritasPorMinuto"] = "60",
    };

    public IMapasEscritaRepository? Repositorio { get; init; }

    public IMapasRepository RepositorioLeitura { get; init; } = new RepositorioFalso(Geometrias.Pontos(1));

    /// <summary>Integracao (P6.5): usa o MapasRepository real no GET (nao registra <see cref="RepositorioLeitura"/>).</summary>
    public bool LeituraReal { get; init; }

    /// <summary>Checagem R-1 falsa. null = usa o VerificadorRoleEscrita real (contra <see cref="Fonte"/> ou 127.0.0.1:1).</summary>
    public IVerificadorRoleEscrita? Verificador { get; init; } = new VerificadorRoleFalso(MotivoEscritaDesligada.Nenhum);

    /// <summary>Quando definida, substitui o pool de escrita (ex.: ServidorPostgresEscritaFalso) e usa o repositorio real.</summary>
    public FonteDadosEscrita? Fonte { get; init; }

    /// <summary>Quando definida, substitui as opcoes do PUT (ex.: prazo curto).</summary>
    public OpcoesAtualizacao? Opcoes { get; init; }

    public string Ambiente { get; init; } = "Development";

    /// <summary>
    /// PO6 (CN-8): quando true, o cabecalho <see cref="CabecalhoIpTeste"/> define o
    /// RemoteIpAddress da requisicao (o TestServer nao tem IP), para simular varios clientes.
    /// </summary>
    public bool IpPorCabecalho { get; init; }

    public const string CabecalhoIpTeste = "X-Teste-Ip";

    public ProvedorLogMemoria Logs { get; } = new();

    /// <summary>Variavel de ambiente PG_* (null remove).</summary>
    public ApiEscritaFactory ComVariavel(string nome, string? valor)
    {
        _variaveis[nome] = valor;
        return this;
    }

    /// <summary>Chave do appsettings (ex.: ApiMapas:LimiteEscritasPorMinuto).</summary>
    public ApiEscritaFactory ComConfiguracao(string chave, string? valor)
    {
        _configuracoes[chave] = valor;
        return this;
    }

    /// <summary>Mensagens formatadas e valores estruturados (PO6: vazamento checado nos dois).</summary>
    public string TextoLogs => string.Join('\n', Logs.Mensagens.Concat(Logs.Propriedades));

    protected override IHost CreateHost(IHostBuilder builder)
    {
        lock (TravaAmbiente.Trava)
        {
            ApiFactory.DefinirVariaveisFicticias();
            foreach (var (nome, valor) in _variaveis)
            {
                Environment.SetEnvironmentVariable(nome, valor);
            }

            try
            {
                return base.CreateHost(builder);
            }
            finally
            {
                ApiFactory.DefinirVariaveisFicticias();
            }
        }
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment(Ambiente);
        foreach (var (chave, valor) in _configuracoes)
        {
            builder.UseSetting(chave, valor);
        }

        builder.ConfigureLogging(l => l.AddProvider(Logs));
        builder.ConfigureTestServices(servicos =>
        {
            if (!LeituraReal)
            {
                servicos.AddScoped(_ => RepositorioLeitura);
            }

            if (Verificador is not null)
            {
                servicos.AddSingleton(Verificador);
            }
            if (Fonte is not null)
            {
                servicos.AddSingleton(Fonte);
            }

            if (Repositorio is not null)
            {
                servicos.AddScoped(_ => Repositorio);
            }

            if (Opcoes is not null)
            {
                servicos.AddSingleton(Opcoes);
            }

            if (IpPorCabecalho)
            {
                servicos.AddTransient<IStartupFilter, IpDeTesteStartupFilter>();
            }
        });
    }
}

/// <summary>So nos testes: copia o cabecalho X-Teste-Ip para Connection.RemoteIpAddress (antes do rate limiter).</summary>
internal sealed class IpDeTesteStartupFilter : IStartupFilter
{
    public Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> Configure(Action<Microsoft.AspNetCore.Builder.IApplicationBuilder> next) => app =>
    {
        app.Use(async (HttpContext contexto, RequestDelegate proximo) =>
        {
            if (contexto.Request.Headers.TryGetValue(ApiEscritaFactory.CabecalhoIpTeste, out var ip)
                && System.Net.IPAddress.TryParse(ip.ToString(), out var endereco))
            {
                contexto.Connection.RemoteIpAddress = endereco;
            }

            await proximo(contexto);
        });
        next(app);
    };
}
