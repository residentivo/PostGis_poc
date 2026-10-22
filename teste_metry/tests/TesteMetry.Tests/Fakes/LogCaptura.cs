using TesteMetry.Diagnostico;

namespace TesteMetry.Tests.Fakes;

/// <summary>Log que guarda todas as mensagens (passos e erros) para inspecao.</summary>
internal sealed class LogCaptura : ILog
{
    private readonly List<string> _passos = [];
    private readonly List<string> _erros = [];
    private readonly object _trava = new();

    public bool Verboso => true;

    public IReadOnlyList<string> Passos
    {
        get
        {
            lock (_trava)
            {
                return [.. _passos];
            }
        }
    }

    public IReadOnlyList<string> Erros
    {
        get
        {
            lock (_trava)
            {
                return [.. _erros];
            }
        }
    }

    public string Tudo => string.Join("\n", Passos.Concat(Erros));

    public void Passo(string mensagem)
    {
        lock (_trava)
        {
            _passos.Add(mensagem);
        }
    }

    public void Erro(string mensagem)
    {
        lock (_trava)
        {
            _erros.Add(mensagem);
        }
    }
}
