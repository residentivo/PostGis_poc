using ApiMapas.Api.Erros;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ApiMapas.Api.Endpoints;

/// <summary>
/// GET /api/mapas/{id}/graphy e GET /api/mapas/{id}/metry (contrato A1).
/// {id} SEM constraint :int: chega como texto e e validado no dominio (IdMapa) para devolver
/// 400 ProblemDetails, nunca 404 de roteamento (RA-4).
/// </summary>
public static class MapasEndpoints
{
    public const string RotaGraphy = "/api/mapas/{id}/graphy";
    public const string RotaMetry = "/api/mapas/{id}/metry";

    public static IEndpointRouteBuilder MapearEndpointsMapas(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapGet(RotaGraphy, (string id, IConsultaGeometriasService servico, HttpContext contexto) =>
                ConsultarAsync(id, ColunaMapa.Graphy, servico, contexto))
            .WithName("GeometriasGraphyPorId");

        rotas.MapGet(RotaMetry, (string id, IConsultaGeometriasService servico, HttpContext contexto) =>
                ConsultarAsync(id, ColunaMapa.Metry, servico, contexto))
            .WithName("GeometriasMetryPorId");

        return rotas;
    }

    internal static async Task<Results<Ok<RespostaGeometriasDto>, ProblemHttpResult>> ConsultarAsync(
        string id,
        ColunaMapa coluna,
        IConsultaGeometriasService servico,
        HttpContext contexto)
    {
        // RA-12: o cancelamento da requisicao chega ate o ExecuteReaderAsync.
        var resultado = await servico.ConsultarAsync(id, coluna, contexto.RequestAborted).ConfigureAwait(false);

        switch (resultado)
        {
            case ResultadoConsulta.Sucesso sucesso:
                return TypedResults.Ok(sucesso.Resposta);

            case ResultadoConsulta.NaoEncontrado naoEncontrado:
                var instance404 = $"/api/mapas/{naoEncontrado.Id.ToString(System.Globalization.CultureInfo.InvariantCulture)}/{naoEncontrado.Coluna}";
                contexto.Items[ProblemasApi.ChaveInstance] = instance404;
                return TypedResults.Problem(new ProblemDetails
                {
                    Status = StatusCodes.Status404NotFound,
                    Type = ProblemasApi.Tipo404,
                    Title = ProblemasApi.Titulo404,
                    Detail = ProblemasApi.Detalhe404(naoEncontrado.Coluna, naoEncontrado.Id),
                    Instance = instance404,
                });

            default:
                // RA-11: instance fixo (modelo da rota), sem o valor recebido.
                var instance400 = coluna == ColunaMapa.Graphy ? RotaGraphy : RotaMetry;
                contexto.Items[ProblemasApi.ChaveInstance] = instance400;
                return TypedResults.Problem(new ProblemDetails
                {
                    Status = StatusCodes.Status400BadRequest,
                    Type = ProblemasApi.Tipo400,
                    Title = ProblemasApi.Titulo400,
                    Detail = ProblemasApi.Detalhe400,
                    Instance = instance400,
                });
        }
    }
}
