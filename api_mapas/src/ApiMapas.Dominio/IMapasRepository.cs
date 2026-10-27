namespace ApiMapas.Dominio;

/// <summary>
/// Leitura (somente SELECT) de UMA geometria de public.mapas por id (D-N7).
/// Contrato do dominio; a implementacao Npgsql fica em ApiMapas.Infraestrutura.
/// </summary>
public interface IMapasRepository
{
    /// <summary>
    /// Le a linha do id (consultas Q1/Q2 com LIMIT 2 - RS-23) numa conexao propria, liberada ao
    /// final. Devolve <see cref="LeituraMapa.NaoEncontrada"/> (0 linhas),
    /// <see cref="LeituraMapa.ColunaVazia"/> (1 linha com a coluna NULL),
    /// <see cref="LeituraMapa.Duplicada"/> (2 linhas: migracao nao aplicada) ou
    /// <see cref="LeituraMapa.Encontrada"/>.
    /// </summary>
    Task<LeituraMapa> ObterPorIdAsync(IdMapa id, ColunaMapa coluna, CancellationToken cancellationToken);
}
