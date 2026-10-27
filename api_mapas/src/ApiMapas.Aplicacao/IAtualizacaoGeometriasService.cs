using ApiMapas.Dominio;

namespace ApiMapas.Aplicacao;

/// <summary>Caso de uso: gravar a geometria de uma coluna de public.mapas para um id existente (PUT; D-N6).</summary>
public interface IAtualizacaoGeometriasService
{
    /// <summary>
    /// Na ordem do contrato: id (sem ler o corpo nem tocar no banco), Content-Type e tamanho (via
    /// <paramref name="leitorCorpo"/>), JSON, geometria e banco. Idempotente; nunca cria id. O prazo total (30 s) cobre todas as
    /// etapas e e ligado a <paramref name="cancellationToken"/>.
    /// </summary>
    /// <param name="idTexto">Texto bruto recebido na rota.</param>
    /// <param name="coluna">Coluna fixada pelo endpoint.</param>
    /// <param name="leitorCorpo">Leitura do corpo feita pela API.</param>
    /// <param name="traceId">TraceId da requisicao (log de auditoria).</param>
    /// <param name="cancellationToken">HttpContext.RequestAborted.</param>
    /// <exception cref="PrazoAtualizacaoEstouradoException">Prazo total estourado (500).</exception>
    /// <exception cref="OperationCanceledException">Cliente desconectou (499).</exception>
    Task<ResultadoAtualizacao> AtualizarAsync(
        string? idTexto,
        ColunaMapa coluna,
        ILeitorCorpo leitorCorpo,
        string traceId,
        CancellationToken cancellationToken);
}
