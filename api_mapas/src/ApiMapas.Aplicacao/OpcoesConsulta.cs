namespace ApiMapas.Aplicacao;

/// <summary>
/// Limites da resposta (RA-12 / RA-13). Lidos da secao "ApiMapas" do appsettings.json.
/// </summary>
public sealed class OpcoesConsulta
{
    public const string Secao = "ApiMapas";

    /// <summary>Maximo de itens em "geometrias" (AJ-1). As consultas usam LIMIT 101 para detectar o excedente.</summary>
    public const int LimiteItens = 100;

    /// <summary>Teto padrao de bytes UTF-8 somando wkt + geojson de todos os itens: 5 MB.</summary>
    public const long LimiteBytesPadrao = 5L * 1024 * 1024;

    /// <summary>
    /// Teto de bytes (wkt + geojson) da resposta. O item que estoura o teto e os seguintes
    /// ficam de fora e a resposta sai com truncado = true.
    /// </summary>
    public long LimiteBytesGeometrias { get; set; } = LimiteBytesPadrao;
}
