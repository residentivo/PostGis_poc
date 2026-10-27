using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;
using ApiMapas.Tests.Suporte;
using Npgsql;

namespace ApiMapas.Tests.Infraestrutura;

/// <summary>P6.4 - ConexaoEscritaConfig (D-P9, RP-6, RP-7, RP-8; CP-11), sem servidor.</summary>
public class ConexaoEscritaConfigTests
{
    private const string UsuarioLeitura = "api_mapas_leitura";
    private const string SenhaLeitura = "SenhaLeitura#123456";
    private const string UsuarioEscrita = "api_mapas_escrita";
    private const string SenhaEscrita = "SenhaEscrita#654321";

    private static Func<string, string?> Ambiente(params (string Nome, string? Valor)[] extras)
    {
        var mapa = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            ["PG_HOST"] = "10.1.2.3",
            ["PG_PORT"] = "6543",
            ["PG_DATABASE"] = "casaos",
            ["PG_USER"] = UsuarioLeitura,
            ["PG_PASSWORD"] = SenhaLeitura,
            ["PG_ESCRITA_USER"] = UsuarioEscrita,
            ["PG_ESCRITA_PASSWORD"] = SenhaEscrita,
        };
        foreach (var (nome, valor) in extras)
        {
            mapa[nome] = valor;
        }

        return n => mapa.TryGetValue(n, out var v) ? v : null;
    }

    private static ConexaoEscritaConfig? Carregar(Func<string, string?> ambiente, out MotivoEscritaDesligada motivo) =>
        ConexaoEscritaConfig.Carregar(ambiente, ConexaoConfig.Carregar(ambiente), out motivo);

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("   ", null)]
    [InlineData(null, " \t ")]
    public void SemAsDuasVariaveis_EscritaDesligada(string? usuario, string? senha)
    {
        var config = Carregar(Ambiente(("PG_ESCRITA_USER", usuario), ("PG_ESCRITA_PASSWORD", senha)), out var motivo);

        Assert.Null(config);
        Assert.Equal(MotivoEscritaDesligada.VariaveisAusentes, motivo);
    }

    [Theory]
    [InlineData(null, SenhaEscrita, "PG_ESCRITA_USER")]
    [InlineData("  ", SenhaEscrita, "PG_ESCRITA_USER")]
    [InlineData(UsuarioEscrita, null, "PG_ESCRITA_PASSWORD")]
    [InlineData(UsuarioEscrita, "", "PG_ESCRITA_PASSWORD")]
    public void SoUmaVariavel_SaiComCodigo1_CitandoSoONome(string? usuario, string? senha, string variavel)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() =>
            Carregar(Ambiente(("PG_ESCRITA_USER", usuario), ("PG_ESCRITA_PASSWORD", senha)), out _));

        Assert.Equal(variavel, ex.Variavel);
        Assert.DoesNotContain(SenhaEscrita, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(UsuarioEscrita, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(UsuarioLeitura)]
    [InlineData("API_MAPAS_LEITURA")]
    [InlineData("  Api_Mapas_Leitura  ")]
    [InlineData("\tapi_mapas_leitura\n")]
    public void UsuarioIgualAoDeLeitura_SaiComCodigo1(string usuario)
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Carregar(Ambiente(("PG_ESCRITA_USER", usuario)), out _));

        Assert.Equal("PG_ESCRITA_USER", ex.Variavel);
        Assert.DoesNotContain(SenhaEscrita, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(SenhaLeitura, ex.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(UsuarioLeitura, ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void UsuarioIgualAoDeLeitura_MesmoComSslInseguro_SaiComCodigo1()
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() =>
            Carregar(Ambiente(("PG_ESCRITA_USER", UsuarioLeitura), ("PG_SSLMODE", "Disable")), out _));
        Assert.Equal("PG_ESCRITA_USER", ex.Variavel);
    }

    [Fact]
    public void SenhaIgualADeLeitura_SaiComCodigo1()
    {
        var ex = Assert.Throws<ConfiguracaoInvalidaException>(() => Carregar(Ambiente(("PG_ESCRITA_PASSWORD", SenhaLeitura)), out _));

        Assert.Equal("PG_ESCRITA_PASSWORD", ex.Variavel);
        Assert.DoesNotContain(SenhaLeitura, ex.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Disable")]
    [InlineData("prefer")]
    [InlineData(" PREFER ")]
    public void SslInseguro_EscritaDesligada(string sslMode)
    {
        var config = Carregar(Ambiente(("PG_SSLMODE", sslMode)), out var motivo);

        Assert.Null(config);
        Assert.Equal(MotivoEscritaDesligada.SslInseguro, motivo);
    }

    [Theory]
    [InlineData(null, SslMode.Require)]
    [InlineData("Require", SslMode.Require)]
    [InlineData("VerifyCA", SslMode.VerifyCA)]
    [InlineData("verifyfull", SslMode.VerifyFull)]
    public void Valida_ConnectionStringConformeRp8(string? sslMode, SslMode esperado)
    {
        var ambiente = Ambiente(("PG_SSLMODE", sslMode), ("PG_ESCRITA_USER", "  " + UsuarioEscrita + " "));
        var config = Carregar(ambiente, out var motivo);

        Assert.NotNull(config);
        Assert.Equal(MotivoEscritaDesligada.Nenhum, motivo);
        var cs = new NpgsqlConnectionStringBuilder(config!.CriarConnectionString());
        Assert.Equal("10.1.2.3", cs.Host);
        Assert.Equal(6543, cs.Port);
        Assert.Equal("casaos", cs.Database);
        Assert.Equal(UsuarioEscrita, cs.Username);
        Assert.Equal(SenhaEscrita, cs.Password);
        Assert.Equal(esperado, cs.SslMode);
        Assert.Equal(5, cs.MaxPoolSize);
        Assert.Equal("ApiMapas.Escrita", cs.ApplicationName);
        Assert.False(cs.PersistSecurityInfo);
        Assert.False(cs.IncludeErrorDetail);
#pragma warning disable CS0618 // TrustServerCertificate: conferir que NAO foi ligado
        Assert.False(cs.TrustServerCertificate);
#pragma warning restore CS0618
        Assert.Equal(15, cs.CommandTimeout);
        Assert.Equal(10, cs.Timeout);
        Assert.Equal(5, ConexaoEscritaConfig.TamanhoMaximoPool);
    }

    [Fact]
    public void DescricaoEToString_SemCredenciais()
    {
        var config = Carregar(Ambiente(), out _)!;

        foreach (var texto in new[] { config.Descricao(), config.ToString() })
        {
            Assert.EndsWith(" (escrita)", texto, StringComparison.Ordinal);
            Assert.Contains("10.1.2.3:6543/casaos", texto, StringComparison.Ordinal);
            Assert.DoesNotContain(UsuarioEscrita, texto, StringComparison.Ordinal);
            Assert.DoesNotContain(SenhaEscrita, texto, StringComparison.Ordinal);
            Assert.DoesNotContain(UsuarioLeitura, texto, StringComparison.Ordinal);
            Assert.DoesNotContain("Password", texto, StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public async Task CriarFonteDados_PoolProprio_ConnectionStringSemSenha()
    {
        var ambiente = Ambiente();
        var leitura = ConexaoConfig.Carregar(ambiente);
        var config = ConexaoEscritaConfig.Carregar(ambiente, leitura, out _)!;

        await using var fonte = config.CriarFonteDados();
        await using var dsLeitura = leitura.CriarDataSource();

        Assert.NotSame(dsLeitura, fonte.DataSource);
        Assert.DoesNotContain(SenhaEscrita, fonte.DataSource.ConnectionString, StringComparison.Ordinal);
        var cs = new NpgsqlConnectionStringBuilder(fonte.DataSource.ConnectionString);
        Assert.Equal(UsuarioEscrita, cs.Username);
        Assert.Equal("ApiMapas.Escrita", cs.ApplicationName);
        Assert.Equal(UsuarioLeitura, new NpgsqlConnectionStringBuilder(dsLeitura.ConnectionString).Username);
    }

    [Fact]
    public void FonteDadosEscrita_DisposeSincrono()
    {
        var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=u;Password=p"));
        fonte.Dispose();
        Assert.Throws<ObjectDisposedException>(() => fonte.DataSource.CreateConnection().Open());
    }

    [Fact]
    public void Carregar_ArgumentosNulos_Lanca()
    {
        var leitura = ConexaoConfig.Carregar(Ambiente());
        Assert.Throws<ArgumentNullException>(() => ConexaoEscritaConfig.Carregar(null!, leitura, out _));
        Assert.Throws<ArgumentNullException>(() => ConexaoEscritaConfig.Carregar(Ambiente(), null!, out _));
    }

    [Theory]
    [InlineData(MotivoEscritaDesligada.Nenhum, "escrita habilitada")]
    [InlineData(MotivoEscritaDesligada.VariaveisAusentes, "PG_ESCRITA_USER e PG_ESCRITA_PASSWORD nao definidas")]
    [InlineData(MotivoEscritaDesligada.SslInseguro, "PG_SSLMODE efetivo e Disable ou Prefer")]
    [InlineData(MotivoEscritaDesligada.RolePrivilegiada, "superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE, e membro de pg_write_all_data ou e dona de public.mapas")]
    [InlineData(MotivoEscritaDesligada.VerificacaoFalhou, "nao foi possivel conferir os atributos da role de escrita")]
    [InlineData((MotivoEscritaDesligada)99, "motivo desconhecido")]
    public void Motivo_Descrever_TextoFixo(MotivoEscritaDesligada motivo, string trecho)
    {
        Assert.Contains(trecho, motivo.Descrever(), StringComparison.Ordinal);
    }
}

/// <summary>
/// P6.4 / P6.8 (C2, C4) - VerificadorRoleEscrita (R-1) contra o ServidorPostgresEscritaFalso:
/// cada criterio desliga a escrita; qualquer excecao vira VerificacaoFalhou, sem derrubar nada.
/// </summary>
public class VerificadorRoleEscritaTests
{
    private static readonly ColunaPg[] Colunas =
    {
        new("rolsuper", ColunaPg.Bool), new("rolbypassrls", ColunaPg.Bool), new("rolcreaterole", ColunaPg.Bool),
        new("pode_truncate", ColunaPg.Bool), new("escreve_tudo", ColunaPg.Bool), new("dona_tabela", ColunaPg.Bool),
        new("pode_delete", ColunaPg.Bool), new("insere_id", ColunaPg.Bool), new("pk_exata", ColunaPg.Bool), new("id_always", ColunaPg.Bool),
        new("atualiza_id", ColunaPg.Bool), new("rolcreatedb", ColunaPg.Bool), new("rolreplication", ColunaPg.Bool), new("membro_de_role", ColunaPg.Bool),
    };

    /// <summary>Linha da V1 (14 colunas, PO5); valores nao informados (10 a 13) vem false.</summary>
    private static RespostaPg.Linhas Atributos(params bool?[] valores) =>
        new RespostaPg.Linhas(Colunas, new[] { valores.Cast<object?>().Concat(Enumerable.Repeat((object?)false, Colunas.Length - valores.Length)).ToArray() });

    private static async Task<(MotivoEscritaDesligada Motivo, LoggerMemoria<VerificadorRoleEscrita> Log, ServidorPostgresEscritaFalso Servidor)> VerificarAsync(
        Func<ComandoPg, RespostaPg?> responder, CancellationToken token = default)
    {
        var servidor = new ServidorPostgresEscritaFalso(responder);
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(servidor.ConnectionString()));
        var log = new LoggerMemoria<VerificadorRoleEscrita>();
        var motivo = await new VerificadorRoleEscrita(fonte, log).VerificarAsync(token);
        await servidor.DisposeAsync();
        return (motivo, log, servidor);
    }

    [Fact]
    public async Task RoleSemPrivilegios_EscritaHabilitada_SqlConstanteSemParametros()
    {
        var (motivo, log, servidor) = await VerificarAsync(_ => Atributos(false, false, false, false, false, false, false, false, true, true));

        Assert.Equal(MotivoEscritaDesligada.Nenhum, motivo);
        var comando = Assert.Single(servidor.Comandos);
        Assert.Equal(VerificadorRoleEscrita.SqlAtributosRole.TrimEnd(';'), comando.Sql);
        Assert.Empty(comando.Parametros);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2202);
    }

    public static TheoryData<int, string> Criterios => new()
    {
        { 0, "rolsuper" },
        { 1, "rolbypassrls" },
        { 2, "rolcreaterole" },
        { 3, "TRUNCATE em public.mapas (C4)" },
        { 4, "membro de pg_write_all_data (C4)" },
        { 5, "dona de public.mapas (C4)" },
        { 6, "DELETE em public.mapas (RS-20)" },
        { 7, "INSERT na coluna id (RS-20)" },
    };

    [Theory]
    [MemberData(nameof(Criterios))]
    public async Task CadaCriterio_DesligaAEscrita(int indice, string criterio)
    {
        var valores = new bool?[10];
        for (var i = 0; i < 8; i++)
        {
            valores[i] = i == indice;
        }

        valores[8] = true;
        valores[9] = true;

        var (motivo, _, _) = await VerificarAsync(_ => Atributos(valores));

        Assert.True(MotivoEscritaDesligada.RolePrivilegiada == motivo, criterio);
    }

    [Fact]
    public async Task TodosOsCriterios_DesligaAEscrita()
    {
        var (motivo, _, _) = await VerificarAsync(_ => Atributos(true, true, true, true, true, true, true, true, true, true));
        Assert.Equal(MotivoEscritaDesligada.RolePrivilegiada, motivo);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public async Task SemPkExataOuIdNaoAlways_DesligaAEscrita_Warning(bool pkExata, bool idAlways)
    {
        var (motivo, log, _) = await VerificarAsync(_ => Atributos(false, false, false, false, false, false, false, false, pkExata, idAlways));

        Assert.Equal(MotivoEscritaDesligada.SemChavePrimaria, motivo);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2206 && e.Nivel == Microsoft.Extensions.Logging.LogLevel.Warning);
    }

    [Fact]
    public void Sql_ConfereOsSeisCriteriosDaSessao()
    {
        var sql = VerificadorRoleEscrita.SqlAtributosRole;
        foreach (var trecho in new[]
        {
            "r.rolsuper", "r.rolbypassrls", "r.rolcreaterole",
            "has_table_privilege(current_user, 'public.mapas', 'TRUNCATE')",
            "pg_has_role(current_user, 'pg_write_all_data', 'USAGE')",
            "c.relowner = r.oid", "WHERE r.rolname = current_user",
        })
        {
            Assert.Contains(trecho, sql, StringComparison.Ordinal);
        }

        Assert.DoesNotContain("@", sql, StringComparison.Ordinal);
    }

    public static TheoryData<string, RespostaPg> Falhas => new()
    {
        { "sem linha", new RespostaPg.Linhas(Colunas, Array.Empty<object?[]>()) },
        { "valor nulo (InvalidCastException)", Atributos(false, null, false, false, false, false, false, false, true, true) },
        { "tabela inexistente 42P01", new RespostaPg.Erro("42P01", "relation \"public.mapas\" does not exist SEGREDO") },
        { "sem permissao 42501", new RespostaPg.Erro("42501") },
        { "conexao cai", new RespostaPg.Derrubar() },
        { "coluna de outro tipo", new RespostaPg.Linhas(new[] { new ColunaPg("rolsuper", ColunaPg.Text) }, new[] { new object?[] { "x" } }) },
    };

    [Theory]
    [MemberData(nameof(Falhas))]
    public async Task QualquerFalha_VerificacaoFalhou_SemLancar_LogSoTipoESqlState(string caso, RespostaPg resposta)
    {
        var (motivo, log, servidor) = await VerificarAsync(_ => resposta);

        Assert.True(MotivoEscritaDesligada.VerificacaoFalhou == motivo, caso);
        Assert.Contains(log.Entradas, e => e.Evento.Id is 2203 or 2204);
        Assert.DoesNotContain("SEGREDO", log.TextoCompleto, StringComparison.Ordinal);
        Assert.DoesNotContain("p_escrita_falsa", log.TextoCompleto, StringComparison.Ordinal);
        Assert.All(log.Entradas, e => Assert.Null(e.Erro));
        Assert.Empty(servidor.ErrosInternos);
    }

    [Fact]
    public async Task Erro42P01_LogComSqlState()
    {
        var (_, log, _) = await VerificarAsync(_ => new RespostaPg.Erro("42P01"));

        var falha = Assert.Single(log.Entradas, e => e.Evento.Id == 2204);
        Assert.Contains("PostgresException", falha.Mensagem, StringComparison.Ordinal);
        Assert.Contains("SqlState 42P01", falha.Mensagem, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Prazo_BancoNaoResponde_VerificacaoFalhou()
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));
        var (motivo, log, _) = await VerificarAsync(
            _ => new RespostaPg.Pendurar(TimeSpan.FromSeconds(10), new RespostaPg.Comando("SELECT 0")), cts.Token);

        Assert.Equal(MotivoEscritaDesligada.VerificacaoFalhou, motivo);
        Assert.Contains(log.Entradas, e => e.Evento.Id == 2204);
    }

    [Fact]
    public async Task TokenJaCancelado_VerificacaoFalhou()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        var (motivo, _, _) = await VerificarAsync(_ => Atributos(false, false, false, false, false, false), cts.Token);

        Assert.Equal(MotivoEscritaDesligada.VerificacaoFalhou, motivo);
    }

    [Fact]
    public async Task BancoForaDoAr_VerificacaoFalhou()
    {
        await using var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create(
            "Host=127.0.0.1;Port=1;Username=u;Password=SENHA_R1;SSL Mode=Disable;Timeout=2;Server Compatibility Mode=NoTypeLoading"));
        var log = new LoggerMemoria<VerificadorRoleEscrita>();

        var motivo = await new VerificadorRoleEscrita(fonte, log).VerificarAsync(CancellationToken.None);

        Assert.Equal(MotivoEscritaDesligada.VerificacaoFalhou, motivo);
        Assert.DoesNotContain("SENHA_R1", log.TextoCompleto, StringComparison.Ordinal);
    }

    /// <summary>C2: excecao que nao e do Npgsql (DataSource descartado) tambem vira VerificacaoFalhou.</summary>
    [Fact]
    public async Task ExcecaoQualquer_ObjectDisposed_VerificacaoFalhou()
    {
        var fonte = new FonteDadosEscrita(NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=u;Password=p"));
        await fonte.DisposeAsync();
        var log = new LoggerMemoria<VerificadorRoleEscrita>();

        var motivo = await new VerificadorRoleEscrita(fonte, log).VerificarAsync(CancellationToken.None);

        Assert.Equal(MotivoEscritaDesligada.VerificacaoFalhou, motivo);
        Assert.Contains("ObjectDisposedException", log.TextoCompleto, StringComparison.Ordinal);
        Assert.Contains("SqlState -", log.TextoCompleto, StringComparison.Ordinal);
    }

    [Fact]
    public void Construtor_ArgumentosNulos_Lanca()
    {
        using var ds = NpgsqlDataSource.Create("Host=127.0.0.1;Port=1;Username=u;Password=p");
        Assert.Throws<ArgumentNullException>(() => new VerificadorRoleEscrita(null!, new LoggerMemoria<VerificadorRoleEscrita>()));
        Assert.Throws<ArgumentNullException>(() => new VerificadorRoleEscrita(new FonteDadosEscrita(ds), null!));
    }
}
