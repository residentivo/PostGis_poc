namespace ApiMapas.Infraestrutura.Dados;

/// <summary>
/// Falha do banco na escrita (500). Substitui a excecao do Npgsql SEM guarda-la como interna:
/// a PostgresException pode trazer MessageText/Hint/Where com trechos do WKT, e o
/// ManipuladorExcecoes registra a excecao inteira (RP-10). Carrega so a etapa, o SqlState e o
/// nome do tipo original.
/// </summary>
public sealed class FalhaBancoEscritaException : Exception
{
    public FalhaBancoEscritaException()
    {
    }

    public FalhaBancoEscritaException(string message)
        : base(message)
    {
    }

    public FalhaBancoEscritaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public FalhaBancoEscritaException(string etapa, string sqlState, string tipoOriginal)
        : base($"Falha no banco de escrita ao {etapa} (SqlState {sqlState}, tipo {tipoOriginal}).")
    {
        Etapa = etapa;
        SqlState = sqlState;
    }

    public string? Etapa { get; }

    public string? SqlState { get; }
}
