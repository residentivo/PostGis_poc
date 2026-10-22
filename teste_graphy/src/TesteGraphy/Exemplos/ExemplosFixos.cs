namespace TesteGraphy.Exemplos;

/// <summary>Exemplos usados quando o programa e executado sem argumentos (Sao Paulo, SRID 4326).</summary>
public static class ExemplosFixos
{
    /// <summary>Praca da Se.</summary>
    public const string PracaDaSe = "POINT(-46.6333 -23.5505)";

    public const string Linha = "LINESTRING(-46.6333 -23.5505, -46.6566 -23.5614, -46.6750 -23.5700)";

    public const string Poligono =
        "POLYGON((-46.6400 -23.5400, -46.6200 -23.5400, -46.6200 -23.5600, -46.6400 -23.5600, -46.6400 -23.5400))";

    /// <summary>Todos os exemplos, na ordem de insercao.</summary>
    public static IReadOnlyList<string> Todos { get; } = [PracaDaSe, Linha, Poligono];
}
