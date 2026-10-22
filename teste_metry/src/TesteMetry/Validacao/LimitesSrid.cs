using System.Globalization;

namespace TesteMetry.Validacao;

/// <summary>Faixa inclusiva de coordenadas aceita para um SRID (D-M6).</summary>
/// <param name="Srid">SRID da faixa.</param>
/// <param name="Nome">Nome do sistema de referencia (somente para mensagens).</param>
/// <param name="MinX">Menor X aceito (inclusivo).</param>
/// <param name="MaxX">Maior X aceito (inclusivo).</param>
/// <param name="MinY">Menor Y aceito (inclusivo).</param>
/// <param name="MaxY">Maior Y aceito (inclusivo).</param>
/// <param name="NomeX">Nome do eixo X (ex.: longitude, E).</param>
/// <param name="NomeY">Nome do eixo Y (ex.: latitude, N).</param>
public sealed record FaixaSrid(
    int Srid,
    string Nome,
    double MinX,
    double MaxX,
    double MinY,
    double MaxY,
    string NomeX,
    string NomeY)
{
    /// <summary>True quando X esta dentro da faixa (comparacao inclusiva).</summary>
    public bool XDentro(double x) => x >= MinX && x <= MaxX;

    /// <summary>True quando Y esta dentro da faixa (comparacao inclusiva).</summary>
    public bool YDentro(double y) => y >= MinY && y <= MaxY;

    /// <summary>Texto da faixa de X para mensagens (InvariantCulture).</summary>
    public string DescreverX() => Descrever(MinX, MaxX);

    /// <summary>Texto da faixa de Y para mensagens (InvariantCulture).</summary>
    public string DescreverY() => Descrever(MinY, MaxY);

    private static string Descrever(double min, double max) =>
        $"[{min.ToString(CultureInfo.InvariantCulture)}, {max.ToString(CultureInfo.InvariantCulture)}]";
}

/// <summary>
/// Tabela UNICA e CONSTANTE de SRIDs permitidos e suas faixas de coordenadas (D-M4/D-M6).
/// Nada desta tabela vem de fora do codigo.
/// </summary>
public static class LimitesSrid
{
    /// <summary>WGS 84 (geografico).</summary>
    public const int Wgs84 = 4326;

    /// <summary>SIRGAS 2000 (geografico).</summary>
    public const int Sirgas2000 = 4674;

    /// <summary>WGS 84 / Pseudo-Mercator (Web Mercator).</summary>
    public const int WebMercator = 3857;

    /// <summary>SIRGAS 2000 / UTM zona 23S.</summary>
    public const int Sirgas2000Utm23S = 31983;

    /// <summary>SRID usado quando PG_SRID nao esta definida.</summary>
    public const int SridPadrao = Wgs84;

    /// <summary>Limite de X e Y do Web Mercator (metros).</summary>
    public const double LimiteWebMercator = 20037508.3428;

    private static readonly FaixaSrid[] Faixas =
    [
        new(Wgs84, "WGS 84", -180d, 180d, -90d, 90d, "longitude", "latitude"),
        new(Sirgas2000, "SIRGAS 2000", -180d, 180d, -90d, 90d, "longitude", "latitude"),
        new(WebMercator, "Web Mercator", -LimiteWebMercator, LimiteWebMercator, -LimiteWebMercator, LimiteWebMercator, "X", "Y"),
        new(Sirgas2000Utm23S, "SIRGAS 2000 / UTM 23S", 0d, 1_000_000d, 0d, 10_000_000d, "E", "N"),
    ];

    /// <summary>SRIDs permitidos, na ordem de documentacao.</summary>
    public static IReadOnlyList<int> Permitidos { get; } = Array.ConvertAll(Faixas, f => f.Srid);

    /// <summary>Lista dos SRIDs permitidos para mensagens (ex.: "4326, 4674, 3857 ou 31983").</summary>
    public static string PermitidosTexto { get; } =
        string.Join(", ", Permitidos.Take(Permitidos.Count - 1).Select(s => s.ToString(CultureInfo.InvariantCulture)))
        + " ou " + Permitidos[^1].ToString(CultureInfo.InvariantCulture);

    /// <summary>True quando o SRID esta na lista permitida.</summary>
    public static bool EhPermitido(int srid) => Array.Exists(Faixas, f => f.Srid == srid);

    /// <summary>Faixa do SRID; lanca <see cref="ArgumentOutOfRangeException"/> fora da lista permitida.</summary>
    public static FaixaSrid Para(int srid)
    {
        foreach (var faixa in Faixas)
        {
            if (faixa.Srid == srid)
            {
                return faixa;
            }
        }

        throw new ArgumentOutOfRangeException(nameof(srid), $"SRID nao permitido (use {PermitidosTexto}).");
    }
}
