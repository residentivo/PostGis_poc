namespace ApiMapas.Aplicacao;

/// <summary>
/// RP-4 / D-P11: o prazo total da escrita (PUT ou POST) estourou (o banco ja fez ROLLBACK). Vira
/// 500 generico no ManipuladorExcecoes. Nao carrega excecao interna (nada de MessageText do banco -
/// RP-10).
/// </summary>
public sealed class PrazoAtualizacaoEstouradoException : TimeoutException
{
    public PrazoAtualizacaoEstouradoException()
        : base("Prazo total da escrita (PUT e POST) estourado.")
    {
    }

    public PrazoAtualizacaoEstouradoException(string message)
        : base(message)
    {
    }

    public PrazoAtualizacaoEstouradoException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
