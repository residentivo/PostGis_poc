using System.Buffers;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ApiMapas.Api.Endpoints;
using ApiMapas.Aplicacao;
using ApiMapas.Tests.Suporte;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace ApiMapas.Tests.Api;

/// <summary>Stream de corpo que falha (simula o Kestrel) ou registra se foi lido.</summary>
internal sealed class CorpoQueFalha(Exception? erro, byte[]? dados = null) : Stream
{
    private readonly MemoryStream _dados = new(dados ?? Array.Empty<byte>());

    public bool Lido { get; private set; }

    public override bool CanRead => true;

    public override bool CanSeek => false;

    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }

    public override void Flush()
    {
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Lido = true;
        var n = _dados.Read(buffer.Span);
        if (n == 0 && erro is not null)
        {
            throw erro;
        }

        return ValueTask.FromResult(n);
    }

    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    public override void SetLength(long value) => throw new NotSupportedException();

    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}

internal sealed class RecursoTamanhoFalso(bool somenteLeitura) : IHttpMaxRequestBodySizeFeature
{
    public bool IsReadOnly => somenteLeitura;

    public long? MaxRequestBodySize { get; set; } = 30_000_000;
}

/// <summary>P6.3 / P6.8 (C5, C6) - LeitorCorpoHttp unitario (DefaultHttpContext), sem servidor.</summary>
public class LeitorCorpoHttpTests
{
    private static DefaultHttpContext Contexto(Stream corpo, string? contentType = "application/json", long? contentLength = null, IHttpMaxRequestBodySizeFeature? recurso = null)
    {
        var contexto = new DefaultHttpContext();
        contexto.Request.Body = corpo;
        contexto.Request.ContentType = contentType;
        contexto.Request.ContentLength = contentLength;
        if (recurso is not null)
        {
            contexto.Features.Set(recurso);
        }

        return contexto;
    }

    [Fact]
    public void Construtor_Valida()
    {
        Assert.Throws<ArgumentNullException>(() => new LeitorCorpoHttp(null!, 10));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeitorCorpoHttp(new DefaultHttpContext(), 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new LeitorCorpoHttp(new DefaultHttpContext(), -1));
    }

    [Fact]
    public async Task ContentTypeRecusado_NaoLeOCorpo()
    {
        var corpo = new CorpoQueFalha(null, Encoding.UTF8.GetBytes("{}"));

        var r = await new LeitorCorpoHttp(Contexto(corpo, "text/plain"), 100).LerAsync(CancellationToken.None);

        Assert.Same(LeituraCorpo.TipoMidiaNaoSuportado.Instancia, r);
        Assert.False(corpo.Lido);
    }

    [Fact]
    public async Task ContentLengthAcima_413_SemLer()
    {
        var corpo = new CorpoQueFalha(null, new byte[10]);

        var r = await new LeitorCorpoHttp(Contexto(corpo, contentLength: 101), 100).LerAsync(CancellationToken.None);

        Assert.Equal(new LeituraCorpo.MuitoGrande(100), r);
        Assert.False(corpo.Lido);
    }

    [Theory]
    [InlineData(1, 1, true)]
    [InlineData(100, 100, true)]
    [InlineData(101, 100, false)]
    [InlineData(16_384, 16_384, true)]
    [InlineData(16_385, 16_384, false)]
    [InlineData(50_000, 1_048_576, true)]
    [InlineData(1_048_577, 1_048_576, false)]
    public async Task LeituraComContadorProprio_SemContentLength(int tamanho, long limite, bool aceito)
    {
        var dados = Enumerable.Range(0, tamanho).Select(i => (byte)(i % 251)).ToArray();

        var r = await new LeitorCorpoHttp(Contexto(new MemoryStream(dados)), limite).LerAsync(CancellationToken.None);

        if (aceito)
        {
            Assert.Equal(dados, Assert.IsType<LeituraCorpo.Lido>(r).Bytes.ToArray());
        }
        else
        {
            Assert.Equal(new LeituraCorpo.MuitoGrande(limite), r);
        }
    }

    [Fact]
    public async Task ContentLengthMenorQueOCorpoReal_ContadorProprioBarra()
    {
        var r = await new LeitorCorpoHttp(Contexto(new MemoryStream(new byte[500]), contentLength: 10), 100).LerAsync(CancellationToken.None);
        Assert.Equal(new LeituraCorpo.MuitoGrande(100), r);
    }

    [Fact]
    public async Task BadHttpRequest413_ViraMuitoGrande()
    {
        var corpo = new CorpoQueFalha(new BadHttpRequestException("Request body too large.", StatusCodes.Status413PayloadTooLarge));

        var r = await new LeitorCorpoHttp(Contexto(corpo), 100).LerAsync(CancellationToken.None);

        Assert.Equal(new LeituraCorpo.MuitoGrande(100), r);
    }

    /// <summary>C5: corpo truncado (400) ou lento (408 do MinRequestBodyDataRate) vira 400 "Corpo inválido", nunca 500.</summary>
    [Theory]
    [InlineData(StatusCodes.Status400BadRequest)]
    [InlineData(StatusCodes.Status408RequestTimeout)]
    [InlineData(StatusCodes.Status431RequestHeaderFieldsTooLarge)]
    public async Task BadHttpRequestQueNaoETamanho_ViraInvalida(int status)
    {
        var corpo = new CorpoQueFalha(new BadHttpRequestException("Unexpected end of request content. SEGREDO", status), Encoding.UTF8.GetBytes("{\"geo"));

        var r = await new LeitorCorpoHttp(Contexto(corpo), 100).LerAsync(CancellationToken.None);

        Assert.Equal(new LeituraCorpo.Invalida(status), r);
    }

    [Fact]
    public async Task OutraExcecao_Propaga()
    {
        var corpo = new CorpoQueFalha(new IOException("conexao perdida"));
        await Assert.ThrowsAsync<IOException>(() => new LeitorCorpoHttp(Contexto(corpo), 100).LerAsync(CancellationToken.None));
    }

    [Fact]
    public async Task Cancelamento_Propaga()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        var corpo = new CorpoQueFalha(new OperationCanceledException(cts.Token));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new LeitorCorpoHttp(Contexto(corpo), 100).LerAsync(cts.Token));
    }

    [Theory]
    [InlineData(false, 100L)]
    [InlineData(true, 30_000_000L)]
    public async Task TetoDoServidor_AjustadoQuandoPermitido(bool somenteLeitura, long esperado)
    {
        var recurso = new RecursoTamanhoFalso(somenteLeitura);

        await new LeitorCorpoHttp(Contexto(new MemoryStream(new byte[5]), recurso: recurso), 100).LerAsync(CancellationToken.None);

        Assert.Equal(esperado, recurso.MaxRequestBodySize);
    }

    /// <summary>C6: o bloco do ArrayPool volta limpo (clearArray: true); o proximo Rent nao ve o corpo.</summary>
    [Fact]
    public async Task C6_ArrayPoolDevolvidoLimpo()
    {
        var dados = Enumerable.Repeat((byte)0xAB, 20_000).ToArray();

        var r = await new LeitorCorpoHttp(Contexto(new MemoryStream(dados)), 1_048_576).LerAsync(CancellationToken.None);
        var reaproveitado = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            Assert.IsType<LeituraCorpo.Lido>(r);
            Assert.DoesNotContain((byte)0xAB, reaproveitado);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(reaproveitado);
        }
    }

    [Fact]
    public async Task C6_ArrayPoolLimpoTambemNo413()
    {
        var dados = Enumerable.Repeat((byte)0xCD, 20_000).ToArray();

        var r = await new LeitorCorpoHttp(Contexto(new MemoryStream(dados)), 100).LerAsync(CancellationToken.None);
        var reaproveitado = ArrayPool<byte>.Shared.Rent(16 * 1024);
        try
        {
            Assert.IsType<LeituraCorpo.MuitoGrande>(r);
            Assert.DoesNotContain((byte)0xCD, reaproveitado);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(reaproveitado);
        }
    }
}

/// <summary>
/// P6.8 (C5) - Kestrel real em 127.0.0.1 (porta dinamica): corpo truncado e corpo lento respondem
/// 400 "Corpo inválido", nunca 500. MinRequestBodyDataRate nao e alterado pela API (RP-5).
/// </summary>
[Collection(ColecaoKestrel.Nome)]
public class LeitorCorpoKestrelTests
{
    private static async Task<(ApiEscritaFactory Fabrica, RepositorioEscritaFalso Repo, Uri Endereco)> SubirAsync()
    {
        var repo = RepositorioEscritaFalso.Sucesso();
        var f = new ApiEscritaFactory { Repositorio = repo };
        f.UseKestrel(0);
        f.StartServer();
        var enderecos = f.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()!.Addresses;
        var endereco = new Uri(enderecos.First());
        Assert.True(IPAddress.IsLoopback(IPAddress.Parse(endereco.Host == "localhost" ? "127.0.0.1" : endereco.Host.Trim('[', ']'))));
        await Task.CompletedTask;
        return (f, repo, endereco);
    }

    private static async Task<string> TrocarAsync(Uri endereco, string cabecalhos, byte[] corpoParcial, Func<Socket, Task> depois)
    {
        using var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
        await socket.ConnectAsync(IPAddress.Loopback, endereco.Port);
        await socket.SendAsync(Encoding.ASCII.GetBytes(cabecalhos));
        await socket.SendAsync(corpoParcial);
        await depois(socket);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var resposta = new MemoryStream();
        var buffer = new byte[4096];
        try
        {
            int n;
            while ((n = await socket.ReceiveAsync(buffer, SocketFlags.None, cts.Token)) > 0)
            {
                resposta.Write(buffer, 0, n);
                var texto = Encoding.UTF8.GetString(resposta.ToArray());
                var fimCabecalho = texto.IndexOf("\r\n\r\n", StringComparison.Ordinal);
                if (fimCabecalho > 0 && texto.Contains("\"traceId\"", StringComparison.Ordinal) && texto.EndsWith('}'))
                {
                    break;
                }
            }
        }
        catch (SocketException)
        {
        }

        return Encoding.UTF8.GetString(resposta.ToArray());
    }

    /// <summary>
    /// Corpo truncado porque o cliente fechou o envio (FIN) antes do Content-Length: o Kestrel trata
    /// como cliente desconectado (RequestAborted) ou "Unexpected end of request content" (400). Em
    /// nenhum caso vira 500 ou "Erro nao tratado".
    /// </summary>
    [Fact]
    public async Task CorpoTruncado_ClienteFecha_Nunca500()
    {
        var (f, repo, endereco) = await SubirAsync();
        await using var _ = f;

        var resposta = await TrocarAsync(endereco,
            "PUT /api/mapas/1/graphy HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: 200\r\n\r\n",
            Encoding.UTF8.GetBytes("{\"geometrias\":[{\"wkt\":\"POI"),
            s =>
            {
                s.Shutdown(SocketShutdown.Send);
                return Task.CompletedTask;
            });

        var limite = DateTime.UtcNow.AddSeconds(5);
        while (!f.Logs.Mensagens.Any(m => m.Contains("cancelado pelo cliente", StringComparison.Ordinal) || m.Contains("leitura do corpo interrompida", StringComparison.Ordinal))
            && DateTime.UtcNow < limite)
        {
            await Task.Delay(20);
        }

        Assert.DoesNotContain("HTTP/1.1 500", resposta, StringComparison.Ordinal);
        if (resposta.Length > 0)
        {
            Assert.StartsWith("HTTP/1.1 400", resposta, StringComparison.Ordinal);
        }

        Assert.Contains(f.Logs.Mensagens, m => m.Contains("cancelado pelo cliente", StringComparison.Ordinal) || m.Contains("leitura do corpo interrompida pelo servidor (StatusCode 400)", StringComparison.Ordinal));
        Assert.Empty(repo.Chamadas);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("Erro nao tratado", StringComparison.Ordinal));
    }

    /// <summary>C5: corpo chunked malformado (BadHttpRequestException 400 do Kestrel) responde 400 "Corpo inválido".</summary>
    [Theory]
    [InlineData("ZZ\r\n{}\r\n0\r\n\r\n")]
    [InlineData("5\r\n{\"geometrias\":[]}\r\n0\r\n\r\n")]
    public async Task CorpoChunkedMalformado_400CorpoInvalido_Nunca500(string corpoChunked)
    {
        var (f, repo, endereco) = await SubirAsync();
        await using var _ = f;

        var resposta = await TrocarAsync(endereco,
            "PUT /api/mapas/1/graphy HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nTransfer-Encoding: chunked\r\n\r\n",
            Encoding.ASCII.GetBytes(corpoChunked),
            _ => Task.CompletedTask);

        Assert.True(resposta.StartsWith("HTTP/1.1 400", StringComparison.Ordinal), resposta);
        Assert.Contains("Corpo inválido", resposta, StringComparison.Ordinal);
        Assert.Contains("application/problem+json", resposta, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", resposta, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("leitura do corpo interrompida pelo servidor (StatusCode 400)", StringComparison.Ordinal));
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("Erro nao tratado", StringComparison.Ordinal));
    }

    [Fact]
    public async Task CorpoAcimaDoLimite_SemContentLength_Chunked_413()
    {
        var (f, repo, endereco) = await SubirAsync();
        await using var _ = f;
        using var cliente = new HttpClient { BaseAddress = endereco };
        var json = CorposPut.Valido[..^1] + new string(' ', 66_000) + "}";

        var r = await cliente.PutAsync("/api/mapas/1/graphy", ConteudoSemTamanho.Criar(json));
        var corpo = await r.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.RequestEntityTooLarge, r.StatusCode);
        Assert.Contains("O corpo excede o limite de 65536 bytes.", corpo, StringComparison.Ordinal);
        Assert.Empty(repo.Chamadas);
    }

    [Fact]
    public async Task Put204_PeloKestrel()
    {
        var (f, repo, endereco) = await SubirAsync();
        await using var _ = f;
        using var cliente = new HttpClient { BaseAddress = endereco };

        var r = await cliente.PutAsync("/api/mapas/1/graphy", Http.Json(CorposPut.Valido));

        Assert.Equal(HttpStatusCode.NoContent, r.StatusCode);
        Assert.Single(repo.Chamadas);
    }

    /// <summary>
    /// Corpo lento: o MinRequestBodyDataRate padrao (240 B/s depois de 5 s de carencia) interrompe a
    /// leitura; a API responde 400 "Corpo inválido" (ou o Kestrel fecha a conexao), nunca 500.
    /// </summary>
    [Fact]
    public async Task CorpoLento_400CorpoInvalido_Nunca500()
    {
        var (f, repo, endereco) = await SubirAsync();
        await using var _ = f;

        var resposta = await TrocarAsync(endereco,
            "PUT /api/mapas/1/metry HTTP/1.1\r\nHost: localhost\r\nContent-Type: application/json\r\nContent-Length: 60000\r\n\r\n",
            Encoding.UTF8.GetBytes("{\"wkt\":"),
            async s =>
            {
                // 1 byte a cada 500 ms (2 B/s, bem abaixo de 240 B/s) por ate 12 s.
                for (var i = 0; i < 24; i++)
                {
                    await Task.Delay(500);
                    try
                    {
                        await s.SendAsync(" "u8.ToArray());
                    }
                    catch (SocketException)
                    {
                        return;
                    }

                    if (s.Available > 0)
                    {
                        return;
                    }
                }
            });

        Assert.DoesNotContain("HTTP/1.1 500", resposta, StringComparison.Ordinal);
        if (resposta.Length > 0)
        {
            Assert.StartsWith("HTTP/1.1 400", resposta, StringComparison.Ordinal);
            Assert.Contains("Corpo inválido", resposta, StringComparison.Ordinal);
        }

        Assert.Empty(repo.Chamadas);
        Assert.DoesNotContain(f.Logs.Mensagens, m => m.Contains("Erro nao tratado", StringComparison.Ordinal));
        Assert.Contains(f.Logs.Mensagens, m => m.Contains("leitura do corpo interrompida pelo servidor (StatusCode 408)", StringComparison.Ordinal));
    }
}
