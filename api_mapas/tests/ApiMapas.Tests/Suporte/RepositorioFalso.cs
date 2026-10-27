using ApiMapas.Dominio;

namespace ApiMapas.Tests.Suporte;

/// <summary>
/// Mock de <see cref="IMapasRepository"/> (uma geometria por id, D-N7): 0 itens configurados =
/// NaoEncontrada; 1 = Encontrada; 2 ou mais = Duplicada (RS-23). <see cref="ColunaVazia"/> simula a
/// linha com a coluna NULL. Registra as chamadas, para provar que ids invalidos nao tocam o banco.
/// </summary>
public sealed class RepositorioFalso : IMapasRepository
{
    private readonly Func<IdMapa, ColunaMapa, IEnumerable<GeometriaMapa>> _fonte;
    private readonly Exception? _erro;
    private readonly bool _colunaVazia;
    private int _chamadas;

    public RepositorioFalso(IEnumerable<GeometriaMapa> itens)
        : this((_, _) => itens)
    {
    }

    public RepositorioFalso(Func<IdMapa, ColunaMapa, IEnumerable<GeometriaMapa>> fonte)
    {
        _fonte = fonte;
    }

    private RepositorioFalso(Exception? erro, bool colunaVazia)
        : this(Array.Empty<GeometriaMapa>())
    {
        _erro = erro;
        _colunaVazia = colunaVazia;
    }

    public static RepositorioFalso Vazio() => new(Array.Empty<GeometriaMapa>());

    /// <summary>Linha existe, mas a coluna da rota e NULL (404).</summary>
    public static RepositorioFalso ColunaVazia() => new(null, colunaVazia: true);

    public static RepositorioFalso QueLanca(Exception erro) => new(erro, colunaVazia: false);

    public int Chamadas => Volatile.Read(ref _chamadas);

    public List<(IdMapa Id, ColunaMapa Coluna)> Pedidos { get; } = new();

    public async Task<LeituraMapa> ObterPorIdAsync(IdMapa id, ColunaMapa coluna, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _chamadas);
        lock (Pedidos)
        {
            Pedidos.Add((id, coluna));
        }

        await Task.Yield();
        cancellationToken.ThrowIfCancellationRequested();
        if (_erro is not null)
        {
            throw _erro;
        }

        if (_colunaVazia)
        {
            return LeituraMapa.ColunaVazia.Instancia;
        }

        var itens = _fonte(id, coluna).Take(2).ToList();
        return itens.Count switch
        {
            0 => LeituraMapa.NaoEncontrada.Instancia,
            1 => new LeituraMapa.Encontrada(itens[0]),
            _ => LeituraMapa.Duplicada.Instancia,
        };
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
