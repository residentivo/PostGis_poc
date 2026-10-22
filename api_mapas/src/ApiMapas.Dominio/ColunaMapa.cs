namespace ApiMapas.Dominio;

/// <summary>
/// Coluna espacial de public.mapas consultada pela API (RA-3). A coluna NUNCA vem do
/// usuario: cada endpoint fixa um valor deste enum e o repositorio escolhe a consulta
/// constante correspondente.
/// </summary>
public enum ColunaMapa
{
    /// <summary>public.mapas.graphy (geography, lida com cast ::geometry).</summary>
    Graphy = 1,

    /// <summary>public.mapas.metry (geometry, SRIDs 4326/4674/3857/31983).</summary>
    Metry = 2,
}

/// <summary>Nome publico (contrato JSON) de cada coluna.</summary>
public static class ColunaMapaExtensoes
{
    public const string NomeGraphy = "graphy";
    public const string NomeMetry = "metry";

    /// <summary>Nome usado no JSON e nas mensagens ("graphy" ou "metry").</summary>
    public static string Nome(this ColunaMapa coluna) => coluna switch
    {
        ColunaMapa.Graphy => NomeGraphy,
        ColunaMapa.Metry => NomeMetry,
        _ => throw new ArgumentOutOfRangeException(nameof(coluna), "coluna nao suportada"),
    };
}
