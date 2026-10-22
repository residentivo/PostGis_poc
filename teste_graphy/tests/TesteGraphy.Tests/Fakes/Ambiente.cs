using TesteGraphy.Configuracao;

namespace TesteGraphy.Tests.Fakes;

/// <summary>Variaveis de ambiente simuladas (nunca le o ambiente real).</summary>
internal static class Ambiente
{
    public const string Sentinela = "S3nh4-Sentinela-XYZ";

    /// <summary>PG_USER e PG_PASSWORD (sentinela) definidos; demais com padrao. Sobrescritas opcionais.</summary>
    public static Func<string, string?> Variaveis(params (string Nome, string? Valor)[] sobrescritas)
    {
        var dados = new Dictionary<string, string?>(StringComparer.Ordinal)
        {
            [ConexaoConfig.VarUsuario] = "usuario_teste",
            [ConexaoConfig.VarSenha] = Sentinela,
        };

        foreach (var (nome, valor) in sobrescritas)
        {
            dados[nome] = valor;
        }

        return nome => dados.TryGetValue(nome, out var valor) ? valor : null;
    }

    public static ConexaoConfig Config(params (string Nome, string? Valor)[] sobrescritas) =>
        ConexaoConfig.Carregar(Variaveis(sobrescritas));
}
