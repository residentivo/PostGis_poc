using System.Text;

namespace TesteGraphy.Comum;

/// <summary>Sanitizacao de texto ecoado no console (R2).</summary>
public static class TextoSeguro
{
    public const int TamanhoMaximoEco = 200;
    public const string Reticencias = "...";

    public const string EcoOmitido = "(argumento omitido: parece conter credenciais)";

    private static readonly string[] IndicadoresCredencial = ["password", "senha", "pwd="];

    /// <summary>
    /// Eco seguro de um argumento WKT (R1/R2): se o texto parecer conter credenciais
    /// (ex.: connection string colada por engano), nao e ecoado; caso contrario, e sanitizado.
    /// </summary>
    public static string EcoWkt(string? wkt)
    {
        if (wkt is not null)
        {
            foreach (var indicador in IndicadoresCredencial)
            {
                if (wkt.Contains(indicador, StringComparison.OrdinalIgnoreCase))
                {
                    return EcoOmitido;
                }
            }
        }

        return Sanitizar(wkt);
    }

    /// <summary>
    /// Troca caracteres de controle (CR, LF, ESC, TAB etc.) por '?' e trunca em
    /// <paramref name="tamanhoMaximo"/> caracteres, acrescentando "..." quando truncado.
    /// </summary>
    public static string Sanitizar(string? texto, int tamanhoMaximo = TamanhoMaximoEco)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var truncado = texto.Length > tamanhoMaximo;
        var limite = truncado ? tamanhoMaximo : texto.Length;
        var sb = new StringBuilder(limite + Reticencias.Length);

        for (var i = 0; i < limite; i++)
        {
            var c = texto[i];
            sb.Append(char.IsControl(c) || EhSeparadorDeLinha(c) ? '?' : c);
        }

        if (truncado)
        {
            sb.Append(Reticencias);
        }

        return sb.ToString();
    }

    /// <summary>U+2028 (line separator) e U+2029 (paragraph separator) tambem quebram linha.</summary>
    private static bool EhSeparadorDeLinha(char c) => c == (char)0x2028 || c == (char)0x2029;
}
