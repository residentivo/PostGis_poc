namespace ApiMapas.Aplicacao;

/// <summary>
/// Limites da resposta do GET (RA-13, D-N7). Lidos da secao "ApiMapas" do appsettings.json.
/// </summary>
public sealed class OpcoesConsulta
{
    public const string Secao = "ApiMapas";

    /// <summary>Teto padrao de bytes UTF-8 (wkt + geojson) da geometria devolvida: 5 MB.</summary>
    public const long LimiteBytesPadrao = 5L * 1024 * 1024;

    /// <summary>
    /// Teto de bytes (wkt + geojson) da geometria. Acima dele a resposta e 500 generico com log
    /// Warning (so acontece com dado inserido fora da API: a escrita limita o WKT a 10.000 caracteres).
    /// </summary>
    public long LimiteBytesGeometrias { get; set; } = LimiteBytesPadrao;
}
