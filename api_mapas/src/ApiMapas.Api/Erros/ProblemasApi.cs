using System.Diagnostics;
using Microsoft.AspNetCore.Http;

namespace ApiMapas.Api.Erros;

/// <summary>Textos fixos dos ProblemDetails (contrato A1). Nenhum ecoa entrada do usuario (RA-11).</summary>
public static class ProblemasApi
{
    public const string Tipo400 = "https://tools.ietf.org/html/rfc9110#section-15.5.1";
    public const string Titulo400 = "Id inválido";
    public const string Detalhe400 = "O id deve ser um inteiro entre 1 e 2147483647.";

    public const string Tipo404 = "https://tools.ietf.org/html/rfc9110#section-15.5.5";
    public const string Titulo404 = "Geometria não encontrada";

    public const string Tipo429 = "https://tools.ietf.org/html/rfc6585#section-4";
    public const string Titulo429 = "Muitas requisições";
    public const string Detalhe429 = "Limite de requisições por minuto atingido. Tente novamente em instantes.";

    public const string Tipo500 = "https://tools.ietf.org/html/rfc9110#section-15.6.1";
    public const string Titulo500 = "Erro interno";
    public const string Detalhe500 = "Não foi possível processar a requisição. Informe o traceId ao suporte.";

    /// <summary>Chave em HttpContext.Items com o "instance" definido pelo endpoint.</summary>
    public const string ChaveInstance = "ApiMapas.Instance";

    /// <summary>Detalhe 404 com o id JA VALIDADO (inteiro) e o nome fixo da coluna.</summary>
    public static string Detalhe404(string coluna, int id) =>
        $"Nenhuma geometria encontrada na coluna '{coluna}' para o id {id.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";

    /// <summary>
    /// Ajuste comum de todos os ProblemDetails: traceId sempre presente e "instance" somente
    /// quando definido pelo endpoint (fixo no 400, id validado no 404). Nos demais casos
    /// (rota desconhecida, 405, 429, 500) o instance e omitido para nao refletir o caminho bruto.
    /// </summary>
    public static void Personalizar(ProblemDetailsContext contexto)
    {
        ArgumentNullException.ThrowIfNull(contexto);

        var http = contexto.HttpContext;
        contexto.ProblemDetails.Instance = http.Items.TryGetValue(ChaveInstance, out var instance) ? instance as string : null;
        contexto.ProblemDetails.Extensions["traceId"] = Activity.Current?.Id ?? http.TraceIdentifier;

        // Remove extensoes que o framework possa acrescentar com detalhes internos.
        contexto.ProblemDetails.Extensions.Remove("exception");
        contexto.ProblemDetails.Extensions.Remove("errors");
    }
}
