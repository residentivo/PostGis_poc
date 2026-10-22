using TesteMetry.Dados;

namespace TesteMetry.Aplicacao;

/// <summary>Situacao final de cada WKT processado.</summary>
public enum StatusItem
{
    /// <summary>Validado, inserido e lido de volta com o SRID configurado (PG_SRID).</summary>
    Inserido,

    /// <summary>Rejeitado pelo WktValidator (nao chegou ao banco).</summary>
    InvalidoLocal,

    /// <summary>
    /// Rejeitado no banco antes do INSERT (nao inserido): ST_IsValid = false, GeometryType fora
    /// da lista permitida ou erro de dado do PostGIS na validacao (ex.: SqlState XX000 de WKT malformado).
    /// </summary>
    InvalidoBanco,

    /// <summary>Inserido, mas ST_SRID lido difere do SRID configurado (D-M5). Codigo de saida 2.</summary>
    SridDivergente,

    /// <summary>Erro retornado pelo servidor (SqlState + MessageText) ou conferencia divergente.</summary>
    ErroBanco,

    /// <summary>Falha de conexao/timeout.</summary>
    ErroConexao,

    /// <summary>Nao processado porque a conexao falhou em um item anterior.</summary>
    NaoProcessado,
}

/// <summary>Resultado de um item. <see cref="WktEcoado"/> ja vem sanitizado (R2).</summary>
public sealed record ResultadoItem(
    int Indice,
    string WktEcoado,
    StatusItem Status,
    RegistroMetry? Registro = null,
    int? IdInserido = null,
    string? Erro = null);

/// <summary>Resultado da execucao completa.</summary>
public sealed record ResultadoExecucao(
    IReadOnlyList<ResultadoItem> Itens,
    bool LimiteExcedido,
    CodigoSaida Codigo);
