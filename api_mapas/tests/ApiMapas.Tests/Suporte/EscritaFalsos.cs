using System.Text;
using ApiMapas.Aplicacao;
using ApiMapas.Dominio;
using ApiMapas.Dominio.Geometrias;
using ApiMapas.Infraestrutura.Configuracao;
using ApiMapas.Infraestrutura.Dados;

namespace ApiMapas.Tests.Suporte;

/// <summary>Chamada recebida pelo <see cref="RepositorioEscritaFalso"/>. <see cref="Id"/> e null no POST.</summary>
public sealed record ChamadaEscrita(IdMapa? Id, ColunaMapa Coluna, GeometriaEntrada Geometria)
{
    /// <summary>"PUT" (AtualizarAsync) ou "POST" (CriarAsync).</summary>
    public string Metodo => Id is null ? "POST" : "PUT";
}

/// <summary>
/// Mock de <see cref="IMapasEscritaRepository"/>: devolve o resultado configurado (ou lanca), registra
/// as chamadas e pode "pendurar" ate o token ser cancelado (prazo/cancelamento) ou ate ser liberado
/// (concorrencia).
/// </summary>
public sealed class RepositorioEscritaFalso : IMapasEscritaRepository
{
    /// <summary>Id devolvido pelo POST em <see cref="Sucesso"/>.</summary>
    public const int IdCriado = 101;

    private readonly Func<ChamadaEscrita, CancellationToken, Task<ResultadoEscrita>> _responder;
    private int _emAndamento;
    private int _maximoSimultaneo;

    public RepositorioEscritaFalso(Func<ChamadaEscrita, CancellationToken, Task<ResultadoEscrita>> responder)
    {
        _responder = responder;
    }

    public RepositorioEscritaFalso(ResultadoEscrita resultado)
        : this((_, _) => Task.FromResult(resultado))
    {
    }

    /// <summary>PUT: Atualizado; POST: Criado(<see cref="IdCriado"/>).</summary>
    public static RepositorioEscritaFalso Sucesso() =>
        new((c, _) => Task.FromResult<ResultadoEscrita>(c.Id is null ? new ResultadoEscrita.Criado(IdCriado) : ResultadoEscrita.Atualizado.Instancia));

    /// <summary>
    /// PO6 (CN-2): POST devolve ids crescentes a partir de <paramref name="primeiro"/> (como a identity),
    /// PUT devolve Atualizado. Seguro para chamadas concorrentes.
    /// </summary>
    public static RepositorioEscritaFalso Sequencial(int primeiro = 500)
    {
        var proximo = primeiro - 1;
        return new((c, _) => Task.FromResult<ResultadoEscrita>(c.Id is null
            ? new ResultadoEscrita.Criado(Interlocked.Increment(ref proximo))
            : ResultadoEscrita.Atualizado.Instancia));
    }

    public static RepositorioEscritaFalso QueLanca(Exception erro) => new((_, _) => Task.FromException<ResultadoEscrita>(erro));

    /// <summary>Espera o token ser cancelado e entao lanca OperationCanceledException (como o Npgsql).</summary>
    public static RepositorioEscritaFalso Pendurado() => new(async (_, ct) =>
    {
        await Task.Delay(Timeout.Infinite, ct);
        return ResultadoEscrita.Conflito.Instancia;
    });

    public List<ChamadaEscrita> Chamadas { get; } = new();

    public int MaximoSimultaneo => Volatile.Read(ref _maximoSimultaneo);

    public Task<ResultadoEscrita> AtualizarAsync(IdMapa id, ColunaMapa coluna, GeometriaEntrada geometria, CancellationToken cancellationToken) =>
        RegistrarAsync(new ChamadaEscrita(id, coluna, geometria), cancellationToken);

    public Task<ResultadoEscrita> CriarAsync(ColunaMapa coluna, GeometriaEntrada geometria, CancellationToken cancellationToken) =>
        RegistrarAsync(new ChamadaEscrita(null, coluna, geometria), cancellationToken);

    private async Task<ResultadoEscrita> RegistrarAsync(ChamadaEscrita chamada, CancellationToken cancellationToken)
    {
        lock (Chamadas)
        {
            Chamadas.Add(chamada);
        }

        var atual = Interlocked.Increment(ref _emAndamento);
        int max;
        while (atual > (max = Volatile.Read(ref _maximoSimultaneo)))
        {
            Interlocked.CompareExchange(ref _maximoSimultaneo, atual, max);
        }

        try
        {
            return await _responder(chamada, cancellationToken);
        }
        finally
        {
            Interlocked.Decrement(ref _emAndamento);
        }
    }
}

/// <summary>Mock de <see cref="ILeitorCorpo"/> com o resultado configurado.</summary>
public sealed class LeitorCorpoFalso : ILeitorCorpo
{
    private readonly Func<CancellationToken, Task<LeituraCorpo>> _ler;

    public LeitorCorpoFalso(Func<CancellationToken, Task<LeituraCorpo>> ler) => _ler = ler;

    public LeitorCorpoFalso(LeituraCorpo leitura)
        : this(_ => Task.FromResult(leitura))
    {
    }

    public static LeitorCorpoFalso Json(string json) => new(new LeituraCorpo.Lido(Encoding.UTF8.GetBytes(json)));

    public int Leituras { get; private set; }

    public Task<LeituraCorpo> LerAsync(CancellationToken cancellationToken)
    {
        Leituras++;
        return _ler(cancellationToken);
    }
}

/// <summary>Mock de <see cref="IVerificadorRoleEscrita"/>.</summary>
public sealed class VerificadorRoleFalso(Func<CancellationToken, Task<MotivoEscritaDesligada>> verificar) : IVerificadorRoleEscrita
{
    public VerificadorRoleFalso(MotivoEscritaDesligada motivo)
        : this(_ => Task.FromResult(motivo))
    {
    }

    public int Chamadas { get; private set; }

    public Task<MotivoEscritaDesligada> VerificarAsync(CancellationToken cancellationToken)
    {
        Chamadas++;
        return verificar(cancellationToken);
    }
}

/// <summary>Corpos de PUT e POST usados nos testes: objeto unico {"wkt","srid"} (Q1 da PU).</summary>
public static class CorposPut
{
    public const string PontoGraphy = "POINT(-46.633309 -23.55052)";
    public const string LinhaGraphy = "LINESTRING(-46.64 -23.56, -46.62 -23.54)";

    public static string Com((string Wkt, int Srid) item) =>
        $"{{\"wkt\":{System.Text.Json.JsonSerializer.Serialize(item.Wkt)},\"srid\":{item.Srid.ToString(System.Globalization.CultureInfo.InvariantCulture)}}}";

    public static string Valido => Com((PontoGraphy, 4326));
}
