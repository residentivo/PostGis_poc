using TesteGraphy.Tests.Fakes;
using TesteGraphy.Validacao;

namespace TesteGraphy.Tests.Validacao;

public class WktValidatorTests
{
    private readonly WktValidator _validador = new();

    [Theory]
    [InlineData("POINT(0 0)", "POINT")]
    [InlineData("point(1 2)", "POINT")]
    [InlineData("  Point ( -46.6333   -23.5505 )  ", "POINT")]
    [InlineData("POINT(+1 -1)", "POINT")]
    [InlineData("POINT(1e1 -2.5E1)", "POINT")]
    [InlineData("POINT(180 90)", "POINT")]
    [InlineData("POINT(-180 -90)", "POINT")]
    [InlineData("POINT(180.0 -90.0)", "POINT")]
    [InlineData("POINT\t(\n0\r\n0\n)", "POINT")]
    [InlineData("LINESTRING(0 0, 1 1)", "LINESTRING")]
    [InlineData("linestring(0 0,1 1,2 2)", "LINESTRING")]
    [InlineData("POLYGON((0 0, 1 0, 1 1, 0 0))", "POLYGON")]
    [InlineData("POLYGON((0 0,10 0,10 10,0 10,0 0),(1 1,2 1,2 2,1 1))", "POLYGON")]
    [InlineData("MULTIPOINT(0 0, 1 1)", "MULTIPOINT")]
    [InlineData("MULTIPOINT((0 0), (1 1))", "MULTIPOINT")]
    [InlineData("MULTIPOINT((0 0), 1 1)", "MULTIPOINT")]
    [InlineData("MULTILINESTRING((0 0,1 1),(2 2,3 3))", "MULTILINESTRING")]
    [InlineData("MULTIPOLYGON(((0 0,1 0,1 1,0 0)),((2 2,3 2,3 3,2 2)))", "MULTIPOLYGON")]
    [InlineData("GEOMETRYCOLLECTION(POINT(0 0), LINESTRING(0 0,1 1))", "GEOMETRYCOLLECTION")]
    [InlineData("GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(POINT(0 0)), POLYGON((0 0,1 0,1 1,0 0)))", "GEOMETRYCOLLECTION")]
    [InlineData("GeometryCollection(MultiPoint(1 1))", "GEOMETRYCOLLECTION")]
    public void Validar_Aceitos(string wkt, string tipo)
    {
        var resultado = _validador.Validar(wkt);

        Assert.True(resultado.Valido, resultado.Erro);
        Assert.Null(resultado.Erro);
        Assert.Equal(tipo, resultado.Tipo);
    }

    [Theory]
    [InlineData(null, "nulo ou vazio")]
    [InlineData("", "nulo ou vazio")]
    [InlineData("   ", "nulo ou vazio")]
    [InlineData("SRID=4326;POINT(0 0)", "SRID=")]
    [InlineData("  srid=4326;POINT(0 0)", "SRID=")]
    [InlineData("POINT((0 0)", "desbalanceados")]
    [InlineData("POINT(0 0))", "desbalanceados")]
    [InlineData(")POINT(0 0)(", "desbalanceados")]
    [InlineData("POINT(0 0", "desbalanceados")]
    [InlineData("POINT(NaN 0)", "nao finito")]
    [InlineData("POINT(0 nan)", "nao finito")]
    [InlineData("POINT(Infinity 0)", "nao finito")]
    [InlineData("POINT(0 inf)", "nao finito")]
    [InlineData("POINT(INF 0)", "nao finito")]
    [InlineData("POINT(1e400 0)", "nao finito")]
    [InlineData("POINT(-1e400 0)", "nao finito")]
    [InlineData("POINT(0 1e400)", "nao finito")]
    [InlineData("POINT(-Infinity 0)", "numero invalido")]
    [InlineData("POINT(1,5 2)", "virgula decimal")]
    [InlineData("POINT(-46,63 -23,55)", "virgula decimal")]
    [InlineData("POINT Z(0 0 0)", "2D")]
    [InlineData("POINT M(0 0 0)", "2D")]
    [InlineData("POINT ZM(0 0 0 0)", "2D")]
    [InlineData("point z (0 0 0)", "2D")]
    [InlineData("POINT(0 0 0)", "apenas 2 coordenadas")]
    [InlineData("POINT EMPTY", "EMPTY")]
    [InlineData("GEOMETRYCOLLECTION EMPTY", "EMPTY")]
    [InlineData("POINT XYZ(0 0)", "palavra inesperada")]
    [InlineData("POINTZ(0 0 0)", "nao permitido")]
    [InlineData("CIRCULARSTRING(0 0, 1 1, 2 0)", "nao permitido")]
    [InlineData("TRIANGLE((0 0, 1 0, 0 1, 0 0))", "nao permitido")]
    [InlineData("CURVEPOLYGON((0 0, 1 0, 1 1, 0 0))", "nao permitido")]
    [InlineData("TIN(((0 0,1 0,0 1,0 0)))", "nao permitido")]
    [InlineData("POINT(0 0) POINT(1 1)", "conteudo inesperado")]
    [InlineData("POINT(0 0)x", "conteudo inesperado")]
    [InlineData("POINT(0 0);", "conteudo inesperado")]
    [InlineData("POINT(0 0)\u0000", "conteudo inesperado")]
    [InlineData("POINT(180.0001 0)", "longitude")]
    [InlineData("POINT(-180.0001 0)", "longitude")]
    [InlineData("POINT(0 90.0001)", "latitude")]
    [InlineData("POINT(0 -90.0001)", "latitude")]
    [InlineData("POINT(-46.6 -23.5)x", "conteudo inesperado")]
    [InlineData("POLYGON((0 0, 1 0, 1 1, 0 1))", "fechado")]
    [InlineData("POLYGON((0 0, 1 0, 0 0))", "ao menos 4")]
    [InlineData("POLYGON(0 0, 1 0, 1 1, 0 0)", "esperado '('")]
    [InlineData("LINESTRING(0 0)", "ao menos 2")]
    [InlineData("MULTILINESTRING((0 0))", "ao menos 2")]
    [InlineData("POINT(a 0)", "numero esperado")]
    [InlineData("POINT()", "numero esperado")]
    [InlineData("MULTIPOINT()", "numero esperado")]
    [InlineData("POINT(1-2 0)", "numero invalido")]
    [InlineData("POINT(0 0, 1 1)", "esperado ')' ou ','")]
    [InlineData("POINT 0 0", "esperado '('")]
    [InlineData("(0 0)", "tipo de geometria esperado")]
    [InlineData("GEOMETRYCOLLECTION()", "tipo de geometria esperado")]
    [InlineData("123", "tipo de geometria esperado")]
    [InlineData("POINT(0 0)'); DROP TABLE public.mapas;--", "desbalanceados")]
    [InlineData("POINT(0 0); DROP TABLE public.mapas;--", "conteudo inesperado")]
    [InlineData("POINT(0 0) --", "conteudo inesperado")]
    public void Validar_Rejeitados(string? wkt, string trechoErro)
    {
        var resultado = _validador.Validar(wkt);

        Assert.False(resultado.Valido);
        Assert.Null(resultado.Tipo);
        Assert.NotNull(resultado.Erro);
        Assert.Contains(trechoErro, resultado.Erro, StringComparison.OrdinalIgnoreCase);
        if (wkt is { Length: > 3 })
        {
            Assert.DoesNotContain(wkt.Trim(), resultado.Erro, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(9, true)]
    [InlineData(10, false)]
    [InlineData(50, false)]
    public void Validar_ProfundidadeMaxima10(int colecoesAninhadas, bool valido)
    {
        // Cada GEOMETRYCOLLECTION acrescenta 1 nivel e o POINT mais 1: 9 + 1 = 10 (aceito); 10 + 1 = 11 (rejeitado).
        var wkt = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", colecoesAninhadas))
            + "POINT(0 0)" + new string(')', colecoesAninhadas);

        var resultado = _validador.Validar(wkt);

        Assert.Equal(valido, resultado.Valido);
        if (!valido)
        {
            Assert.Contains("profundidade", resultado.Erro, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(10, false)]
    [InlineData(11, true)]
    public void Validar_ParentesesSoltosAlemDe10_RejeitaPorProfundidade(int niveis, bool porProfundidade)
    {
        var wkt = "POINT" + new string('(', niveis) + "0 0" + new string(')', niveis);

        var resultado = _validador.Validar(wkt);

        Assert.False(resultado.Valido);
        Assert.Equal(porProfundidade, resultado.Erro!.Contains("profundidade", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(WktValidator.TamanhoMaximo, true)]
    [InlineData(WktValidator.TamanhoMaximo + 1, false)]
    [InlineData(WktValidator.TamanhoMaximo * 10, false)]
    public void Validar_LimiteDeTamanho(int tamanho, bool valido)
    {
        const string inicio = "POINT(0 ";
        const string fim = "0)";
        var wkt = inicio + new string(' ', tamanho - inicio.Length - fim.Length) + fim;
        Assert.Equal(tamanho, wkt.Length);

        var resultado = _validador.Validar(wkt);

        Assert.Equal(valido, resultado.Valido);
        if (!valido)
        {
            Assert.Contains("10000", resultado.Erro, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Validar_MuitosPontos_DentroDoLimite_Aceito()
    {
        var pontos = string.Join(",", Enumerable.Range(0, 900).Select(i => $"{i % 180} {i % 90}"));

        var resultado = _validador.Validar($"LINESTRING({pontos})");

        Assert.True(resultado.Valido, resultado.Erro);
    }

    [Theory]
    [InlineData("POINT(0 0)", true)]
    [InlineData("POINT(0 0", false)]
    public void Validar_RegistraPassosNoLog(string wkt, bool valido)
    {
        var log = new LogCaptura();

        new WktValidator(log).Validar(wkt);

        Assert.Contains(log.Passos, p => p.Contains(valido ? "ok (tipo POINT)" : "rejeitado", StringComparison.Ordinal));
    }

    [Fact]
    public void TiposPermitidos_SaoOs7Esperados()
    {
        Assert.Equal(
            ["POINT", "LINESTRING", "POLYGON", "MULTIPOINT", "MULTILINESTRING", "MULTIPOLYGON", "GEOMETRYCOLLECTION"],
            WktValidator.TiposPermitidos);
    }

    [Fact]
    public void ResultadoValidacao_Fabricas()
    {
        var ok = ResultadoValidacao.Ok("POINT");
        var falha = ResultadoValidacao.Falha("motivo");

        Assert.True(ok.Valido);
        Assert.Equal("POINT", ok.Tipo);
        Assert.Null(ok.Erro);
        Assert.False(falha.Valido);
        Assert.Equal("motivo", falha.Erro);
        Assert.Null(falha.Tipo);
    }
}
