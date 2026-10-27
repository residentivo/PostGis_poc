using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Tests.Suporte;

/// <summary>Logger em memoria habilitado em todos os niveis (exercita os logs Debug e permite checar vazamentos).</summary>
public sealed class LoggerMemoria<T> : ILogger<T>
{
    public ConcurrentQueue<(LogLevel Nivel, EventId Evento, string Mensagem, Exception? Erro)> Entradas { get; } = new();

    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        Entradas.Enqueue((logLevel, eventId, formatter(state, exception), exception));
    }

    public string TextoCompleto => string.Join('\n', Entradas.Select(e => e.Mensagem));
}

/// <summary>Provedor que guarda todas as mensagens de log da API em memoria.</summary>
public sealed class ProvedorLogMemoria : ILoggerProvider
{
    public ConcurrentQueue<string> Mensagens { get; } = new();

    public ILogger CreateLogger(string categoryName) => new LoggerSimples(this, categoryName);

    public void Dispose()
    {
    }

    private sealed class LoggerSimples(ProvedorLogMemoria dono, string categoria) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            dono.Mensagens.Enqueue($"{logLevel} {categoria} {formatter(state, exception)} {exception}");
            dono.Propriedades.Enqueue(PropriedadesDoEstado.Ler(state));
        }
    }

    /// <summary>PO6: valores estruturados de cada log (o que um provedor estruturado gravaria).</summary>
    public ConcurrentQueue<string> Propriedades { get; } = new();
}

/// <summary>Le o estado estruturado do LoggerMessage pelo indexador e pelo enumerador.</summary>
internal static class PropriedadesDoEstado
{
    public static string Ler<TState>(TState state)
    {
        if (state is not IReadOnlyList<KeyValuePair<string, object?>> lista)
        {
            return string.Empty;
        }

        var partes = new List<string>();
        for (var i = 0; i < lista.Count; i++)
        {
            partes.Add($"{lista[i].Key}={lista[i].Value}");
        }

        foreach (var (chave, valor) in (IEnumerable<KeyValuePair<string, object?>>)lista)
        {
            partes.Add($"{chave}={valor}");
        }

        System.Collections.IEnumerable naoGenerico = lista;
        foreach (var _ in naoGenerico)
        {
        }

        try
        {
            _ = lista[lista.Count];
        }
        catch (IndexOutOfRangeException)
        {
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return string.Join(' ', partes);
    }
}
