using System.Globalization;
using Npgsql;

namespace ApiMapas.Infraestrutura.Configuracao;

/// <summary>
/// Parametros de conexao lidos SOMENTE de variaveis de ambiente PG_* (D-A4, RA-8, RA-9),
/// no mesmo padrao de teste_metry. A senha fica em campo privado e so sai daqui dentro da
/// connection string, que nunca e exibida nem logada.
/// </summary>
public sealed class ConexaoConfig
{
    public const string VarHost = "PG_HOST";
    public const string VarPorta = "PG_PORT";
    public const string VarBase = "PG_DATABASE";
    public const string VarUsuario = "PG_USER";
    public const string VarSenha = "PG_PASSWORD";
    public const string VarSslMode = "PG_SSLMODE";

    public const string HostPadrao = "192.168.168.106";
    public const int PortaPadrao = 5432;
    public const string BasePadrao = "casaos";

    /// <summary>RA-8: padrao Require.</summary>
    public const SslMode SslModePadrao = SslMode.Require;

    /// <summary>RA-12: timeout de conexao 10 s.</summary>
    public const int TimeoutConexaoSegundos = 10;

    /// <summary>RA-12: CommandTimeout no maximo 15 s.</summary>
    public const int TimeoutComandoSegundos = 15;

    /// <summary>RA-12: Maximum Pool Size 20.</summary>
    public const int TamanhoMaximoPool = 20;

    public const string NomeAplicacao = "ApiMapas";

    /// <summary>RA-8: lista permitida de PG_SSLMODE (sem diferenciar maiusculas/minusculas).</summary>
    private static readonly Dictionary<string, SslMode> SslModesPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Disable"] = SslMode.Disable,
        ["Prefer"] = SslMode.Prefer,
        ["Require"] = SslMode.Require,
        ["VerifyCA"] = SslMode.VerifyCA,
        ["VerifyFull"] = SslMode.VerifyFull,
    };

    private readonly string _usuario;
    private readonly string _senha;

    private ConexaoConfig(string host, int porta, string baseDados, string usuario, string senha, SslMode sslMode)
    {
        Host = host;
        Porta = porta;
        BaseDados = baseDados;
        _usuario = usuario;
        _senha = senha;
        SslMode = sslMode;
    }

    public string Host { get; }

    public int Porta { get; }

    public string BaseDados { get; }

    public SslMode SslMode { get; }

    /// <summary>True quando o modo SSL efetivo e Disable ou Prefer (gera warning ao iniciar - RA-8).</summary>
    public bool SslInseguro => SslMode is SslMode.Disable or SslMode.Prefer;

    /// <summary>
    /// Le e valida as variaveis. Lanca <see cref="ConfiguracaoInvalidaException"/> citando apenas
    /// o nome da variavel (nunca o valor).
    /// </summary>
    /// <param name="lerVariavel">Leitor injetavel (em producao: Environment.GetEnvironmentVariable).</param>
    public static ConexaoConfig Carregar(Func<string, string?> lerVariavel)
    {
        ArgumentNullException.ThrowIfNull(lerVariavel);

        var host = LerOpcional(lerVariavel, VarHost) ?? HostPadrao;
        var baseDados = LerOpcional(lerVariavel, VarBase) ?? BasePadrao;
        var porta = LerPorta(lerVariavel);
        var sslMode = LerSslMode(lerVariavel);

        var usuario = LerOpcional(lerVariavel, VarUsuario)
            ?? throw new ConfiguracaoInvalidaException(VarUsuario, "e obrigatoria e nao foi definida (defina no shell antes de subir a API)");

        var senha = lerVariavel(VarSenha);
        if (string.IsNullOrWhiteSpace(senha))
        {
            throw new ConfiguracaoInvalidaException(VarSenha, "e obrigatoria e nao foi definida (defina no shell antes de subir a API)");
        }

        return new ConexaoConfig(host, porta, baseDados, usuario, senha, sslMode);
    }

    /// <summary>RA-9: descricao segura, so host/porta/base/ssl (sem usuario e sem senha).</summary>
    public string Descricao() =>
        $"{TextoSeguro.Sanitizar(Host)}:{Porta.ToString(CultureInfo.InvariantCulture)}/" +
        $"{TextoSeguro.Sanitizar(BaseDados)} ssl={SslMode}";

    /// <summary>Nunca expor credenciais via ToString.</summary>
    public override string ToString() => Descricao();

    /// <summary>
    /// RP-8: true quando <paramref name="usuario"/> e o mesmo usuario da leitura (depois do Trim,
    /// sem diferenciar maiusculas). Nao expoe o usuario.
    /// </summary>
    internal bool UsuarioIgual(string usuario) =>
        string.Equals(_usuario.Trim(), usuario.Trim(), StringComparison.OrdinalIgnoreCase);

    /// <summary>RP-8: true quando <paramref name="senha"/> e igual a senha da leitura (comparacao em tempo constante).</summary>
    internal bool SenhaIgual(string senha) =>
        System.Security.Cryptography.CryptographicOperations.FixedTimeEquals(
            System.Text.Encoding.UTF8.GetBytes(_senha), System.Text.Encoding.UTF8.GetBytes(senha));

    /// <summary>
    /// Monta a connection string. CONTEM A SENHA: nunca exibir nem logar.
    /// Sem IncludeErrorDetail e sem TrustServerCertificate (RA-6/RA-8); Persist Security Info = false (RA-9).
    /// </summary>
    internal string CriarConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Porta,
            Database = BaseDados,
            Username = _usuario,
            Password = _senha,
            SslMode = SslMode,
            Timeout = TimeoutConexaoSegundos,
            CommandTimeout = TimeoutComandoSegundos,
            MaxPoolSize = TamanhoMaximoPool,
            ApplicationName = NomeAplicacao,
            PersistSecurityInfo = false,
            IncludeErrorDetail = false,
        };

        return builder.ConnectionString;
    }

    /// <summary>Cria o pool de conexoes (nao abre conexao).</summary>
    public NpgsqlDataSource CriarDataSource() => NpgsqlDataSource.Create(CriarConnectionString());

    private static string? LerOpcional(Func<string, string?> lerVariavel, string nome)
    {
        var valor = lerVariavel(nome);
        return string.IsNullOrWhiteSpace(valor) ? null : valor.Trim();
    }

    private static int LerPorta(Func<string, string?> lerVariavel)
    {
        var texto = LerOpcional(lerVariavel, VarPorta);
        if (texto is null)
        {
            return PortaPadrao;
        }

        if (texto.Length > 5
            || !int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var porta)
            || porta < 1 || porta > 65535)
        {
            throw new ConfiguracaoInvalidaException(VarPorta, "deve ser um numero inteiro entre 1 e 65535");
        }

        return porta;
    }

    private static SslMode LerSslMode(Func<string, string?> lerVariavel)
    {
        var bruto = lerVariavel(VarSslMode);
        if (bruto is null)
        {
            return SslModePadrao;
        }

        if (string.IsNullOrWhiteSpace(bruto) || !SslModesPermitidos.TryGetValue(bruto.Trim(), out var modo))
        {
            throw new ConfiguracaoInvalidaException(VarSslMode, "tem valor nao permitido (use Disable, Prefer, Require, VerifyCA ou VerifyFull)");
        }

        return modo;
    }
}
