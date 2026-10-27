using System.Runtime.CompilerServices;

namespace ApiMapas.Tests.Suporte;

/// <summary>
/// Copia das variaveis PG_* do usuario feita ANTES de qualquer teste (as fabricas da API
/// sobrescrevem o processo com valores ficticios). Usada apenas pelos testes de integracao opt-in.
/// </summary>
public static class AmbienteOriginal
{
    private static readonly Dictionary<string, string?> Valores = new(StringComparer.Ordinal);

    [ModuleInitializer]
    internal static void Capturar()
    {
        foreach (var nome in new[] { "PG_HOST", "PG_PORT", "PG_DATABASE", "PG_USER", "PG_PASSWORD", "PG_SSLMODE", "PG_RUN_INTEGRATION", "PG_TEST_ID",
            "PG_ESCRITA_USER", "PG_ESCRITA_PASSWORD", "PG_TESTE_USER", "PG_TESTE_PASSWORD" })
        {
            Valores[nome] = Environment.GetEnvironmentVariable(nome);
        }
    }

    public static string? Ler(string nome) => Valores.TryGetValue(nome, out var v) ? v : null;

    public static bool IntegracaoHabilitada => Ler("PG_RUN_INTEGRATION") == "1";

    /// <summary>
    /// Integracao da escrita (P6.5): alem de PG_RUN_INTEGRATION=1, exige a role de escrita
    /// (PG_ESCRITA_USER/PG_ESCRITA_PASSWORD) e a role mapas_teste (PG_TESTE_USER/PG_TESTE_PASSWORD),
    /// que cria e apaga os ids de teste (R-3).
    /// </summary>
    public static bool IntegracaoEscritaHabilitada =>
        IntegracaoHabilitada
        && !string.IsNullOrWhiteSpace(Ler("PG_ESCRITA_USER")) && !string.IsNullOrWhiteSpace(Ler("PG_ESCRITA_PASSWORD"))
        && !string.IsNullOrWhiteSpace(Ler("PG_TESTE_USER")) && !string.IsNullOrWhiteSpace(Ler("PG_TESTE_PASSWORD"));
}
