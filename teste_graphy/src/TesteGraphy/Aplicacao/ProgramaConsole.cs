using System.Globalization;
using TesteGraphy.Comum;
using TesteGraphy.Configuracao;
using TesteGraphy.Dados;
using TesteGraphy.Diagnostico;
using TesteGraphy.Exemplos;
using TesteGraphy.Validacao;

namespace TesteGraphy.Aplicacao;

/// <summary>
/// Orquestracao do console (F4.7), separada de Program.cs para ser testavel.
/// Argumentos = lista de WKT (nunca senha/connection string - R1); sem argumentos usa
/// <see cref="ExemplosFixos"/>. Nunca imprime ex.ToString(), stack trace ou connection string (R2).
/// </summary>
public sealed class ProgramaConsole
{
    private readonly TextWriter _saida;
    private readonly TextWriter _saidaErro;
    private readonly Func<string, string?> _lerVariavel;
    private readonly Func<ConexaoConfig, ILog, IGraphyRepository> _fabricaRepositorio;
    private readonly ILog _log;

    public ProgramaConsole(
        TextWriter saida,
        TextWriter saidaErro,
        Func<string, string?> lerVariavel,
        Func<ConexaoConfig, ILog, IGraphyRepository> fabricaRepositorio,
        ILog log)
    {
        ArgumentNullException.ThrowIfNull(saida);
        ArgumentNullException.ThrowIfNull(saidaErro);
        ArgumentNullException.ThrowIfNull(lerVariavel);
        ArgumentNullException.ThrowIfNull(fabricaRepositorio);
        ArgumentNullException.ThrowIfNull(log);
        _saida = saida;
        _saidaErro = saidaErro;
        _lerVariavel = lerVariavel;
        _fabricaRepositorio = fabricaRepositorio;
        _log = log;
    }

    public async Task<int> ExecutarAsync(IReadOnlyList<string> args, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(args);

        _saida.WriteLine("TesteGraphy - insercao em public.mapas.graphy (SRID 4326)");

        _log.Passo("Carregando configuracao de conexao (variaveis de ambiente).");
        ConexaoConfig config;
        try
        {
            config = ConexaoConfig.Carregar(_lerVariavel, _log);
        }
        catch (ConfiguracaoInvalidaException ex)
        {
            _log.Erro($"Configuracao invalida: {ex.NomeVariavel}.");
            _saidaErro.WriteLine($"Configuracao invalida: {ex.Message}");
            return (int)CodigoSaida.ConfiguracaoInvalida;
        }

        _saida.WriteLine($"Destino: {config.Descricao()}");
        if (config.AvisoSsl is { } aviso)
        {
            _saidaErro.WriteLine(aviso);
        }

        IReadOnlyList<string?> wkts = args.Count == 0 ? ExemplosFixos.Todos : args;
        _saida.WriteLine(args.Count == 0
            ? $"Fonte: {ExemplosFixos.Todos.Count} exemplos fixos (Sao Paulo)"
            : $"Fonte: {args.Count} WKT(s) informado(s) por argumento");

        IGraphyRepository? repositorio = null;
        try
        {
            _log.Passo("Criando repositorio.");
            repositorio = _fabricaRepositorio(config, _log);
            var servico = new InsercaoService(repositorio, new WktValidator(_log), _log);

            var resultado = await servico.ExecutarAsync(wkts, cancellationToken).ConfigureAwait(false);
            Imprimir(resultado, config);
            return (int)resultado.Codigo;
        }
        catch (OperationCanceledException)
        {
            _log.Erro("Execucao cancelada.");
            _saidaErro.WriteLine("Execucao cancelada.");
            return (int)CodigoSaida.FalhaBanco;
        }
        catch (FalhaConexaoException ex)
        {
            _log.Erro($"Falha de conexao ({ex.TipoExcecao}).");
            _saidaErro.WriteLine(MensagemConexao(config, ex.TipoExcecao));
            return (int)CodigoSaida.FalhaBanco;
        }
        catch (FalhaBancoException ex)
        {
            var erro = InsercaoService.FormatarErroBanco(ex);
            _log.Erro($"Erro do banco: {erro}.");
            _saidaErro.WriteLine($"Erro do banco: {erro}");
            return (int)CodigoSaida.FalhaBanco;
        }
#pragma warning disable CA1031 // Ultima barreira: nunca exibir ex.ToString()/stack (R2).
        catch (Exception ex)
#pragma warning restore CA1031
        {
            _log.Erro($"Erro inesperado ({ex.GetType().Name}).");
            _saidaErro.WriteLine($"Erro inesperado ({ex.GetType().Name}).");
            return (int)CodigoSaida.FalhaBanco;
        }
        finally
        {
            if (repositorio is IAsyncDisposable descartavel)
            {
                await descartavel.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>Mensagem generica de falha de conexao (R2): host:porta + tipo da excecao.</summary>
    public static string MensagemConexao(ConexaoConfig config, string tipoExcecao)
    {
        ArgumentNullException.ThrowIfNull(config);
        return $"falha de conexao com {config.Endpoint()} ({tipoExcecao})";
    }

    private void Imprimir(ResultadoExecucao resultado, ConexaoConfig config)
    {
        if (resultado.LimiteExcedido)
        {
            _saidaErro.WriteLine(
                $"Quantidade de WKT excede o maximo de {InsercaoService.MaximoWktPorExecucao} por execucao; nada foi inserido.");
            return;
        }

        foreach (var item in resultado.Itens)
        {
            var prefixo = $"[{item.Indice.ToString(CultureInfo.InvariantCulture)}]";
            switch (item.Status)
            {
                case StatusItem.Inserido when item.Registro is not null:
                    _saida.WriteLine(
                        $"{prefixo} OK id={item.Registro.Id.ToString(CultureInfo.InvariantCulture)} " +
                        $"SRID={item.Registro.Srid.ToString(CultureInfo.InvariantCulture)} " +
                        $"ST_AsText={TextoSeguro.Sanitizar(item.Registro.Wkt)}");
                    break;

                case StatusItem.InvalidoLocal:
                    _saidaErro.WriteLine($"{prefixo} WKT INVALIDO (validacao local): {item.Erro} | WKT: {item.WktEcoado}");
                    break;

                case StatusItem.InvalidoBanco:
                    _saidaErro.WriteLine($"{prefixo} WKT INVALIDO (ST_IsValid): {item.Erro} | WKT: {item.WktEcoado}");
                    break;

                case StatusItem.ErroConexao:
                    _saidaErro.WriteLine($"{prefixo} ERRO: {MensagemConexao(config, item.Erro ?? "desconhecido")}");
                    break;

                case StatusItem.NaoProcessado:
                    _saidaErro.WriteLine($"{prefixo} NAO PROCESSADO: {item.Erro} | WKT: {item.WktEcoado}");
                    break;

                default:
                    var id = item.IdInserido is { } valor
                        ? $" (id inserido={valor.ToString(CultureInfo.InvariantCulture)})"
                        : string.Empty;
                    _saidaErro.WriteLine($"{prefixo} ERRO DO BANCO{id}: {item.Erro} | WKT: {item.WktEcoado}");
                    break;
            }
        }

        var inseridos = resultado.Itens.Count(x => x.Status == StatusItem.Inserido);
        _saida.WriteLine(
            $"Resumo: {inseridos.ToString(CultureInfo.InvariantCulture)} de " +
            $"{resultado.Itens.Count.ToString(CultureInfo.InvariantCulture)} inserido(s). " +
            $"Codigo de saida {((int)resultado.Codigo).ToString(CultureInfo.InvariantCulture)}.");
    }
}
