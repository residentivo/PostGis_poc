namespace ApiMapas.Dominio;

/// <summary>
/// Leitura (somente SELECT) das geometrias de public.mapas por id.
/// Contrato do dominio; a implementacao Npgsql fica em ApiMapas.Infraestrutura.
/// </summary>
public interface IMapasRepository
{
    /// <summary>
    /// Devolve, em fluxo, as linhas nao nulas da coluna para o id (no maximo 101 - LIMIT 101
    /// das consultas de referencia). Cada chamada usa sua propria conexao, liberada quando a
    /// enumeracao termina ou e interrompida.
    /// </summary>
    IAsyncEnumerable<GeometriaMapa> ListarPorIdAsync(IdMapa id, ColunaMapa coluna, CancellationToken cancellationToken);
}
