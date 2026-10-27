using System.Text.Json;

namespace ApiMapas.Aplicacao;

/// <summary>
/// Corpo 200 de GET /api/mapas/{id}/graphy|metry: UMA geometria por id (D-N7, Q2 da PU), na ordem
/// {"id","coluna","tipo","srid","wkt","geojson","x","y","longitude","latitude"}.
/// <see cref="Geojson"/> e um objeto JSON (nao string).
/// </summary>
public sealed record RespostaGeometriaDto(
    int Id,
    string Coluna,
    string Tipo,
    int Srid,
    string Wkt,
    JsonElement Geojson,
    double? X,
    double? Y,
    double? Longitude,
    double? Latitude);
