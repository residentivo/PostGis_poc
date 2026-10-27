using ApiMapas.Dominio;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Aplicacao;

/// <summary>
/// Caso de uso do PUT /api/mapas/{id}/graphy|metry com UMA geometria por id (D-N6). Ordem do
/// contrato (D-N9): id (sem corpo e sem banco), Content-Type e tamanho (pelo <see cref="ILeitorCorpo"/>
/// da API), JSON (RP-11), geometria (RP-14) e banco (W1/W2: UPDATE pela PK). O prazo total (RP-4)
/// cobre todas as etapas; o estouro vira <see cref="PrazoAtualizacaoEstouradoException"/> (500,
/// D-P11). Logs de passo em Debug, ativados por "Verboso": true; falhas sem WKT, sem corpo e sem
/// JsonException (RP-10/RP-13).
/// </summary>
public sealed partial class AtualizacaoGeometriasService : IAtualizacaoGeometriasService
{
    private readonly IMapasEscritaRepository _repositorio;
    private readonly ILogger<AtualizacaoGeometriasService> _log;
    private readonly TimeSpan _prazoTotal;

    public AtualizacaoGeometriasService(
        IMapasEscritaRepository repositorio,
        OpcoesAtualizacao opcoes,
        ILogger<AtualizacaoGeometriasService> log)
    {
        ArgumentNullException.ThrowIfNull(repositorio);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(log);

        if (opcoes.PrazoTotal <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(opcoes), "PrazoTotal deve ser maior que zero");
        }

        _repositorio = repositorio;
        _log = log;
        _prazoTotal = opcoes.PrazoTotal;
    }

    public async Task<ResultadoAtualizacao> AtualizarAsync(
        string? idTexto,
        ColunaMapa coluna,
        ILeitorCorpo leitorCorpo,
        string traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leitorCorpo);
        var nomeColuna = coluna.Nome();
        traceId ??= string.Empty;

        // 1. Id (400 sem ler o corpo e sem banco).
        LogValidandoId(nomeColuna);
        if (!IdMapa.TentarCriar(idTexto, out var id))
        {
            LogIdInvalido(nomeColuna, idTexto?.Length ?? 0);
            return ResultadoAtualizacao.IdInvalido.Instancia;
        }

        // RP-4 / D-P11: prazo total ligado ao RequestAborted.
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prazo.CancelAfter(_prazoTotal);
        var token = prazo.Token;

        try
        {
            // 2 a 5. Content-Type (415), tamanho (413), JSON (400) e geometria (400).
            LogLendoCorpo(id.Valor, nomeColuna);
            var entrada = await EntradaGeometria.LerAsync(leitorCorpo, coluna, token).ConfigureAwait(false);
            switch (entrada)
            {
                case EntradaGeometria.Etapa.TipoMidia:
                    LogTipoMidia(id.Valor, nomeColuna);
                    return ResultadoAtualizacao.TipoMidiaNaoSuportado.Instancia;

                case EntradaGeometria.Etapa.MuitoGrande muitoGrande:
                    LogCorpoGrande(id.Valor, nomeColuna, muitoGrande.Limite);
                    return new ResultadoAtualizacao.CorpoMuitoGrande(muitoGrande.Limite);

                case EntradaGeometria.Etapa.LeituraInterrompida interrompida:
                    // C5: so o StatusCode; nada da excecao.
                    LogLeituraInterrompida(id.Valor, nomeColuna, interrompida.StatusCode);
                    return ResultadoAtualizacao.CorpoInvalido.Instancia;

                case EntradaGeometria.Etapa.JsonInvalido:
                    LogCorpoInvalido(id.Valor, nomeColuna);
                    return ResultadoAtualizacao.CorpoInvalido.Instancia;

                case EntradaGeometria.Etapa.GeometriaInvalida invalidaLocal:
                    LogGeometriaInvalida(id.Valor, nomeColuna);
                    return new ResultadoAtualizacao.GeometriaInvalida(invalidaLocal.Motivo);
            }

            var geometria = ((EntradaGeometria.Etapa.Valida)entrada).Geometria;

            // 6. Banco: validacao (400), UPDATE pela PK (404/409/204/500).
            LogGravando(id.Valor, nomeColuna);
            var resultado = await _repositorio.AtualizarAsync(id, coluna, geometria, token).ConfigureAwait(false);

            switch (resultado)
            {
                case ResultadoEscrita.Atualizado:
                    LogAuditoria(id.Valor, nomeColuna, traceId);
                    return new ResultadoAtualizacao.Sucesso(id.Valor, nomeColuna);

                case ResultadoEscrita.GeometriaInvalida invalida:
                    LogGeometriaInvalida(id.Valor, nomeColuna);
                    return new ResultadoAtualizacao.GeometriaInvalida(invalida.Motivo);

                case ResultadoEscrita.NaoEncontrado:
                    LogNaoEncontrado(id.Valor, nomeColuna);
                    return new ResultadoAtualizacao.NaoEncontrado(id.Valor, nomeColuna);

                case ResultadoEscrita.Conflito:
                    LogConflito(id.Valor, nomeColuna);
                    return new ResultadoAtualizacao.Conflito(id.Valor, nomeColuna);

                default:
                    // ResultadoEscrita.Criado nunca vem do UPDATE.
                    LogResultadoInesperado(id.Valor, nomeColuna, resultado?.GetType().Name ?? "null");
                    throw new InvalidOperationException("Resultado de escrita nao suportado no PUT.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cliente desconectou: o repositorio ja fez ROLLBACK; o ManipuladorExcecoes responde 499.
            LogCancelada(id.Valor, nomeColuna);
            throw;
        }
        catch (OperationCanceledException) when (prazo.IsCancellationRequested)
        {
            // D-P11: prazo de 30 s estourado; ROLLBACK ja feito; 500 generico.
            LogPrazoEstourado(id.Valor, nomeColuna, (int)_prazoTotal.TotalSeconds);
            throw new PrazoAtualizacaoEstouradoException(
                $"Prazo total de {(int)_prazoTotal.TotalSeconds} s do PUT estourado (id {id}, coluna {nomeColuna}).");
        }
    }

    // ---- Logs estruturados (RP-10/RP-13, RS-4): so metodo, id e coluna. Nunca WKT nem corpo. ----

    [LoggerMessage(EventId = 4101, Level = LogLevel.Debug, Message = "PUT: validando o id recebido para a coluna {Coluna}.")]
    private partial void LogValidandoId(string coluna);

    [LoggerMessage(EventId = 4102, Level = LogLevel.Information, Message = "PUT: id invalido recebido para a coluna {Coluna} (tamanho {Tamanho}); corpo nao lido e banco nao acessado.")]
    private partial void LogIdInvalido(string coluna, int tamanho);

    [LoggerMessage(EventId = 4103, Level = LogLevel.Debug, Message = "PUT: lendo o corpo do id {Id} na coluna {Coluna}.")]
    private partial void LogLendoCorpo(int id, string coluna);

    [LoggerMessage(EventId = 4104, Level = LogLevel.Information, Message = "PUT: Content-Type nao suportado para o id {Id} na coluna {Coluna} (415).")]
    private partial void LogTipoMidia(int id, string coluna);

    [LoggerMessage(EventId = 4105, Level = LogLevel.Information, Message = "PUT: corpo acima de {Limite} bytes para o id {Id} na coluna {Coluna} (413).")]
    private partial void LogCorpoGrande(int id, string coluna, long limite);

    [LoggerMessage(EventId = 4106, Level = LogLevel.Information, Message = "PUT: corpo invalido (JSON fora do formato) para o id {Id} na coluna {Coluna}.")]
    private partial void LogCorpoInvalido(int id, string coluna);

    [LoggerMessage(EventId = 4107, Level = LogLevel.Information, Message = "PUT: geometria rejeitada para o id {Id} na coluna {Coluna} (400 Geometria invalida).")]
    private partial void LogGeometriaInvalida(int id, string coluna);

    [LoggerMessage(EventId = 4109, Level = LogLevel.Debug, Message = "PUT: gravando a geometria do id {Id} na coluna {Coluna} (UPDATE pela PK).")]
    private partial void LogGravando(int id, string coluna);

    [LoggerMessage(EventId = 4110, Level = LogLevel.Information, Message = "Auditoria PUT: id {Id}, coluna {Coluna} atualizada (traceId {TraceId}).")]
    private partial void LogAuditoria(int id, string coluna, string traceId);

    [LoggerMessage(EventId = 4111, Level = LogLevel.Information, Message = "PUT: id {Id} sem linha em public.mapas (coluna {Coluna}); nada gravado.")]
    private partial void LogNaoEncontrado(int id, string coluna);

    [LoggerMessage(EventId = 4112, Level = LogLevel.Warning, Message = "PUT: atualizacao concorrente do id {Id} (coluna {Coluna}); lock nao obtido em 5 s.")]
    private partial void LogConflito(int id, string coluna);

    [LoggerMessage(EventId = 4113, Level = LogLevel.Information, Message = "PUT do id {Id} na coluna {Coluna} cancelado pelo cliente.")]
    private partial void LogCancelada(int id, string coluna);

    [LoggerMessage(EventId = 4114, Level = LogLevel.Error, Message = "PUT do id {Id} na coluna {Coluna}: prazo total de {Segundos} s estourado; ROLLBACK.")]
    private partial void LogPrazoEstourado(int id, string coluna, int segundos);

    [LoggerMessage(EventId = 4115, Level = LogLevel.Information, Message = "PUT: leitura do corpo interrompida pelo servidor (StatusCode {StatusCode}) para o id {Id} na coluna {Coluna}; 400.")]
    private partial void LogLeituraInterrompida(int id, string coluna, int statusCode);

    [LoggerMessage(EventId = 4116, Level = LogLevel.Error, Message = "PUT do id {Id} na coluna {Coluna}: resultado de escrita inesperado ({Tipo}).")]
    private partial void LogResultadoInesperado(int id, string coluna, string tipo);
}
