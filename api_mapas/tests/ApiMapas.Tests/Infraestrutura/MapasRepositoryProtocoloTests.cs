using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace ApiMapas.Tests.Infraestrutura;

/// <summary>
/// MapasRepository real (Npgsql) contra o mock do protocolo PostgreSQL em 127.0.0.1 (porta efemera).
/// Nenhum banco real e acessado.
/// </summary>
public class MapasRepositoryProtocoloTests
{
    private static async Task<(List<GeometriaMapa> Itens, LoggerMemoria<MapasRepository> Log, ServidorPostgresFalso Servidor)> ListarAsync(
        RespostaFalsa resposta, ColunaMapa coluna, string id = "1")
    {
        var servidor = new ServidorPostgresFalso(_ => resposta);
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var log = new LoggerMemoria<MapasRepository>();
        var repo = new MapasRepository(ds, log);
        IdMapa.TentarCriar(id, out var idMapa);

        var itens = new List<GeometriaMapa>();
        try
        {
            await foreach (var g in repo.ListarPorIdAsync(idMapa, coluna, CancellationToken.None))
            {
                itens.Add(g);
            }
        }
        finally
        {
            await servidor.DisposeAsync();
        }

        return (itens, log, servidor);
    }

    [Theory]
    [InlineData(ColunaMapa.Graphy, "m.graphy::geometry", "7", 7)]
    [InlineData(ColunaMapa.Metry, "m.metry AS g", "2147483647", 2147483647)]
    public async Task Envia_SqlConstante_ComParametroInt4(ColunaMapa coluna, string trecho, string id, int esperado)
    {
        var (itens, _, servidor) = await ListarAsync(new RespostaFalsa.Linhas(Array.Empty<GeometriaMapa>()), coluna, id);

        Assert.Empty(itens);
        var consulta = Assert.Single(servidor.Consultas);
        Assert.Contains(trecho, consulta.Sql, StringComparison.Ordinal);
        Assert.Contains("WHERE m.id = $1", consulta.Sql, StringComparison.Ordinal);
        Assert.DoesNotContain("@id", consulta.Sql, StringComparison.Ordinal);
        Assert.Equal(new[] { 23 }, consulta.OidsParametros); // 23 = int4
        Assert.Equal(esperado, consulta.ValorId);
        Assert.Empty(servidor.ErrosInternos);
    }

    public static TheoryData<int, int> Quantidades => new() { { 1, 1 }, { 3, 3 }, { 101, 101 } };

    [Theory]
    [MemberData(nameof(Quantidades))]
    public async Task LeLinhas_NaOrdem_ComNulos(int quantidade, int esperado)
    {
        var linhas = Geometrias.Pontos(quantidade - 1).Prepend(Geometrias.Poligono()).ToList();

        var (itens, log, _) = await ListarAsync(new RespostaFalsa.Linhas(linhas), ColunaMapa.Graphy);

        Assert.Equal(esperado, itens.Count);
        Assert.Equal(linhas, itens);
        Assert.Null(itens[0].X);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2003 && e.Mensagem.Contains($"{esperado} linha", StringComparison.Ordinal));
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2004); // conexao devolvida
    }

    [Fact]
    public async Task PontoSrid0_LongitudeLatitudeNulas()
    {
        var (itens, _, _) = await ListarAsync(new RespostaFalsa.Linhas(new[] { Geometrias.Ponto(0, 333000.5, 7394000.25) }), ColunaMapa.Metry);

        var g = Assert.Single(itens);
        Assert.Equal(0, g.Srid);
        Assert.Equal(333000.5, g.X);
        Assert.Null(g.Longitude);
        Assert.Null(g.Latitude);
    }

    [Theory]
    [InlineData("42501", "permission denied for table mapas")]
    [InlineData("57014", "canceling statement due to statement timeout")]
    [InlineData("42P01", "relation \"public.mapas\" does not exist")]
    public async Task ErroNaExecucao_PostgresException_LogaSqlStateSemDetalhe(string sqlState, string mensagem)
    {
        var ex = await Assert.ThrowsAsync<PostgresException>(() =>
            ListarAsync(new RespostaFalsa.ErroNaExecucao(sqlState, mensagem), ColunaMapa.Graphy));

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

        await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await foreach (var _ in new MapasRepository(ds, log).ListarPorIdAsync(id, ColunaMapa.Metry, CancellationToken.None))
            {
            }
        });

        var falha = Assert.Single(log.Entradas, e => e.Evento.Id == 2005);
        Assert.Equal(LogLevel.Error, falha.Nivel);
        Assert.Contains("abrir conexao/executar consulta", falha.Mensagem, StringComparison.Ordinal);
        Assert.Contains("SqlState 42501", falha.Mensagem, StringComparison.Ordinal);
        Assert.Contains("PostgresException", falha.Mensagem, StringComparison.Ordinal);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2004); // conexao liberada mesmo com erro
        Assert.DoesNotContain("p_falsa", log.TextoCompleto, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ErroDuranteLeitura_PropagaDepoisDasLinhas_LogEtapaLerLinhas()
    {
        var servidor = new ServidorPostgresFalso(_ => new RespostaFalsa.ErroDuranteLeitura(Geometrias.Pontos(2).ToList(), "XX000", "falha interna"));
        await using var _s = servidor;
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var log = new LoggerMemoria<MapasRepository>();
        IdMapa.TentarCriar("5", out var id);
        var lidos = 0;

        var ex = await Assert.ThrowsAsync<PostgresException>(async () =>
        {
            await foreach (var _ in new MapasRepository(ds, log).ListarPorIdAsync(id, ColunaMapa.Graphy, CancellationToken.None))
            {
                lidos++;
            }
        });

        Assert.Equal("XX000", ex.SqlState);
        Assert.Equal(2, lidos);
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

        await Assert.ThrowsAsync<InvalidCastException>(async () =>
        {
            await foreach (var _ in new MapasRepository(ds, log).ListarPorIdAsync(id, ColunaMapa.Graphy, CancellationToken.None))
            {
            }
        });

        Assert.Contains(log.Entradas, e => e.Evento.Id == 2005 && e.Mensagem.Contains("InvalidCastException", StringComparison.Ordinal));
    }

    /// <summary>Servico real + repositorio real + mock do protocolo: 101 linhas -> 100 itens e truncado.</summary>
    [Theory]
    [InlineData(0, typeof(ResultadoConsulta.NaoEncontrado))]
    [InlineData(1, typeof(ResultadoConsulta.Sucesso))]
    [InlineData(101, typeof(ResultadoConsulta.Sucesso))]
    public async Task ServicoComRepositorioReal_Fluxo(int linhas, Type esperado)
    {
        var servidor = new ServidorPostgresFalso(_ => new RespostaFalsa.Linhas(Geometrias.Pontos(linhas).ToList()));
        await using var _s = servidor;
        await using var ds = NpgsqlDataSource.Create(servidor.ConnectionString);
        var servico = new ConsultaGeometriasService(
            new MapasRepository(ds, new LoggerMemoria<MapasRepository>()), new OpcoesConsulta(), new LoggerMemoria<ConsultaGeometriasService>());

        var resultado = await servico.ConsultarAsync("9", ColunaMapa.Metry, CancellationToken.None);

        Assert.IsType(esperado, resultado);
        if (resultado is ResultadoConsulta.Sucesso s)
        {
            Assert.Equal(Math.Min(linhas, 100), s.Resposta.Quantidade);
            Assert.Equal(linhas > 100, s.Resposta.Truncado);
        }
    }
}
