using System.Runtime.CompilerServices;
using ApiMapas.Dominio;

namespace ApiMapas.Tests.Suporte;

/// <summary>
/// Mock de <see cref="IMapasRepository"/>: devolve as geometrias configuradas (ou lanca a
/// excecao configurada) e registra as chamadas, para provar que ids invalidos nao tocam o banco.
/// </summary>
public sealed class RepositorioFalso : IMapasRepository
{
    private readonly Func<IdMapa, ColunaMapa, IEnumerable<GeometriaMapa>> _fonte;
    private readonly Exception? _erro;
    private readonly int _lancarAposItens;
    private int _chamadas;

    public RepositorioFalso(IEnumerable<GeometriaMapa> itens)
        : this((_, _) => itens)
    {
    }

    public RepositorioFalso(Func<IdMapa, ColunaMapa, IEnumerable<GeometriaMapa>> fonte)
    {
        _fonte = fonte;
    }

    private RepositorioFalso(Exception erro, int lancarAposItens, IEnumerable<GeometriaMapa> itens)
        : this(itens)
    {
        _erro = erro;
        _lancarAposItens = lancarAposItens;
    }

    public static RepositorioFalso Vazio() => new(Array.Empty<GeometriaMapa>());

    public static RepositorioFalso QueLanca(Exception erro, int aposItens = 0, IEnumerable<GeometriaMapa>? itens = null) =>
        new(erro, aposItens, itens ?? Array.Empty<GeometriaMapa>());

    public int Chamadas => Volatile.Read(ref _chamadas);

    public List<(IdMapa Id, ColunaMapa Coluna)> Pedidos { get; } = new();

    public async IAsyncEnumerable<GeometriaMapa> ListarPorIdAsync(
        IdMapa id,
        ColunaMapa coluna,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _chamadas);
        lock (Pedidos)
        {
            Pedidos.Add((id, coluna));
        }

        await Task.Yield();
        var emitidos = 0;
        foreach (var item in _fonte(id, coluna))
        {
            if (_erro is not null && emitidos >= _lancarAposItens)
            {
                throw _erro;
            }

            cancellationToken.ThrowIfCancellationRequested();
            emitidos++;
            yield return item;
        }

        if (_erro is not null)
        {
            throw _erro;
        }
    }
}

/// <summary>Fabrica de geometrias de teste.</summary>
public static class Geometrias
{
    public static GeometriaMapa Ponto(int srid = 4326, double x = -46.633309, double y = -23.55052) =>
        new("POINT", srid, $"POINT({Fmt(x)} {Fmt(y)})",
            $"{{\"type\":\"Point\",\"coordinates\":[{Fmt(x)},{Fmt(y)}]}}",
            x, y, srid == 0 ? null : x, srid == 0 ? null : y);

    public static GeometriaMapa Poligono(int srid = 31983) =>
        new("POLYGON", srid, "POLYGON((0 0,1 0,1 1,0 0))",
            "{\"type\":\"Polygon\",\"coordinates\":[[[0,0],[1,0],[1,1],[0,0]]]}",
            null, null, null, null);

    public static GeometriaMapa ComTamanho(int bytesWkt) =>
        new("LINESTRING", 4326, new string('a', bytesWkt), "{}", null, null, null, null);

    public static IEnumerable<GeometriaMapa> Pontos(int quantidade) =>
        Enumerable.Range(1, quantidade).Select(i => Ponto(4326, i, i));

    private static string Fmt(double v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
