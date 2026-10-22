namespace TesteGraphy.Configuracao;

/// <summary>
/// Configuracao ausente ou invalida. A mensagem cita SOMENTE o nome da variavel,
/// nunca o valor (R1/R2/R5). Codigo de saida 1.
/// </summary>
public sealed class ConfiguracaoInvalidaException : Exception
{
    public ConfiguracaoInvalidaException(string nomeVariavel, string motivo)
        : base($"Variavel de ambiente {nomeVariavel}: {motivo}.")
    {
        NomeVariavel = nomeVariavel;
    }

    public string NomeVariavel { get; }
}
