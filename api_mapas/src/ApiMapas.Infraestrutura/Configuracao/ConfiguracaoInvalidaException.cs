namespace ApiMapas.Infraestrutura.Configuracao;

/// <summary>
/// Configuracao invalida ou ausente: impede a API de subir. A mensagem cita apenas o
/// nome da variavel e o motivo, nunca o valor recebido (RA-9).
/// </summary>
public sealed class ConfiguracaoInvalidaException : Exception
{
    public ConfiguracaoInvalidaException()
    {
    }

    public ConfiguracaoInvalidaException(string message)
        : base(message)
    {
    }

    public ConfiguracaoInvalidaException(string message, Exception innerException)
        : base(message, innerException)
    {
    }

    public ConfiguracaoInvalidaException(string variavel, string motivo)
        : base($"Configuracao invalida: {variavel} {motivo}.")
    {
        Variavel = variavel;
    }

    /// <summary>Nome da variavel/chave com problema (nunca o valor).</summary>
    public string? Variavel { get; }
}
