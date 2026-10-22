using System.Collections.Concurrent;
using TesteMetry.Dados;

namespace TesteMetry.Tests.Fakes;

/// <summary>
/// IMetryRepository falso (sem banco). Por padrao: ST_IsValid = true com tipo POINT, ids
/// sequenciais a partir de 1 e leitura devolvendo o mesmo WKT com o SRID recebido no INSERT.
/// Registra todos os SRIDs recebidos (RQ-1/RQ-2). Cada operacao pode ser substituida.
/// </summary>
internal sealed class RepositorioFalso : IMetryRepository, IAsyncDisposable
{
    private readonly ConcurrentDictionary<int, (string Wkt, int Srid)> _gravados = new();
    private readonly ConcurrentQueue<int> _sridsRecebidos = new();
    private int _proximoId;

    public Func<string, int, CancellationToken, Task<ValidacaoBanco>>? AoValidar { get; set; }

    public Func<string, int, CancellationToken, Task<int>>? AoInserir { get; set; }

    public Func<int, CancellationToken, Task<RegistroMetry?>>? AoLer { get; set; }

    public int ChamadasValidar { get; private set; }

    public int ChamadasInserir { get; private set; }

    public int ChamadasLer { get; private set; }

    public int ChamadasDispose { get; private set; }

    public int TotalChamadas => ChamadasValidar + ChamadasInserir + ChamadasLer;

    public IReadOnlyList<int> SridsRecebidos => [.. _sridsRecebidos];

    public List<string> WktsValidados { get; } = [];

    public Task<ValidacaoBanco> ValidarNoBancoAsync(string wkt, int srid, CancellationToken cancellationToken = default)
    {
        ChamadasValidar++;
        _sridsRecebidos.Enqueue(srid);
        WktsValidados.Add(wkt);
        cancellationToken.ThrowIfCancellationRequested();
        return AoValidar?.Invoke(wkt, srid, cancellationToken) ?? Task.FromResult(new ValidacaoBanco(true, TipoDe(wkt)));
    }

    public Task<int> InserirAsync(string wkt, int srid, CancellationToken cancellationToken = default)
    {
        ChamadasInserir++;
        _sridsRecebidos.Enqueue(srid);
        cancellationToken.ThrowIfCancellationRequested();
        if (AoInserir is not null)
        {
            return AoInserir(wkt, srid, cancellationToken);
        }

        var id = Interlocked.Increment(ref _proximoId);
        _gravados[id] = (wkt, srid);
        return Task.FromResult(id);
    }

    public Task<RegistroMetry?> LerAsync(int id, CancellationToken cancellationToken = default)
    {
        ChamadasLer++;
        cancellationToken.ThrowIfCancellationRequested();
        if (AoLer is not null)
        {
            return AoLer(id, cancellationToken);
        }

        return Task.FromResult<RegistroMetry?>(
            _gravados.TryGetValue(id, out var g) ? new RegistroMetry(id, g.Wkt, g.Srid) : null);
    }

    public ValueTask DisposeAsync()
    {
        ChamadasDispose++;
        return ValueTask.CompletedTask;
    }

    private static string TipoDe(string wkt)
    {
        var fim = wkt.IndexOf('(', StringComparison.Ordinal);
        return (fim < 0 ? wkt : wkt[..fim]).Trim().ToUpperInvariant();
    }
}
