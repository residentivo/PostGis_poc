using System.Text.Json;
using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;

namespace ApiMapas.Aplicacao;

/// <summary>
/// Etapas de entrada comuns ao PUT e ao POST (D-N9): Content-Type (415) e tamanho (413) pelo
/// <see cref="ILeitorCorpo"/>, JSON do objeto unico {"wkt","srid"} (400 "Corpo inválido", RP-11) e
/// validacao local da geometria (400 "Geometria inválida", RP-14). Nao registra log: cada servico
/// registra com os proprios EventIds e o metodo (RS-4). Nada do corpo nem da JsonException sai daqui.
/// </summary>
internal static class EntradaGeometria
{
    /// <summary>
    /// RP-11: nomes exatos (sensiveis a maiusculas), campos obrigatorios (required), MaxDepth 8,
    /// membro desconhecido e propriedade duplicada rejeitados, numero so como numero JSON e
    /// null proibido onde o tipo nao e anulavel.
    /// </summary>
    internal static readonly JsonSerializerOptions OpcoesJson = new()
    {
        PropertyNameCaseInsensitive = false,
        MaxDepth = 8,
        UnmappedMemberHandling = System.Text.Json.Serialization.JsonUnmappedMemberHandling.Disallow,
        AllowDuplicateProperties = false,
        NumberHandling = System.Text.Json.Serialization.JsonNumberHandling.Strict,
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
    };

    /// <summary>Resultado fechado das etapas de entrada.</summary>
    internal abstract record Etapa
    {
        private Etapa()
        {
        }

        /// <summary>Geometria valida localmente, pronta para o banco.</summary>
        internal sealed record Valida(GeometriaEntrada Geometria) : Etapa;

        /// <summary>415.</summary>
        internal sealed record TipoMidia : Etapa
        {
            internal static readonly TipoMidia Instancia = new();
        }

        /// <summary>413.</summary>
        internal sealed record MuitoGrande(long Limite) : Etapa;

        /// <summary>Leitura interrompida pelo servidor (C5): 400 "Corpo inválido". So o StatusCode.</summary>
        internal sealed record LeituraInterrompida(int StatusCode) : Etapa;

        /// <summary>JSON fora do formato: 400 "Corpo inválido".</summary>
        internal sealed record JsonInvalido : Etapa
        {
            internal static readonly JsonInvalido Instancia = new();
        }

        /// <summary>Geometria rejeitada localmente: 400 "Geometria inválida" com motivo fixo.</summary>
        internal sealed record GeometriaInvalida(string Motivo) : Etapa;
    }

    /// <summary>Le o corpo, desserializa e valida a geometria para a coluna, nesta ordem.</summary>
    internal static async Task<Etapa> LerAsync(ILeitorCorpo leitorCorpo, ColunaMapa coluna, CancellationToken cancellationToken)
    {
        var leitura = await leitorCorpo.LerAsync(cancellationToken).ConfigureAwait(false);
        switch (leitura)
        {
            case LeituraCorpo.TipoMidiaNaoSuportado:
                return Etapa.TipoMidia.Instancia;

            case LeituraCorpo.MuitoGrande muitoGrande:
                return new Etapa.MuitoGrande(muitoGrande.Limite);

            case LeituraCorpo.Invalida invalida:
                return new Etapa.LeituraInterrompida(invalida.StatusCode);

            case LeituraCorpo.Lido lido:
                if (!TentarDesserializar(lido.Bytes, out var corpo))
                {
                    return Etapa.JsonInvalido.Instancia;
                }

                return GeometriaEntrada.TentarCriar(corpo!.Wkt, corpo.Srid, coluna, out var geometria, out var motivo)
                    ? new Etapa.Valida(geometria!)
                    : new Etapa.GeometriaInvalida(motivo!);

            default:
                throw new InvalidOperationException("Leitura do corpo nao suportada.");
        }
    }

    /// <summary>
    /// RP-11: desserializa com as opcoes estritas. Corpo nulo, raiz que nao e objeto, campo faltando,
    /// extra, duplicado, com outra caixa, srid em texto, wkt nulo ou profundidade acima de 8
    /// respondem false. Nada da excecao e registrado (ela pode conter trecho do corpo).
    /// </summary>
    internal static bool TentarDesserializar(ReadOnlyMemory<byte> bytes, out CorpoGeometriaDto? corpo)
    {
        corpo = null;
        try
        {
            corpo = JsonSerializer.Deserialize<CorpoGeometriaDto>(bytes.Span, OpcoesJson);
        }
        catch (JsonException)
        {
            return false;
        }

        if (corpo?.Wkt is null)
        {
            corpo = null;
            return false;
        }

        return true;
    }
}
