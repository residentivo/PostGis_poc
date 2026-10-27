using System.Globalization;
using ApiMapas.Api.Erros;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace ApiMapas.Api.Endpoints;

/// <summary>
/// GET /api/mapas/{id}/graphy e GET /api/mapas/{id}/metry (uma geometria por id - D-N7), e a escrita:
/// PUT /api/mapas/{id}/graphy|metry (D-N6) e POST /api/mapas/graphy|metry (D-N5).
/// {id} SEM constraint :int: chega como texto e e validado no dominio (IdMapa) para devolver
/// 400 ProblemDetails, nunca 404 de roteamento (RA-4).
/// </summary>
public static class MapasEndpoints
{
    public const string RotaGraphy = "/api/mapas/{id}/graphy";
    public const string RotaMetry = "/api/mapas/{id}/metry";

    /// <summary>D-N5: POST sem id na rota (o id vem da identity).</summary>
    public const string RotaPostGraphy = "/api/mapas/graphy";

    /// <summary>D-N5: POST sem id na rota (o id vem da identity).</summary>
    public const string RotaPostMetry = "/api/mapas/metry";

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

    /// <summary>
    /// PUT /api/mapas/{id}/graphy|metry e POST /api/mapas/graphy|metry. So e chamado com a escrita
    /// habilitada (D-P9, D-N10); sem ele, as rotas nao sao mapeadas: o PUT responde 405 e o POST 404
    /// (RS-21). Sem method override e sem CORS (RP-12, RS-3). O corpo e lido pelo
    /// <see cref="LeitorCorpoHttp"/>, nunca pelo binding automatico (RS-1). Todos levam o
    /// <see cref="MarcadorEscritaMapas"/> (filtro de origem e limitadores de escrita compartilhados).
    /// </summary>
    public static IEndpointRouteBuilder MapearEndpointsEscrita(this IEndpointRouteBuilder rotas)
    {
        ArgumentNullException.ThrowIfNull(rotas);

        rotas.MapPut(RotaGraphy, ([FromRoute] string id, [FromServices] IAtualizacaoGeometriasService servico,
                    [FromServices] OpcoesAtualizacao opcoes, HttpContext contexto) =>
                AtualizarAsync(id, ColunaMapa.Graphy, servico, opcoes, contexto))
            .WithName("AtualizarGeometriasGraphyPorId")
            .WithMetadata(MarcadorEscritaMapas.Instancia);

        rotas.MapPut(RotaMetry, ([FromRoute] string id, [FromServices] IAtualizacaoGeometriasService servico,
                    [FromServices] OpcoesAtualizacao opcoes, HttpContext contexto) =>
                AtualizarAsync(id, ColunaMapa.Metry, servico, opcoes, contexto))
            .WithName("AtualizarGeometriasMetryPorId")
            .WithMetadata(MarcadorEscritaMapas.Instancia);

        rotas.MapPost(RotaPostGraphy, ([FromServices] ICriacaoGeometriasService servico,
                    [FromServices] OpcoesAtualizacao opcoes, HttpContext contexto) =>
                CriarAsync(ColunaMapa.Graphy, servico, opcoes, contexto))
            .WithName("CriarGeometriaGraphy")
            .WithMetadata(MarcadorEscritaMapas.Instancia);

        rotas.MapPost(RotaPostMetry, ([FromServices] ICriacaoGeometriasService servico,
                    [FromServices] OpcoesAtualizacao opcoes, HttpContext contexto) =>
                CriarAsync(ColunaMapa.Metry, servico, opcoes, contexto))
            .WithName("CriarGeometriaMetry")
            .WithMetadata(MarcadorEscritaMapas.Instancia);

        return rotas;
    }

    internal static async Task<Results<NoContent, ProblemHttpResult>> AtualizarAsync(
        string id,
        ColunaMapa coluna,
        IAtualizacaoGeometriasService servico,
        OpcoesAtualizacao opcoes,
        HttpContext contexto)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? contexto.TraceIdentifier;
        var leitor = new LeitorCorpoHttp(contexto, opcoes.LimiteBytesCorpo);

        // RP-4: o servico liga o prazo de 30 s a este token; cancelamento chega ate o banco.
        var resultado = await servico.AtualizarAsync(id, coluna, leitor, traceId, contexto.RequestAborted).ConfigureAwait(false);

        // RA-11: 400/413/415 usam o modelo da rota; 404/409 usam o id JA VALIDADO.
        var modeloRota = coluna == ColunaMapa.Graphy ? RotaGraphy : RotaMetry;

        switch (resultado)
        {
            case ResultadoAtualizacao.Sucesso:
                return TypedResults.NoContent();

            case ResultadoAtualizacao.TipoMidiaNaoSuportado:
                return Problema(contexto, StatusCodes.Status415UnsupportedMediaType, ProblemasApi.Tipo415,
                    ProblemasApi.Titulo415, ProblemasApi.Detalhe415, modeloRota);

            case ResultadoAtualizacao.CorpoMuitoGrande grande:
                return Problema(contexto, StatusCodes.Status413PayloadTooLarge, ProblemasApi.Tipo413,
                    ProblemasApi.Titulo413, ProblemasApi.Detalhe413(grande.Limite), modeloRota);

            case ResultadoAtualizacao.CorpoInvalido:
                return Problema(contexto, StatusCodes.Status400BadRequest, ProblemasApi.Tipo400,
                    ProblemasApi.Titulo400Corpo, ProblemasApi.Detalhe400Corpo, modeloRota);

            case ResultadoAtualizacao.GeometriaInvalida invalida:
                return Problema(contexto, StatusCodes.Status400BadRequest, ProblemasApi.Tipo400,
                    ProblemasApi.Titulo400Geometria, ProblemasApi.Detalhe400Geometria(invalida.Motivo), modeloRota);

            case ResultadoAtualizacao.NaoEncontrado naoEncontrado:
                return Problema(contexto, StatusCodes.Status404NotFound, ProblemasApi.Tipo404,
                    ProblemasApi.Titulo404Mapa, ProblemasApi.Detalhe404Mapa(naoEncontrado.Id),
                    InstanceComId(naoEncontrado.Id, naoEncontrado.Coluna));

            case ResultadoAtualizacao.Conflito conflito:
                return Problema(contexto, StatusCodes.Status409Conflict, ProblemasApi.Tipo409,
                    ProblemasApi.Titulo409, ProblemasApi.Detalhe409(conflito.Id),
                    InstanceComId(conflito.Id, conflito.Coluna));

            default:
                // ResultadoAtualizacao.IdInvalido
                return Problema(contexto, StatusCodes.Status400BadRequest, ProblemasApi.Tipo400,
                    ProblemasApi.Titulo400, ProblemasApi.Detalhe400, modeloRota);
        }
    }

    /// <summary>
    /// POST (D-N5): 201 via TypedResults.Created, SEM corpo, com Location relativo
    /// /api/mapas/{id}/{coluna} montado a partir do int do RETURNING id (RS-4, RS-25). Nao e idempotente.
    /// </summary>
    internal static async Task<Results<Created, ProblemHttpResult>> CriarAsync(
        ColunaMapa coluna,
        ICriacaoGeometriasService servico,
        OpcoesAtualizacao opcoes,
        HttpContext contexto)
    {
        var traceId = System.Diagnostics.Activity.Current?.Id ?? contexto.TraceIdentifier;
        var leitor = new LeitorCorpoHttp(contexto, opcoes.LimiteBytesCorpo);

        var resultado = await servico.CriarAsync(coluna, leitor, traceId, contexto.RequestAborted).ConfigureAwait(false);

        // RA-11: instance fixo (rota do POST); nada vem do usuario.
        var modeloRota = coluna == ColunaMapa.Graphy ? RotaPostGraphy : RotaPostMetry;

        switch (resultado)
        {
            case ResultadoCriacao.Sucesso sucesso:
                return TypedResults.Created(InstanceComId(sucesso.Id, sucesso.Coluna));

            case ResultadoCriacao.TipoMidiaNaoSuportado:
                return Problema(contexto, StatusCodes.Status415UnsupportedMediaType, ProblemasApi.Tipo415,
                    ProblemasApi.Titulo415, ProblemasApi.Detalhe415, modeloRota);

            case ResultadoCriacao.CorpoMuitoGrande grande:
                return Problema(contexto, StatusCodes.Status413PayloadTooLarge, ProblemasApi.Tipo413,
                    ProblemasApi.Titulo413, ProblemasApi.Detalhe413(grande.Limite), modeloRota);

            case ResultadoCriacao.GeometriaInvalida invalida:
                return Problema(contexto, StatusCodes.Status400BadRequest, ProblemasApi.Tipo400,
                    ProblemasApi.Titulo400Geometria, ProblemasApi.Detalhe400Geometria(invalida.Motivo), modeloRota);

            case ResultadoCriacao.Conflito:
                return Problema(contexto, StatusCodes.Status409Conflict, ProblemasApi.Tipo409,
                    ProblemasApi.Titulo409, ProblemasApi.Detalhe409Criacao, modeloRota);

            default:
                // ResultadoCriacao.CorpoInvalido
                return Problema(contexto, StatusCodes.Status400BadRequest, ProblemasApi.Tipo400,
                    ProblemasApi.Titulo400Corpo, ProblemasApi.Detalhe400Corpo, modeloRota);
        }
    }

    private static string InstanceComId(int id, string coluna) =>
        $"/api/mapas/{id.ToString(CultureInfo.InvariantCulture)}/{coluna}";

    private static ProblemHttpResult Problema(HttpContext contexto, int status, string tipo, string titulo, string detalhe, string instance)
    {
        contexto.Items[ProblemasApi.ChaveInstance] = instance;
        return TypedResults.Problem(new ProblemDetails
        {
            Status = status,
            Type = tipo,
            Title = titulo,
            Detail = detalhe,
            Instance = instance,
        });
    }

    internal static async Task<Results<Ok<RespostaGeometriaDto>, ProblemHttpResult>> ConsultarAsync(
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
                return Problema(contexto, StatusCodes.Status404NotFound, ProblemasApi.Tipo404,
                    ProblemasApi.Titulo404, ProblemasApi.Detalhe404(naoEncontrado.Coluna, naoEncontrado.Id),
                    InstanceComId(naoEncontrado.Id, naoEncontrado.Coluna));

            default:
                // RA-11: instance fixo (modelo da rota), sem o valor recebido.
                return Problema(contexto, StatusCodes.Status400BadRequest, ProblemasApi.Tipo400,
                    ProblemasApi.Titulo400, ProblemasApi.Detalhe400, coluna == ColunaMapa.Graphy ? RotaGraphy : RotaMetry);
        }
    }
}
