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
    public const string ChaveLimiteBytesCorpoPut = OpcoesConsulta.Secao + ":LimiteBytesCorpoPut";
    public const string ChaveLimiteEscritas = OpcoesConsulta.Secao + ":LimiteEscritasPorMinuto";

    /// <summary>REC-A1: 60 requisicoes por minuto por IP.</summary>
    public const int LimiteRequisicoesPadrao = 60;

    /// <summary>Porta/URL padrao quando nada for configurado (contrato A1).</summary>
    public const string UrlPadrao = "http://localhost:5080";

    private ConfiguracaoApi(bool verboso, long limiteBytes, int limiteRequisicoes, long limiteBytesCorpoPut, int limiteEscritas)
    {
        Verboso = verboso;
        LimiteBytesGeometrias = limiteBytes;
        LimiteRequisicoesPorMinuto = limiteRequisicoes;
        LimiteBytesCorpoPut = limiteBytesCorpoPut;
        LimiteEscritasPorMinuto = limiteEscritas;
    }

    /// <summary>Liga os logs de passo (nivel Debug) das categorias ApiMapas.*.</summary>
    public bool Verboso { get; }

    public long LimiteBytesGeometrias { get; }

    public int LimiteRequisicoesPorMinuto { get; }

    /// <summary>D-N9 / RP-6: teto do corpo do PUT e do POST, 1..1048576 bytes (padrao 65536). A chave mantem o nome.</summary>
    public long LimiteBytesCorpoPut { get; }

    /// <summary>D-N9 / RP-6: escritas (PUT e POST somados) por minuto, por IP e na API toda, 1..60 (padrao 10); soma-se ao limite global.</summary>
    public int LimiteEscritasPorMinuto { get; }

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

        // RP-6: fora da faixa, a API sai com codigo 1.
        var limiteBytesCorpoPut = LerInteiroNaFaixa(configuracao, ChaveLimiteBytesCorpoPut,
            OpcoesAtualizacao.LimiteBytesCorpoPadrao, 1, OpcoesAtualizacao.LimiteBytesCorpoMaximo);
        var limiteEscritas = LerInteiroNaFaixa(configuracao, ChaveLimiteEscritas,
            OpcoesAtualizacao.LimiteEscritasPorMinutoPadrao, 1, OpcoesAtualizacao.LimiteEscritasPorMinutoMaximo);

        return new ConfiguracaoApi(verboso, limiteBytes, (int)limiteRequisicoes, limiteBytesCorpoPut, (int)limiteEscritas);
    }

    public OpcoesConsulta CriarOpcoesConsulta() => new() { LimiteBytesGeometrias = LimiteBytesGeometrias };

    public OpcoesAtualizacao CriarOpcoesAtualizacao() => new() { LimiteBytesCorpo = LimiteBytesCorpoPut };

    private static long LerInteiroNaFaixa(IConfiguration configuracao, string chave, long padrao, long minimo, long maximo)
    {
        var texto = configuracao[chave];
        if (string.IsNullOrWhiteSpace(texto))
        {
            return padrao;
        }

        if (!long.TryParse(texto.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var valor)
            || valor < minimo || valor > maximo)
        {
            throw new ConfiguracaoInvalidaException(chave,
                $"deve ser um inteiro entre {minimo.ToString(CultureInfo.InvariantCulture)} e {maximo.ToString(CultureInfo.InvariantCulture)}");
        }

        return valor;
    }

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
