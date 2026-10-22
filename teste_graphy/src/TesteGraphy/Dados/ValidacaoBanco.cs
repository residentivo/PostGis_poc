namespace TesteGraphy.Dados;

/// <summary>Resultado de ST_IsValid / GeometryType calculados pelo PostGIS.</summary>
public sealed record ValidacaoBanco(bool Valido, string? Tipo);
