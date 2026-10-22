namespace TesteGraphy.Diagnostico;

/// <summary>
/// Log da aplicacao. <see cref="Passo"/> so e emitido quando o modo verboso
/// estiver ativo (appsettings.json -> "Verboso": true). <see cref="Erro"/> e
/// sempre emitido. Nunca registrar senha, connection string ou stack trace (R2).
/// </summary>
public interface ILog
{
    bool Verboso { get; }

    void Passo(string mensagem);

    void Erro(string mensagem);
}
