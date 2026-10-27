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

    [LoggerMessage(EventId = 4006, Level = LogLevel.Information, Message = "Escrita (PUT e POST) HABILITADA: banco {Destino}; limite {LimiteBytes} bytes por corpo; {LimiteEscritas} escritas (PUT e POST) por minuto por IP e na API toda; no maximo {Simultaneos} escritas simultaneas; prazo {PrazoSegundos} s.")]
    public static partial void EscritaHabilitada(ILogger log, string destino, long limiteBytes, int limiteEscritas, int simultaneos, int prazoSegundos);

    [LoggerMessage(EventId = 4007, Level = LogLevel.Warning, Message = "Escrita (PUT e POST) DESLIGADA: {Motivo}. Os PUT de graphy e metry respondem 405 e os POST respondem 404.")]
    public static partial void EscritaDesligada(ILogger log, string motivo);

    [LoggerMessage(EventId = 4008, Level = LogLevel.Warning, Message = "ApiMapas aceita ESCRITA (PUT e POST) SEM autenticacao (U-1): so vale em maquina de um unico usuario, com a API apenas em loopback e nunca exposta por proxy, tunel ou port-forward.")]
    public static partial void EscritaSemAutenticacao(ILogger log);

    [LoggerMessage(EventId = 4009, Level = LogLevel.Warning, Message = "Escrita (PUT e POST) recusada pelo rate limiting ({LimiteEscritas} escritas/min por IP ou na API toda, {Simultaneos} escritas simultaneas ou {Limite} req/min por IP).")]
    public static partial void RateLimitEscritaRecusou(ILogger log, int limiteEscritas, int simultaneos, int limite);

    [LoggerMessage(EventId = 4010, Level = LogLevel.Debug, Message = "Escrita (PUT e POST): rotas PUT {RotaGraphy} e {RotaMetry} e POST {RotaPostGraphy} e {RotaPostMetry} mapeadas.")]
    public static partial void EscritaMapeada(ILogger log, string rotaGraphy, string rotaMetry, string rotaPostGraphy, string rotaPostMetry);

    [LoggerMessage(EventId = 4011, Level = LogLevel.Warning, Message = "R-1: a checagem da role de escrita lancou {TipoErro}; escrita desligada.")]
    public static partial void VerificacaoRoleLancou(ILogger log, string tipoErro);

    [LoggerMessage(EventId = 4012, Level = LogLevel.Debug, Message = "Escrita {Metodo} recusada pelo filtro de origem (403; RS-22): {Motivo}.")]
    public static partial void OrigemRecusada(ILogger log, string metodo, string motivo);

    [LoggerMessage(EventId = 4013, Level = LogLevel.Debug, Message = "Escrita {Metodo}: filtro de origem aprovou a requisicao.")]
    public static partial void OrigemAprovada(ILogger log, string metodo);
}
