using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;

namespace ApiMapas.Tests.Dominio;

/// <summary>P6.1 - GeometriaEntrada, politica de SRID por coluna e LimitesSrid (D-P5, RP-14).</summary>
public class GeometriaEntradaTests
{
    [Theory]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(-46.633309 -23.55052)", "POINT")]
    [InlineData(ColunaMapa.Graphy, 4326, "  LINESTRING(-46.64 -23.56, -46.62 -23.54)  ", "LINESTRING")]
    [InlineData(ColunaMapa.Graphy, 4326, "POLYGON((-180 -90, 180 -90, 180 90, -180 -90))", "POLYGON")]
    [InlineData(ColunaMapa.Metry, 4326, "POINT(-46.63 -23.55)", "POINT")]
    [InlineData(ColunaMapa.Metry, 4674, "MULTIPOINT(-46.63 -23.55, -46.64 -23.56)", "MULTIPOINT")]
    [InlineData(ColunaMapa.Metry, 3857, "POINT(-5191195.21 -2698729.46)", "POINT")]
    [InlineData(ColunaMapa.Metry, 31983, "POLYGON((333000 7394000,334000 7394000,334000 7395000,333000 7394000))", "POLYGON")]
    public void TentarCriar_Valida(ColunaMapa coluna, int srid, string wkt, string tipo)
    {
        Assert.True(GeometriaEntrada.TentarCriar(wkt, srid, coluna, out var geometria, out var motivo), motivo);

        Assert.Null(motivo);
        Assert.NotNull(geometria);
        Assert.Equal(wkt.Trim(' '), geometria!.Wkt);
        Assert.Equal(srid, geometria.Srid);
        Assert.Equal(tipo, geometria.Tipo);
    }

    // ---- D-P5: graphy so 4326; metry 4326/4674/3857/31983 ----
    [Theory]
    [InlineData(ColunaMapa.Graphy, 4674, "srid não permitido para a coluna 'graphy' (aceitos: 4326)")]
    [InlineData(ColunaMapa.Graphy, 3857, "srid não permitido para a coluna 'graphy' (aceitos: 4326)")]
    [InlineData(ColunaMapa.Graphy, 31983, "srid não permitido para a coluna 'graphy' (aceitos: 4326)")]
    [InlineData(ColunaMapa.Graphy, 0, "srid não permitido para a coluna 'graphy' (aceitos: 4326)")]
    [InlineData(ColunaMapa.Graphy, -4326, "srid não permitido para a coluna 'graphy' (aceitos: 4326)")]
    [InlineData(ColunaMapa.Graphy, 4269, "srid não permitido para a coluna 'graphy' (aceitos: 4326)")]
    [InlineData(ColunaMapa.Graphy, int.MaxValue, "srid não permitido para a coluna 'graphy' (aceitos: 4326)")]
    [InlineData(ColunaMapa.Metry, 0, "srid não permitido para a coluna 'metry' (aceitos: 4326, 4674, 3857, 31983)")]
    [InlineData(ColunaMapa.Metry, 4269, "srid não permitido para a coluna 'metry' (aceitos: 4326, 4674, 3857, 31983)")]
    [InlineData(ColunaMapa.Metry, 900913, "srid não permitido para a coluna 'metry' (aceitos: 4326, 4674, 3857, 31983)")]
    [InlineData(ColunaMapa.Metry, int.MinValue, "srid não permitido para a coluna 'metry' (aceitos: 4326, 4674, 3857, 31983)")]
    public void TentarCriar_SridForaDaColuna(ColunaMapa coluna, int srid, string esperado)
    {
        Assert.False(GeometriaEntrada.TentarCriar("POINT(0 0)", srid, coluna, out var geometria, out var motivo));

        Assert.Null(geometria);
        Assert.Equal(esperado, motivo);
    }

    /// <summary>
    /// RP-14: o SRID e verificado ANTES das faixas e do WKT. Um WKT que estouraria a faixa do SRID
    /// recebido (ou que nem e valido) responde o motivo de SRID.
    /// </summary>
    [Theory]
    [InlineData(ColunaMapa.Graphy, 3857, "POINT(30000000 0)")]
    [InlineData(ColunaMapa.Graphy, 31983, "POINT(-1 -1)")]
    [InlineData(ColunaMapa.Graphy, 0, null)]
    [InlineData(ColunaMapa.Graphy, 0, "lixo;SRID=")]
    [InlineData(ColunaMapa.Metry, 4269, "POINT(500 500)")]
    [InlineData(ColunaMapa.Metry, 1, "")]
    public void TentarCriar_SridVerificadoAntesDoWkt(ColunaMapa coluna, int srid, string? wkt)
    {
        Assert.False(GeometriaEntrada.TentarCriar(wkt, srid, coluna, out _, out var motivo));

        Assert.StartsWith("srid não permitido para a coluna", motivo, StringComparison.Ordinal);
    }

    /// <summary>
    /// A faixa vem do SRID recebido: as mesmas coordenadas de Web Mercator/UTM sao aceitas em metry
    /// com 3857/31983 e rejeitadas com 4326 (inclusive em graphy, onde o cast para geography NAO falha
    /// no banco: esta checagem e a unica protecao).
    /// </summary>
    [Theory]
    [InlineData(ColunaMapa.Metry, 3857, "POINT(-5191195.21 -2698729.46)", true)]
    [InlineData(ColunaMapa.Metry, 4326, "POINT(-5191195.21 -2698729.46)", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(-5191195.21 -2698729.46)", false)]
    [InlineData(ColunaMapa.Metry, 31983, "POINT(333250 7394600)", true)]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(333250 7394600)", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(-23.55 -46.63)", true)]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(-46.63 -123.55)", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(200 0)", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(-180.000000001 0)", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "POINT(180 90.000000001)", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "LINESTRING(0 0, 0 0, 181 0)", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "GEOMETRYCOLLECTION(POINT(0 0), MULTIPOINT((0 0), (0 95)))", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "MULTIPOLYGON(((0 0, 1 0, 1 1, 0 0)), ((0 0, 1 0, 1 -91, 0 0)))", false)]
    [InlineData(ColunaMapa.Graphy, 4326, "POLYGON((0 0, 1 0, 1 1, 0 0), (0 0, 1e3 0, 1 1, 0 0))", false)]
    public void TentarCriar_FaixaDoSridRecebido(ColunaMapa coluna, int srid, string wkt, bool valido)
    {
        var ok = GeometriaEntrada.TentarCriar(wkt, srid, coluna, out var geometria, out var motivo);

        Assert.True(valido == ok, motivo);
        Assert.Equal(valido, geometria is not null);
        if (!valido)
        {
            Assert.Contains($"fora da faixa", motivo, StringComparison.Ordinal);
            Assert.Contains($"do SRID {srid}", motivo, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData(ColunaMapa.Graphy, null, "WKT nulo ou vazio")]
    [InlineData(ColunaMapa.Metry, "   ", "WKT nulo ou vazio")]
    [InlineData(ColunaMapa.Graphy, "POINT Z (0 0 0)", "(U+005A)")]
    [InlineData(ColunaMapa.Metry, "SRID=4326;POINT(0 0)", "prefixo SRID= (EWKT)")]
    [InlineData(ColunaMapa.Graphy, "POINT EMPTY", "geometria EMPTY não é permitida")]
    [InlineData(ColunaMapa.Metry, "0101000000000000000000F03F000000000000F03F", "caractere não permitido")]
    public void TentarCriar_WktInvalido_MotivoDoValidador(ColunaMapa coluna, string? wkt, string trecho)
    {
        Assert.False(GeometriaEntrada.TentarCriar(wkt, 4326, coluna, out var geometria, out var motivo));

        Assert.Null(geometria);
        Assert.Contains(trecho, motivo, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(10_000, true)]
    [InlineData(10_001, false)]
    public void TentarCriar_LimiteDe10000Caracteres(int tamanho, bool valido)
    {
        var inicio = "LINESTRING(0 0";
        var corpo = new System.Text.StringBuilder(inicio);
        while (corpo.Length < tamanho - 5)
        {
            corpo.Append(", 1 1");
        }

        var wkt = corpo.ToString();
        wkt += new string(' ', tamanho - 1 - wkt.Length) + ")";
        Assert.Equal(tamanho, wkt.Length);

        var ok = GeometriaEntrada.TentarCriar(wkt, 4326, ColunaMapa.Graphy, out _, out var motivo);

        Assert.Equal(valido, ok);
        if (!valido)
        {
            Assert.Equal("WKT excede o limite de 10000 caracteres", motivo);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void TentarCriar_ColunaForaDoEnum_Lanca(int coluna)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            GeometriaEntrada.TentarCriar("POINT(0 0)", 4326, (ColunaMapa)coluna, out _, out _));
    }

    // ---------------- PoliticaSridColuna ----------------

    [Theory]
    [InlineData(ColunaMapa.Graphy, new[] { 4326 }, "4326")]
    [InlineData(ColunaMapa.Metry, new[] { 4326, 4674, 3857, 31983 }, "4326, 4674, 3857, 31983")]
    public void Politica_AceitosPorColuna(ColunaMapa coluna, int[] esperados, string texto)
    {
        Assert.Equal(esperados, PoliticaSridColuna.Aceitos(coluna));
        Assert.Equal(texto, PoliticaSridColuna.AceitosTexto(coluna));
        foreach (var srid in esperados)
        {
            Assert.True(PoliticaSridColuna.Aceita(coluna, srid));
        }

        Assert.False(PoliticaSridColuna.Aceita(coluna, 0));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(99)]
    public void Politica_ColunaForaDoEnum_Lanca(int coluna)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PoliticaSridColuna.Aceitos((ColunaMapa)coluna));
    }

    // ---------------- LimitesSrid / FaixaSrid ----------------

    [Theory]
    [InlineData(4326, -180d, 180d, -90d, 90d, "longitude", "latitude")]
    [InlineData(4674, -180d, 180d, -90d, 90d, "longitude", "latitude")]
    [InlineData(3857, -20037508.3428, 20037508.3428, -20037508.3428, 20037508.3428, "X", "Y")]
    [InlineData(31983, 0d, 1_000_000d, 0d, 10_000_000d, "E", "N")]
    public void LimitesSrid_TabelaConstante(int srid, double minX, double maxX, double minY, double maxY, string eixoX, string eixoY)
    {
        var faixa = LimitesSrid.Para(srid);

        Assert.NotNull(faixa);
        Assert.Equal((srid, minX, maxX, minY, maxY, eixoX, eixoY),
            (faixa!.Srid, faixa.MinX, faixa.MaxX, faixa.MinY, faixa.MaxY, faixa.NomeX, faixa.NomeY));
        Assert.False(string.IsNullOrWhiteSpace(faixa.Nome));
        Assert.True(faixa.XDentro(minX) && faixa.XDentro(maxX) && faixa.YDentro(minY) && faixa.YDentro(maxY));
        Assert.False(faixa.XDentro(Math.BitDecrement(minX)) || faixa.XDentro(Math.BitIncrement(maxX)));
        Assert.False(faixa.YDentro(Math.BitDecrement(minY)) || faixa.YDentro(Math.BitIncrement(maxY)));
        Assert.False(faixa.XDentro(double.NaN) || faixa.YDentro(double.NaN));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4269)]
    [InlineData(-1)]
    public void LimitesSrid_Desconhecido_Nulo(int srid)
    {
        Assert.Null(LimitesSrid.Para(srid));
    }

    [Fact]
    public void LimitesSrid_ConhecidosEDescricao()
    {
        Assert.Equal(new[] { 4326, 4674, 3857, 31983 }, LimitesSrid.Conhecidos);
        Assert.Equal("[-20037508.3428, 20037508.3428]", LimitesSrid.Para(3857)!.DescreverX());
        Assert.Equal("[0, 10000000]", LimitesSrid.Para(31983)!.DescreverY());
    }

    // ---------------- MotivosGeometria ----------------

    [Fact]
    public void Motivos_TextosFixosDoContrato()
    {
        Assert.Equal("geometria inválida segundo o PostGIS (ST_IsValid)", MotivosGeometria.InvalidaPostgis);
        Assert.Equal("tipo devolvido pelo banco diverge do WKT", MotivosGeometria.TipoDiverge);
    }

    // ---------------- ResultadoEscrita ----------------

    [Fact]
    public void ResultadoEscrita_Variantes()
    {
        ResultadoEscrita[] todos =
        [
            ResultadoEscrita.Atualizado.Instancia,
            new ResultadoEscrita.Criado(7),
            new ResultadoEscrita.GeometriaInvalida("m"),
            ResultadoEscrita.NaoEncontrado.Instancia,
            ResultadoEscrita.Conflito.Instancia,
        ];

        Assert.Equal(5, todos.Distinct().Count());
        Assert.Equal(new ResultadoEscrita.Criado(7), todos[1]);
        Assert.Same(ResultadoEscrita.NaoEncontrado.Instancia, todos[3]);
    }
}
