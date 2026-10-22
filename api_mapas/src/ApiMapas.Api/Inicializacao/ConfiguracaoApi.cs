using System.Globalization;
using ApiMapas.Aplicacao;
using ApiMapas.Infraestrutura.Configuracao;

namespace ApiMapas.Api.Inicializacao;

/// <summary>
/// Configuracao NAO sensivel lida do appsettings.json: "Verboso" e a secao "ApiMapas".
/// Credenciais nunca sao lidas daqui (RA-9): somente PG_* do ambiente.
/// </summary>
public sealed class ConfiguracaoApi
{
    public const string ChaveVerboso = "Verboso";
    public const string ChaveLimiteBytes = OpcoesConsulta.Secao + ":LimiteBytesGeometrias";
    public const string ChaveLimiteRequisicoes = OpcoesConsulta.Secao + ":LimiteRequisicoesPorMinuto";

    /// <summary>REC-A1: 60 requisicoes por minuto por IP.</summary>
    public const int LimiteRequisicoesPadrao = 60;

    /// <summary>Porta/URL padrao quando nada for configurado (contrato A1).</summary>
    public const string UrlPadrao = "http://localhost:5080";

    private ConfiguracaoApi(bool verboso, long limiteBytes, int limiteRequisicoes)
    {
        Verboso = verboso;
        LimiteBytesGeometrias = limiteBytes;
        LimiteRequisicoesPorMinuto = limiteRequisicoes;
    }

    /// <summary>Liga os logs de passo (nivel Debug) das categorias ApiMapas.*.</summary>
    public bool Verboso { get; }

    public long LimiteBytesGeometrias { get; }

    public int LimiteRequisicoesPorMinuto { get; }

    public static ConfiguracaoApi Carregar(IConfiguration configuracao)
    {
        ArgumentNullException.ThrowIfNull(configuracao);

        var verbosoTexto = configuracao[ChaveVerboso];
        var verboso = false;
        if (!string.IsNullOrWhiteSpace(verbosoTexto) && !bool.TryParse(verbosoTexto, out verboso))
        {
            throw new ConfiguracaoInvalidaException(ChaveVerboso, "deve ser true ou false");
        }

        var limiteBytes = LerInteiroPositivo(configuracao, ChaveLimiteBytes, OpcoesConsulta.LimiteBytesPadrao);
        var limiteRequisicoes = LerInteiroPositivo(configuracao, ChaveLimiteRequisicoes, LimiteRequisicoesPadrao);
        if (limiteRequisicoes > int.MaxValue)
        {
            throw new ConfiguracaoInvalidaException(ChaveLimiteRequisicoes, "e grande demais");
        }

        return new ConfiguracaoApi(verboso, limiteBytes, (int)limiteRequisicoes);
    }

    public OpcoesConsulta CriarOpcoesConsulta() => new() { LimiteBytesGeometrias = LimiteBytesGeometrias };

    private static long LerInteiroPositivo(IConfiguration configuracao, string chave, long padrao)
    {
        var texto = configuracao[chave];
        if (string.IsNullOrWhiteSpace(texto))
        {
            return padrao;
        }

        if (!long.TryParse(texto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var valor) || valor <= 0)
        {
            throw new ConfiguracaoInvalidaException(chave, "deve ser um inteiro maior que zero");
        }

        return valor;
    }
}
