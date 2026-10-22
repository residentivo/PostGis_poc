using System.Collections.Concurrent;
using TesteGraphy.Dados;

namespace TesteGraphy.Tests.Fakes;

/// <summary>
/// Repositorio falso (sem banco). Por padrao: ST_IsValid = true, ids sequenciais a partir de 1
/// e leitura devolvendo o mesmo WKT com SRID 4326. Cada operacao pode ser substituida.
/// </summary>
internal sealed class RepositorioFalso : IGraphyRepository, IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, string> _gravados = new();
    private int _proximoId;

    public Func<string, CancellationToken, Task<ValidacaoBanco>>? AoValidar { get; set; }

    public Func<string, CancellationToken, Task<int>>? AoInserir { get; set; }

    public Func<int, CancellationToken, Task<RegistroGraphy?>>? AoLer { get; set; }

    public int ChamadasValidar { get; private set; }

    public int ChamadasInserir { get; private set; }

    public int ChamadasLer { get; private set; }

    public int ChamadasDispose { get; private set; }

    public int TotalChamadas => ChamadasValidar + ChamadasInserir + ChamadasLer;

    public Task<ValidacaoBanco> ValidarNoBancoAsync(string wkt, CancellationToken cancellationToken = default)
    {
        ChamadasValidar++;
        cancellationToken.ThrowIfCancellationRequested();
        return AoValidar?.Invoke(wkt, cancellationToken) ?? Task.FromResult(new ValidacaoBanco(true, "POINT"));
    }

    public Task<int> InserirAsync(string wkt, CancellationToken cancellationToken = default)
    {
        ChamadasInserir++;
        cancellationToken.ThrowIfCancellationRequested();
        if (AoInserir is not null)
        {
            return AoInserir(wkt, cancellationToken);
        }

        var id = Interlocked.Increment(ref _proximoId);
        _gravados[id] = wkt;
        return Task.FromResult(id);
    }

    public Task<RegistroGraphy?> LerAsync(int id, CancellationToken cancellationToken = default)
    {
        ChamadasLer++;
        cancellationToken.ThrowIfCancellationRequested();
        if (AoLer is not null)
        {
            return AoLer(id, cancellationToken);
        }

        return Task.FromResult<RegistroGraphy?>(
            _gravados.TryGetValue(id, out var wkt) ? new RegistroGraphy(id, wkt, 4326) : null);
    }

    public ValueTask DisposeAsync()
    {
        ChamadasDispose++;
        return ValueTask.CompletedTask;
    }
}
