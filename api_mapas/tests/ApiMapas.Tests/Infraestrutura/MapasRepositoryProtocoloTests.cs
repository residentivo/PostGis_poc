using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ApiMapas.Tests.Infraestrutura;

/// <summary>
/// MapasRepository real (Npgsql) contra o mock do protocolo PostgreSQL em 127.0.0.1 (porta efemera).
/// Nenhum banco real e acessado. Ajustado no PO4 ao modelo de uma geometria por id (D-N7, RS-23).
/// </summary>
public class MapasRepositoryProtocoloTests
{
    private static async Task<(LeituraMapa Leitura, LoggerMemoria<MapasRepository> Log, ServidorPostgresFalso Servidor)> ObterAsync(
        RespostaFalsa resposta, ColunaMapa coluna, string id = "1")
    {
        var servidor = new ServidorPostgresFalso(_ => resposta);
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var log = new LoggerMemoria<MapasRepository>();
        var repo = new MapasRepository(ds, log);
        IdMapa.TentarCriar(id, out var idMapa);

        try
        {
            var leitura = await repo.ObterPorIdAsync(idMapa, coluna, CancellationToken.None);
            return (leitura, log, servidor);
        }
        finally
        {
            await servidor.DisposeAsync();
        }
    }

    [Theory]
    [InlineData(ColunaMapa.Graphy, "m.graphy::geometry", "7", 7)]
    [InlineData(ColunaMapa.Metry, "m.metry AS g", "2147483647", 2147483647)]
    public async Task Envia_SqlConstante_ComParametroInt4(ColunaMapa coluna, string trecho, string id, int esperado)
    {
        var (leitura, _, servidor) = await ObterAsync(new RespostaFalsa.Linhas(Array.Empty<GeometriaMapa>()), coluna, id);

        Assert.Same(LeituraMapa.NaoEncontrada.Instancia, leitura);
        var consulta = Assert.Single(servidor.Consultas);
        Assert.Contains(trecho, consulta.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE m.id = $1", consulta.Sql, StringComparison.Ordinal);
        Assert.Contains("LIMIT 2", consulta.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@id", consulta.Sql, StringComparison.Ordinal);
        Assert.Equal(new[] { 23 }, consulta.OidsParametros); // 23 = int4
        Assert.Equal(esperado, consulta.ValorId);
        Assert.Empty(servidor.ErrosInternos);
    }

    [Fact]
    public async Task UmaLinha_Encontrada_ComNulos()
    {
        var (leitura, log, _) = await ObterAsync(new RespostaFalsa.Linhas(new[] { Geometrias.Poligono() }), ColunaMapa.Graphy);

        var g = Assert.IsType<LeituraMapa.Encontrada>(leitura).Geometria;
        Assert.Equal(Geometrias.Poligono(), g);
        Assert.Null(g.X);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2003);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2004); // conexao devolvida
    }

    [Fact]
    public async Task DuasLinhas_Duplicada()
    {
        var (leitura, _, _) = await ObterAsync(new RespostaFalsa.Linhas(Geometrias.Pontos(2).ToList()), ColunaMapa.Metry);

        Assert.Same(LeituraMapa.Duplicada.Instancia, leitura);
    }

    [Fact]
    public async Task ColunaNula_ColunaVazia()
    {
        var (leitura, _, _) = await ObterAsync(new RespostaFalsa.ColunaVazia(), ColunaMapa.Metry);

        Assert.Same(LeituraMapa.ColunaVazia.Instancia, leitura);
    }

    [Fact]
    public async Task PontoSrid0_LongitudeLatitudeNulas()
    {
        var (leitura, _, _) = await ObterAsync(new RespostaFalsa.Linhas(new[] { Geometrias.Ponto(0, 333000.5, 7394000.25) }), ColunaMapa.Metry);

        var g = Assert.IsType<LeituraMapa.Encontrada>(leitura).Geometria;
        Assert.Equal(0, g.Srid);
        Assert.Equal(333000.5, g.X);
        Assert.Null(g.Longitude);
        Assert.Null(g.Latitude);
    }

    [Theory]
    [InlineData("42501", "permission denied for table mapas")]
    [InlineData("57014", "canceling statement due to statement timeout")]
    [InlineData("42P01", "relation \"public.mapas\" does not exist")]
    public async Task ErroNaExecucao_PostgresException(string sqlState, string mensagem)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ObterAsync(new RespostaFalsa.ErroNaExecucao(sqlState, mensagem), ColunaMapa.Graphy));

        Assert.Equal(sqlState, ex.SqlState);
    }

    [Fact]
    public async Task ErroNaExecucao_LogComEtapaESqlState()
    {
        var servidor = new ServidorPostgresFalso(_ => new RespostaFalsa.ErroNaExecucao("42501", "permission denied"));
        await using var _s = servidor;
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var log = new LoggerMemoria<MapasRepository>();
        IdMapa.TentarCriar("5", out var id);

        await Assert.ThrowsAsync<PostgresException>(() => new MapasRepository(ds, log).ObterPorIdAsync(id, ColunaMapa.Metry, CancellationToken.None));

        var falha = Assert.Single(log.Entradas, e => e.Evento.Id == 2005);
        Assert.Equal(LogLevel.Error, falha.Nivel);
        Assert.Contains("SqlState 42501", falha.Mensagem, StringComparison.Ordinal);
        Assert.Contains("PostgresException", falha.Mensagem, StringComparison.Ordinal);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2004); // conexao liberada mesmo com erro
        Assert.DoesNotContain("p_falsa", log.TextoCompleto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErroDuranteLeitura_Propaga_LogEtapaLerLinhas()
    {
        var servidor = new ServidorPostgresFalso(_ => new RespostaFalsa.ErroDuranteLeitura(Geometrias.Pontos(1).ToList(), "XX000", "falha interna"));
        await using var _s = servidor;
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var log = new LoggerMemoria<MapasRepository>();
        IdMapa.TentarCriar("5", out var id);

        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            new MapasRepository(ds, log).ObterPorIdAsync(id, ColunaMapa.Graphy, CancellationToken.None));

        Assert.Equal("XX000", ex.SqlState);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2005 && e.Mensagem.Contains("ler linhas", StringComparison.Ordinal));
    }

    [Fact]
    public async Task TipoDeColunaInesperado_InvalidCastLogadoEPropagado()
    {
        var servidor = new ServidorPostgresFalso(_ => new RespostaFalsa.TipoErrado());
        await using var _s = servidor;
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var log = new LoggerMemoria<MapasRepository>();
        IdMapa.TentarCriar("5", out var id);

        await Assert.ThrowsAsync<InvalidCastException>(() =>
            new MapasRepository(ds, log).ObterPorIdAsync(id, ColunaMapa.Graphy, CancellationToken.None));

        Assert.Contains(log.Entradas, e => e.Evento.Id == 2005 && e.Mensagem.Contains("InvalidCastException", StringComparison.Ordinal));
    }

    /// <summary>Servico real + repositorio real + mock do protocolo: 0 = 404, 1 = 200, 2 = 500 (RS-23).</summary>
    [Theory]
    [InlineData(0, typeof(ResultadoConsulta.NaoEncontrado))]
    [InlineData(1, typeof(ResultadoConsulta.Sucesso))]
    [InlineData(2, null)]
    public async Task ServicoComRepositorioReal_Fluxo(int linhas, Type? esperado)
    {
        var servidor = new ServidorPostgresFalso(_ => new RespostaFalsa.Linhas(Geometrias.Pontos(linhas).ToList()));
        await using var _s = servidor;
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var servico = new ConsultaGeometriasService(
            new MapasRepository(ds, new LoggerMemoria<MapasRepository>()), new OpcoesConsulta(), new LoggerMemoria<ConsultaGeometriasService>());

        if (esperado is null)
        {
            await Assert.ThrowsAsync<LeituraInconsistenteException>(() => servico.ConsultarAsync("9", ColunaMapa.Metry, CancellationToken.None));
            return;
        }

        var resultado = await servico.ConsultarAsync("9", ColunaMapa.Metry, CancellationToken.None);
        Assert.IsType(esperado, resultado);
    }
}
