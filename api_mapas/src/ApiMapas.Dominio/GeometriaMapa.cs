namespace ApiMapas.Dominio;

/// <summary>
/// Uma linha nao nula da coluna consultada, exatamente como devolvida pelas consultas
/// Q1/Q2 (api_mapas/sql/consultas_referencia.sql).
/// </summary>
/// <param name="Tipo">GeometryType (POINT, LINESTRING, POLYGON, MULTI*, ...).</param>
/// <param name="Srid">ST_SRID da geometria.</param>
/// <param name="Wkt">ST_AsText no SRID nativo.</param>
/// <param name="GeoJson">Texto GeoJSON (WGS84 quando SRID diferente de 0; nativo quando SRID 0).</param>
/// <param name="X">ST_X nativo (so POINT).</param>
/// <param name="Y">ST_Y nativo (so POINT).</param>
/// <param name="Longitude">ST_X em 4326 (so POINT com SRID diferente de 0).</param>
/// <param name="Latitude">ST_Y em 4326 (so POINT com SRID diferente de 0).</param>
public sealed record GeometriaMapa(
    string Tipo,
    int Srid,
    string Wkt,
    string GeoJson,
    double? X,
    double? Y,
    double? Longitude,
    double? Latitude);
