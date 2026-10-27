using System.Text;
using System.Text.Json;
using ApiMapas.Dominio;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Aplicacao;

/// <summary>
/// Caso de uso do GET com UMA geometria por id (D-N7, RS-23). Logs de passo em Debug (ativados por
/// "Verboso": true no appsettings.json); erros sempre em Error. Nunca loga o id bruto recebido
/// (RA-10) nem o WKT.
/// </summary>
public sealed partial class ConsultaGeometriasService : IConsultaGeometriasService
{
    private readonly IMapasRepository _repositorio;
    private readonly ILogger<ConsultaGeometriasService> _log;
    private readonly long _limiteBytes;

    public ConsultaGeometriasService(IMapasRepository repositorio, OpcoesConsulta opcoes, ILogger<ConsultaGeometriasService> log)
    {
        ArgumentNullException.ThrowIfNull(repositorio);
        ArgumentNullException.ThrowIfNull(opcoes);
        ArgumentNullException.ThrowIfNull(log);

        if (opcoes.LimiteBytesGeometrias <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(opcoes), "LimiteBytesGeometrias deve ser maior que zero");
        }

        _repositorio = repositorio;
        _log = log;
        _limiteBytes = opcoes.LimiteBytesGeometrias;
    }

    public async Task<ResultadoConsulta> ConsultarAsync(string? idTexto, ColunaMapa coluna, CancellationToken cancellationToken)
    {
        var nomeColuna = coluna.Nome();
        LogValidandoId(nomeColuna);

        if (!IdMapa.TentarCriar(idTexto, out var id))
        {
            // RA-10: nunca o valor bruto, so o tamanho.
            LogIdInvalido(nomeColuna, idTexto?.Length ?? 0);
            return ResultadoConsulta.IdInvalido.Instancia;
        }

        LogIdValido(id.Valor, nomeColuna);

        LeituraMapa leitura;
        try
        {
            leitura = await _repositorio.ObterPorIdAsync(id, coluna, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            LogCancelada(id.Valor, nomeColuna);
            throw;
        }
        catch (Exception ex)
        {
            // Sem a excecao: o stack trace completo e logado so no manipulador global (com traceId).
            LogFalha(id.Valor, nomeColuna, ex.GetType().Name);
            throw;
        }

        switch (leitura)
        {
            case LeituraMapa.NaoEncontrada:
                LogSemLinha(id.Valor, nomeColuna);
                return new ResultadoConsulta.NaoEncontrado(id.Valor, nomeColuna);

            case LeituraMapa.ColunaVazia:
                LogColunaVazia(id.Valor, nomeColuna);
                return new ResultadoConsulta.NaoEncontrado(id.Valor, nomeColuna);

            case LeituraMapa.Duplicada:
                // RS-23: sem a PK o id pode ter varias linhas; a resposta e 500 generico.
                LogDuplicada(id.Valor, nomeColuna);
                throw new LeituraInconsistenteException(
                    $"O id {id} tem mais de uma linha em public.mapas (coluna {nomeColuna}); migracao para PRIMARY KEY nao aplicada.");

            case LeituraMapa.Encontrada encontrada:
                var geometria = encontrada.Geometria;
                var tamanho = (long)Encoding.UTF8.GetByteCount(geometria.Wkt) + Encoding.UTF8.GetByteCount(geometria.GeoJson);
                if (tamanho > _limiteBytes)
                {
                    // D-N7: so com dado inserido fora da API (a escrita limita o WKT a 10.000 caracteres).
                    LogLimiteBytes(_limiteBytes, tamanho, id.Valor, nomeColuna);
                    throw new LeituraInconsistenteException(
                        $"A geometria do id {id} na coluna {nomeColuna} passa do teto de {_limiteBytes} bytes.");
                }

                RespostaGeometriaDto resposta;
                try
                {
                    resposta = Mapear(geometria, id, nomeColuna);
                }
                catch (InvalidOperationException ex)
                {
                    // GeoJSON invalido devolvido pelo banco: so o tipo vai ao log.
                    LogFalha(id.Valor, nomeColuna, ex.GetType().Name);
                    throw;
                }

                LogDevolvendo(tamanho, id.Valor, nomeColuna);
                return new ResultadoConsulta.Sucesso(resposta);

            default:
                throw new InvalidOperationException("Leitura nao suportada.");
        }
    }

    /// <summary>
    /// Converte a linha do banco no DTO. O GeoJSON vira objeto via JsonDocument.Parse; o
    /// elemento e clonado para sobreviver ao dispose do documento (RA-13).
    /// </summary>
    internal static RespostaGeometriaDto Mapear(GeometriaMapa geometria, IdMapa id, string nomeColuna)
    {
        JsonElement geojson;
        try
        {
            using var documento = JsonDocument.Parse(geometria.GeoJson);
            geojson = documento.RootElement.Clone();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"GeoJSON invalido devolvido pelo banco (id {id.Valor}, coluna {nomeColuna}).", ex);
        }

        return new RespostaGeometriaDto(
            id.Valor,
            nomeColuna,
            geometria.Tipo,
            geometria.Srid,
            geometria.Wkt,
            geojson,
            geometria.X,
            geometria.Y,
            geometria.Longitude,
            geometria.Latitude);
    }

    // ---- Logs estruturados (RA-10). Debug = passos (so com "Verboso": true). ----

    [LoggerMessage(EventId = 1001, Level = LogLevel.Debug, Message = "Validando o id recebido para a coluna {Coluna}.")]
    private partial void LogValidandoId(string coluna);

    [LoggerMessage(EventId = 1002, Level = LogLevel.Information, Message = "Id invalido recebido para a coluna {Coluna} (tamanho {Tamanho}); banco nao consultado.")]
    private partial void LogIdInvalido(string coluna, int tamanho);

    [LoggerMessage(EventId = 1003, Level = LogLevel.Debug, Message = "Id {Id} valido; consultando a coluna {Coluna}.")]
    private partial void LogIdValido(int id, string coluna);

    [LoggerMessage(EventId = 1004, Level = LogLevel.Error, Message = "O id {Id} tem mais de uma linha em public.mapas (coluna {Coluna}): a migracao para PRIMARY KEY nao foi aplicada; resposta 500.")]
    private partial void LogDuplicada(int id, string coluna);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "A geometria do id {Id} na coluna {Coluna} tem {Bytes} bytes (wkt + geojson), acima do teto de {LimiteBytes}; resposta 500.")]
    private partial void LogLimiteBytes(long limiteBytes, long bytes, int id, string coluna);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Consulta do id {Id} na coluna {Coluna} cancelada pelo cliente.")]
    private partial void LogCancelada(int id, string coluna);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Error, Message = "Falha ao consultar o id {Id} na coluna {Coluna} ({TipoErro}).")]
    private partial void LogFalha(int id, string coluna, string tipoErro);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Debug, Message = "Nenhuma linha para o id {Id} (coluna {Coluna}).")]
    private partial void LogSemLinha(int id, string coluna);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug, Message = "Devolvendo a geometria ({Bytes} bytes) do id {Id} na coluna {Coluna}.")]
    private partial void LogDevolvendo(long bytes, int id, string coluna);

    [LoggerMessage(EventId = 1010, Level = LogLevel.Debug, Message = "O id {Id} existe, mas a coluna {Coluna} esta NULL.")]
    private partial void LogColunaVazia(int id, string coluna);
}
