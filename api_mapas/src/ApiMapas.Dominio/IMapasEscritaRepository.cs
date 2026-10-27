using ApiMapas.Dominio.Geometrias;

namespace ApiMapas.Dominio;

/// <summary>
/// Escrita (PUT e POST) em public.mapas, uma geometria por id (D-N5, D-N6). Contrato do dominio; a
/// implementacao Npgsql fica em ApiMapas.Infraestrutura e usa SOMENTE a conexao de escrita
/// (api_mapas_escrita). Cada chamada usa uma conexao propria. Nenhum caminho apaga linha (Q4) e
/// nenhum INSERT informa id (a identity gera o id).
/// </summary>
public interface IMapasEscritaRepository
{
    /// <summary>
    /// PUT: valida a geometria no banco (W*.V) e, numa transacao READ COMMITTED com lock_timeout
    /// de 5 s, faz o UPDATE da coluna pela PK (W*.2). rowcount 0 = <see cref="ResultadoEscrita.NaoEncontrado"/>;
    /// 1 = <see cref="ResultadoEscrita.Atualizado"/>; mais de 1 = ROLLBACK e excecao (500).
    /// Qualquer falha, cancelamento ou prazo estourado faz ROLLBACK.
    /// </summary>
    Task<ResultadoEscrita> AtualizarAsync(IdMapa id, ColunaMapa coluna, GeometriaEntrada geometria, CancellationToken cancellationToken);

    /// <summary>
    /// POST: valida a geometria no banco (W*.V) e, numa transacao READ COMMITTED com lock_timeout
    /// de 5 s, faz o INSERT da coluna com RETURNING id (W3/W4). Devolve
    /// <see cref="ResultadoEscrita.Criado"/> com o id gerado pela identity. Nao e idempotente.
    /// </summary>
    Task<ResultadoEscrita> CriarAsync(ColunaMapa coluna, GeometriaEntrada geometria, CancellationToken cancellationToken);
}
