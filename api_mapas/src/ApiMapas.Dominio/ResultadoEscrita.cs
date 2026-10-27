namespace ApiMapas.Dominio;

/// <summary>
/// Resultado da escrita no banco (PUT W1/W2 e POST W3/W4; D-N5, D-N6). Falhas de banco (500) nao
/// sao resultados: viram excecao sanitizada na Infraestrutura.
/// </summary>
public abstract record ResultadoEscrita
{
    private ResultadoEscrita()
    {
    }

    /// <summary>PUT: UPDATE de exatamente 1 linha e COMMIT (204).</summary>
    public sealed record Atualizado : ResultadoEscrita
    {
        public static readonly Atualizado Instancia = new();
    }

    /// <summary>POST: INSERT e COMMIT; <paramref name="Id"/> e o int devolvido pelo RETURNING id (201).</summary>
    public sealed record Criado(int Id) : ResultadoEscrita;

    /// <summary>Geometria rejeitada na validacao do banco (W*.V); nada foi gravado (400). Motivo da lista fixa.</summary>
    public sealed record GeometriaInvalida(string Motivo) : ResultadoEscrita;

    /// <summary>PUT: UPDATE nao achou o id (rowcount 0; ROLLBACK; o PUT nao cria id - D-P3) (404).</summary>
    public sealed record NaoEncontrado : ResultadoEscrita
    {
        public static readonly NaoEncontrado Instancia = new();
    }

    /// <summary>Lock nao obtido em 5 s (55P03) ou deadlock no POST (40P01); ROLLBACK (409).</summary>
    public sealed record Conflito : ResultadoEscrita
    {
        public static readonly Conflito Instancia = new();
    }
}
