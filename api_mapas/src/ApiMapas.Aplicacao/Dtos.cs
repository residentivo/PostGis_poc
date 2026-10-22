using System.Text.Json;

namespace ApiMapas.Aplicacao;

/// <summary>Corpo 200 de GET /api/mapas/{id}/graphy|metry (contrato A1).</summary>
public sealed record RespostaGeometriasDto(
    int Id,
    string Coluna,
    int Quantidade,
    bool Truncado,
    IReadOnlyList<GeometriaDto> Geometrias);

/// <summary>Um item de "geometrias". <see cref="Geojson"/> e um objeto JSON (nao string).</summary>
public sealed record GeometriaDto(
    string Tipo,
    int Srid,
    string Wkt,
    JsonElement Geojson,
    double? X,
    double? Y,
    double? Longitude,
    double? Latitude);
