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
        foreach (var nome in new[] { "PG_HOST", "PG_PORT", "PG_DATABASE", "PG_USER", "PG_PASSWORD", "PG_SSLMODE", "PG_RUN_INTEGRATION", "PG_TEST_ID" })
        {
            Valores[nome] = Environment.GetEnvironmentVariable(nome);
        }
    }

    public static string? Ler(string nome) => Valores.TryGetValue(nome, out var v) ? v : null;

    public static bool IntegracaoHabilitada => Ler("PG_RUN_INTEGRATION") == "1";
}
