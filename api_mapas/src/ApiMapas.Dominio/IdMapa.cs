using System.Globalization;

namespace ApiMapas.Dominio;

/// <summary>
/// Id de public.mapas (int4) ja validado (RA-4). So pode ser criado por <see cref="TentarCriar"/>,
/// que aceita apenas inteiro decimal 1..2147483647, somente digitos ASCII 0-9, sem sinal,
/// sem espacos, ate 10 caracteres.
/// </summary>
public readonly record struct IdMapa
{
    /// <summary>Maximo de caracteres aceitos antes de qualquer conversao (int.MaxValue tem 10 digitos).</summary>
    public const int TamanhoMaximo = 10;

    public const int Minimo = 1;
    public const int Maximo = int.MaxValue;

    private IdMapa(int valor) => Valor = valor;

    public int Valor { get; }

    /// <summary>
    /// Valida o texto recebido da rota. A ordem importa (RA-4): tamanho antes de converter,
    /// depois somente digitos ASCII, depois int.TryParse estrito e por fim a faixa.
    /// </summary>
    public static bool TentarCriar(string? texto, out IdMapa id)
    {
        id = default;

        if (string.IsNullOrEmpty(texto) || texto.Length > TamanhoMaximo)
        {
            return false;
        }

        foreach (var c in texto)
        {
            if (!char.IsAsciiDigit(c))
            {
                return false;
            }
        }

        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var valor)
            || valor < Minimo)
        {
            return false;
        }

        id = new IdMapa(valor);
        return true;
    }

    public override string ToString() => Valor.ToString(CultureInfo.InvariantCulture);
}
