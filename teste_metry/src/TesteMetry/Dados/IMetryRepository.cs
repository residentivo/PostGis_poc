namespace TesteMetry.Dados;

/// <summary>
/// Acesso a public.mapas.metry. Cada operacao usa sua propria conexao/comando.
/// O SRID e sempre o int validado da configuracao (RQ-1) e vai ao banco como @srid Integer.
/// Implementacoes devem traduzir erros para <see cref="FalhaBancoException"/>
/// (erro do servidor) ou <see cref="FalhaConexaoException"/> (conexao/timeout).
/// Nao ha operacao de exclusao: o programa nunca executa DELETE (RQ-6).
/// </summary>
public interface IMetryRepository
{
    /// <summary>ST_IsValid + GeometryType do WKT no SRID informado (dupla validacao - D-M7).</summary>
    Task<ValidacaoBanco> ValidarNoBancoAsync(string wkt, int srid, CancellationToken cancellationToken = default);

    /// <summary>INSERT em public.mapas (metry) sem informar id; retorna o id do RETURNING.</summary>
    Task<int> InserirAsync(string wkt, int srid, CancellationToken cancellationToken = default);

    /// <summary>Leitura de conferencia por id (id, ST_AsText, ST_SRID); null quando o id nao existe.</summary>
    Task<RegistroMetry?> LerAsync(int id, CancellationToken cancellationToken = default);
}
