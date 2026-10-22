using TesteMetry.Comum;

namespace TesteMetry.Tests.Comum;

/// <summary>TextoSeguro (R2) e correcao C-1 da M5.</summary>
public class TextoSeguroTests
{
    private const string Emoji = "\U0001F600"; // par surrogate (2 chars)

    [Theory]
    [InlineData(null, "")]
    [InlineData("", "")]
    [InlineData("POINT(0 0)", "POINT(0 0)")]
    [InlineData("a\rb\nc", "a?b?c")]
    [InlineData("a\tb", "a?b")]
    [InlineData("\u001B[31mvermelho", "?[31mvermelho")]
    [InlineData("\u0000\u0007\u007F", "???")]
    [InlineData("\u0085", "?")]
    [InlineData("abc\u202Edef", "abc?def")]                  // RLO
    [InlineData("\u2066x\u2069", "?x?")]                     // isolates bidi
    [InlineData("a\u200Bb\u200Cc\u200Dd", "a?b?c?d")]       // zero width
    [InlineData("a\u00A0b", "a?b")]                          // NBSP
    [InlineData("a\u2003b\u3000c", "a?b?c")]                 // outros espacos Unicode
    [InlineData("\uFEFFabc", "?abc")]                        // BOM
    [InlineData("a\u2028b\u2029c", "a?b?c")]                 // separadores de linha/paragrafo
    [InlineData("a\uE000b", "a?b")]                          // uso privado
    [InlineData("a\u0378b", "a?b")]                          // nao atribuido
    [InlineData("a\U000E0001b", "a?b")]                      // tag (Format) fora do BMP
    [InlineData("a\U000F0000b", "a?b")]                      // uso privado suplementar
    [InlineData("Sao Paulo: a\u00E7\u00E3o, Pra\u00E7a da S\u00E9, \u00C1\u00C9\u00CD\u00D3\u00DA \u00F1 \u00FC", "Sao Paulo: a\u00E7\u00E3o, Pra\u00E7a da S\u00E9, \u00C1\u00C9\u00CD\u00D3\u00DA \u00F1 \u00FC")]
    [InlineData("a" + Emoji + "b", "a" + Emoji + "b")]
    [InlineData("espaco comum ok", "espaco comum ok")]
    public void Sanitizar(string? entrada, string esperado)
    {
        Assert.Equal(esperado, TextoSeguro.Sanitizar(entrada));
    }

    /// <summary>Surrogates soltos nao podem ir em InlineData (o xUnit os troca por U+FFFD na serializacao).</summary>
    [Fact]
    public void SurrogateSolto_Trocado()
    {
        const char alto = (char)0xD800;
        const char baixo = (char)0xDC00;

        Assert.Equal("a?b", TextoSeguro.Sanitizar("a" + alto + "b"));
        Assert.Equal("a?b", TextoSeguro.Sanitizar("a" + baixo + "b"));
        Assert.Equal("a??b", TextoSeguro.Sanitizar("a" + baixo + alto + "b"));
        Assert.Equal("a?", TextoSeguro.Sanitizar("a" + alto));
    }

    [Fact]
    public void Truncamento_200MaisReticencias()
    {
        var texto = new string('x', 250);

        var resultado = TextoSeguro.Sanitizar(texto);

        Assert.Equal(new string('x', 200) + "...", resultado);
    }

    [Fact]
    public void ExatamenteNoLimite_NaoTrunca()
    {
        var texto = new string('x', 200);

        Assert.Equal(texto, TextoSeguro.Sanitizar(texto));
    }

    [Theory]
    [InlineData(198, 200, false)] // emoji nos chars 199-200: cabe inteiro
    [InlineData(199, 199, true)]  // emoji nos chars 200-201: high surrogate na posicao 200 -> recua
    public void Emoji_NaFronteira_NaoPartePar(int prefixo, int esperadoAntesReticencias, bool emojiRemovido)
    {
        var texto = new string('a', prefixo) + Emoji + "bbbbbbbb";

        var resultado = TextoSeguro.Sanitizar(texto);

        Assert.EndsWith("...", resultado, StringComparison.Ordinal);
        var corpo = resultado[..^3];
        Assert.Equal(esperadoAntesReticencias, corpo.Length);
        Assert.Equal(emojiRemovido, !corpo.Contains(Emoji, StringComparison.Ordinal));
        Assert.False(char.IsHighSurrogate(corpo[^1]));
        Assert.DoesNotContain('?', corpo);
        for (var i = 0; i < corpo.Length; i++)
        {
            if (char.IsHighSurrogate(corpo[i]))
            {
                Assert.True(i + 1 < corpo.Length && char.IsLowSurrogate(corpo[i + 1]));
            }
        }
    }

    [Fact]
    public void Emoji_TerminandoExatamenteEm200_Mantido()
    {
        var texto = new string('a', 198) + Emoji;

        Assert.Equal(texto, TextoSeguro.Sanitizar(texto));
    }

    [Theory]
    [InlineData("abcdef", 3, "abc...")]
    [InlineData("abc", 3, "abc")]
    [InlineData("abc", 0, "...")]
    [InlineData("abc", -5, "...")]
    [InlineData("a\nbcdef", 2, "a?...")]
    public void TamanhoMaximoInformado(string entrada, int maximo, string esperado)
    {
        Assert.Equal(esperado, TextoSeguro.Sanitizar(entrada, maximo));
    }

    [Theory]
    [InlineData("Host=x;Password=segredo", true)]
    [InlineData("host=x;password=segredo", true)]
    [InlineData("SENHA=123", true)]
    [InlineData("Server=x;Pwd=abc", true)]
    [InlineData("POINT(0 0)", false)]
    [InlineData("POINT(0 0)\nlinha", false)]
    [InlineData(null, false)]
    public void EcoWkt(string? wkt, bool omitido)
    {
        var eco = TextoSeguro.EcoWkt(wkt);

        if (omitido)
        {
            Assert.Equal(TextoSeguro.EcoOmitido, eco);
        }
        else
        {
            Assert.Equal(TextoSeguro.Sanitizar(wkt), eco);
        }
    }
}
