using System.Globalization;
using Npgsql;
using TesteMetry.Comum;
using TesteMetry.Diagnostico;
using TesteMetry.Validacao;

namespace TesteMetry.Configuracao;

/// <summary>
/// Parametros de conexao e SRID lidos de variaveis de ambiente (R1, R2, R5, D-M4, RQ-1, RQ-2).
/// A senha fica em campo privado e so sai daqui dentro da connection string,
/// que nunca e exibida nem logada.
/// </summary>
public sealed class ConexaoConfig
{
    public const string VarHost = "PG_HOST";
    public const string VarPorta = "PG_PORT";
    public const string VarBase = "PG_DATABASE";
    public const string VarUsuario = "PG_USER";
    public const string VarSenha = "PG_PASSWORD";
    public const string VarSslMode = "PG_SSLMODE";
    public const string VarSrid = "PG_SRID";

    public const string HostPadrao = "192.168.168.106";
    public const int PortaPadrao = 5432;
    public const string BasePadrao = "casaos";
    public const SslMode SslModePadrao = SslMode.Prefer;

    public const int TimeoutConexaoSegundos = 10;
    public const int TimeoutComandoSegundos = 30;
    public const string NomeAplicacao = "TesteMetry";

    /// <summary>Maximo de caracteres aceitos em PG_SRID (D-M4).</summary>
    public const int TamanhoMaximoSrid = 6;

    /// <summary>Lista permitida de PG_SSLMODE (R5), sem diferenciar maiusculas/minusculas.</summary>
    private static readonly Dictionary<string, SslMode> SslModesPermitidos = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Disable"] = SslMode.Disable,
        ["Prefer"] = SslMode.Prefer,
        ["Require"] = SslMode.Require,
        ["VerifyCA"] = SslMode.VerifyCA,
        ["VerifyFull"] = SslMode.VerifyFull,
    };

    private readonly string _senha;

    private ConexaoConfig(string host, int porta, string baseDados, string usuario, string senha, SslMode sslMode, int srid)
    {
        Srid = srid;
        Host = host;
        Porta = porta;
        BaseDados = baseDados;
        Usuario = usuario;
        _senha = senha;
        SslMode = sslMode;
    }

    public string Host { get; }

    public int Porta { get; }

    public string BaseDados { get; }

    public string Usuario { get; }

    public SslMode SslMode { get; }

    /// <summary>
    /// SRID validado (lista permitida de <see cref="LimitesSrid"/>). Convertido uma unica vez
    /// e enviado ao banco SOMENTE como parametro @srid Integer (RQ-1).
    /// </summary>
    public int Srid { get; }

    /// <summary>True quando o modo SSL efetivo e Disable ou Prefer (gera aviso - R5).</summary>
    public bool SslInseguro => SslMode is SslMode.Disable or SslMode.Prefer;

    /// <summary>Aviso a exibir quando <see cref="SslInseguro"/>; caso contrario, null.</summary>
    public string? AvisoSsl => SslInseguro
        ? $"Aviso: {VarSslMode} efetivo = {SslMode}. A conexao pode trafegar SEM criptografia " +
          "e sem verificacao do servidor. Recomendado: Require, VerifyCA ou VerifyFull."
        : null;

    /// <summary>
    /// Le e valida a configuracao. Lanca <see cref="ConfiguracaoInvalidaException"/>
    /// citando apenas o nome da variavel.
    /// </summary>
    /// <param name="lerVariavel">Leitor injetavel (ex.: Environment.GetEnvironmentVariable).</param>
    /// <param name="log">Log opcional (somente passos, nunca valores sensiveis).</param>
    public static ConexaoConfig Carregar(Func<string, string?> lerVariavel, ILog? log = null)
    {
        ArgumentNullException.ThrowIfNull(lerVariavel);
        log ??= LogNulo.Instancia;

        log.Passo($"Lendo {VarHost}, {VarPorta}, {VarBase}, {VarSslMode}, {VarSrid} (com padroes) e {VarUsuario}/{VarSenha} (obrigatorias).");

        var host = LerOpcional(lerVariavel, VarHost) ?? HostPadrao;
        var baseDados = LerOpcional(lerVariavel, VarBase) ?? BasePadrao;
        var porta = LerPorta(lerVariavel);
        var sslMode = LerSslMode(lerVariavel);
        var srid = LerSrid(lerVariavel, log);

        var usuario = LerOpcional(lerVariavel, VarUsuario)
            ?? throw new ConfiguracaoInvalidaException(VarUsuario, "obrigatoria e nao definida");

        var senha = lerVariavel(VarSenha);
        if (string.IsNullOrWhiteSpace(senha))
        {
            throw new ConfiguracaoInvalidaException(VarSenha, "obrigatoria e nao definida");
        }

        var config = new ConexaoConfig(host, porta, baseDados, usuario, senha, sslMode, srid);
        log.Passo($"Configuracao carregada: {config.Descricao()}, ssl={sslMode}.");
        return config;
    }

    /// <summary>Descricao segura (R2): host:porta/base, usuario e o SRID validado (int).</summary>
    public string Descricao() =>
        $"{TextoSeguro.Sanitizar(Host)}:{Porta.ToString(CultureInfo.InvariantCulture)}/" +
        $"{TextoSeguro.Sanitizar(BaseDados)} usuario={TextoSeguro.Sanitizar(Usuario)} " +
        $"srid={Srid.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Endpoint seguro para mensagens de falha de conexao: host:porta.</summary>
    public string Endpoint() => $"{TextoSeguro.Sanitizar(Host)}:{Porta.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>Nunca expor a senha via ToString.</summary>
    public override string ToString() => Descricao();

    /// <summary>
    /// Monta a connection string. CONTEM A SENHA: nunca exibir nem logar.
    /// Sem IncludeErrorDetail e sem TrustServerCertificate (R2/R5).
    /// </summary>
    internal string CriarConnectionString()
    {
        var builder = new NpgsqlConnectionStringBuilder
        {
            Host = Host,
            Port = Porta,
            Database = BaseDados,
            Username = Usuario,
            Password = _senha,
            SslMode = SslMode,
            Timeout = TimeoutConexaoSegundos,
            CommandTimeout = TimeoutComandoSegundos,
            ApplicationName = NomeAplicacao,
            PersistSecurityInfo = false,
        };

        return builder.ConnectionString;
    }

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

        if (!int.TryParse(texto, NumberStyles.None, CultureInfo.InvariantCulture, out var porta)
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
            // Ausente = padrao Prefer (com aviso). Definida vazia/so espacos = invalida (D1/R5).
            return SslModePadrao;
        }

        if (string.IsNullOrWhiteSpace(bruto) || !SslModesPermitidos.TryGetValue(bruto.Trim(), out var modo))
        {
            throw new ConfiguracaoInvalidaException(VarSslMode, "valor nao permitido (use Disable, Prefer, Require, VerifyCA ou VerifyFull)");
        }

        return modo;
    }

    /// <summary>
    /// D-M4 / RQ-1 / REC-1: ausente -> 4326; definida vazia ou so espacos -> erro; parsing
    /// estrito sem Regex (so digitos ASCII, sem sinal/espaco/separador/prefixo, ate 6
    /// caracteres, sem zero a esquerda); valor fora da lista permitida -> erro. Nenhuma
    /// mensagem (nem o log) contem o valor recebido: so o nome da variavel e os permitidos.
    /// </summary>
    private static int LerSrid(Func<string, string?> lerVariavel, ILog log)
    {
        var bruto = lerVariavel(VarSrid);
        if (bruto is null)
        {
            log.Passo($"{VarSrid} ausente; usando o padrao {LimitesSrid.SridPadrao.ToString(CultureInfo.InvariantCulture)}.");
            return LimitesSrid.SridPadrao;
        }

        var motivoPermitidos = $"valor nao permitido (use {LimitesSrid.PermitidosTexto})";

        if (string.IsNullOrWhiteSpace(bruto))
        {
            throw new ConfiguracaoInvalidaException(VarSrid, $"definida porem vazia; {motivoPermitidos}");
        }

        if (bruto.Length > TamanhoMaximoSrid)
        {
            throw new ConfiguracaoInvalidaException(VarSrid, motivoPermitidos);
        }

        foreach (var c in bruto)
        {
            if (!char.IsAsciiDigit(c))
            {
                throw new ConfiguracaoInvalidaException(VarSrid, $"deve conter somente digitos 0-9; {motivoPermitidos}");
            }
        }

        if (bruto[0] == '0')
        {
            throw new ConfiguracaoInvalidaException(VarSrid, $"zeros a esquerda nao sao aceitos; {motivoPermitidos}");
        }

        if (!int.TryParse(bruto, NumberStyles.None, CultureInfo.InvariantCulture, out var srid)
            || !LimitesSrid.EhPermitido(srid))
        {
            throw new ConfiguracaoInvalidaException(VarSrid, motivoPermitidos);
        }

        log.Passo($"{VarSrid} validado: {srid.ToString(CultureInfo.InvariantCulture)}.");
        return srid;
    }
}
