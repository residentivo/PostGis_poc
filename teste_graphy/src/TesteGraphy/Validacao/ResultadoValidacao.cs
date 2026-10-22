namespace TesteGraphy.Validacao;

/// <summary>Resultado da validacao local de um WKT (R4).</summary>
public sealed class ResultadoValidacao
{
    private ResultadoValidacao(bool valido, string? erro, string? tipo)
    {
        Valido = valido;
        Erro = erro;
        Tipo = tipo;
    }

    /// <summary>True quando o WKT passou em todas as regras locais.</summary>
    public bool Valido { get; }

    /// <summary>Motivo da rejeicao (nunca ecoa o WKT completo); null quando valido.</summary>
    public string? Erro { get; }

    /// <summary>Tipo da geometria raiz em maiusculas (ex.: POINT) quando valido.</summary>
    public string? Tipo { get; }

    public static ResultadoValidacao Ok(string tipo) => new(true, null, tipo);

    public static ResultadoValidacao Falha(string erro) => new(false, erro, null);
}
