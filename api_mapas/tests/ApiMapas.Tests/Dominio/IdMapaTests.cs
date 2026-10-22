using ApiMapas.Dominio;

namespace ApiMapas.Tests.Dominio;

public class IdMapaTests
{
    [Theory]
    [InlineData("abc")]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("1.5")]
    [InlineData("+1")]
    [InlineData("2147483648")]
    [InlineData("9999999999")]
    [InlineData("12345678901")]
    [InlineData("1'")]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData(" 1")]
    [InlineData("1 ")]
    [InlineData("0x1")]
    [InlineData("1\t")]
    [InlineData("1e3")]
    [InlineData("1,0")]
    [InlineData("00000000000")] // 11 caracteres
    [InlineData("0000000000")]  // 10 zeros = 0
    [InlineData(null)]
    public void TentarCriar_Invalido_DevolveFalseEDefault(string? texto)
    {
        var ok = IdMapa.TentarCriar(texto, out var id);

        Assert.False(ok);
        Assert.Equal(default, id);
        Assert.Equal(0, id.Valor);
    }

    /// <summary>Digitos Unicode nao ASCII (char.IsDigit = true) devem ser recusados.</summary>
    [Theory]
    [InlineData(0x0661, "", "")]   // arabe-indico 1, sozinho
    [InlineData(0x0661, "1", "")]  // "1" + arabe-indico
    [InlineData(0x0661, "", "2")]
    [InlineData(0x06F1, "", "")]   // persa 1
    [InlineData(0x0967, "", "")]   // devanagari 1
    [InlineData(0xFF11, "", "")]   // digito largo 1
    [InlineData(0x00B9, "", "")]   // sobrescrito 1
    [InlineData(0x00A0, "1", "")]  // NBSP
    public void TentarCriar_DigitoUnicodeNaoAscii_Recusa(int codigo, string antes, string depois)
    {
        var texto = antes + (char)codigo + depois;

        Assert.False(IdMapa.TentarCriar(texto, out var id));
        Assert.Equal(0, id.Valor);
    }

    [Theory]
    [InlineData("1", 1)]
    [InlineData("01", 1)]
    [InlineData("0000000001", 1)]
    [InlineData("42", 42)]
    [InlineData("2147483646", 2147483646)]
    [InlineData("2147483647", 2147483647)]
    public void TentarCriar_Valido_DevolveValor(string texto, int esperado)
    {
        var ok = IdMapa.TentarCriar(texto, out var id);

        Assert.True(ok);
        Assert.Equal(esperado, id.Valor);
        Assert.Equal(esperado.ToString(System.Globalization.CultureInfo.InvariantCulture), id.ToString());
    }

    [Fact]
    public void Constantes_DoContrato()
    {
        Assert.Equal(10, IdMapa.TamanhoMaximo);
        Assert.Equal(1, IdMapa.Minimo);
        Assert.Equal(int.MaxValue, IdMapa.Maximo);
    }

    [Fact]
    public void Igualdade_PorValor()
    {
        IdMapa.TentarCriar("7", out var a);
        IdMapa.TentarCriar("007", out var b);
        Assert.Equal(a, b);
    }
}
