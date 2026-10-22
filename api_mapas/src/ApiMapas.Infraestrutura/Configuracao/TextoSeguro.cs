using System.Globalization;
using System.Text;

namespace ApiMapas.Infraestrutura.Configuracao;

/// <summary>Sanitizacao de textos de configuracao antes de ir ao log (troca controles por '?' e trunca).</summary>
public static class TextoSeguro
{
    public const int TamanhoMaximo = 200;

    public static string Sanitizar(string? texto, int tamanhoMaximo = TamanhoMaximo)
    {
        if (string.IsNullOrEmpty(texto))
        {
            return string.Empty;
        }

        var limite = Math.Min(texto.Length, Math.Max(tamanhoMaximo, 0));
        var sb = new StringBuilder(limite + 3);
        for (var i = 0; i < limite; i++)
        {
            var c = texto[i];
            var categoria = CharUnicodeInfo.GetUnicodeCategory(c);
            var perigoso = categoria is UnicodeCategory.Control or UnicodeCategory.Format
                or UnicodeCategory.LineSeparator or UnicodeCategory.ParagraphSeparator
                or UnicodeCategory.Surrogate or UnicodeCategory.PrivateUse or UnicodeCategory.OtherNotAssigned
                || (categoria == UnicodeCategory.SpaceSeparator && c != ' ');
            sb.Append(perigoso ? '?' : c);
        }

        if (texto.Length > limite)
        {
            sb.Append("...");
        }

        return sb.ToString();
    }
}
