namespace TesteMetry.Diagnostico;

/// <summary>Log que descarta todas as mensagens (padrao quando nenhum log e informado).</summary>
public sealed class LogNulo : ILog
{
    public static readonly LogNulo Instancia = new();

    private LogNulo()
    {
    }

    public bool Verboso => false;

    public void Passo(string mensagem)
    {
    }

    public void Erro(string mensagem)
    {
    }
}
