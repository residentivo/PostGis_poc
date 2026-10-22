using TesteMetry.Validacao;

namespace TesteMetry.Exemplos;

/// <summary>
/// Exemplos usados quando o programa e executado sem argumentos (D-M8): 3 WKT em Sao Paulo
/// (Praca da Se, uma linha e um poligono) expressos no SRID configurado. Os valores
/// projetados (3857 e 31983) sao aproximados; precisao nao e requisito.
/// </summary>
public static class ExemplosFixos
{
    // ---- 4326 (WGS 84) e 4674 (SIRGAS 2000): graus decimais ----

    /// <summary>Praca da Se (graus).</summary>
    public const string PracaDaSeGraus = "POINT(-46.6333 -23.5505)";

    public const string LinhaGraus = "LINESTRING(-46.6333 -23.5505, -46.6566 -23.5614, -46.6750 -23.5700)";

    public const string PoligonoGraus =
        "POLYGON((-46.6400 -23.5400, -46.6200 -23.5400, -46.6200 -23.5600, -46.6400 -23.5600, -46.6400 -23.5400))";

    // ---- 3857 (Web Mercator): metros ----

    /// <summary>Praca da Se (Web Mercator).</summary>
    public const string PracaDaSeWebMercator = "POINT(-5191195.21 -2698729.46)";

    public const string LinhaWebMercator =
        "LINESTRING(-5191195.21 -2698729.46, -5193788.95 -2700053.14, -5195837.23 -2701097.59)";

    public const string PoligonoWebMercator =
        "POLYGON((-5191941.05 -2697454.45, -5189714.66 -2697454.45, -5189714.66 -2699883.12, " +
        "-5191941.05 -2699883.12, -5191941.05 -2697454.45))";

    // ---- 31983 (SIRGAS 2000 / UTM 23S): metros (E N) ----

    /// <summary>Praca da Se (UTM 23S).</summary>
    public const string PracaDaSeUtm23S = "POINT(333250 7394600)";

    public const string LinhaUtm23S = "LINESTRING(333250 7394600, 330872 7393392, 328995 7392439)";

    public const string PoligonoUtm23S =
        "POLYGON((332566 7395763, 334607 7395763, 334607 7393547, 332566 7393547, 332566 7395763))";

    private static readonly IReadOnlyList<string> Graus = [PracaDaSeGraus, LinhaGraus, PoligonoGraus];

    private static readonly IReadOnlyList<string> WebMercator = [PracaDaSeWebMercator, LinhaWebMercator, PoligonoWebMercator];

    private static readonly IReadOnlyList<string> Utm23S = [PracaDaSeUtm23S, LinhaUtm23S, PoligonoUtm23S];

    /// <summary>Exemplos do SRID, na ordem de insercao (ponto, linha, poligono).</summary>
    /// <exception cref="ArgumentOutOfRangeException">SRID fora da lista permitida.</exception>
    public static IReadOnlyList<string> Para(int srid) => srid switch
    {
        LimitesSrid.Wgs84 or LimitesSrid.Sirgas2000 => Graus,
        LimitesSrid.WebMercator => WebMercator,
        LimitesSrid.Sirgas2000Utm23S => Utm23S,
        _ => throw new ArgumentOutOfRangeException(nameof(srid), $"SRID nao permitido (use {LimitesSrid.PermitidosTexto})."),
    };
}
