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

        // ---- D-P9 / RP-7 / RP-8: conexao de ESCRITA opcional (PG_ESCRITA_*); null = escrita (PUT e POST) desligada ----
        var conexaoEscrita = ConexaoEscritaConfig.Carregar(lerVariavel, conexao, out var motivoEscritaDesligada);

        // ---- RA-7: cabecalhos em todas as respostas; sem header Server ----
        builder.Services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, CabecalhosSegurancaStartupFilter>());
        builder.WebHost.ConfigureKestrel(opcoes => opcoes.AddServerHeader = false);

        // ---- RA-6 / RA-10 / RA-11: ProblemDetails e tratamento global de excecoes ----
        builder.Services.AddProblemDetails(opcoes => opcoes.CustomizeProblemDetails = ProblemasApi.Personalizar);
        builder.Services.AddExceptionHandler<ManipuladorExcecoes>();

        // ---- REC-A1: rate limiting por IP ----
        builder.Services.AddRateLimiter(opcoes => ConfigurarRateLimiter(
            opcoes, configuracaoApi.LimiteRequisicoesPorMinuto, configuracaoApi.LimiteEscritasPorMinuto));

        // ---- Camadas (DDD) ----
        builder.Services.AddSingleton(conexao);
        builder.Services.AddSingleton<NpgsqlDataSource>(_ => conexao.CriarDataSource());
        builder.Services.AddSingleton(configuracaoApi.CriarOpcoesConsulta());
        builder.Services.AddScoped<IMapasRepository, MapasRepository>();
        builder.Services.AddScoped<IConsultaGeometriasService, ConsultaGeometriasService>();

        // ---- Escrita (PUT e POST): pool proprio, separado do de leitura (CP-10) ----
        var opcoesAtualizacao = configuracaoApi.CriarOpcoesAtualizacao();
        builder.Services.AddSingleton(opcoesAtualizacao);
        if (conexaoEscrita is not null)
        {
            builder.Services.AddSingleton(conexaoEscrita);
            builder.Services.AddSingleton(_ => conexaoEscrita.CriarFonteDados());
            builder.Services.AddSingleton<IVerificadorRoleEscrita, VerificadorRoleEscrita>();
            builder.Services.AddScoped<IMapasEscritaRepository, MapasEscritaRepository>();
            builder.Services.AddScoped<IAtualizacaoGeometriasService, AtualizacaoGeometriasService>();
            builder.Services.AddScoped<ICriacaoGeometriasService, CriacaoGeometriasService>();
        }

        var app = builder.Build();

        // ---- R-1 / RS-20: uma checagem na subida; privilegio a mais, falta da PK exata ou banco sem
        // resposta desligam a escrita (PUT e POST) ----
        if (conexaoEscrita is not null)
        {
            motivoEscritaDesligada = VerificarRoleEscrita(app.Services, app.Logger);
            if (motivoEscritaDesligada != MotivoEscritaDesligada.Nenhum)
            {
                // C3: escrita desligada; nenhuma conexao da role de escrita fica aberta no pool.
                app.Services.GetRequiredService<FonteDadosEscrita>().DataSource.Clear();
            }
        }

        var destino = conexao.Descricao();
        LogsInicializacao.Iniciando(app.Logger, destino, configuracaoApi.Verboso,
            configuracaoApi.LimiteBytesGeometrias, configuracaoApi.LimiteRequisicoesPorMinuto);
        LogsInicializacao.SemAutenticacao(app.Logger);
        if (conexao.SslInseguro)
        {
            LogsInicializacao.SslInseguro(app.Logger, ConexaoConfig.VarSslMode, conexao.SslMode.ToString());
        }

        // RP-13: informa se a escrita (PUT e POST) esta habilitada (ou o motivo) e avisa da escrita sem autenticacao.
        var escritaHabilitada = conexaoEscrita is not null && motivoEscritaDesligada == MotivoEscritaDesligada.Nenhum;
        if (escritaHabilitada)
        {
            var destinoEscrita = conexaoEscrita!.Descricao();
            var prazoSegundos = (int)opcoesAtualizacao.PrazoTotal.TotalSeconds;
            LogsInicializacao.EscritaHabilitada(app.Logger, destinoEscrita, opcoesAtualizacao.LimiteBytesCorpo,
                configuracaoApi.LimiteEscritasPorMinuto, OpcoesAtualizacao.EscritasSimultaneas, prazoSegundos);
            LogsInicializacao.EscritaSemAutenticacao(app.Logger);
        }
        else
        {
            var motivoTexto = motivoEscritaDesligada.Descrever();
            LogsInicializacao.EscritaDesligada(app.Logger, motivoTexto);
        }

        // RA-6: handler generico em QUALQUER ambiente (inclusive Development). Ele fica dentro da
        // pagina de desenvolvedor que o WebApplication adiciona sozinho em Development, entao toda
        // excecao e tratada aqui antes; a API nunca chama UseDeveloperExceptionPage.
        app.UseExceptionHandler();
        app.UseStatusCodePages();

        // RS-2 / RS-22: 403 de origem nos endpoints de escrita (PUT e POST), ANTES do rate limiter,
        // para que a recusa nao consuma os limites (CN-7). Middleware, nunca filtro de endpoint.
        app.UseMiddleware<OrigemEscritaMiddleware>();
        app.UseRateLimiter();

        // RP-12 / RS-3: PROIBIDO chamar app.UseHttpMethodOverride() (ou qualquer middleware que troque
        // HttpRequest.Method a partir de X-HTTP-Method-Override, X-HTTP-Method, X-Method-Override ou
        // _method). Com ele, um POST de formulario de outra origem viraria PUT sem preflight de CORS,
        // e um POST com override PUT/DELETE deixaria de ser so um INSERT. Esses cabecalhos sao
        // ignorados: o metodo e sempre o da linha de requisicao.

        app.MapearEndpointsMapas();
        if (escritaHabilitada)
        {
            // D-P9 / RS-21: com a escrita desligada, as rotas nao sao mapeadas: o PUT responde 405
            // (a rota GET existe) e o POST responde 404 (a rota nao existe).
            app.MapearEndpointsEscrita();
            LogsInicializacao.EscritaMapeada(app.Logger, MapasEndpoints.RotaGraphy, MapasEndpoints.RotaMetry,
                MapasEndpoints.RotaPostGraphy, MapasEndpoints.RotaPostMetry);
        }

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

    /// <summary>Prazo da checagem R-1 na subida (timeout de conexao + folga).</summary>
    internal const int PrazoVerificacaoRoleSegundos = 15;

    /// <summary>
    /// R-1: executa a checagem da role de escrita uma vez, com prazo proprio. Qualquer falha
    /// (inclusive prazo) desliga a escrita; nunca impede a API de subir.
    /// </summary>
    private static MotivoEscritaDesligada VerificarRoleEscrita(IServiceProvider servicos, ILogger log)
    {
        using var prazo = new CancellationTokenSource(TimeSpan.FromSeconds(PrazoVerificacaoRoleSegundos));
        try
        {
            var verificador = servicos.GetRequiredService<IVerificadorRoleEscrita>();
            return verificador.VerificarAsync(prazo.Token).GetAwaiter().GetResult();
        }
#pragma warning disable CA1031 // O-1: a checagem nunca derruba a API; qualquer falha desliga a escrita.
        catch (Exception ex)
#pragma warning restore CA1031
        {
            // So o nome do tipo (a mensagem pode conter detalhes do banco).
            LogsInicializacao.VerificacaoRoleLancou(log, ex.GetType().Name);
            return MotivoEscritaDesligada.VerificacaoFalhou;
        }
    }

    /// <summary>True para os endpoints de escrita PUT e POST (marcados com <see cref="MarcadorEscritaMapas"/>).</summary>
    private static bool EhEscrita(HttpContext contexto) =>
        contexto.GetEndpoint()?.Metadata.GetMetadata<MarcadorEscritaMapas>() is not null;

    private static string ChaveIp(HttpContext contexto) => contexto.Connection.RemoteIpAddress?.ToString() ?? "desconhecido";

    private static void ConfigurarRateLimiter(Microsoft.AspNetCore.RateLimiting.RateLimiterOptions opcoes, int limitePorMinuto, int limiteEscritasPorMinuto)
    {
        opcoes.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // REC-A1: limite global por IP (todas as rotas).
        var global = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            RateLimitPartition.GetFixedWindowLimiter(
                ChaveIp(contexto),
                _ => new FixedWindowRateLimiterOptions
                {
                    PermitLimit = limitePorMinuto,
                    Window = TimeSpan.FromMinutes(1),
                    QueueLimit = 0,
                    AutoReplenishment = true,
                }));

        // D-P4: escritas (PUT e POST somados) por minuto por IP (soma-se ao global).
        var escritaPorIp = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            EhEscrita(contexto)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    "escrita:" + ChaveIp(contexto),
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limiteEscritasPorMinuto,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })
                : RateLimitPartition.GetNoLimiter("livre"));

        // RS-25: escritas (PUT e POST somados) por minuto na API TODA, sem particao por IP.
        var escritaGlobal = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            EhEscrita(contexto)
                ? RateLimitPartition.GetFixedWindowLimiter(
                    "escrita-global",
                    _ => new FixedWindowRateLimiterOptions
                    {
                        PermitLimit = limiteEscritasPorMinuto,
                        Window = TimeSpan.FromMinutes(1),
                        QueueLimit = 0,
                        AutoReplenishment = true,
                    })
                : RateLimitPartition.GetNoLimiter("livre"));

        // R-2: no maximo 3 escritas (PUT e POST) simultaneas na API toda, sem fila (o excedente responde 429).
        var escritaSimultanea = PartitionedRateLimiter.Create<HttpContext, string>(contexto =>
            EhEscrita(contexto)
                ? RateLimitPartition.GetConcurrencyLimiter(
                    "escrita",
                    _ => new ConcurrencyLimiterOptions
                    {
                        PermitLimit = OpcoesAtualizacao.EscritasSimultaneas,
                        QueueLimit = 0,
                    })
                : RateLimitPartition.GetNoLimiter("livre"));

        // PO5 achado 2: escritaSimultanea vem PRIMEIRO. O encadeamento para no primeiro limitador que
        // recusa, entao uma escrita recusada por simultaneidade (429) nao gasta as cotas de janela fixa
        // (global por IP, escrita por IP e escrita global). Para o GET, escritaSimultanea e NoLimiter.
        opcoes.GlobalLimiter = PartitionedRateLimiter.CreateChained(escritaSimultanea, global, escritaPorIp, escritaGlobal);

        opcoes.OnRejected = async (contexto, cancelamento) =>
        {
            var http = contexto.HttpContext;
            var log = http.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiMapas.Api.RateLimiter");
            if (EhEscrita(http))
            {
                LogsInicializacao.RateLimitEscritaRecusou(log, limiteEscritasPorMinuto, OpcoesAtualizacao.EscritasSimultaneas, limitePorMinuto);
            }
            else
            {
                LogsInicializacao.RateLimitRecusou(log, limitePorMinuto);
            }

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
