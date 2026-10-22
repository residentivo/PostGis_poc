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

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            dono.Mensagens.Enqueue($"{logLevel} {categoria} {formatter(state, exception)} {exception}");
    }
}
