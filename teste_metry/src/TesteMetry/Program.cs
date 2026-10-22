using TesteMetry.Aplicacao;
using TesteMetry.Configuracao;
using TesteMetry.Dados;
using TesteMetry.Diagnostico;

// TesteMetry - programa de TESTE: insere WKT em public.mapas.metry (geometry) no SRID de PG_SRID
// (4326 padrao, 4674, 3857 ou 31983).
// Uso: dotnet run --project src/TesteMetry -- "POINT(-46.63 -23.55)" ["LINESTRING(...)" ...]
// Credenciais e SRID SOMENTE por variaveis de ambiente PG_* (R1, RQ-2). O arquivo .env NAO e carregado.
// Saida: 0 ok | 1 configuracao invalida | 2 WKT invalido/SRID divergente | 3 banco/conexao.

ConfiguracaoAplicacao configuracaoApp;
try
{
    configuracaoApp = ConfiguracaoAplicacao.Carregar(Path.Combine(AppContext.BaseDirectory, ConfiguracaoAplicacao.NomeArquivo));
}
#pragma warning disable CA1031 // F5: qualquer falha ao ler appsettings.json -> so o nome do tipo (R2).
catch (Exception ex)
#pragma warning restore CA1031
{
    configuracaoApp = new ConfiguracaoAplicacao(false, $"falha ao carregar {ConfiguracaoAplicacao.NomeArquivo} ({ex.GetType().Name}); modo verboso desligado");
}

var log = new ConsoleLog(configuracaoApp.Verboso, Console.Error);
if (configuracaoApp.Aviso is { } avisoConfig)
{
    Console.Error.WriteLine($"Aviso: {avisoConfig}");
}

log.Passo("Modo verboso ativo.");

using var cancelamento = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) =>
{
    e.Cancel = true;
    cancelamento.Cancel();
};

var programa = new ProgramaConsole(
    Console.Out,
    Console.Error,
    Environment.GetEnvironmentVariable,
    static (config, logRepositorio) => new MetryRepository(config, logRepositorio),
    log);

var codigo = await programa.ExecutarAsync(args, cancelamento.Token);
log.Passo($"Encerrando com codigo {codigo}.");
return codigo;
