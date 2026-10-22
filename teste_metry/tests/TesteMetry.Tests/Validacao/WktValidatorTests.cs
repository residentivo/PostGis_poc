using System.Globalization;
using TesteMetry.Tests.Fakes;
using TesteMetry.Validacao;

namespace TesteMetry.Tests.Validacao;

/// <summary>M6.2 - WktValidator por SRID (D-M6, RQ-3, RQ-4, RQ-5).</summary>
public class WktValidatorTests
{
    public static readonly TheoryData<int> SridsPermitidos = new() { 4326, 4674, 3857, 31983 };

    [Theory]
    [InlineData(4326, "POINT(-46.6333 -23.5505)", "POINT")]
    [InlineData(4326, "point(0 0)", "POINT")]
    [InlineData(4326, "  POINT (0 0)  ", "POINT")]
    [InlineData(4326, "POINT(+1 -1)", "POINT")]
    [InlineData(4326, "POINT(1e1 -1E1)", "POINT")]
    [InlineData(4326, "POINT(-0 0)", "POINT")]
    [InlineData(4326, "LINESTRING(0 0, 1 1)", "LINESTRING")]
    [InlineData(4326, "POLYGON((0 0, 1 0, 1 1, 0 0))", "POLYGON")]
    [InlineData(4326, "POLYGON((0 0,10 0,10 10,0 10,0 0),(1 1,2 1,2 2,1 1))", "POLYGON")]
    [InlineData(4326, "MULTIPOINT(0 0, 1 1)", "MULTIPOINT")]
    [InlineData(4326, "MULTIPOINT((0 0), (1 1))", "MULTIPOINT")]
    [InlineData(4326, "MULTILINESTRING((0 0, 1 1), (2 2, 3 3))", "MULTILINESTRING")]
    [InlineData(4326, "MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((5 5, 6 5, 6 6, 5 5)))", "MULTIPOLYGON")]
    [InlineData(4326, "GEOMETRYCOLLECTION(POINT(0 0), LINESTRING(0 0, 1 1))", "GEOMETRYCOLLECTION")]
    [InlineData(4326, "GeometryCollection(GEOMETRYCOLLECTION(POINT(0 0)))", "GEOMETRYCOLLECTION")]
    [InlineData(3857, "POINT(-5191195.21 -2698729.46)", "POINT")]
    [InlineData(31983, "POINT(333250 7394600)", "POINT")]
    [InlineData(4674, "POINT(-46.6333 -23.5505)", "POINT")]
    public void Validos(int srid, string wkt, string tipo)
    {
        var resultado = new WktValidator(srid).Validar(wkt);

        Assert.True(resultado.Valido, resultado.Erro);
        Assert.Equal(tipo, resultado.Tipo);
        Assert.Null(resultado.Erro);
    }

    // ---- RQ-4: negativos obrigatorios (em todos os SRIDs) ----
    public static TheoryData<int, string, string> Rq4 => Combinar(
        ("SRID=4326;POINT(0 0)", "prefixo SRID= (EWKT) nao permitido"),
        ("srid=3857;POINT(0 0)", "prefixo SRID= (EWKT) nao permitido"),
        (" SRID=4326;POINT(0 0)", "prefixo SRID= (EWKT) nao permitido"),
        ("POINT(0 0);SRID=3857", "(U+003B) na posicao 11 (EWKT nao permitido"),
        ("POINT(0 0)=", "(U+003D) na posicao 11 (EWKT nao permitido"),
        ("CIRCULARSTRING(0 0,1 1,2 0)", "caractere nao permitido (U+0041)"),
        ("TRIANGLE((0 0,1 0,0 1,0 0))", "caractere nao permitido (U+0041)"),
        ("BOX(0 0,1 1)", "caractere nao permitido (U+0042) na posicao 1"),
        ("0101000000000000000000F03F000000000000F03F", "caractere nao permitido (U+0046)"),
        ("0101000000000000000000f03f000000000000f03f", "caractere nao permitido (U+0066)"),
        ("010100000000000000000000000000000000000000", "tipo de geometria esperado na posicao 1"),
        ("\\x0101000000000000000000F03F", "caractere nao permitido (U+005C) na posicao 1"),
        ("POINT(0\u00A00)", "(U+00A0) na posicao 8 (somente o espaco comum e aceito)"),
        ("POINT\u00A0(0 0)", "(U+00A0) na posicao 6 (somente o espaco comum e aceito)"),
        ("\u00A0POINT(0 0)", "(U+00A0) na posicao 1"),
        ("POINT(0 0)\u00A0", "(U+00A0) na posicao 11"));

    [Theory]
    [MemberData(nameof(Rq4))]
    public void Rq4_Rejeitados(int srid, string wkt, string trechoErro)
    {
        var resultado = new WktValidator(srid).Validar(wkt);

        Assert.False(resultado.Valido);
        Assert.Null(resultado.Tipo);
        Assert.Contains(trechoErro, resultado.Erro, StringComparison.Ordinal);
    }

    // ---- Regras gerais (independentes do SRID) ----
    public static TheoryData<int, string, string> Gerais => Combinar(
        (string.Empty, "WKT nulo ou vazio"),
        ("   ", "WKT nulo ou vazio"),
        ("\t\r\n", "WKT nulo ou vazio"),
        ("POINT Z (0 0 0)", "(U+005A) na posicao 7 (apenas geometrias 2D"),
        ("POINTZ(0 0 0)", "(U+005A)"),
        ("POINT M (0 0 0)", "apenas geometrias 2D sao aceitas"),
        ("POINT EMPTY", "geometria EMPTY nao e permitida"),
        ("POINT LINE(0 0)", "palavra inesperada na posicao 7"),
        ("POINTM(0 0 0)", "tipo de geometria nao permitido na posicao 1"),
        ("POLYGONS((0 0,1 0,1 1,0 0))", "tipo de geometria nao permitido"),
        ("(0 0)", "tipo de geometria esperado na posicao 1"),
        ("POINT 0 0", "esperado '(' na posicao 7"),
        ("POINT", "esperado '(' na posicao 6"),
        ("POINT(0 0 0)", "apenas 2 coordenadas (X Y) por ponto sao aceitas"),
        ("POINT(0 0,)", "esperado ')' ou ','"),
        ("POINT(1,5 2)", "coordenada deve ter X e Y separados por espaco"),
        ("POINT(1,5 2,5)", "coordenada deve ter X e Y separados por espaco"),
        ("POINT( )", "numero esperado na posicao 8"),
        ("POINT(- 0)", "numero invalido na posicao 7"),
        ("POINT(1.2.3 0)", "numero invalido"),
        ("POINT(1e999 0)", "numero nao finito (NaN/Infinity)"),
        ("POINT(0 -1e999)", "numero nao finito (NaN/Infinity)"),
        ("POINT(NaN 0)", "caractere nao permitido (U+0061)"),
        ("POINT(Infinity 0)", "caractere nao permitido (U+0066)"),
        ("POINT(INF 0)", "caractere nao permitido (U+0046)"),
        ("POINT(0 0", "parenteses desbalanceados (falta ')')"),
        ("POINT(0 0))", "parenteses desbalanceados (')' sem abertura na posicao 11)"),
        (")POINT(0 0)(", "parenteses desbalanceados (')' sem abertura na posicao 1)"),
        ("POINT(0 0) POINT(1 1)", "conteudo inesperado apos a geometria na posicao 12"),
        ("POINT(0 0)0", "conteudo inesperado apos a geometria"),
        ("LINESTRING(0 0)", "LINESTRING exige ao menos 2 pontos"),
        ("POLYGON((0 0, 1 0, 0 0))", "anel de POLYGON exige ao menos 4 pontos"),
        ("POLYGON((0 0, 1 0, 1 1, 0 1))", "anel de POLYGON deve ser fechado"),
        ("MULTIPOINT((0 0), (1 1 1))", "apenas 2 coordenadas"),
        ("MULTILINESTRING((0 0))", "LINESTRING exige ao menos 2 pontos"),
        ("MULTIPOLYGON(((0 0, 1 0, 1 1, 0 1)))", "anel de POLYGON deve ser fechado"),
        ("GEOMETRYCOLLECTION(POINT(0 0), CURVE(0 0))", "caractere nao permitido (U+0056)"),
        ("GEOMETRYCOLLECTION(POINT EMPTY)", "geometria EMPTY nao e permitida"),
        ("POINT(0\t0)", "(U+0009) na posicao 8 (caractere de controle)"),
        ("POINT(0 0)\u001B[2J", "(U+001B) na posicao 11 (caractere de controle)"),
        ("POINT(0 0)\u202E", "(U+202E) na posicao 11"),
        ("POINT(0 0)\u200B", "(U+200B)"),
        ("\uFEFFPOINT(0 0)", "(U+FEFF) na posicao 1"),
        ("POINT(\u0661 0)", "(U+0661)"),
        ("POINT(0 0)'; DROP TABLE mapas; --", "(U+0027) na posicao 11"),
        ("POINT(0 0)/*", "(U+002F)"));

    [Theory]
    [MemberData(nameof(Gerais))]
    public void Gerais_Rejeitados(int srid, string wkt, string trechoErro)
    {
        var resultado = new WktValidator(srid).Validar(wkt);

        Assert.False(resultado.Valido);
        Assert.Contains(trechoErro, resultado.Erro, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(SridsPermitidos))]
    public void Nulo_Rejeitado(int srid)
    {
        var resultado = new WktValidator(srid).Validar(null);

        Assert.False(resultado.Valido);
        Assert.Equal("WKT nulo ou vazio", resultado.Erro);
    }

    [Theory]
    [InlineData(10, true)]
    [InlineData(11, false)]
    public void ProfundidadeMaxima10(int profundidade, bool valido)
    {
        // (profundidade - 1) GEOMETRYCOLLECTION + POINT: cada um abre 1 parentese.
        var colecoes = profundidade - 1;
        var wkt = string.Concat(Enumerable.Repeat("GEOMETRYCOLLECTION(", colecoes)) + "POINT(0 0)" + new string(')', colecoes);

        var resultado = new WktValidator(4326).Validar(wkt);

        Assert.Equal(valido, resultado.Valido);
        if (!valido)
        {
            Assert.Equal("profundidade de parenteses excede o maximo de 10", resultado.Erro);
        }
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void TamanhoMaximo10000(int tamanho, bool valido)
    {
        const string ponto = "POINT(0 0)";
        var wkt = ponto + new string(' ', tamanho - ponto.Length);

        var resultado = new WktValidator(4326).Validar(wkt);

        Assert.Equal(tamanho, wkt.Length);
        Assert.Equal(valido, resultado.Valido);
        if (!valido)
        {
            Assert.Equal("WKT excede o limite de 10000 caracteres", resultado.Erro);
        }
    }

    // ---- RQ-5 / D-M6: limites exatos (inclusivos) e logo fora, por SRID ----
    public static TheoryData<int, string, string, bool> Limites()
    {
        var dados = new TheoryData<int, string, string, bool>();
        foreach (var srid in new[] { 4326, 4674 })
        {
            dados.Add(srid, "180", "90", true);
            dados.Add(srid, "-180", "-90", true);
            dados.Add(srid, "180.0000001", "0", false);
            dados.Add(srid, "-180.0000001", "0", false);
            dados.Add(srid, "0", "90.0000001", false);
            dados.Add(srid, "0", "-90.0000001", false);
            dados.Add(srid, "181", "0", false);
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
        var validador = new WktValidator(srid);
        var p = $"{x} {y}";
        var (ox, oy) = srid == 31983 ? ("500000", "7000000") : ("0", "0");
        var o = $"{ox} {oy}";

        var formas = new[]
        {
            $"POINT({p})",
            $"LINESTRING({o}, {p})",
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
            var resultado = validador.Validar(wkt);
            Assert.True(valido == resultado.Valido, $"SRID {srid}: {wkt} -> {resultado.Erro}");
            if (!valido)
            {
                Assert.Contains($"fora da faixa", resultado.Erro, StringComparison.Ordinal);
                Assert.Contains($"do SRID {srid}", resultado.Erro, StringComparison.Ordinal);
            }
        }
    }

    [Theory]
    [InlineData(4326, "POINT(181 0)", "longitude fora da faixa [-180, 180] do SRID 4326")]
    [InlineData(4674, "POINT(0 91)", "latitude fora da faixa [-90, 90] do SRID 4674")]
    [InlineData(3857, "POINT(30000000 0)", "X fora da faixa [-20037508.3428, 20037508.3428] do SRID 3857")]
    [InlineData(3857, "POINT(0 -30000000)", "Y fora da faixa [-20037508.3428, 20037508.3428] do SRID 3857")]
    [InlineData(31983, "POINT(-1 0)", "E fora da faixa [0, 1000000] do SRID 31983")]
    [InlineData(31983, "POINT(1 10000001)", "N fora da faixa [0, 10000000] do SRID 31983")]
    public void Limites_MensagemCitaEixoFaixaESrid(int srid, string wkt, string trecho)
    {
        var resultado = new WktValidator(srid).Validar(wkt);

        Assert.False(resultado.Valido);
        Assert.Contains(trecho, resultado.Erro, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("POINT(-5191195.21 -2698729.46)", 3857, true)]
    [InlineData("POINT(-5191195.21 -2698729.46)", 4326, false)]
    [InlineData("POINT(-5191195.21 -2698729.46)", 4674, false)]
    [InlineData("POINT(-5191195.21 -2698729.46)", 31983, false)]
    [InlineData("POINT(333250 7394600)", 31983, true)]
    [InlineData("POINT(333250 7394600)", 4326, false)]
    [InlineData("POINT(333250 7394600)", 4674, false)]
    [InlineData("POINT(333250 7394600)", 3857, true)]
    [InlineData("POINT(-46.6333 -23.5505)", 31983, false)]
    [InlineData("POINT(-46.6333 -23.5505)", 3857, true)]
    public void Rq5_MesmoWktDependeDoSrid(string wkt, int srid, bool valido)
    {
        Assert.Equal(valido, new WktValidator(srid).Validar(wkt).Valido);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-4326)]
    [InlineData(2154)]
    [InlineData(4269)]
    [InlineData(int.MaxValue)]
    public void Construtor_SridForaDaLista_Lanca(int srid)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new WktValidator(srid));
    }

    [Theory]
    [MemberData(nameof(SridsPermitidos))]
    public void Srid_ExpostoELogSemEcoDoWkt(int srid)
    {
        var log = new LogCaptura();
        var validador = new WktValidator(srid, log);
        const string marcador = "POINT(0 0)'; DROP TABLE mapas; --";

        validador.Validar("POINT(0 0)");
        validador.Validar(marcador);

        Assert.Equal(srid, validador.Srid);
        Assert.Contains(log.Passos, p => p.Contains($"SRID {srid.ToString(CultureInfo.InvariantCulture)}", StringComparison.Ordinal));
        Assert.Contains(log.Passos, p => p.Contains("ok (tipo POINT)", StringComparison.Ordinal));
        Assert.Contains(log.Passos, p => p.Contains("rejeitado (", StringComparison.Ordinal));
        Assert.DoesNotContain("DROP", log.Tudo, StringComparison.Ordinal);
    }

    [Fact]
    public void TiposPermitidos_Lista()
    {
        Assert.Equal(
            new[] { "POINT", "LINESTRING", "POLYGON", "MULTIPOINT", "MULTILINESTRING", "MULTIPOLYGON", "GEOMETRYCOLLECTION" },
            WktValidator.TiposPermitidos);
    }

    [Fact]
    public void Erro_NuncaEcoaOWktInteiro()
    {
        var wkt = "POINT(0 0) " + new string('X', 500);

        var resultado = new WktValidator(4326).Validar(wkt);

        Assert.False(resultado.Valido);
        Assert.DoesNotContain("XXXX", resultado.Erro, StringComparison.Ordinal);
    }

    private static string Mais(string v) =>
        (double.Parse(v, CultureInfo.InvariantCulture) + 1).ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// MF-1: NaN/Infinity por extenso barrados pela lista de caracteres; estouro de expoente
    /// = "nao finito"; letra permitida na posicao de numero = "numero esperado"; expoente
    /// incompleto = "numero invalido". Mensagem exata, com a posicao (base 1) do token.
    /// </summary>
    public static TheoryData<int, string, string> Mf1 => Combinar(
        ("POINT(NaN 0)", "caractere nao permitido (U+0061) na posicao 8"),
        ("POINT(nan 0)", "caractere nao permitido (U+0061) na posicao 8"),
        ("POINT(0 NaN)", "caractere nao permitido (U+0061) na posicao 10"),
        ("POINT(Infinity 0)", "caractere nao permitido (U+0066) na posicao 9"),
        ("POINT(-Infinity 0)", "caractere nao permitido (U+0066) na posicao 10"),
        ("POINT(inf 0)", "caractere nao permitido (U+0066) na posicao 9"),
        ("POINT(INF 0)", "caractere nao permitido (U+0046) na posicao 9"),
        ("POINT(1e999 0)", "numero nao finito (NaN/Infinity) na posicao 7"),
        ("POINT(-1e999 0)", "numero nao finito (NaN/Infinity) na posicao 7"),
        ("POINT(+1E999 0)", "numero nao finito (NaN/Infinity) na posicao 7"),
        ("POINT(0 1e999)", "numero nao finito (NaN/Infinity) na posicao 9"),
        ("POINT(0 -1e999)", "numero nao finito (NaN/Infinity) na posicao 9"),
        ("LINESTRING(0 0, 1e999 1)", "numero nao finito (NaN/Infinity) na posicao 17"),
        ("POINT(PO 0)", "numero esperado na posicao 7"),
        ("POINT(N 0)", "numero esperado na posicao 7"),
        ("POINT(0 PO)", "numero esperado na posicao 9"),
        ("POINT(1 Tim)", "numero esperado na posicao 9"),
        ("MULTIPOINT(0 0, LIN 1)", "numero esperado na posicao 17"),
        ("POINT(e 0)", "numero invalido na posicao 7"),
        ("POINT(E 0)", "numero invalido na posicao 7"),
        ("POINT(1e 0)", "numero invalido na posicao 7"),
        ("POINT(1e+ 0)", "numero invalido na posicao 7"),
        ("POINT(0 1e)", "numero invalido na posicao 9"),
        ("POINT(0 e1)", "numero invalido na posicao 9"));

    [Theory]
    [MemberData(nameof(Mf1))]
    public void Mf1_NaoFinitosELetrasNaPosicaoDeNumero_Rejeitados(int srid, string wkt, string erroEsperado)
    {
        var log = new LogCaptura();

        var resultado = new WktValidator(srid, log).Validar(wkt);

        Assert.False(resultado.Valido);
        Assert.Null(resultado.Tipo);
        Assert.Equal(erroEsperado, resultado.Erro);
        Assert.Contains(log.Passos, p => p == $"Validacao local do WKT: rejeitado ({erroEsperado}).");
    }

    private static TheoryData<int, string, string> Combinar(params (string Wkt, string Erro)[] casos)
    {
        var dados = new TheoryData<int, string, string>();
        foreach (var srid in new[] { 4326, 4674, 3857, 31983 })
        {
            foreach (var (wkt, erro) in casos)
            {
                dados.Add(srid, wkt, erro);
            }
        }

        return dados;
    }
}
