using Npgsql;
using NpgsqlTypes;
using TesteGraphy.Aplicacao;
using TesteGraphy.Configuracao;
using TesteGraphy.Dados;
using TesteGraphy.Exemplos;

namespace TesteGraphy.Tests.Dados;

/// <summary>
/// [Fact] de integracao (R7): so executa com PG_RUN_INTEGRATION=1 E PG_USER E PG_PASSWORD definidos;
/// caso contrario o teste e pulado (Skip) sem tentar conectar.
/// </summary>
public sealed class IntegracaoFactAttribute : FactAttribute
{
    public const string VarOptIn = "PG_RUN_INTEGRATION";

    public IntegracaoFactAttribute()
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

[Trait("Category", "Integration")]
public sealed class GraphyRepositoryIntegrationTests : IAsyncLifetime
{
    private readonly List<int> _idsInseridos = [];
    private ConexaoConfig? _config;
    private GraphyRepository? _repositorio;

    public Task InitializeAsync()
    {
        if (IntegracaoFactAttribute.Habilitada)
        {
            _config = ConexaoConfig.Carregar(Environment.GetEnvironmentVariable);
            _repositorio = new GraphyRepository(_config);
        }

        return Task.CompletedTask;
    }

    /// <summary>Limpeza: apaga SOMENTE os ids do RETURNING, um a um, exigindo rowcount == 1.</summary>
    public async Task DisposeAsync()
    {
        var falhas = new List<string>();
        try
        {
            if (_config is not null && _idsInseridos.Count > 0)
            {
                await using var fonte = NpgsqlDataSource.Create(_config.CriarConnectionString());
                foreach (var id in _idsInseridos)
                {
                    await using var conexao = await fonte.OpenConnectionAsync();
                    await using var transacao = await conexao.BeginTransactionAsync();
                    await using var comando = new NpgsqlCommand(GraphyRepository.SqlExcluir, conexao, transacao);
                    comando.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = id });
                    var linhas = await comando.ExecuteNonQueryAsync();
                    if (linhas == 1)
                    {
                        await transacao.CommitAsync();
                    }
                    else
                    {
                        await transacao.RollbackAsync();
                        falhas.Add($"DELETE do id {id} afetou {linhas} linha(s); revertido (rollback).");
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

    [IntegracaoFact]
    public async Task ExemplosFixos_InsereLeDeVoltaEConfereTipoESrid()
    {
        var repo = _repositorio!;
        var esperados = new Dictionary<string, string>
        {
            [ExemplosFixos.PracaDaSe] = "POINT",
            [ExemplosFixos.Linha] = "LINESTRING",
            [ExemplosFixos.Poligono] = "POLYGON",
        };

        foreach (var (wkt, tipo) in esperados)
        {
            var validacao = await repo.ValidarNoBancoAsync(wkt);
            Assert.True(validacao.Valido);
            Assert.Equal(tipo, validacao.Tipo);

            var id = await repo.InserirAsync(wkt);
            _idsInseridos.Add(id);

            var registro = await repo.LerAsync(id);
            Assert.NotNull(registro);
            Assert.Equal(id, registro!.Id);
            Assert.Equal(InsercaoService.SridEsperado, registro.Srid);
            Assert.StartsWith(tipo, registro.Wkt, StringComparison.OrdinalIgnoreCase);
        }
    }

    [IntegracaoFact]
    public async Task GeometriaInvalida_StIsValidFalso()
    {
        // Gravata-borboleta: passa no validador local, mas e invalida para o PostGIS.
        var validacao = await _repositorio!.ValidarNoBancoAsync("POLYGON((0 0, 1 1, 1 0, 0 1, 0 0))");

        Assert.False(validacao.Valido);
    }

    [IntegracaoFact]
    public async Task IdInexistente_LerRetornaNulo()
    {
        Assert.Null(await _repositorio!.LerAsync(-1));
    }
}
