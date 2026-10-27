using System.Buffers;
using ApiMapas.Aplicacao;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Primitives;

namespace ApiMapas.Api.Endpoints;

/// <summary>
/// Leitura do corpo do PUT e do POST pela API (RP-5, RP-12, RS-1). O corpo e lido aqui, e nao pelo binding
/// automatico do minimal API, para que todo erro saia em ProblemDetails.
/// - Content-Type: so "application/json" (sem diferenciar maiusculas, RFC 9110); o parametro
///   charset e opcional e, se vier, so utf-8 (sem diferenciar maiusculas). Qualquer outro subtipo,
///   parametro extra ou mais de um cabecalho: 415.
/// - Tamanho: Content-Length acima do limite responde 413 sem ler; sem Content-Length (chunked),
///   a leitura usa contador proprio e para no primeiro byte acima do limite. A
///   BadHttpRequestException 413 do Kestrel tambem vira 413; qualquer outra (corpo lento ou
///   truncado) vira 400 "Corpo inválido" (C5). MinRequestBodyDataRate nao e alterado.
/// </summary>
internal sealed class LeitorCorpoHttp : ILeitorCorpo
{
    public const string TipoJson = "application/json";
    public const string Charset = "charset";
    public const string Utf8 = "utf-8";

    private const int TamanhoBloco = 16 * 1024;

    private readonly HttpContext _contexto;
    private readonly long _limite;

    public LeitorCorpoHttp(HttpContext contexto, long limite)
    {
        ArgumentNullException.ThrowIfNull(contexto);
        ArgumentOutOfRangeException.ThrowIfLessThan(limite, 1);
        _contexto = contexto;
        _limite = limite;
    }

    public async Task<LeituraCorpo> LerAsync(CancellationToken cancellationToken)
    {
        var requisicao = _contexto.Request;

        if (!ContentTypeAceito(requisicao.Headers.ContentType))
        {
            return LeituraCorpo.TipoMidiaNaoSuportado.Instancia;
        }

        if (requisicao.ContentLength is long tamanhoDeclarado && tamanhoDeclarado > _limite)
        {
            return new LeituraCorpo.MuitoGrande(_limite);
        }

        // Teto tambem no servidor (Kestrel), quando ainda pode ser alterado.
        var recursoTamanho = _contexto.Features.Get<IHttpMaxRequestBodySizeFeature>();
        if (recursoTamanho is { IsReadOnly: false })
        {
            recursoTamanho.MaxRequestBodySize = _limite;
        }

        try
        {
            return await LerComLimiteAsync(requisicao.Body, requisicao.ContentLength, cancellationToken).ConfigureAwait(false);
        }
        catch (BadHttpRequestException ex) when (ex.StatusCode == StatusCodes.Status413PayloadTooLarge)
        {
            return new LeituraCorpo.MuitoGrande(_limite);
        }
        catch (BadHttpRequestException ex)
        {
            // C5: corpo lento (MinRequestBodyDataRate) ou truncado: 400 "Corpo inválido".
            // Nada da excecao e registrado; o servico loga so o StatusCode.
            return new LeituraCorpo.Invalida(ex.StatusCode);
        }
    }

    private async Task<LeituraCorpo> LerComLimiteAsync(Stream corpo, long? tamanhoDeclarado, CancellationToken cancellationToken)
    {
        var capacidadeInicial = (int)Math.Min(tamanhoDeclarado ?? TamanhoBloco, _limite);
        using var memoria = new MemoryStream(capacidadeInicial);
        var bloco = ArrayPool<byte>.Shared.Rent(TamanhoBloco);
        try
        {
            long total = 0;
            int lidos;
            while ((lidos = await corpo.ReadAsync(bloco.AsMemory(0, TamanhoBloco), cancellationToken).ConfigureAwait(false)) > 0)
            {
                total += lidos;
                if (total > _limite)
                {
                    return new LeituraCorpo.MuitoGrande(_limite);
                }

                memoria.Write(bloco, 0, lidos);
            }

            return new LeituraCorpo.Lido(new ReadOnlyMemory<byte>(memoria.GetBuffer(), 0, (int)memoria.Length));
        }
        finally
        {
            // C6: o bloco pode conter trecho do corpo; limpa antes de devolver ao pool.
            ArrayPool<byte>.Shared.Return(bloco, clearArray: true);
        }
    }

    /// <summary>
    /// RP-12 / C1: tipo "application/json" sem diferenciar maiusculas (RFC 9110 8.3.1), sem outros
    /// subtipos (*+json, json-patch...), com no maximo um parametro, "charset=utf-8" (sem
    /// diferenciar maiusculas), e um unico cabecalho Content-Type.
    /// </summary>
    internal static bool ContentTypeAceito(StringValues valores)
    {
        if (valores.Count != 1 || string.IsNullOrEmpty(valores[0]))
        {
            return false;
        }

        var partes = valores[0]!.Split(';');
        if (!string.Equals(partes[0].Trim(' ', '\t'), TipoJson, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (partes.Length == 1)
        {
            return true;
        }

        if (partes.Length > 2)
        {
            return false;
        }

        var parametro = partes[1].Trim(' ', '\t');
        var igual = parametro.IndexOf('=', StringComparison.Ordinal);
        if (igual <= 0)
        {
            return false;
        }

        var nome = parametro[..igual].Trim(' ', '\t');
        var valor = parametro[(igual + 1)..].Trim(' ', '\t');
        if (valor.Length >= 2 && valor[0] == '"' && valor[^1] == '"')
        {
            valor = valor[1..^1];
        }

        return string.Equals(nome, Charset, StringComparison.OrdinalIgnoreCase)
            && string.Equals(valor, Utf8, StringComparison.OrdinalIgnoreCase);
    }
}
