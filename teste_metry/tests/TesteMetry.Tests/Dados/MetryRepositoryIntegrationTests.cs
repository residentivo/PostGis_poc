using Npgsql;
using NpgsqlTypes;
using TesteMetry.Configuracao;
using TesteMetry.Dados;
using TesteMetry.Exemplos;

namespace TesteMetry.Tests.Dados;

/// <summary>
/// [Theory] de integracao (R7/M6.6): so executa com PG_RUN_INTEGRATION=1 E PG_USER E PG_PASSWORD
/// definidos; caso contrario cada caso e pulado (Skip) sem tentar conectar.
/// </summary>
public sealed class IntegracaoTheoryAttribute : TheoryAttribute
{
    public const string VarOptIn = "PG_RUN_INTEGRATION";

    public IntegracaoTheoryAttribute()
    {
        if (!Habilitada)
        {
            Skip = $"Integracao desabilitada: requer {VarOptIn}=1 e {ConexaoConfig.VarUsuario}/{ConexaoConfig.VarSenha} definidos.";
        }
    }

    public static bool Habilitada =>
        Environment.GetEnvironmentVariable(VarOptIn) == "1"
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ConexaoConfig.VarUsuario))
        && !string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ConexaoConfig.VarSenha));
}

/// <summary>
/// M6.6 - um INSERT por SRID permitido; leitura confere ST_SRID. Limpeza RQ-6: no DisposeAsync
/// (sempre executado, mesmo apos falha do teste), para CADA id do RETURNING, um DELETE em
/// NpgsqlTransaction propria com @id e @srid Integer; rowcount != 1 -> ROLLBACK e falha.
/// </summary>
[Trait("Category", "Integration")]
public sealed class MetryRepositoryIntegrationTests : IAsyncLifetime
{
    /// <summary>DELETE de limpeza (somente nos testes - o programa nao executa DELETE). Igual a insert_referencia.sql.</summary>
    internal const string SqlExcluirTeste =
        "DELETE FROM public.mapas\n" +
        "WHERE id = @id\n" +
        "  AND ST_SRID(metry) = @srid;";

    private readonly List<(int Id, int Srid)> _inseridos = [];
    private ConexaoConfig? _config;
    private MetryRepository? _repositorio;

    public Task InitializeAsync()
    {
        if (IntegracaoTheoryAttribute.Habilitada)
        {
            _config = ConexaoConfig.Carregar(Environment.GetEnvironmentVariable);
            _repositorio = new MetryRepository(_config);
        }

        return Task.CompletedTask;
    }

    public async Task DisposeAsync()
    {
        var falhas = new List<string>();
        try
        {
            if (_config is not null && _inseridos.Count > 0)
            {
                await using var fonte = NpgsqlDataSource.Create(_config.CriarConnectionString());
                foreach (var (id, srid) in _inseridos)
                {
                    // Cada id em try proprio: falha em um nao impede a limpeza dos demais.
                    try
                    {
                        await using var conexao = await fonte.OpenConnectionAsync();
                        await using var transacao = await conexao.BeginTransactionAsync();
                        await using var comando = new NpgsqlCommand(SqlExcluirTeste, conexao, transacao);
                        comando.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id });
                        comando.Parameters.Add(new NpgsqlParameter("srid", NpgsqlDbType.Integer) { Value = srid });
                        var linhas = await comando.ExecuteNonQueryAsync();
                        if (linhas == 1)
                        {
                            await transacao.CommitAsync();
                        }
                        else
                        {
                            await transacao.RollbackAsync();
                            falhas.Add($"DELETE do id {id} (SRID {srid}) afetou {linhas} linha(s); revertido (rollback).");
                        }
                    }
                    catch (Exception ex) when (ex is NpgsqlException or TimeoutException)
                    {
                        falhas.Add($"DELETE do id {id} (SRID {srid}) falhou ({ex.GetType().Name}).");
                    }
                }
            }
        }
        finally
        {
            if (_repositorio is not null)
            {
                await _repositorio.DisposeAsync();
            }
        }

        Assert.True(falhas.Count == 0, string.Join(" ", falhas));
    }

    [IntegracaoTheory]
    [InlineData(4326)]
    [InlineData(4674)]
    [InlineData(3857)]
    [InlineData(31983)]
    public async Task ExemplosFixos_InsereLeDeVoltaEConfereTipoESrid(int srid)
    {
        var repo = _repositorio!;
        var tipos = new[] { "POINT", "LINESTRING", "POLYGON" };
        var exemplos = ExemplosFixos.Para(srid);

        for (var i = 0; i < exemplos.Count; i++)
        {
            var validacao = await repo.ValidarNoBancoAsync(exemplos[i], srid);
            Assert.True(validacao.Valido);
            Assert.Equal(tipos[i], validacao.Tipo);

            var id = await repo.InserirAsync(exemplos[i], srid);
            _inseridos.Add((id, srid));

            var registro = await repo.LerAsync(id);
            Assert.NotNull(registro);
            Assert.Equal(id, registro!.Id);
            Assert.Equal(srid, registro.Srid);
            Assert.StartsWith(tipos[i], registro.Wkt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [IntegracaoTheory]
    [InlineData(4326)]
    [InlineData(3857)]
    public async Task GeometriaInvalida_StIsValidFalso(int srid)
    {
        var validacao = await _repositorio!.ValidarNoBancoAsync("POLYGON((0 0, 1 1, 1 0, 0 1, 0 0))", srid);

        Assert.False(validacao.Valido);
    }

    [IntegracaoTheory]
    [InlineData(-1)]
    public async Task IdInexistente_LerRetornaNulo(int id)
    {
        Assert.Null(await _repositorio!.LerAsync(id));
    }
}

/// <summary>Garante que, sem PG_RUN_INTEGRATION=1, a integracao e pulada (nao conecta).</summary>
public class IntegracaoOptInTests
{
    [Fact]
    public void SemOptIn_IntegracaoPulada()
    {
        if (Environment.GetEnvironmentVariable(IntegracaoTheoryAttribute.VarOptIn) == "1")
        {
            return;
        }

        Assert.False(IntegracaoTheoryAttribute.Habilitada);
        Assert.Contains("PG_RUN_INTEGRATION=1", new IntegracaoTheoryAttribute().Skip, StringComparison.Ordinal);
    }

    [Fact]
    public void SqlExcluirTeste_FiltraIdESridParametrizados()
    {
        Assert.Contains("WHERE id = @id", MetryRepositoryIntegrationTests.SqlExcluirTeste, StringComparison.Ordinal);
        Assert.Contains("AND ST_SRID(metry) = @srid", MetryRepositoryIntegrationTests.SqlExcluirTeste, StringComparison.Ordinal);
        Assert.DoesNotContain(MetryRepositoryIntegrationTests.SqlExcluirTeste, char.IsAsciiDigit);
    }
}
