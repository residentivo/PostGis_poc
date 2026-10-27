using ApiMapas.Dominio;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Aplicacao;

/// <summary>
/// Caso de uso do POST /api/mapas/graphy|metry (D-N5): cria uma linha nova com o id da identity,
/// preenchendo so a coluna da rota. Ordem do contrato (D-N9): Content-Type e tamanho (pelo
/// <see cref="ILeitorCorpo"/> da API), JSON (RP-11), geometria (RP-14) e banco (W3/W4: INSERT ...
/// RETURNING id). Nao e idempotente (RS-5). O prazo total (RP-4) cobre todas as etapas; o estouro
/// vira <see cref="PrazoAtualizacaoEstouradoException"/> (500). Logs de passo em Debug, ativados por
/// "Verboso": true; EventIds proprios do POST (42xx, RS-4); nunca WKT, corpo ou JsonException.
/// </summary>
public sealed partial class CriacaoGeometriasService : ICriacaoGeometriasService
{
    private readonly IMapasEscritaRepository _repositorio;
    private readonly ILogger<CriacaoGeometriasService> _log;
    private readonly TimeSpan _prazoTotal;

    public CriacaoGeometriasService(
        IMapasEscritaRepository repositorio,
        OpcoesAtualizacao opcoes,
        ILogger<CriacaoGeometriasService> log)
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

    public async Task<ResultadoCriacao> CriarAsync(
        ColunaMapa coluna,
        ILeitorCorpo leitorCorpo,
        string traceId,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(leitorCorpo);
        var nomeColuna = coluna.Nome();
        traceId ??= string.Empty;

        // RP-4 / D-P11: prazo total ligado ao RequestAborted.
        using var prazo = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        prazo.CancelAfter(_prazoTotal);
        var token = prazo.Token;

        try
        {
            // 1 a 4. Content-Type (415), tamanho (413), JSON (400) e geometria (400).
            LogLendoCorpo(nomeColuna);
            var entrada = await EntradaGeometria.LerAsync(leitorCorpo, coluna, token).ConfigureAwait(false);
            switch (entrada)
            {
                case EntradaGeometria.Etapa.TipoMidia:
                    LogTipoMidia(nomeColuna);
                    return ResultadoCriacao.TipoMidiaNaoSuportado.Instancia;

                case EntradaGeometria.Etapa.MuitoGrande muitoGrande:
                    LogCorpoGrande(nomeColuna, muitoGrande.Limite);
                    return new ResultadoCriacao.CorpoMuitoGrande(muitoGrande.Limite);

                case EntradaGeometria.Etapa.LeituraInterrompida interrompida:
                    LogLeituraInterrompida(nomeColuna, interrompida.StatusCode);
                    return ResultadoCriacao.CorpoInvalido.Instancia;

                case EntradaGeometria.Etapa.JsonInvalido:
                    LogCorpoInvalido(nomeColuna);
                    return ResultadoCriacao.CorpoInvalido.Instancia;

                case EntradaGeometria.Etapa.GeometriaInvalida invalidaLocal:
                    LogGeometriaInvalida(nomeColuna);
                    return new ResultadoCriacao.GeometriaInvalida(invalidaLocal.Motivo);
            }

            var geometria = ((EntradaGeometria.Etapa.Valida)entrada).Geometria;

            // 5. Banco: validacao (400), INSERT ... RETURNING id (201/409/500).
            LogGravando(nomeColuna);
            var resultado = await _repositorio.CriarAsync(coluna, geometria, token).ConfigureAwait(false);

            switch (resultado)
            {
                case ResultadoEscrita.Criado criado:
                    LogAuditoria(criado.Id, nomeColuna, traceId);
                    return new ResultadoCriacao.Sucesso(criado.Id, nomeColuna);

                case ResultadoEscrita.GeometriaInvalida invalida:
                    LogGeometriaInvalida(nomeColuna);
                    return new ResultadoCriacao.GeometriaInvalida(invalida.Motivo);

                case ResultadoEscrita.Conflito:
                    LogConflito(nomeColuna);
                    return new ResultadoCriacao.Conflito(nomeColuna);

                default:
                    // Atualizado/NaoEncontrado nunca vem do INSERT.
                    LogResultadoInesperado(nomeColuna, resultado?.GetType().Name ?? "null");
                    throw new InvalidOperationException("Resultado de escrita nao suportado no POST.");
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Cliente desconectou: o repositorio ja fez ROLLBACK; o ManipuladorExcecoes responde 499.
            LogCancelada(nomeColuna);
            throw;
        }
        catch (OperationCanceledException) when (prazo.IsCancellationRequested)
        {
            // D-P11: prazo de 30 s estourado; ROLLBACK ja feito; 500 generico.
            LogPrazoEstourado(nomeColuna, (int)_prazoTotal.TotalSeconds);
            throw new PrazoAtualizacaoEstouradoException(
                $"Prazo total de {(int)_prazoTotal.TotalSeconds} s do POST estourado (coluna {nomeColuna}).");
        }
    }

    // ---- Logs estruturados do POST (RS-4): so metodo, coluna e id gerado. Nunca WKT nem corpo. ----

    [LoggerMessage(EventId = 4201, Level = LogLevel.Debug, Message = "POST: lendo o corpo para a coluna {Coluna}.")]
    private partial void LogLendoCorpo(string coluna);

    [LoggerMessage(EventId = 4202, Level = LogLevel.Information, Message = "POST: Content-Type nao suportado para a coluna {Coluna} (415).")]
    private partial void LogTipoMidia(string coluna);

    [LoggerMessage(EventId = 4203, Level = LogLevel.Information, Message = "POST: corpo acima de {Limite} bytes para a coluna {Coluna} (413).")]
    private partial void LogCorpoGrande(string coluna, long limite);

    [LoggerMessage(EventId = 4204, Level = LogLevel.Information, Message = "POST: corpo invalido (JSON fora do formato) para a coluna {Coluna}.")]
    private partial void LogCorpoInvalido(string coluna);

    [LoggerMessage(EventId = 4205, Level = LogLevel.Information, Message = "POST: geometria rejeitada para a coluna {Coluna} (400 Geometria invalida).")]
    private partial void LogGeometriaInvalida(string coluna);

    [LoggerMessage(EventId = 4206, Level = LogLevel.Debug, Message = "POST: gravando a geometria na coluna {Coluna} (INSERT ... RETURNING id).")]
    private partial void LogGravando(string coluna);

    [LoggerMessage(EventId = 4207, Level = LogLevel.Information, Message = "Auditoria POST: id {Id} criado com a coluna {Coluna} preenchida (traceId {TraceId}).")]
    private partial void LogAuditoria(int id, string coluna, string traceId);

    [LoggerMessage(EventId = 4208, Level = LogLevel.Warning, Message = "POST: escrita concorrente na coluna {Coluna} (lock nao obtido em 5 s ou deadlock); ROLLBACK, 409.")]
    private partial void LogConflito(string coluna);

    [LoggerMessage(EventId = 4209, Level = LogLevel.Information, Message = "POST na coluna {Coluna} cancelado pelo cliente.")]
    private partial void LogCancelada(string coluna);

    [LoggerMessage(EventId = 4210, Level = LogLevel.Error, Message = "POST na coluna {Coluna}: prazo total de {Segundos} s estourado; ROLLBACK.")]
    private partial void LogPrazoEstourado(string coluna, int segundos);

    [LoggerMessage(EventId = 4211, Level = LogLevel.Information, Message = "POST: leitura do corpo interrompida pelo servidor (StatusCode {StatusCode}) para a coluna {Coluna}; 400.")]
    private partial void LogLeituraInterrompida(string coluna, int statusCode);

    [LoggerMessage(EventId = 4212, Level = LogLevel.Error, Message = "POST na coluna {Coluna}: resultado de escrita inesperado ({Tipo}).")]
    private partial void LogResultadoInesperado(string coluna, string tipo);
}
