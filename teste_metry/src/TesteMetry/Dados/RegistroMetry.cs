namespace TesteMetry.Dados;

/// <summary>Registro lido de volta de public.mapas: id, ST_AsText(metry) e ST_SRID(metry).</summary>
public sealed record RegistroMetry(int Id, string Wkt, int Srid);
