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

    // ---- PUT /api/mapas/{id}/graphy|metry e POST /api/mapas/graphy|metry. Nada ecoa o corpo. ----

    public const string Titulo400Corpo = "Corpo inválido";
    public const string Detalhe400Corpo = "O corpo deve ser um JSON no formato {\"wkt\":\"...\",\"srid\":4326}.";

    public const string Tipo403 = "https://tools.ietf.org/html/rfc9110#section-15.5.4";
    public const string Titulo403Origem = "Origem não permitida";
    public const string Detalhe403Origem = "Escrita aceita somente sem Origin e com Sec-Fetch-Site ausente, none ou same-origin.";

    public const string Titulo400Geometria = "Geometria inválida";

    public const string Titulo404Mapa = "Mapa não encontrado";

    public const string Tipo409 = "https://tools.ietf.org/html/rfc9110#section-15.5.10";
    public const string Titulo409 = "Atualização concorrente";

    public const string Tipo413 = "https://tools.ietf.org/html/rfc9110#section-15.5.14";
    public const string Titulo413 = "Corpo muito grande";

    public const string Tipo415 = "https://tools.ietf.org/html/rfc9110#section-15.5.16";
    public const string Titulo415 = "Tipo de mídia não suportado";
    public const string Detalhe415 = "Use Content-Type: application/json (charset utf-8 opcional).";

    /// <summary>Detalhe 400 da geometria: so o motivo da lista fixa, sem indice (corpo com uma geometria so; nunca o WKT).</summary>
    public static string Detalhe400Geometria(string motivo) => motivo;

    /// <summary>Detalhe 404 do PUT com o id JA VALIDADO.</summary>
    public static string Detalhe404Mapa(int id) =>
        $"Nenhuma linha encontrada em public.mapas para o id {id.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";

    /// <summary>Detalhe 409 com o id JA VALIDADO.</summary>
    public static string Detalhe409(int id) =>
        $"Outra atualização do id {id.ToString(System.Globalization.CultureInfo.InvariantCulture)} está em andamento. Tente novamente.";

    /// <summary>Detalhe 409 do POST (RS-18): mesmo titulo do PUT, sem id (o id ainda nao existe).</summary>
    public const string Detalhe409Criacao = "Outra atualização de public.mapas está em andamento. Tente novamente.";

    /// <summary>Detalhe 413 com o limite configurado.</summary>
    public static string Detalhe413(long limite) =>
        $"O corpo excede o limite de {limite.ToString(System.Globalization.CultureInfo.InvariantCulture)} bytes.";

    /// <summary>Chave em HttpContext.Items com o "instance" definido pelo endpoint.</summary>
    public const string ChaveInstance = "ApiMapas.Instance";

    /// <summary>Detalhe 404 com o id JA VALIDADO (inteiro) e o nome fixo da coluna.</summary>
    public static string Detalhe404(string coluna, int id) =>
        $"Nenhuma geometria encontrada na coluna '{coluna}' para o id {id.ToString(System.Globalization.CultureInfo.InvariantCulture)}.";

    /// <summary>
    /// Ajuste comum de todos os ProblemDetails: traceId sempre presente e "instance" somente
    /// quando definido pelo endpoint (fixo no 400, id validado no 404). Nos demais casos
    /// (rota desconhecida, 403 de origem, 405, 429, 500) o instance e omitido para nao refletir o caminho bruto.
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
