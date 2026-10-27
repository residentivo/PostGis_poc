namespace ApiMapas.Dominio.Geometrias;

/// <summary>Limites da escrita (PUT /api/mapas/{id}/graphy|metry e POST /api/mapas/graphy|metry; D-N9).</summary>
public static class LimitesEscrita
{
    /// <summary>Maximo de caracteres do WKT (igual aos programas de teste).</summary>
    public const int TamanhoMaximoWkt = WktValidator.TamanhoMaximo;
}
