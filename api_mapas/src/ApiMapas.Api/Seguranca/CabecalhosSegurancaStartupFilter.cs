namespace ApiMapas.Api.Seguranca;

/// <summary>
/// RA-7: X-Content-Type-Options: nosniff e Cache-Control: no-store em TODAS as respostas,
/// inclusive erros (400/404/405/429/500 e a rejeicao de host do HostFiltering).
/// Registrado como o primeiro IStartupFilter, fica no ponto mais externo do pipeline.
/// Os cabecalhos sao gravados em OnStarting, que roda por ultimo e sobrevive ao
/// Response.Clear() do UseExceptionHandler.
/// </summary>
public sealed class CabecalhosSegurancaStartupFilter : IStartupFilter
{
    public const string ValorNoSniff = "nosniff";
    public const string ValorNoStore = "no-store";

    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        ArgumentNullException.ThrowIfNull(next);

        return app =>
        {
            app.Use(static (contexto, proximo) =>
            {
                contexto.Response.OnStarting(static estado =>
                {
                    var resposta = (HttpResponse)estado;
                    resposta.Headers.XContentTypeOptions = ValorNoSniff;
                    resposta.Headers.CacheControl = ValorNoStore;
                    return Task.CompletedTask;
                }, contexto.Response);

                return proximo(contexto);
            });

            next(app);
        };
    }
}
