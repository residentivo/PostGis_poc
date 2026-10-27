namespace ApiMapas.Aplicacao;

/// <summary>
/// Leitura do corpo do PUT e do POST, feita pela camada de API (Content-Type e limite de bytes - RP-5/RP-12).
/// No PUT, o servico chama <see cref="LerAsync"/> somente depois de validar o id, para que um id invalido
/// responda 400 sem ler o corpo (ordem do contrato).
/// </summary>
public interface ILeitorCorpo
{
    /// <summary>Confere o Content-Type e le o corpo com limite de bytes.</summary>
    Task<LeituraCorpo> LerAsync(CancellationToken cancellationToken);
}

/// <summary>Resultado fechado da leitura do corpo.</summary>
public abstract record LeituraCorpo
{
    private LeituraCorpo()
    {
    }

    /// <summary>Corpo lido inteiro, dentro do limite.</summary>
    public sealed record Lido(ReadOnlyMemory<byte> Bytes) : LeituraCorpo;

    /// <summary>Content-Type ausente ou diferente de application/json (charset utf-8 opcional) - 415.</summary>
    public sealed record TipoMidiaNaoSuportado : LeituraCorpo
    {
        public static readonly TipoMidiaNaoSuportado Instancia = new();
    }

    /// <summary>Corpo acima do limite (Content-Length ou contagem na leitura) - 413.</summary>
    public sealed record MuitoGrande(long Limite) : LeituraCorpo;

    /// <summary>
    /// Leitura interrompida pelo servidor (corpo lento ou truncado; C5) - 400 "Corpo inválido".
    /// Carrega so o StatusCode da BadHttpRequestException, nunca a excecao.
    /// </summary>
    public sealed record Invalida(int StatusCode) : LeituraCorpo;
}
