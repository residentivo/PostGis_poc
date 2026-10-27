using ApiMapas.Dominio.Geometrias;

namespace ApiMapas.Tests.Dominio;

/// <summary>
/// P6.1 - WktValidator do PUT (D-P5, D-P7, RP-14). Casos portados de
/// teste_metry/tests/TesteMetry.Tests/Validacao/WktValidatorTests.cs, com as mensagens fixas
/// (acentuadas) da copia adaptada em ApiMapas.Dominio.Geometrias.
/// </summary>
public class WktValidatorTests
{
    private static FaixaSrid Faixa(int srid) => LimitesSrid.Para(srid)!;

    public static readonly TheoryData<int> SridsConhecidos = new() { 4326, 4674, 3857, 31983 };

    [Theory]
    [InlineData(4326, "POINT(-46.6333 -23.5505)", "POINT")]
    [InlineData(4326, "point(0 0)", "POINT")]
    [InlineData(4326, "  POINT (0 0)  ", "POINT")]
    [InlineData(4326, "POINT(+1 -1)", "POINT")]
    [InlineData(4326, "POINT(1e1 -1E1)", "POINT")]
    [InlineData(4326, "POINT(-0 0)", "POINT")]
    [InlineData(4326, "POINT(.5 5.)", "POINT")]
    [InlineData(4326, "POINT(1e-999 0)", "POINT")]
    [InlineData(4326, "LINESTRING(0 0, 1 1)", "LINESTRING")]
    [InlineData(4326, "LineString(0 0,1 1,2 2)", "LINESTRING")]
    [InlineData(4326, "POLYGON((0 0, 1 0, 1 1, 0 0))", "POLYGON")]
    [InlineData(4326, "POLYGON((0 0,10 0,10 10,0 10,0 0),(1 1,2 1,2 2,1 1))", "POLYGON")]
    [InlineData(4326, "MULTIPOINT(0 0, 1 1)", "MULTIPOINT")]
    [InlineData(4326, "MULTIPOINT((0 0), (1 1))", "MULTIPOINT")]
    [InlineData(4326, "MULTIPOINT((0 0), 1 1)", "MULTIPOINT")]
    [InlineData(4326, "MULTILINESTRING((0 0, 1 1), (2 2, 3 3))", "MULTILINESTRING")]
    [InlineData(4326, "MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", "MULTIPOLYGON")]
    [InlineData(4326, "GEOMETRYCOLLECTION(POINT(0 0), LINESTRING(0 0, 1 1))", "GEOMETRYCOLLECTION")]
    [InlineData(4326, "GeometryCollection(GEOMETRYCOLLECTION(POINT(0 0)))", "GEOMETRYCOLLECTION")]
    [InlineData(3857, "POINT(-5191195.21 -2698729.46)", "POINT")]
    [InlineData(31983, "POINT(333250 7394600)", "POINT")]
    [InlineData(4674, "POINT(-46.6333 -23.5505)", "POINT")]
    public void Validos(int srid, string wkt, string tipo)
    {
        var resultado = WktValidator.Validar(wkt, Faixa(srid));

        Assert.True(resultado.Valido, resultado.Motivo);
        Assert.Equal(tipo, resultado.Tipo);
        Assert.Null(resultado.Motivo);
    }

    private static TheoryData<int, string, string> Combinar(params (string Wkt, string Trecho)[] casos)
    {
        var dados = new TheoryData<int, string, string>();
        foreach (var srid in new[] { 4326, 4674, 3857, 31983 })
        {
            foreach (var (wkt, trecho) in casos)
            {
                dados.Add(srid, wkt, trecho);
            }
        }

        return dados;
    }

    // ---- EWKT, HEX/WKB, tipos fora da lista e espacos Unicode (em todos os SRIDs) ----
    public static TheoryData<int, string, string> ForaDoFormato => Combinar(
        ("SRID=4326;POINT(0 0)", "prefixo SRID= (EWKT) não permitido"),
        ("srid=3857;POINT(0 0)", "prefixo SRID= (EWKT) não permitido"),
        (" SRID=4326;POINT(0 0)", "prefixo SRID= (EWKT) não permitido"),
        ("SRID", "prefixo SRID= (EWKT) não permitido"),
        ("POINT(0 0);SRID=3857", "(U+003B) na posição 11 (EWKT não permitido"),
        ("POINT(0 0)=", "(U+003D) na posição 11 (EWKT não permitido"),
        ("CIRCULARSTRING(0 0,1 1,2 0)", "caractere não permitido (U+0041)"),
        ("TRIANGLE((0 0,1 0,0 1,0 0))", "caractere não permitido (U+0041)"),
        ("TIN(((0 0,0 1,1 0,0 0)))", "tipo de geometria não permitido na posição 1"),
        ("BOX(0 0,1 1)", "caractere não permitido (U+0042) na posição 1"),
        ("0101000000000000000000F03F000000000000F03F", "caractere não permitido (U+0046)"),
        ("0101000000000000000000f03f000000000000f03f", "caractere não permitido (U+0066)"),
        ("010100000000000000000000000000000000000000", "tipo de geometria esperado na posição 1"),
        ("\\x0101000000000000000000F03F", "caractere não permitido (U+005C) na posição 1"),
        ("POINT(0 0)", "(U+00A0) na posição 8 (somente o espaço comum é aceito)"),
        ("POINT (0 0)", "(U+00A0) na posição 6 (somente o espaço comum é aceito)"),
        (" POINT(0 0)", "(U+00A0) na posição 1"),
        ("POINT(0 0) ", "(U+00A0) na posição 11"));

    [Theory]
    [MemberData(nameof(ForaDoFormato))]
    public void ForaDoFormato_Rejeitados(int srid, string wkt, string trecho)
    {
        var resultado = WktValidator.Validar(wkt, Faixa(srid));

        Assert.False(resultado.Valido);
        Assert.Null(resultado.Tipo);
        Assert.Contains(trecho, resultado.Motivo, StringComparison.Ordinal);
    }

    // ---- Regras gerais (independentes do SRID) ----
    public static TheoryData<int, string, string> Gerais => Combinar(
        (string.Empty, "WKT nulo ou vazio"),
        ("   ", "WKT nulo ou vazio"),
        ("\t\r\n", "WKT nulo ou vazio"),
        ("POINT Z (0 0 0)", "(U+005A) na posição 7 (apenas geometrias 2D"),
        ("POINTZ(0 0 0)", "(U+005A)"),
        ("point z (0 0 0)", "(U+007A)"),
        ("POINT M (0 0 0)", "apenas geometrias 2D são aceitas"),
        ("POINT EMPTY", "geometria EMPTY não é permitida"),
        ("POINT LINE(0 0)", "palavra inesperada na posição 7"),
        ("POINTM(0 0 0)", "tipo de geometria não permitido na posição 1"),
        ("POLYGONS((0 0,1 0,1 1,0 0))", "tipo de geometria não permitido"),
        ("(0 0)", "tipo de geometria esperado na posição 1"),
        ("POINT 0 0", "esperado '(' na posição 7"),
        ("POINT", "esperado '(' na posição 6"),
        ("POINT(0 0 0)", "apenas 2 coordenadas (X Y) por ponto são aceitas"),
        ("POINT(0 0,)", "esperado ')' ou ','"),
        ("POINT(0 0, 1 1)", "esperado ')' ou ','"),
        ("POINT(1,5 2)", "coordenada deve ter X e Y separados por espaço"),
        ("POINT(1,5 2,5)", "coordenada deve ter X e Y separados por espaço"),
        ("POINT( )", "número esperado na posição 8"),
        ("POINT(- 0)", "número inválido na posição 7"),
        ("POINT(1.2.3 0)", "número inválido"),
        ("POINT(1e 0)", "número inválido"),
        ("POINT(1-2 0)", "número inválido"),
        ("POINT(--1 0)", "número inválido"),
        ("POINT(1e999 0)", "número não finito (NaN/Infinity)"),
        ("POINT(0 -1e999)", "número não finito (NaN/Infinity)"),
        ("POINT(NaN 0)", "caractere não permitido (U+0061)"),
        ("POINT(Infinity 0)", "caractere não permitido (U+0066)"),
        ("POINT(INF 0)", "caractere não permitido (U+0046)"),
        ("POINT(0x10 0)", "caractere não permitido (U+0078)"),
        ("POINT(0 0", "parênteses desbalanceados (falta ')')"),
        ("POINT(0 0))", "parênteses desbalanceados (')' sem abertura na posição 11)"),
        (")POINT(0 0)(", "parênteses desbalanceados (')' sem abertura na posição 1)"),
        ("POINT(0 0) POINT(1 1)", "conteúdo inesperado após a geometria na posição 12"),
        ("POINT(0 0)0", "conteúdo inesperado após a geometria"),
        ("LINESTRING(0 0)", "LINESTRING exige ao menos 2 pontos"),
        ("POLYGON((0 0, 1 0, 0 0))", "anel de POLYGON exige ao menos 4 pontos"),
        ("POLYGON((0 0, 1 0, 1 1, 0 1))", "anel de POLYGON deve ser fechado"),
        ("MULTIPOINT((0 0), (1 1 1))", "apenas 2 coordenadas"),
        ("MULTILINESTRING((0 0))", "LINESTRING exige ao menos 2 pontos"),
        ("MULTIPOLYGON(((0 0, 1 0, 1 1, 0 1)))", "anel de POLYGON deve ser fechado"),
        ("GEOMETRYCOLLECTION(POINT(0 0), CURVE(0 0))", "caractere não permitido (U+0056)"),
        ("GEOMETRYCOLLECTION(POINT EMPTY)", "geometria EMPTY não é permitida"),
        ("GEOMETRYCOLLECTION EMPTY", "geometria EMPTY não é permitida"),
        ("GEOMETRYCOLLECTION()", "tipo de geometria esperado"),
        ("POINT(0\t0)", "(U+0009) na posição 8 (caractere de controle)"),
        ("POINT(0 0)\u001B[2J", "(U+001B) na posição 11 (caractere de controle)"),
        ("POINT(0 0)‮", "(U+202E) na posição 11"),
        ("POINT(0 0)​", "(U+200B)"),
        ("﻿POINT(0 0)", "(U+FEFF) na posição 1"),
        ("POINT(١ 0)", "(U+0661)"),
        ("POINT(0 0)'; DROP TABLE mapas; --", "(U+0027) na posição 11"),
        ("POINT(0 0)/*", "(U+002F)"));

    [Theory]
    [MemberData(nameof(Gerais))]
    public void Gerais_Rejeitados(int srid, string wkt, string trecho)
    {
        var resultado = WktValidator.Validar(wkt, Faixa(srid));

        Assert.False(resultado.Valido);
        Assert.Null(resultado.Tipo);
        Assert.Contains(trecho, resultado.Motivo, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SridsConhecidos))]
    public void Nulo_Rejeitado(int srid)
    {
        var resultado = WktValidator.Validar(null, Faixa(srid));

        Assert.False(resultado.Valido);
        Assert.Equal("WKT nulo ou vazio", resultado.Motivo);
    }

    [Fact]
    public void FaixaNula_Lanca()
    {
        Assert.Throws<ArgumentNullException>(() => WktValidator.Validar("POINT(0 0)", null!));
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void ProfundidadeMaxima10(int profundidade, bool valido)
    {
        var colecoes = profundidade - 1;
        var wkt = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", colecoes)) + "POINT(0 0)" + new string(')', colecoes);

        var resultado = WktValidator.Validar(wkt, Faixa(4326));

        Assert.Equal(valido, resultado.Valido);
        if (!valido)
        {
            Assert.Equal("profundidade de parênteses excede o máximo de 10", resultado.Motivo);
        }
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void TamanhoMaximo10000_ContaOsEspacosDasPontas(int tamanho, bool valido)
    {
        const string ponto = "POINT(0 0)";
        var wkt = ponto + new string(' ', tamanho - ponto.Length);

        var resultado = WktValidator.Validar(wkt, Faixa(4326));

        Assert.Equal(tamanho, wkt.Length);
        Assert.Equal(valido, resultado.Valido);
        if (!valido)
        {
            Assert.Equal("WKT excede o limite de 10000 caracteres", resultado.Motivo);
        }
    }

    [Fact]
    public void TamanhoMaximo_LimitesEscritaUsaOMesmoValor()
    {
        Assert.Equal(10_000, LimitesEscrita.TamanhoMaximoWkt);
        Assert.Equal(WktValidator.TamanhoMaximo, LimitesEscrita.TamanhoMaximoWkt);
    }

    // ---- D-P5: limites exatos (inclusivos) e logo fora, por SRID, em TODOS os niveis ----
    public static TheoryData<int, string, string, bool> Limites()
    {
        var dados = new TheoryData<int, string, string, bool>();
        foreach (var srid in new[] { 4326, 4674 })
        {
            dados.Add(srid, "180", "90", true);
            dados.Add(srid, "-180", "-90", true);
            dados.Add(srid, "+180", "+90", true);
            dados.Add(srid, "1.8e2", "9e1", true);
            dados.Add(srid, "180.0000001", "0", false);
            dados.Add(srid, "-180.0000001", "0", false);
            dados.Add(srid, "0", "90.0000001", false);
            dados.Add(srid, "0", "-90.0000001", false);
            dados.Add(srid, "181", "0", false);
            dados.Add(srid, "1.8000001e2", "0", false);
            dados.Add(srid, "0", "9.0000001E1", false);
            dados.Add(srid, "360", "0", false);
            dados.Add(srid, "-46.63", "-123.55", false);
            dados.Add(srid, "1e308", "0", false);
        }

        dados.Add(3857, "20037508.3428", "20037508.3428", true);
        dados.Add(3857, "-20037508.3428", "-20037508.3428", true);
        dados.Add(3857, "20037508.3429", "0", false);
        dados.Add(3857, "-20037508.3429", "0", false);
        dados.Add(3857, "0", "20037508.3429", false);
        dados.Add(3857, "0", "-20037508.3429", false);

        dados.Add(31983, "0", "0", true);
        dados.Add(31983, "1000000", "10000000", true);
        dados.Add(31983, "-0.001", "0", false);
        dados.Add(31983, "1000000.001", "0", false);
        dados.Add(31983, "0", "-0.001", false);
        dados.Add(31983, "0", "10000000.001", false);
        return dados;
    }

    [Theory]
    [MemberData(nameof(Limites))]
    public void Limites_FaixaInclusiva_EmTodosOsNiveis(int srid, string x, string y, bool valido)
    {
        var faixa = Faixa(srid);
        var p = $"{x} {y}";
        var (ox, oy) = srid == 31983 ? ("500000", "7000000") : ("0", "0");
        var o = $"{ox} {oy}";

        var formas = new[]
        {
            $"POINT({p})",
            $"LINESTRING({o}, {p})",
            $"LINESTRING({p}, {o})",
            $"POLYGON(({o}, {p}, {ox} {Mais(oy)}, {o}))",
            $"POLYGON(({o}, {Mais(ox)} {oy}, {ox} {Mais(oy)}, {o}), ({o}, {p}, {Mais(ox)} {Mais(oy)}, {o}))",
            $"MULTIPOINT({o}, {p})",
            $"MULTIPOINT(({o}), ({p}))",
            $"MULTILINESTRING(({o}, {Mais(ox)} {oy}), ({o}, {p}))",
            $"MULTIPOLYGON((({o}, {Mais(ox)} {oy}, {ox} {Mais(oy)}, {o})), (({o}, {p}, {ox} {Mais(oy)}, {o})))",
            $"GEOMETRYCOLLECTION(POINT({o}), POINT({p}))",
            $"GEOMETRYCOLLECTION(POINT({o}), GEOMETRYCOLLECTION(MULTILINESTRING(({o}, {p}))))",
            $"GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(GEOMETRYCOLLECTION(MULTIPOLYGON((({o}, {p}, {ox} {Mais(oy)}, {o}))))))",
        };

        foreach (var wkt in formas)
        {
            var resultado = WktValidator.Validar(wkt, faixa);
            Assert.True(valido == resultado.Valido, $"SRID {srid}: {wkt} -> {resultado.Motivo}");
            if (!valido)
            {
                Assert.Contains("fora da faixa", resultado.Motivo, StringComparison.Ordinal);
                Assert.Contains($"do SRID {srid}", resultado.Motivo, StringComparison.Ordinal);
            }
        }
    }

    private static string Mais(string valor) =>
        (double.Parse(valor, System.Globalization.CultureInfo.InvariantCulture) + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

    [Theory]
    [InlineData(4326, "POINT(181 0)", "longitude fora da faixa [-180, 180] do SRID 4326 antes da posição 11")]
    [InlineData(4326, "POINT(0 -91)", "latitude fora da faixa [-90, 90] do SRID 4326 na posição 9")]
    [InlineData(4674, "POINT(0 91)", "latitude fora da faixa [-90, 90] do SRID 4674")]
    [InlineData(3857, "POINT(30000000 0)", "X fora da faixa [-20037508.3428, 20037508.3428] do SRID 3857")]
    [InlineData(3857, "POINT(0 -30000000)", "Y fora da faixa [-20037508.3428, 20037508.3428] do SRID 3857")]
    [InlineData(31983, "POINT(-1 0)", "E fora da faixa [0, 1000000] do SRID 31983")]
    [InlineData(31983, "POINT(1 10000001)", "N fora da faixa [0, 10000000] do SRID 31983")]
    public void Limites_MensagemCitaEixoFaixaESrid_SemEcoarCoordenada(int srid, string wkt, string trecho)
    {
        var resultado = WktValidator.Validar(wkt, Faixa(srid));

        Assert.False(resultado.Valido);
        Assert.Contains(trecho, resultado.Motivo, StringComparison.Ordinal);
        Assert.DoesNotContain(wkt, resultado.Motivo, StringComparison.Ordinal);
    }

    /// <summary>RP-14: nenhuma mensagem ecoa o WKT nem trecho reconhecivel dele.</summary>
    [Theory]
    [InlineData("POINT(0 0)'; DROP TABLE mapas; --")]
    [InlineData("POINT(0 0)<script>alert(1)</script>")]
    [InlineData("SRID=4326;POINT(SEGREDO 0)")]
    [InlineData("POINT(123456789 987654321)")]
    [InlineData("LINESTRING(SEGREDO)")]
    public void Motivo_NuncaEcoaOWkt(string wkt)
    {
        var resultado = WktValidator.Validar(wkt, Faixa(4326));

        Assert.False(resultado.Valido);
        Assert.DoesNotContain("DROP", resultado.Motivo, StringComparison.Ordinal);
        Assert.DoesNotContain("script", resultado.Motivo, StringComparison.Ordinal);
        Assert.DoesNotContain("SEGREDO", resultado.Motivo, StringComparison.Ordinal);
        Assert.DoesNotContain("123456789", resultado.Motivo, StringComparison.Ordinal);
    }

    [Fact]
    public void TiposPermitidos_SaoOsSeteDoContrato()
    {
        Assert.Equal(
            new[] { "POINT", "LINESTRING", "POLYGON", "MULTIPOINT", "MULTILINESTRING", "MULTIPOLYGON", "GEOMETRYCOLLECTION" },
            WktValidator.TiposPermitidos);
    }
}
