namespace ApiMapas.Dominio.Geometrias;

/// <summary>
/// Motivos FIXOS de "Geometria inválida" que nao vem do <see cref="WktValidator"/> (contrato do
/// PUT e do POST). Nenhum ecoa o WKT nem o corpo (RP-14).
/// </summary>
public static class MotivosGeometria
{
    /// <summary>Validacao no banco (W*.V): ST_IsValid falso ou erro do PostGIS (XX000/classe 22).</summary>
    public const string InvalidaPostgis = "geometria inválida segundo o PostGIS (ST_IsValid)";

    /// <summary>GeometryType do banco diferente do tipo lido localmente.</summary>
    public const string TipoDiverge = "tipo devolvido pelo banco diverge do WKT";

    /// <summary>SRID fora da lista da coluna (verificado ANTES das faixas - RP-14).</summary>
    public static string SridNaoPermitido(ColunaMapa coluna) =>
        $"srid não permitido para a coluna '{coluna.Nome()}' (aceitos: {PoliticaSridColuna.AceitosTexto(coluna)})";
}
