using System.Text.Json.Serialization;

namespace ApiMapas.Aplicacao;

/// <summary>
/// Corpo do PUT /api/mapas/{id}/graphy|metry e do POST /api/mapas/graphy|metry: objeto unico
/// {"wkt":"...","srid":4326} (Q1 da PU). Nomes exatos e sensiveis a maiusculas; os dois campos sao
/// obrigatorios e o srid e um inteiro JSON sem padrao (RP-11, D-P5).
/// </summary>
public sealed class CorpoGeometriaDto
{
    [JsonPropertyName("wkt")]
    public required string Wkt { get; init; }

    [JsonPropertyName("srid")]
    public required int Srid { get; init; }
}
