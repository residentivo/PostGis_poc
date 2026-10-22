using System.Text.Json;

namespace TesteGraphy.Configuracao;

/// <summary>
/// Configuracao NAO sensivel da aplicacao (appsettings.json). Contem APENAS a chave
/// "Verboso"; credenciais nunca sao lidas deste arquivo (R1).
/// </summary>
public sealed class ConfiguracaoAplicacao
{
    public const string NomeArquivo = "appsettings.json";
    public const string ChaveVerboso = "Verboso";

    public ConfiguracaoAplicacao(bool verboso, string? aviso = null)
    {
        Verboso = verboso;
        Aviso = aviso;
    }

    public bool Verboso { get; }

    /// <summary>Aviso gerado quando o arquivo existe mas nao pode ser interpretado.</summary>
    public string? Aviso { get; }

    /// <summary>Le o arquivo; ausente ou invalido -> verboso desligado (com aviso se invalido).</summary>
    public static ConfiguracaoAplicacao Carregar(string caminhoArquivo)
    {
        if (!File.Exists(caminhoArquivo))
        {
            return new ConfiguracaoAplicacao(false);
        }

        try
        {
            return Interpretar(File.ReadAllText(caminhoArquivo));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return new ConfiguracaoAplicacao(false, $"nao foi possivel ler {NomeArquivo} ({ex.GetType().Name}); modo verboso desligado");
        }
    }

    /// <summary>Interpreta o conteudo JSON do arquivo de configuracao.</summary>
    public static ConfiguracaoAplicacao Interpretar(string json)
    {
        try
        {
            using var documento = JsonDocument.Parse(json, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 8,
            });

            if (documento.RootElement.ValueKind == JsonValueKind.Object
                && documento.RootElement.TryGetProperty(ChaveVerboso, out var valor))
            {
                if (valor.ValueKind is JsonValueKind.True or JsonValueKind.False)
                {
                    return new ConfiguracaoAplicacao(valor.GetBoolean());
                }

                return new ConfiguracaoAplicacao(false, $"chave \"Verboso\" de {NomeArquivo} deve ser true/false; modo verboso desligado");
            }

            return new ConfiguracaoAplicacao(false);
        }
        catch (JsonException)
        {
            return new ConfiguracaoAplicacao(false, $"{NomeArquivo} invalido; modo verboso desligado");
        }
    }
}
