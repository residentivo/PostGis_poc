namespace ApiMapas.Api.Inicializacao;

/// <summary>Logs estruturados da inicializacao e do pipeline (RA-9/RA-10: sem credenciais).</summary>
internal static partial class LogsInicializacao
{
    [LoggerMessage(EventId = 4001, Level = LogLevel.Information, Message = "ApiMapas iniciando: banco {Destino}; verboso={Verboso}; limite {LimiteBytes} bytes por resposta; {LimiteRequisicoes} req/min por IP.")]
    public static partial void Iniciando(ILogger log, string destino, bool verboso, long limiteBytes, int limiteRequisicoes);

    [LoggerMessage(EventId = 4002, Level = LogLevel.Warning, Message = "ApiMapas NAO tem autenticacao: uso somente local (loopback).")]
    public static partial void SemAutenticacao(ILogger log);

    [LoggerMessage(EventId = 4003, Level = LogLevel.Warning, Message = "{Variavel} efetivo = {SslMode}: a conexao com o banco pode trafegar SEM criptografia e sem verificacao do servidor. Recomendado: Require, VerifyCA ou VerifyFull.")]
    public static partial void SslInseguro(ILogger log, string variavel, string sslMode);

    [LoggerMessage(EventId = 4004, Level = LogLevel.Debug, Message = "Pipeline configurado: rotas {RotaGraphy} e {RotaMetry}.")]
    public static partial void PipelineConfigurado(ILogger log, string rotaGraphy, string rotaMetry);

    [LoggerMessage(EventId = 4005, Level = LogLevel.Warning, Message = "Requisicao recusada pelo rate limiting ({Limite} req/min por IP).")]
    public static partial void RateLimitRecusou(ILogger log, int limite);
}
