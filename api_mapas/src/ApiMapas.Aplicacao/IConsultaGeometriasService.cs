using ApiMapas.Dominio;

namespace ApiMapas.Aplicacao;

/// <summary>Caso de uso: geometrias de public.mapas por id em uma coluna.</summary>
public interface IConsultaGeometriasService
{
    /// <summary>
    /// Valida o id (sem tocar no banco quando invalido) e consulta a coluna.
    /// </summary>
    /// <param name="idTexto">Texto bruto recebido na rota.</param>
    /// <param name="coluna">Coluna fixada pelo endpoint.</param>
    /// <param name="cancellationToken">HttpContext.RequestAborted (RA-12).</param>
    Task<ResultadoConsulta> ConsultarAsync(string? idTexto, ColunaMapa coluna, CancellationToken cancellationToken);
}
