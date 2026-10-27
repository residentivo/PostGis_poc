using ApiMapas.Api.Endpoints;
using ApiMapas.Api.Erros;
using ApiMapas.Api.Inicializacao;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace ApiMapas.Api.Seguranca;

/// <summary>
/// RS-2 / RS-22: recusa com 403 a escrita (PUT e POST) que pareca vir de um navegador de outra
/// origem. Registrado entre UseStatusCodePages e UseRateLimiter (nunca como filtro de endpoint), para
/// que o 403 saia SEM consumir os limites de requisicao (CN-7). Atua so nos endpoints com
/// <see cref="MarcadorEscritaMapas"/>; o GET nao e afetado. Responde 403 quando:
/// - o cabecalho Origin esta presente (qualquer valor, inclusive vazio ou "null");
/// - Sec-Fetch-Site chega com mais de um valor (cabecalho repetido ou lista separada por virgula);
/// - Sec-Fetch-Site, depois do trim, nao e "none" nem "same-origin" (sem diferenciar maiusculas).
/// Sec-Fetch-Site ausente e aceito (clientes que nao sao navegador, como curl e Postman).
/// Nao le o corpo, nao ecoa nenhum cabecalho na resposta (ProblemDetails de texto fixo) e registra
/// log Debug sem os valores dos cabecalhos.
/// </summary>
public sealed class OrigemEscritaMiddleware
{
    public const string CabecalhoSecFetchSite = "Sec-Fetch-Site";
    public const string ValorNone = "none";
    public const string ValorSameOrigin = "same-origin";

    private readonly RequestDelegate _proximo;

    public OrigemEscritaMiddleware(RequestDelegate proximo)
    {
        ArgumentNullException.ThrowIfNull(proximo);
        _proximo = proximo;
    }

    public async Task InvokeAsync(HttpContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        if (contexto.GetEndpoint()?.Metadata.GetMetadata<MarcadorEscritaMapas>() is null)
        {
            await _proximo(contexto).ConfigureAwait(false);
            return;
        }

        var log = contexto.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("ApiMapas.Api.OrigemEscrita");
        var metodo = MetodoParaLog(contexto.Request.Method);
        var motivo = MotivoRecusa(contexto.Request.Headers);
        if (motivo is null)
        {
            LogsInicializacao.OrigemAprovada(log, metodo);
            await _proximo(contexto).ConfigureAwait(false);
            return;
        }

        LogsInicializacao.OrigemRecusada(log, metodo, motivo);
        contexto.Response.StatusCode = StatusCodes.Status403Forbidden;
        var problemas = contexto.RequestServices.GetRequiredService<IProblemDetailsService>();
        await problemas.WriteAsync(new ProblemDetailsContext
        {
            HttpContext = contexto,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status403Forbidden,
                Type = ProblemasApi.Tipo403,
                Title = ProblemasApi.Titulo403Origem,
                Detail = ProblemasApi.Detalhe403Origem,
            },
        }).ConfigureAwait(false);
    }

    /// <summary>
    /// Devolve o motivo FIXO da recusa (sem valores de cabecalho) ou null quando a requisicao passa.
    /// </summary>
    internal static string? MotivoRecusa(IHeaderDictionary cabecalhos)
    {
        ArgumentNullException.ThrowIfNull(cabecalhos);

        if (cabecalhos.ContainsKey(HeaderNames.Origin))
        {
            return "cabecalho Origin presente";
        }

        if (!cabecalhos.TryGetValue(CabecalhoSecFetchSite, out var valores))
        {
            return null;
        }

        if (valores.Count != 1 || valores[0] is null || valores[0]!.Contains(',', StringComparison.Ordinal))
        {
            return "Sec-Fetch-Site com mais de um valor";
        }

        var valor = valores[0]!.Trim();
        if (string.Equals(valor, ValorNone, StringComparison.OrdinalIgnoreCase)
            || string.Equals(valor, ValorSameOrigin, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        return "Sec-Fetch-Site diferente de none/same-origin";
    }

    /// <summary>So PUT ou POST no log (o metodo vem do roteamento, mas nunca e ecoado como texto livre).</summary>
    private static string MetodoParaLog(string metodo) =>
        HttpMethods.IsPost(metodo) ? "POST" : HttpMethods.IsPut(metodo) ? "PUT" : "outro";
}
