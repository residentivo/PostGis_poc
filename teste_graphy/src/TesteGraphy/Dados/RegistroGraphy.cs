namespace TesteGraphy.Dados;

/// <summary>Registro lido de volta de public.mapas: id, ST_AsText(graphy) e ST_SRID(graphy::geometry).</summary>
public sealed record RegistroGraphy(int Id, string Wkt, int Srid);
