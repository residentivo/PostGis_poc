using System.Diagnostics;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace ApiMapas.Api.Erros;

/// <summary>
/// RA-6 / RA-10 / CA-4: qualquer excecao nao tratada vira 500 ProblemDetails generico, em
/// qualquer ambiente. A excecao completa vai so para o log, junto com o traceId; o corpo
/// leva apenas title/detail genericos e o traceId.
/// </summary>
public sealed partial class ManipuladorExcecoes : IExceptionHandler
{
    /// <summary>Status nao padrao (nginx) para requisicao abortada pelo cliente; sem corpo.</summary>
    public const int StatusClienteDesconectou = 499;

    private readonly IProblemDetailsService _problemDetails;
    private readonly ILogger<ManipuladorExcecoes> _log;

    public ManipuladorExcecoes(IProblemDetailsService problemDetails, ILogger<ManipuladorExcecoes> log)
    {
        _problemDetails = problemDetails;
        _log = log;
    }

    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(httpContext);
        ArgumentNullException.ThrowIfNull(exception);

        var traceId = Activity.Current?.Id ?? httpContext.TraceIdentifier;

        // Rota (modelo) em vez do caminho bruto: o caminho pode conter entrada do usuario (RA-10).
        // O UseExceptionHandler limpa o endpoint do contexto; o original fica em IExceptionHandlerFeature.
        var endpoint = httpContext.Features.Get<IExceptionHandlerFeature>()?.Endpoint ?? httpContext.GetEndpoint();
        var rota = (endpoint as RouteEndpoint)?.RoutePattern.RawText ?? "(sem rota)";

        if (exception is OperationCanceledException && httpContext.RequestAborted.IsCancellationRequested)
        {
            LogCancelada(httpContext.Request.Method, rota, traceId);
            httpContext.Response.StatusCode = StatusClienteDesconectou;
            return true;
        }

        LogErroNaoTratado(exception, httpContext.Request.Method, rota, traceId, exception.GetType().Name);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        // Exception NAO e repassada ao ProblemDetailsContext: nada da excecao vai ao corpo.
        return await _problemDetails.TryWriteAsync(new ProblemDetailsContext
        {
            HttpContext = httpContext,
            ProblemDetails = new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Type = ProblemasApi.Tipo500,
                Title = ProblemasApi.Titulo500,
                Detail = ProblemasApi.Detalhe500,
            },
        }).ConfigureAwait(false);
    }

    [LoggerMessage(EventId = 3001, Level = LogLevel.Information, Message = "Requisicao {Metodo} {Rota} cancelada pelo cliente (traceId {TraceId}).")]
    private partial void LogCancelada(string metodo, string rota, string traceId);

    [LoggerMessage(EventId = 3002, Level = LogLevel.Error, Message = "Erro nao tratado em {Metodo} {Rota} (traceId {TraceId}, tipo {TipoErro}).")]
    private partial void LogErroNaoTratado(Exception ex, string metodo, string rota, string traceId, string tipoErro);
}
