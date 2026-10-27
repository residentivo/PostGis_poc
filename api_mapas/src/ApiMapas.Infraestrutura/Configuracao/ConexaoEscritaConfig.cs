using Npgsql;

namespace ApiMapas.Infraestrutura.Configuracao;

/// <summary>
/// Motivo de a escrita (PUT e POST) estar desligada (D-P9, D-N10). As rotas de escrita nao sao
/// mapeadas: o PUT responde 405 e o POST 404 (RS-21).
/// </summary>
public enum MotivoEscritaDesligada
{
    /// <summary>Escrita habilitada.</summary>
    Nenhum = 0,

    /// <summary>PG_ESCRITA_USER e PG_ESCRITA_PASSWORD ausentes.</summary>
    VariaveisAusentes = 1,

    /// <summary>PG_SSLMODE efetivo Disable ou Prefer (RP-7).</summary>
    SslInseguro = 2,

    /// <summary>
    /// A role conectada e superusuario, tem BYPASSRLS, CREATEROLE, CREATEDB ou REPLICATION, tem
    /// TRUNCATE ou DELETE em public.mapas, tem INSERT ou UPDATE na coluna id, e membro de qualquer
    /// outra role (inclui pg_write_all_data) ou e dona de public.mapas (R-1 / C4 / RS-20 / PO5).
    /// </summary>
    RolePrivilegiada = 3,

    /// <summary>A checagem da role na subida falhou (banco nao respondeu ou erro) (R-1).</summary>
    VerificacaoFalhou = 4,

    /// <summary>
    /// public.mapas sem a PRIMARY KEY exata mapas_pkey em id, ou com id que nao e GENERATED ALWAYS:
    /// a migracao (database/migracao_mapas_pk.sql) nao foi aplicada (RS-20, D-N10).
    /// </summary>
    SemChavePrimaria = 5,
}

/// <summary>Textos fixos (sem valores de configuracao) de cada motivo, para o log de inicio.</summary>
public static class MotivoEscritaDesligadaExtensoes
{
    public static string Descrever(this MotivoEscritaDesligada motivo) => motivo switch
    {
        MotivoEscritaDesligada.Nenhum => "escrita habilitada",
        MotivoEscritaDesligada.VariaveisAusentes =>
            $"{ConexaoEscritaConfig.VarUsuario} e {ConexaoEscritaConfig.VarSenha} nao definidas",
        MotivoEscritaDesligada.SslInseguro =>
            $"{ConexaoConfig.VarSslMode} efetivo e Disable ou Prefer (a escrita exige Require, VerifyCA ou VerifyFull)",
        MotivoEscritaDesligada.RolePrivilegiada =>
            "a role de escrita e superusuario, tem BYPASSRLS/CREATEROLE/TRUNCATE, e membro de pg_write_all_data ou e dona de public.mapas, ou ainda tem DELETE em public.mapas, INSERT ou UPDATE na coluna id, CREATEDB, REPLICATION ou e membro de outra role (use api_mapas_escrita, database/roles.sql revisado)",
        MotivoEscritaDesligada.VerificacaoFalhou =>
            "nao foi possivel conferir os atributos da role de escrita na subida (banco indisponivel ou erro)",
        MotivoEscritaDesligada.SemChavePrimaria =>
            "public.mapas nao tem a PRIMARY KEY mapas_pkey exata em id ou o id nao e GENERATED ALWAYS (aplicar database/migracao_mapas_pk.sql)",
        _ => "motivo desconhecido",
    };
}

/// <summary>
/// Conexao de ESCRITA (D-P9, RP-7, RP-8): PG_ESCRITA_USER/PG_ESCRITA_PASSWORD; host, porta, base e
/// PG_SSLMODE sao os mesmos da leitura. Pool proprio de 5 conexoes, ApplicationName
/// ApiMapas.Escrita. A senha fica em campo privado e so sai na connection string, que nunca e
/// exibida nem logada.
/// </summary>
public sealed class ConexaoEscritaConfig
{
    public const string VarUsuario = "PG_ESCRITA_USER";
    public const string VarSenha = "PG_ESCRITA_PASSWORD";

    /// <summary>D-P9: Maximum Pool Size 5 (alinhado ao CONNECTION LIMIT 5 da role).</summary>
    public const int TamanhoMaximoPool = 5;

    public const string NomeAplicacao = "ApiMapas.Escrita";

    private readonly ConexaoConfig _leitura;
    private readonly string _usuario;
    private readonly string _senha;

    private ConexaoEscritaConfig(ConexaoConfig leitura, string usuario, string senha)
    {
        _leitura = leitura;
        _usuario = usuario;
        _senha = senha;
    }

    /// <summary>
    /// Le e valida PG_ESCRITA_*. Devolve null (escrita desligada, com o motivo) quando as duas
    /// variaveis estao ausentes ou o SSL efetivo e Disable/Prefer. Lanca
    /// <see cref="ConfiguracaoInvalidaException"/> (a API sai com codigo 1, citando so o nome da
    /// variavel) quando so uma das duas existe, quando o usuario e o mesmo de PG_USER (Trim, sem
    /// diferenciar maiusculas) ou quando a senha e igual a PG_PASSWORD (RP-8).
    /// </summary>
    public static ConexaoEscritaConfig? Carregar(Func<string, string?> lerVariavel, ConexaoConfig leitura, out MotivoEscritaDesligada motivo)
    {
        ArgumentNullException.ThrowIfNull(lerVariavel);
        ArgumentNullException.ThrowIfNull(leitura);

        var usuarioBruto = lerVariavel(VarUsuario);
        var senha = lerVariavel(VarSenha);
        var temUsuario = !string.IsNullOrWhiteSpace(usuarioBruto);
        var temSenha = !string.IsNullOrWhiteSpace(senha);

        if (!temUsuario && !temSenha)
        {
            motivo = MotivoEscritaDesligada.VariaveisAusentes;
            return null;
        }

        if (!temUsuario)
        {
            throw new ConfiguracaoInvalidaException(VarUsuario,
                $"nao foi definida, mas {VarSenha} foi (defina as duas para ligar a escrita PUT e POST, ou nenhuma para deixa-la desligada)");
        }

        if (!temSenha)
        {
            throw new ConfiguracaoInvalidaException(VarSenha,
                $"nao foi definida, mas {VarUsuario} foi (defina as duas para ligar a escrita PUT e POST, ou nenhuma para deixa-la desligada)");
        }

        var usuario = usuarioBruto!.Trim();
        if (leitura.UsuarioIgual(usuario))
        {
            throw new ConfiguracaoInvalidaException(VarUsuario,
                $"nao pode ser o mesmo usuario de {ConexaoConfig.VarUsuario} (use a role api_mapas_escrita)");
        }

        if (leitura.SenhaIgual(senha!))
        {
            throw new ConfiguracaoInvalidaException(VarSenha,
                $"nao pode ser igual a {ConexaoConfig.VarSenha} (cada role tem senha propria)");
        }

        if (leitura.SslInseguro)
        {
            motivo = MotivoEscritaDesligada.SslInseguro;
            return null;
        }

        motivo = MotivoEscritaDesligada.Nenhum;
        return new ConexaoEscritaConfig(leitura, usuario, senha!);
    }

    /// <summary>Descricao segura: so host/porta/base/ssl (sem usuario e sem senha).</summary>
    public string Descricao() => _leitura.Descricao() + " (escrita)";

    /// <summary>Nunca expor credenciais via ToString.</summary>
    public override string ToString() => Descricao();

    /// <summary>
    /// Monta a connection string. CONTEM A SENHA: nunca exibir nem logar. Pool 5, sem
    /// IncludeErrorDetail e sem TrustServerCertificate; Persist Security Info = false (RP-8).
    /// </summary>
    internal string CriarConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = _leitura.Host,
            Port = _leitura.Porta,
            Database = _leitura.BaseDados,
            Username = _usuario,
            Password = _senha,
            SslMode = _leitura.SslMode,
            Timeout = ConexaoConfig.TimeoutConexaoSegundos,
            CommandTimeout = ConexaoConfig.TimeoutComandoSegundos,
            MaxPoolSize = TamanhoMaximoPool,
            ApplicationName = NomeAplicacao,
            PersistSecurityInfo = false,
            IncludeErrorDetail = false,
        };

        return builder.ConnectionString;
    }

    /// <summary>Cria o pool de escrita (nao abre conexao), separado do pool de leitura.</summary>
    public FonteDadosEscrita CriarFonteDados() => new(NpgsqlDataSource.Create(CriarConnectionString()));
}
