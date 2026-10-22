using System.Globalization;
using System.Text;

namespace TesteMetry.Comum;

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
        var limite = truncado ? Math.Max(tamanhoMaximo, 0) : texto.Length;

        // C-1 (M5): nao cortar no meio de um par surrogate.
        if (truncado && limite > 0 && char.IsHighSurrogate(texto[limite - 1]))
        {
            limite--;
        }

        var sb = new StringBuilder(limite + Reticencias.Length);

        for (var i = 0; i < limite; i++)
        {
            var c = texto[i];

            // Par surrogate valido (ex.: emoji): mantido inteiro.
            if (char.IsHighSurrogate(c) && i + 1 < limite && char.IsLowSurrogate(texto[i + 1]))
            {
                var categoriaPar = CharUnicodeInfo.GetUnicodeCategory(texto, i);
                if (EhPerigoso(categoriaPar, c))
                {
                    sb.Append('?');
                }
                else
                {
                    sb.Append(c).Append(texto[i + 1]);
                }

                i++;
                continue;
            }

            sb.Append(EhPerigoso(CharUnicodeInfo.GetUnicodeCategory(c), c) ? '?' : c);
        }

        if (truncado)
        {
            sb.Append(Reticencias);
        }

        return sb.ToString();
    }

    /// <summary>
    /// C-1 (M5): controle (CR, LF, ESC, TAB), formato (U+202E, U+2066-2069, U+200B-200D, U+FEFF),
    /// separadores de linha/paragrafo (U+2028/2029), surrogate solto, uso privado, nao atribuido
    /// e espacos que nao sejam o espaco comum (ex.: U+00A0). Letras acentuadas sao mantidas.
    /// </summary>
    private static bool EhPerigoso(UnicodeCategory categoria, char c) => categoria switch
    {
        UnicodeCategory.Control => true,
        UnicodeCategory.Format => true,
        UnicodeCategory.LineSeparator => true,
        UnicodeCategory.ParagraphSeparator => true,
        UnicodeCategory.Surrogate => true,
        UnicodeCategory.PrivateUse => true,
        UnicodeCategory.OtherNotAssigned => true,
        UnicodeCategory.SpaceSeparator => c != ' ',
        _ => false,
    };
}
