namespace ApiMapas.Aplicacao;

/// <summary>Resultado do PUT, traduzido pela API em 204 / 400 / 404 / 409 / 413 / 415.</summary>
public abstract record ResultadoAtualizacao
{
    private ResultadoAtualizacao()
    {
    }

    /// <summary>Id fora da regra (400 "Id inválido"); corpo nao lido e banco nao tocado.</summary>
    public sealed record IdInvalido : ResultadoAtualizacao
    {
        public static readonly IdInvalido Instancia = new();
    }

    /// <summary>Content-Type nao aceito (415).</summary>
    public sealed record TipoMidiaNaoSuportado : ResultadoAtualizacao
    {
        public static readonly TipoMidiaNaoSuportado Instancia = new();
    }

    /// <summary>Corpo acima do limite de bytes (413).</summary>
    public sealed record CorpoMuitoGrande(long Limite) : ResultadoAtualizacao;

    /// <summary>JSON malformado ou fora do formato (400 "Corpo inválido"). Nao carrega a JsonException.</summary>
    public sealed record CorpoInvalido : ResultadoAtualizacao
    {
        public static readonly CorpoInvalido Instancia = new();
    }

    /// <summary>Geometria rejeitada (400 "Geometria inválida"); motivo da lista fixa, nunca o WKT.</summary>
    public sealed record GeometriaInvalida(string Motivo) : ResultadoAtualizacao;

    /// <summary>Id sem linha em public.mapas (404). O PUT nunca cria id (D-P3).</summary>
    public sealed record NaoEncontrado(int Id, string Coluna) : ResultadoAtualizacao;

    /// <summary>Lock de linha nao obtido em 5 s (409).</summary>
    public sealed record Conflito(int Id, string Coluna) : ResultadoAtualizacao;

    /// <summary>Gravado (204).</summary>
    public sealed record Sucesso(int Id, string Coluna) : ResultadoAtualizacao;
}
