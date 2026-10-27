using System.Globalization;

namespace ApiMapas.Dominio.Geometrias;

/// <summary>
/// SRIDs aceitos por coluna no PUT e no POST (D-P5, D-N5): graphy so 4326 (geography lon/lat); metry 4326, 4674,
/// 3857 e 31983. Constante; nada vem do usuario.
/// </summary>
public static class PoliticaSridColuna
{
    private static readonly int[] SridsGraphy = [LimitesSrid.Wgs84];

    private static readonly int[] SridsMetry =
    [
        LimitesSrid.Wgs84, LimitesSrid.Sirgas2000, LimitesSrid.WebMercator, LimitesSrid.Sirgas2000Utm23S,
    ];

    /// <summary>SRIDs aceitos para a coluna.</summary>
    public static IReadOnlyList<int> Aceitos(ColunaMapa coluna) => coluna switch
    {
        ColunaMapa.Graphy => SridsGraphy,
        ColunaMapa.Metry => SridsMetry,
        _ => throw new ArgumentOutOfRangeException(nameof(coluna), "coluna nao suportada"),
    };

    /// <summary>True quando o SRID e aceito na coluna.</summary>
    public static bool Aceita(ColunaMapa coluna, int srid) => Aceitos(coluna).Contains(srid);

    /// <summary>Lista para mensagens (ex.: "4326, 4674, 3857, 31983").</summary>
    public static string AceitosTexto(ColunaMapa coluna) =>
        string.Join(", ", Aceitos(coluna).Select(s => s.ToString(CultureInfo.InvariantCulture)));
}
