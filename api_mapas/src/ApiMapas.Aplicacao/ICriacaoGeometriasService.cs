using ApiMapas.Dominio;

namespace ApiMapas.Aplicacao;

/// <summary>Caso de uso: criar uma linha nova em public.mapas com a geometria de uma coluna (POST; D-N5).</summary>
public interface ICriacaoGeometriasService
{
    /// <summary>
    /// Na ordem do contrato (D-N9): Content-Type e tamanho (via <paramref name="leitorCorpo"/>), JSON,
    /// geometria e banco. O prazo total (30 s) cobre todas as etapas e e ligado a
    /// <paramref name="cancellationToken"/>. Nao e idempotente: cada chamada bem-sucedida cria um id novo.
    /// </summary>
    /// <param name="coluna">Coluna fixada pelo endpoint.</param>
    /// <param name="leitorCorpo">Leitura do corpo feita pela API.</param>
    /// <param name="traceId">TraceId da requisicao (log de auditoria).</param>
    /// <param name="cancellationToken">HttpContext.RequestAborted.</param>
    /// <exception cref="PrazoAtualizacaoEstouradoException">Prazo total estourado (500).</exception>
    /// <exception cref="OperationCanceledException">Cliente desconectou (499).</exception>
    Task<ResultadoCriacao> CriarAsync(
        ColunaMapa coluna,
        ILeitorCorpo leitorCorpo,
        string traceId,
        CancellationToken cancellationToken);
}
