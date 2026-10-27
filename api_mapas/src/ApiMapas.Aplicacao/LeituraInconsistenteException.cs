namespace ApiMapas.Aplicacao;

/// <summary>
/// O GET nao pode responder com seguranca (D-N7, RS-23): o id tem 2 linhas (migracao para PRIMARY
/// KEY nao aplicada) ou a geometria passa do teto ApiMapas:LimiteBytesGeometrias. Vira 500 generico
/// no ManipuladorExcecoes. A mensagem leva so id, coluna e o motivo fixo (nunca WKT).
/// </summary>
public sealed class LeituraInconsistenteException : Exception
{
    public LeituraInconsistenteException()
        : base("Leitura inconsistente de public.mapas.")
    {
    }

    public LeituraInconsistenteException(string message)
        : base(message)
    {
    }

    public LeituraInconsistenteException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
