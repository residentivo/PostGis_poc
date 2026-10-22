namespace TesteMetry.Dados;

/// <summary>
/// Erro retornado pelo PostgreSQL (traduzido de PostgresException). Carrega somente
/// SqlState e MessageText (R2); sem Detail, sem stack de origem e sem connection string.
/// </summary>
public sealed class FalhaBancoException : Exception
{
    public FalhaBancoException(string? sqlState, string mensagemServidor)
        : base(sqlState is null ? mensagemServidor : $"{sqlState}: {mensagemServidor}")
    {
        SqlState = sqlState;
        MensagemServidor = mensagemServidor;
    }

    public string? SqlState { get; }

    public string MensagemServidor { get; }
}
