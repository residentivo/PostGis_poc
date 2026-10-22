using System.Globalization;
using TesteGraphy.Comum;
using TesteGraphy.Dados;
using TesteGraphy.Diagnostico;
using TesteGraphy.Validacao;

namespace TesteGraphy.Aplicacao;

/// <summary>
/// Caso de uso: para cada WKT -> valida local -> valida no banco (ST_IsValid) ->
/// insere -> le de volta e confere SRID 4326. Um WKT invalido nao impede os demais;
/// uma falha de CONEXAO interrompe os itens restantes (marcados como NaoProcessado).
/// Maximo de <see cref="MaximoWktPorExecucao"/> WKT por execucao (R4).
/// </summary>
public sealed class InsercaoService
{
    public const int MaximoWktPorExecucao = 100;
    public const int SridEsperado = 4326;

    private readonly IGraphyRepository _repositorio;
    private readonly WktValidator _validador;
    private readonly ILog _log;

    public InsercaoService(IGraphyRepository repositorio, WktValidator validador, ILog? log = null)
    {
        ArgumentNullException.ThrowIfNull(repositorio);
        ArgumentNullException.ThrowIfNull(validador);
        _repositorio = repositorio;
        _validador = validador;
        _log = log ?? LogNulo.Instancia;
    }

    public async Task<ResultadoExecucao> ExecutarAsync(IReadOnlyList<string?> wkts, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(wkts);

        _log.Passo($"Iniciando processamento de {wkts.Count} WKT(s).");

        if (wkts.Count > MaximoWktPorExecucao)
        {
            _log.Erro($"Quantidade de WKT ({wkts.Count}) excede o maximo de {MaximoWktPorExecucao} por execucao; nada foi inserido.");
            return new ResultadoExecucao([], LimiteExcedido: true, CodigoSaida.WktInvalido);
        }

        var itens = new List<ResultadoItem>(wkts.Count);
        var conexaoFalhou = false;

        for (var i = 0; i < wkts.Count; i++)
        {
            var indice = i + 1;
            var wkt = wkts[i];
            var eco = TextoSeguro.EcoWkt(wkt);

            if (conexaoFalhou)
            {
                _log.Passo($"Item {indice}: nao processado (conexao falhou anteriormente).");
                itens.Add(new ResultadoItem(indice, eco, StatusItem.NaoProcessado, Erro: "nao processado: falha de conexao em item anterior"));
                continue;
            }

            var item = await ProcessarItemAsync(indice, wkt, eco, cancellationToken).ConfigureAwait(false);
            itens.Add(item);
            conexaoFalhou = item.Status == StatusItem.ErroConexao;
        }

        var codigo = CalcularCodigo(itens);
        _log.Passo($"Processamento concluido: {itens.Count(x => x.Status == StatusItem.Inserido)} inserido(s), codigo de saida {(int)codigo}.");
        return new ResultadoExecucao(itens, LimiteExcedido: false, codigo);
    }

    private async Task<ResultadoItem> ProcessarItemAsync(int indice, string? wkt, string eco, CancellationToken ct)
    {
        _log.Passo($"Item {indice}: validacao local.");
        var local = _validador.Validar(wkt);
        if (!local.Valido || wkt is null)
        {
            var erro = local.Erro ?? "WKT nulo";
            _log.Erro($"Item {indice}: WKT invalido (local): {erro}.");
            return new ResultadoItem(indice, eco, StatusItem.InvalidoLocal, Erro: erro);
        }

        int? idInserido = null;
        try
        {
            _log.Passo($"Item {indice}: validacao no banco (ST_IsValid).");
            var noBanco = await _repositorio.ValidarNoBancoAsync(wkt, ct).ConfigureAwait(false);
            if (!noBanco.Valido)
            {
                const string erro = "geometria invalida segundo ST_IsValid; nao inserida";
                _log.Erro($"Item {indice}: {erro}.");
                return new ResultadoItem(indice, eco, StatusItem.InvalidoBanco, Erro: erro);
            }

            _log.Passo($"Item {indice}: inserindo.");
            idInserido = await _repositorio.InserirAsync(wkt, ct).ConfigureAwait(false);

            _log.Passo($"Item {indice}: lendo de volta id = {idInserido.Value.ToString(CultureInfo.InvariantCulture)}.");
            var registro = await _repositorio.LerAsync(idInserido.Value, ct).ConfigureAwait(false);
            if (registro is null)
            {
                var erro = $"registro id = {idInserido.Value.ToString(CultureInfo.InvariantCulture)} nao encontrado na leitura de conferencia";
                _log.Erro($"Item {indice}: {erro}.");
                return new ResultadoItem(indice, eco, StatusItem.ErroBanco, IdInserido: idInserido, Erro: erro);
            }

            if (registro.Srid != SridEsperado)
            {
                var erro = $"SRID lido = {registro.Srid.ToString(CultureInfo.InvariantCulture)}, esperado {SridEsperado}";
                _log.Erro($"Item {indice}: {erro}.");
                return new ResultadoItem(indice, eco, StatusItem.ErroBanco, registro, idInserido, erro);
            }

            _log.Passo($"Item {indice}: inserido e conferido.");
            return new ResultadoItem(indice, eco, StatusItem.Inserido, registro, idInserido);
        }
        catch (FalhaBancoException ex)
        {
            var erro = FormatarErroBanco(ex);
            _log.Erro($"Item {indice}: erro do banco: {erro}.");
            return new ResultadoItem(indice, eco, StatusItem.ErroBanco, IdInserido: idInserido, Erro: erro);
        }
        catch (FalhaConexaoException ex)
        {
            _log.Erro($"Item {indice}: falha de conexao ({ex.TipoExcecao}).");
            return new ResultadoItem(indice, eco, StatusItem.ErroConexao, IdInserido: idInserido, Erro: ex.TipoExcecao);
        }
    }

    /// <summary>Somente SqlState + MessageText, sanitizados (R2).</summary>
    public static string FormatarErroBanco(FalhaBancoException ex)
    {
        ArgumentNullException.ThrowIfNull(ex);
        var mensagem = TextoSeguro.Sanitizar(ex.MensagemServidor);
        return ex.SqlState is null ? mensagem : $"SqlState {TextoSeguro.Sanitizar(ex.SqlState)}: {mensagem}";
    }

    private static CodigoSaida CalcularCodigo(IReadOnlyCollection<ResultadoItem> itens)
    {
        if (itens.Any(x => x.Status is StatusItem.ErroBanco or StatusItem.ErroConexao or StatusItem.NaoProcessado))
        {
            return CodigoSaida.FalhaBanco;
        }

        if (itens.Any(x => x.Status is StatusItem.InvalidoLocal or StatusItem.InvalidoBanco))
        {
            return CodigoSaida.WktInvalido;
        }

        return CodigoSaida.Ok;
    }
}
