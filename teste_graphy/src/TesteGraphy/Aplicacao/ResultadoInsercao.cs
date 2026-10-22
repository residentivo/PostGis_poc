using TesteGraphy.Dados;

namespace TesteGraphy.Aplicacao;

/// <summary>Situacao final de cada WKT processado.</summary>
public enum StatusItem
{
    /// <summary>Validado, inserido e lido de volta com SRID 4326.</summary>
    Inserido,

    /// <summary>Rejeitado pelo WktValidator (nao chegou ao banco).</summary>
    InvalidoLocal,

    /// <summary>Rejeitado por ST_IsValid = false (nao inserido).</summary>
    InvalidoBanco,

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
    RegistroGraphy? Registro = null,
    int? IdInserido = null,
    string? Erro = null);

/// <summary>Resultado da execucao completa.</summary>
public sealed record ResultadoExecucao(
    IReadOnlyList<ResultadoItem> Itens,
    bool LimiteExcedido,
    CodigoSaida Codigo);
