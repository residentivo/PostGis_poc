using System.Text;
using System.Text.Json;
using ApiMapas.Dominio;
using Microsoft.Extensions.Logging;

namespace ApiMapas.Aplicacao;

/// <summary>
/// Implementa o caso de uso (A4.1). Logs de passo em Debug (ativados por "Verboso": true no
/// appsettings.json); erros sempre em Error. Nunca loga o id bruto recebido (RA-10).
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

        var itens = new List<GeometriaDto>();
        long bytesAcumulados = 0;
        var truncado = false;

        try
        {
            await foreach (var geometria in _repositorio.ListarPorIdAsync(id, coluna, cancellationToken)
                               .WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                if (itens.Count >= OpcoesConsulta.LimiteItens)
                {
                    truncado = true;
                    LogLimiteItens(OpcoesConsulta.LimiteItens, id.Valor, nomeColuna);
                    break;
                }

                var tamanho = (long)Encoding.UTF8.GetByteCount(geometria.Wkt) + Encoding.UTF8.GetByteCount(geometria.GeoJson);
                if (bytesAcumulados + tamanho > _limiteBytes)
                {
                    truncado = true;
                    LogLimiteBytes(_limiteBytes, itens.Count, id.Valor, nomeColuna);
                    break;
                }

                bytesAcumulados += tamanho;
                itens.Add(Mapear(geometria, id, nomeColuna));
            }
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

        if (itens.Count == 0 && !truncado)
        {
            LogNaoEncontrado(id.Valor, nomeColuna);
            return new ResultadoConsulta.NaoEncontrado(id.Valor, nomeColuna);
        }

        LogDevolvendo(itens.Count, bytesAcumulados, truncado, id.Valor, nomeColuna);

        return new ResultadoConsulta.Sucesso(new RespostaGeometriasDto(id.Valor, nomeColuna, itens.Count, truncado, itens));
    }

    /// <summary>
    /// Converte a linha do banco no DTO. O GeoJSON vira objeto via JsonDocument.Parse; o
    /// elemento e clonado para sobreviver ao dispose do documento (RA-13).
    /// </summary>
    internal static GeometriaDto Mapear(GeometriaMapa geometria, IdMapa id, string nomeColuna)
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

        return new GeometriaDto(
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

    [LoggerMessage(EventId = 1004, Level = LogLevel.Debug, Message = "Mais de {Limite} geometrias para o id {Id} na coluna {Coluna}; resposta truncada.")]
    private partial void LogLimiteItens(int limite, int id, string coluna);

    [LoggerMessage(EventId = 1005, Level = LogLevel.Warning, Message = "Teto de {LimiteBytes} bytes (wkt + geojson) atingido no item {Indice} do id {Id} na coluna {Coluna}; resposta truncada.")]
    private partial void LogLimiteBytes(long limiteBytes, int indice, int id, string coluna);

    [LoggerMessage(EventId = 1006, Level = LogLevel.Debug, Message = "Consulta do id {Id} na coluna {Coluna} cancelada pelo cliente.")]
    private partial void LogCancelada(int id, string coluna);

    [LoggerMessage(EventId = 1007, Level = LogLevel.Error, Message = "Falha ao consultar o id {Id} na coluna {Coluna} ({TipoErro}).")]
    private partial void LogFalha(int id, string coluna, string tipoErro);

    [LoggerMessage(EventId = 1008, Level = LogLevel.Debug, Message = "Nenhuma geometria nao nula para o id {Id} na coluna {Coluna}.")]
    private partial void LogNaoEncontrado(int id, string coluna);

    [LoggerMessage(EventId = 1009, Level = LogLevel.Debug, Message = "Devolvendo {Quantidade} geometria(s) ({Bytes} bytes, truncado={Truncado}) para o id {Id} na coluna {Coluna}.")]
    private partial void LogDevolvendo(int quantidade, long bytes, bool truncado, int id, string coluna);
}
