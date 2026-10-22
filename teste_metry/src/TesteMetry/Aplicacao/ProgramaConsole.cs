using System.Globalization;
using TesteMetry.Comum;
using TesteMetry.Configuracao;
using TesteMetry.Dados;
using TesteMetry.Diagnostico;
using TesteMetry.Exemplos;
using TesteMetry.Validacao;

namespace TesteMetry.Aplicacao;

/// <summary>
/// Orquestracao do console (F4.7), separada de Program.cs para ser testavel.
/// Argumentos = lista de WKT (nunca senha/connection string - R1, nem SRID - RQ-2); sem
/// argumentos usa <see cref="ExemplosFixos.Para(int)"/> do SRID de PG_SRID. Nunca imprime ex.ToString(), stack trace ou connection string (R2).
/// </summary>
public sealed class ProgramaConsole
{
    private readonly TextWriter _saida;
    private readonly TextWriter _saidaErro;
    private readonly Func<string, string?> _lerVariavel;
    private readonly Func<ConexaoConfig, ILog, IMetryRepository> _fabricaRepositorio;
    private readonly ILog _log;

    public ProgramaConsole(
        TextWriter saida,
        TextWriter saidaErro,
        Func<string, string?> lerVariavel,
        Func<ConexaoConfig, ILog, IMetryRepository> fabricaRepositorio,
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

        _saida.WriteLine("TesteMetry - insercao em public.mapas.metry (geometry, SRID de PG_SRID)");

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

        // RQ-2: o SRID vem so da configuracao (ambiente); os argumentos sao apenas WKT.
        var srid = config.Srid;
        var sridTexto = srid.ToString(CultureInfo.InvariantCulture);
        _log.Passo($"SRID efetivo: {sridTexto}.");

        // MF-3 (R-10, R-11): SRID sem exemplos fixos ou sem faixa no validador e erro de
        // CONFIGURACAO (codigo 1), detectado antes de criar o repositorio. Try restrito: so
        // ArgumentOutOfRangeException destas duas chamadas; mensagem sem ex.Message/ParamName/ActualValue.
        IReadOnlyList<string?> wkts;
        WktValidator validador;
        try
        {
            _log.Passo(args.Count == 0 ? "Carregando exemplos fixos do SRID." : "Usando WKT(s) dos argumentos.");
            wkts = args.Count == 0 ? ExemplosFixos.Para(srid) : args;

            _log.Passo("Criando validador WKT do SRID.");
            validador = new WktValidator(srid, _log);
        }
        catch (ArgumentOutOfRangeException)
        {
            _log.Erro("Configuracao invalida: PG_SRID");
            _saidaErro.WriteLine($"Configuracao invalida: PG_SRID nao permitido (use {LimitesSrid.PermitidosTexto}).");
            return (int)CodigoSaida.ConfiguracaoInvalida;
        }

        IMetryRepository? repositorio = null;
        try
        {
            _saida.WriteLine(args.Count == 0
                ? $"Fonte: {wkts.Count} exemplos fixos (Sao Paulo, SRID {sridTexto})"
                : $"Fonte: {args.Count} WKT(s) informado(s) por argumento (SRID {sridTexto})");

            _log.Passo("Criando repositorio.");
            repositorio = _fabricaRepositorio(config, _log);

            // R-12: a mesma instancia do validador criada acima (sem uma segunda).
            var servico = new InsercaoService(repositorio, validador, _log);

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

                case StatusItem.SridDivergente:
                    var idDivergente = item.IdInserido is { } idValor
                        ? $" (id inserido={idValor.ToString(CultureInfo.InvariantCulture)})"
                        : string.Empty;
                    _saidaErro.WriteLine($"{prefixo} SRID DIVERGENTE{idDivergente}: {item.Erro} | WKT: {item.WktEcoado}");
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
