namespace TesteGraphy.Dados;

/// <summary>
/// Acesso a public.mapas.graphy. Cada operacao usa sua propria conexao/comando.
/// Implementacoes devem traduzir erros para <see cref="FalhaBancoException"/>
/// (erro do servidor) ou <see cref="FalhaConexaoException"/> (conexao/timeout).
/// </summary>
public interface IGraphyRepository
{
    /// <summary>ST_IsValid + GeometryType do WKT com SRID 4326 (dupla validacao - R4).</summary>
    Task<ValidacaoBanco> ValidarNoBancoAsync(string wkt, CancellationToken cancellationToken = default);

    /// <summary>INSERT em public.mapas (graphy) sem informar id; retorna o id do RETURNING.</summary>
    Task<int> InserirAsync(string wkt, CancellationToken cancellationToken = default);

    /// <summary>Leitura de conferencia por id; null quando o id nao existe.</summary>
    Task<RegistroGraphy?> LerAsync(int id, CancellationToken cancellationToken = default);
}
