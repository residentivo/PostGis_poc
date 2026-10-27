using ApiMapas.Dominio;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Npgsql;
using NpgsqlTypes;

namespace ApiMapas.Tests.Infraestrutura;

public class MapasRepositoryTests
{
    [Theory]
    [InlineData(ColunaMapa.Graphy, "m.graphy::geometry AS g", "(t.g IS NOT NULL)", 1)]
    [InlineData(ColunaMapa.Metry, "m.metry AS g", "(t.g IS NOT NULL)", 2147483647)]
    public void CriarComando_SqlConstanteEParametroInteger(ColunaMapa coluna, string origem, string filtro, int valor)
    {
        Assert.True(IdMapa.TentarCriar(valor.ToString(System.Globalization.CultureInfo.InvariantCulture), out var id));

        using var comando = MapasRepository.CriarComando(coluna, id);

        Assert.Same(MapasRepository.SqlPara(coluna), comando.CommandText);
        Assert.Contains(origem, comando.CommandText, StringComparison.Ordinal);
        Assert.Contains(filtro, comando.CommandText, StringComparison.Ordinal);
        Assert.Contains("WHERE m.id = @id", comando.CommandText, StringComparison.Ordinal);
        Assert.Contains("LIMIT 2", comando.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("ctid", comando.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("2147483647", comando.CommandText, StringComparison.Ordinal);
        Assert.DoesNotContain("= 1", comando.CommandText, StringComparison.Ordinal);

        var p = Assert.Single(comando.Parameters);
        Assert.Equal("id", p.ParameterName);
        Assert.Equal(NpgsqlDbType.Integer, p.NpgsqlDbType);
        Assert.Equal(valor, p.Value);
        Assert.Null(comando.Connection);
    }

    [Theory]
    [InlineData(ColunaMapa.Graphy)]
    [InlineData(ColunaMapa.Metry)]
    public void Sql_NaoExpoeCtidNoSelectExterno_EColunasNaOrdem(ColunaMapa coluna)
    {
        var sql = MapasRepository.SqlPara(coluna);
        var selectExterno = sql[..sql.IndexOf("FROM (", StringComparison.Ordinal)];

        Assert.DoesNotContain("ctid", selectExterno, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("linha", selectExterno, StringComparison.OrdinalIgnoreCase);
        var aliases = new[] { "AS tipo", "AS srid", "AS wkt", "AS geojson", "AS x", "AS y", "AS longitude", "AS latitude", "AS preenchida" };
        var posicoes = aliases.Select(a => selectExterno.IndexOf(a, StringComparison.Ordinal)).ToArray();
        Assert.All(posicoes, p => Assert.True(p > 0));
        Assert.Equal(posicoes.OrderBy(p => p), posicoes);
        Assert.DoesNotContain("INSERT", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("UPDATE", sql, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("DELETE", sql, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(ColunaMapa.Graphy, "-- Q1 (graphy)")]
    [InlineData(ColunaMapa.Metry, "-- Q2 (metry)")]
    public void Sql_IdenticoAoArquivoDeReferenciaDoDataBrain(ColunaMapa coluna, string marcador)
    {
        var arquivo = File.ReadAllText(Path.Combine(RaizSolucao(), "sql", "consultas_referencia.sql")).Replace("\r\n", "\n", StringComparison.Ordinal);
        var inicioBloco = arquivo.IndexOf(marcador, StringComparison.Ordinal);
        Assert.True(inicioBloco >= 0);
        var inicio = arquivo.IndexOf("SELECT GeometryType", inicioBloco, StringComparison.Ordinal);
        var fim = arquivo.IndexOf("LIMIT 2) AS t;", inicio, StringComparison.Ordinal) + "LIMIT 2) AS t;".Length;

        Assert.Equal(arquivo[inicio..fim], MapasRepository.SqlPara(coluna));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(3)]
    public void SqlPara_ColunaInvalida_Lanca(int valor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => MapasRepository.SqlPara((ColunaMapa)valor));
        IdMapa.TentarCriar("1", out var id);
        Assert.Throws<ArgumentOutOfRangeException>(() => MapasRepository.CriarComando((ColunaMapa)valor, id));
    }

    [Fact]
    public void Construtor_ArgumentosNulos_Lanca()
    {
        using var ds = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=u;Password=p");
        Assert.Throws<ArgumentNullException>(() => new MapasRepository(null!, NullLogger<MapasRepository>.Instance));
        Assert.Throws<ArgumentNullException>(() => new MapasRepository(ds, null!));
    }

    /// <summary>
    /// Banco inacessivel (127.0.0.1:1, nada escuta; nunca o servidor real): a excecao Npgsql
    /// propaga, o log registra tipo/etapa sem senha e a conexao e liberada.
    /// </summary>
    [Theory]
    [InlineData(ColunaMapa.Graphy)]
    [InlineData(ColunaMapa.Metry)]
    public async Task ListarPorIdAsync_BancoInacessivel_PropagaSemVazarSenha(ColunaMapa coluna)
    {
        var config = ConexaoConfig.Carregar(n => n switch
        {
            "PG_HOST" => "127.0.0.1",
            "PG_PORT" => "1",
            "PG_USER" => "u_teste",
            "PG_PASSWORD" => "SenhaQueNaoPodeVazar",
            "PG_SSLMODE" => "Disable",
            _ => null,
        });
        await using var ds = config.CriarDataSource();
        var log = new LoggerMemoria<MapasRepository>();
        var repo = new MapasRepository(ds, log);
        IdMapa.TentarCriar("1", out var id);

        var ex = await Assert.ThrowsAnyAsync<Exception>(async () =>
        {
            await repo.ObterPorIdAsync(id, coluna, CancellationToken.None);
        });

        Assert.True(ex is NpgsqlException or TimeoutException or System.Net.Sockets.SocketException, ex.GetType().FullName);
        var falha = Assert.Single(log.Entradas, e => e.Evento.Id == 2005);
        Assert.Equal(LogLevel.Error, falha.Nivel);
        Assert.Contains("abrir conexao", falha.Mensagem, StringComparison.Ordinal);
        Assert.DoesNotContain("SenhaQueNaoPodeVazar", log.TextoCompleto, StringComparison.Ordinal);
        Assert.DoesNotContain(log.Entradas, e => e.Evento.Id == 2004); // conexao nunca aberta
    }

    [Fact]
    public async Task ListarPorIdAsync_TokenCancelado_NaoAbreConexao()
    {
        await using var ds = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=u;Password=p;Timeout=2");
        var repo = new MapasRepository(ds, new LoggerMemoria<MapasRepository>());
        IdMapa.TentarCriar("1", out var id);
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await repo.ObterPorIdAsync(id, ColunaMapa.Graphy, cts.Token);
        });
    }

    internal static string RaizSolucao()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ApiMapas.sln")))
        {
            dir = dir.Parent;
        }

        return dir?.FullName ?? throw new InvalidOperationException("ApiMapas.sln nao encontrado");
    }
}
