using System.Globalization;

namespace ApiMapas.Dominio.Geometrias;

/// <summary>
/// Faixa inclusiva de coordenadas aceita para um SRID (D-P5).
/// Origem: teste_metry/src/TesteMetry/Validacao/LimitesSrid.cs (copia adaptada; o projeto de
/// dominio NAO referencia teste_metry).
/// </summary>
/// <param name="Srid">SRID da faixa.</param>
/// <param name="Nome">Nome do sistema de referencia (somente documentacao).</param>
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

    /// <summary>Texto da faixa de X para mensagens (InvariantCulture, so constantes).</summary>
    public string DescreverX() => Descrever(MinX, MaxX);

    /// <summary>Texto da faixa de Y para mensagens (InvariantCulture, so constantes).</summary>
    public string DescreverY() => Descrever(MinY, MaxY);

    private static string Descrever(double min, double max) =>
        $"[{min.ToString(CultureInfo.InvariantCulture)}, {max.ToString(CultureInfo.InvariantCulture)}]";
}

/// <summary>
/// Tabela UNICA e CONSTANTE dos SRIDs conhecidos e de suas faixas (D-P5). Nada desta tabela vem
/// de fora do codigo. Quais SRIDs valem em cada coluna fica em <see cref="PoliticaSridColuna"/>.
/// Origem: teste_metry/src/TesteMetry/Validacao/LimitesSrid.cs.
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

    /// <summary>Limite de X e Y do Web Mercator (metros).</summary>
    public const double LimiteWebMercator = 20037508.3428;

    private static readonly FaixaSrid[] Faixas =
    [
        new(Wgs84, "WGS 84", -180d, 180d, -90d, 90d, "longitude", "latitude"),
        new(Sirgas2000, "SIRGAS 2000", -180d, 180d, -90d, 90d, "longitude", "latitude"),
        new(WebMercator, "Web Mercator", -LimiteWebMercator, LimiteWebMercator, -LimiteWebMercator, LimiteWebMercator, "X", "Y"),
        new(Sirgas2000Utm23S, "SIRGAS 2000 / UTM 23S", 0d, 1_000_000d, 0d, 10_000_000d, "E", "N"),
    ];

    /// <summary>SRIDs conhecidos, na ordem de documentacao.</summary>
    public static IReadOnlyList<int> Conhecidos { get; } = Array.ConvertAll(Faixas, f => f.Srid);

    /// <summary>Faixa do SRID, ou null quando o SRID nao esta na tabela.</summary>
    public static FaixaSrid? Para(int srid)
    {
        foreach (var faixa in Faixas)
        {
            if (faixa.Srid == srid)
            {
                return faixa;
            }
        }

        return null;
    }
}
