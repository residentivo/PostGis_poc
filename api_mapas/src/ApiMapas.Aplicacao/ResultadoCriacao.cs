namespace ApiMapas.Aplicacao;

/// <summary>Resultado do POST, traduzido pela API em 201 / 400 / 409 / 413 / 415 (D-N5).</summary>
public abstract record ResultadoCriacao
{
    private ResultadoCriacao()
    {
    }

    /// <summary>Content-Type nao aceito (415).</summary>
    public sealed record TipoMidiaNaoSuportado : ResultadoCriacao
    {
        public static readonly TipoMidiaNaoSuportado Instancia = new();
    }

    /// <summary>Corpo acima do limite de bytes (413).</summary>
    public sealed record CorpoMuitoGrande(long Limite) : ResultadoCriacao;

    /// <summary>JSON malformado ou fora do formato (400 "Corpo inválido").</summary>
    public sealed record CorpoInvalido : ResultadoCriacao
    {
        public static readonly CorpoInvalido Instancia = new();
    }

    /// <summary>Geometria rejeitada (400 "Geometria inválida"); motivo da lista fixa, nunca o WKT.</summary>
    public sealed record GeometriaInvalida(string Motivo) : ResultadoCriacao;

    /// <summary>55P03 ou 40P01 no INSERT (409; RS-18).</summary>
    public sealed record Conflito(string Coluna) : ResultadoCriacao;

    /// <summary>Criado (201). <paramref name="Id"/> e o int do RETURNING id; vai para o Location.</summary>
    public sealed record Sucesso(int Id, string Coluna) : ResultadoCriacao;
}
