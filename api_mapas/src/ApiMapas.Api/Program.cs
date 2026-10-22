using System.Globalization;
using System.Threading.RateLimiting;
using ApiMapas.Api.Endpoints;
using ApiMapas.Api.Erros;
using ApiMapas.Api.Inicializacao;
using ApiMapas.Api.Seguranca;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

namespace ApiMapas.Api;

/// <summary>
/// Ponto de entrada da API api_mapas. SEM AUTENTICACAO: so sobe ouvindo em loopback (RA-5).
/// </summary>
public partial class Program
{
    public const int CodigoSaidaConfiguracao = 1;

    public static int Main(string[] args)
    {
        WebApplication app;
        try
        {
            app = CriarAplicacao(args, Environment.GetEnvironmentVariable);
        }
        catch (ConfiguracaoInvalidaException ex)
        {
            // Mensagem cita so o nome da variavel/chave, nunca o valor (RA-9).
            Console.Error.WriteLine($"[ApiMapas] A API nao foi iniciada. {ex.Message}");
            return CodigoSaidaConfiguracao;
        }

        app.Run();
        return 0;
    }

    /// <summary>
    /// Monta a aplicacao. <paramref name="lerVariavel"/> le as variaveis PG_* (injetavel nos testes).
    /// Lanca <see cref="ConfiguracaoInvalidaException"/> quando a configuracao impede a subida.
    /// </summary>
    public static WebApplication CriarAplicacao(string[] args, Func<string, string?> lerVariavel)
    {
        var builder = WebApplication.CreateBuilder(args);

        // ---- Configuracao nao sensivel (appsettings.json) e logs ----
        var configuracaoApi = ConfiguracaoApi.Carregar(builder.Configuration);
        if (configuracaoApi.Verboso)
        {
            // Logs de passo (LogDebug) das camadas ApiMapas.* so aparecem com "Verboso": true.
            builder.Logging.AddFilter("ApiMapas", LogLevel.Debug);
        }

        // ---- RA-5: somente loopback ----
        ValidarEnderecosDeEscuta(builder);

        // ---- RA-8 / RA-9: conexao so por PG_* (obrigatorias PG_USER/PG_PASSWORD) ----
        var conexao = ConexaoConfig.Carregar(lerVariavel);

        // ---- RA-7: cabecalhos em todas as respostas; sem header Server ----
        builder.Services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, CabecalhosSegurancaStartupFilter>());
        builder.WebHost.ConfigureKestrel(opcoes => opcoes.AddServerHeader = false);

        // ---- RA-6 / RA-10 / RA-11: ProblemDetails e tratamento global de excecoes ----
        builder.Services.AddProblemDetails(opcoes => opcoes.CustomizeProblemDetails = ProblemasApi.Personalizar);
        builder.Services.AddExceptionHandler<ManipuladorExcecoes>();

        // ---- REC-A1: rate limiting por IP ----
        builder.Services.AddRateLimiter(opcoes => ConfigurarRateLimiter(opcoes, configuracaoApi.LimiteRequisicoesPorMinuto));

        // ---- Camadas (DDD) ----
        builder.Services.AddSingleton(conexao);
        builder.Services.AddSingleton<NpgsqlDataSource>(_ => conexao.CriarDataSource());
        builder.Services.AddSingleton(configuracaoApi.CriarOpcoesConsulta());
        builder.Services.AddScoped<IMapasRepository, MapasRepository>();
        builder.Services.AddScoped<IConsultaGeometriasService, ConsultaGeometriasService>();

        var app = builder.Build();

        var destino = conexao.Descricao();
        LogsInicializacao.Iniciando(app.Logger, destino, configuracaoApi.Verboso,
            configuracaoApi.LimiteBytesGeometrias, configuracaoApi.LimiteRequisicoesPorMinuto);
        LogsInicializacao.SemAutenticacao(app.Logger);
        if (conexao.SslInseguro)
        {
            LogsInicializacao.SslInseguro(app.Logger, ConexaoConfig.VarSslMode, conexao.SslMode.ToString());
        }

        // RA-6: handler generico em QUALQUER ambiente (inclusive Development). Ele fica dentro da
        // pagina de desenvolvedor que o WebApplication adiciona sozinho em Development, entao toda
        // excecao e tratada aqui antes; a API nunca chama UseDeveloperExceptionPage.
        app.UseExceptionHandler();
        app.UseStatusCodePages();
        app.UseRateLimiter();

        app.MapearEndpointsMapas();

        LogsInicializacao.PipelineConfigurado(app.Logger, MapasEndpoints.RotaGraphy, MapasEndpoints.RotaMetry);
        return app;
    }

    /// <summary>
    /// RA-5: recusa subir se "urls"/ASPNETCORE_URLS, HTTP_PORTS/HTTPS_PORTS ou Kestrel:Endpoints
    /// apontarem para fora do loopback. Sem nada configurado, usa http://localhost:5080.
    /// </summary>
    private static void ValidarEnderecosDeEscuta(WebApplicationBuilder builder)
    {
        var configuracao = builder.Configuration;

        foreach (var endpoint in configuracao.GetSection("Kestrel:Endpoints").GetChildren())
        {
            var url = endpoint["Url"];
            if (url is not null && !ValidadorEnderecosLoopback.EhLoopback(url))
            {
                throw new ConfiguracaoInvalidaException("Kestrel:Endpoints",
                    $"aponta para um endereco que nao e loopback ({url}). A API nao tem autenticacao e so pode ouvir em localhost, 127.0.0.1 ou [::1]");
            }
        }

        var urls = configuracao["urls"];
        if (string.IsNullOrWhiteSpace(urls))
        {
            if (!string.IsNullOrWhiteSpace(configuracao["http_ports"]) || !string.IsNullOrWhiteSpace(configuracao["https_ports"]))
            {
                throw new ConfiguracaoInvalidaException("HTTP_PORTS/HTTPS_PORTS",
                    "fazem o Kestrel ouvir em todas as interfaces. A API nao tem autenticacao: use ASPNETCORE_URLS=http://localhost:<porta>");
            }

            builder.WebHost.UseUrls(ConfiguracaoApi.UrlPadrao);
            return;
        }

        var recusada = ValidadorEnderecosLoopback.PrimeiraNaoLoopback(urls);
        if (recusada is not null)
        {
            throw new ConfiguracaoInvalidaException("urls/ASPNETCORE_URLS",
                $"contem um endereco que nao e loopback ({recusada}). A API nao tem autenticacao e so pode ouvir em localhost, 127.0.0.1 ou [::1]");
        }
    }

    private static void ConfigurarRateLimiter(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions opcoes, int limitePorMinuto)
    {
        opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
        opcoes.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            RateLimitPartition.GetFixedWindowLimiter(
                contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido",
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limitePorMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

        opcoes.OnRejected = async (contexto, cancelamento) =>
        {
            var http = contexto.HttpContext;
            var log = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiMapas.Api.RateLimiter");
            LogsInicializacao.RateLimitRecusou(log, limitePorMinuto);

            if (contexto.Lease.TryGetMetadata(MetadataName.RetryAfter, out var esperar))
            {
                http.Response.Headers.RetryAfter = ((int)Math.Ceiling(esperar.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
            }

            http.Response.StatusCode = StatusCodes.Status429TooManyRequests;
            var problemas = http.RequestServices.GetRequiredService<IProblemDetailsService>();
            await problemas.WriteAsync(new ProblemDetailsContext
            {
                HttpContext = http,
                ProblemDetails = new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Type = ProblemasApi.Tipo429,
                    Title = ProblemasApi.Titulo429,
                    Detail = ProblemasApi.Detalhe429,
                },
            }).ConfigureAwait(false);
        };
    }
}
