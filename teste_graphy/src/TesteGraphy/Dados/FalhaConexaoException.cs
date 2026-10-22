namespace TesteGraphy.Dados;

/// <summary>
/// Falha de conexao/comunicacao (NpgsqlException, SocketException, TimeoutException).
/// Carrega somente o NOME do tipo da excecao original (R2); a mensagem exibida ao
/// usuario e generica: "falha de conexao com host:porta (Tipo)".
/// </summary>
public sealed class FalhaConexaoException : Exception
{
    public FalhaConexaoException(string tipoExcecao)
        : base($"falha de conexao ({tipoExcecao})")
    {
        TipoExcecao = tipoExcecao;
    }

    public string TipoExcecao { get; }
}
