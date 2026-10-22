using TesteGraphy.Aplicacao;
using TesteGraphy.Configuracao;
using TesteGraphy.Tests.Fakes;

namespace TesteGraphy.Tests.Aplicacao;

[CollectionDefinition(Nome, DisableParallelization = true)]
public sealed class ColecaoConsole
{
    public const string Nome = "Console global";
}

/// <summary>
/// Executa o Program.cs (top-level) via ponto de entrada do assembly. So roda quando
/// PG_USER e PG_PASSWORD NAO estao definidos no ambiente: o programa encerra com codigo 1
/// (configuracao invalida) antes de qualquer tentativa de conexao.
/// </summary>
[Collection(ColecaoConsole.Nome)]
public class ProgramEntryPointTests
{
    private static bool SemCredenciais =>
        string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ConexaoConfig.VarUsuario))
        && string.IsNullOrEmpty(Environment.GetEnvironmentVariable(ConexaoConfig.VarSenha));

    [Theory]
    [InlineData(null)]
    [InlineData("POINT(0 0)")]
    public void Main_SemCredenciais_RetornaConfiguracaoInvalida(string? wkt)
    {
        if (!SemCredenciais)
        {
            return; // nunca conectar a partir de teste unitario
        }

        var ponto = typeof(ProgramaConsole).Assembly.EntryPoint!;
        var saidaOriginal = Console.Out;
        var erroOriginal = Console.Error;
        using var saida = new StringWriter();
        using var erro = new StringWriter();
        Console.SetOut(saida);
        Console.SetError(erro);
        try
        {
            string[] args = wkt is null ? [] : [wkt];
            var retorno = ponto.Invoke(null, [args]);

            Assert.Equal((int)CodigoSaida.ConfiguracaoInvalida, Assert.IsType<int>(retorno));
            Assert.Contains("TesteGraphy - insercao em public.mapas.graphy", saida.ToString(), StringComparison.Ordinal);
            Assert.Contains("Configuracao invalida: Variavel de ambiente PG_USER", erro.ToString(), StringComparison.Ordinal);
            Assert.DoesNotContain(Ambiente.Sentinela, saida + erro.ToString(), StringComparison.Ordinal);
        }
        finally
        {
            Console.SetOut(saidaOriginal);
            Console.SetError(erroOriginal);
        }
    }
}
