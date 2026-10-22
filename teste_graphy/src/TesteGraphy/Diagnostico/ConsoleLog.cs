using System.Globalization;

namespace TesteGraphy.Diagnostico;

/// <summary>Log em console: passos (somente no modo verboso) e erros vao para a saida de erro.</summary>
public sealed class ConsoleLog : ILog
{
    private readonly TextWriter _saidaErro;

    public ConsoleLog(bool verboso, TextWriter saidaErro)
    {
        ArgumentNullException.ThrowIfNull(saidaErro);
        Verboso = verboso;
        _saidaErro = saidaErro;
    }

    public bool Verboso { get; }

    public void Passo(string mensagem)
    {
        if (Verboso)
        {
            _saidaErro.WriteLine($"[verboso {Agora()}] {mensagem}");
        }
    }

    public void Erro(string mensagem)
    {
        _saidaErro.WriteLine($"[erro {Agora()}] {mensagem}");
    }

    private static string Agora() => DateTime.Now.ToString("HH:mm:ss.fff", CultureInfo.InvariantCulture);
}
