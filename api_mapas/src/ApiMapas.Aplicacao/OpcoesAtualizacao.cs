namespace ApiMapas.Aplicacao;

/// <summary>Limites da escrita, compartilhados por PUT e POST (D-P4, D-N9). Lidos da secao "ApiMapas" do appsettings.json.</summary>
public sealed class OpcoesAtualizacao
{
    /// <summary>D-N9 / RS-25: teto padrao do corpo do PUT e do POST: 64 KiB (o corpo tem uma geometria so).</summary>
    public const long LimiteBytesCorpoPadrao = 65_536;

    /// <summary>Maior valor aceito para ApiMapas:LimiteBytesCorpoPut (RP-6).</summary>
    public const long LimiteBytesCorpoMaximo = 1_048_576;

    /// <summary>Padrao de escritas (PUT e POST somados) por minuto, por IP e na API toda (D-P4, RS-25).</summary>
    public const int LimiteEscritasPorMinutoPadrao = 10;

    /// <summary>Maior valor aceito para ApiMapas:LimiteEscritasPorMinuto (RP-6).</summary>
    public const int LimiteEscritasPorMinutoMaximo = 60;

    /// <summary>R-2: no maximo 3 escritas (PUT e POST) simultaneas na API toda; o excedente responde 429.</summary>
    public const int EscritasSimultaneas = 3;

    /// <summary>RP-4 / D-P11: prazo total de cada PUT ou POST.</summary>
    public static readonly TimeSpan PrazoTotalPadrao = TimeSpan.FromSeconds(30);

    /// <summary>Teto de bytes do corpo do PUT e do POST (1..1048576; padrao 65536).</summary>
    public long LimiteBytesCorpo { get; set; } = LimiteBytesCorpoPadrao;

    /// <summary>Prazo total da escrita (leitura do corpo + banco). Estouro responde 500 (D-P11).</summary>
    public TimeSpan PrazoTotal { get; set; } = PrazoTotalPadrao;
}
