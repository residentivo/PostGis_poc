using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TesteGraphy.Tests.Fakes;

/// <summary>
/// Servidor TCP minimo em 127.0.0.1 (porta efemera) que fala apenas o inicio do protocolo
/// do PostgreSQL: recusa SSL/GSS ('N') e responde ao StartupMessage com um ErrorResponse
/// (SqlState/Message/Detail/Hint configuraveis) ou fecha a conexao. Nunca acessa a rede externa.
/// </summary>
internal sealed class ServidorPostgresFalso : IAsyncDisposable
{
    private const int CodigoSsl = 80877103;
    private const int CodigoGss = 80877104;
    private const int ProtocoloV3 = 196608;

    private readonly TcpListener _ouvinte;
    private readonly CancellationTokenSource _parar = new();
    private readonly Task _laco;
    private readonly string? _sqlState;
    private readonly string _mensagem;
    private readonly string _detalhe;
    private readonly string _dica;

    private ServidorPostgresFalso(string? sqlState, string mensagem, string detalhe, string dica)
    {
        _sqlState = sqlState;
        _mensagem = mensagem;
        _detalhe = detalhe;
        _dica = dica;
        _ouvinte = new TcpListener(IPAddress.Loopback, 0);
        _ouvinte.Start();
        _laco = Task.Run(AceitarAsync);
    }

    public int Porta => ((IPEndPoint)_ouvinte.LocalEndpoint).Port;

    public int Conexoes { get; private set; }

    /// <summary>Responde ao startup com ErrorResponse FATAL.</summary>
    public static ServidorPostgresFalso ComErro(string sqlState, string mensagem, string detalhe, string dica) =>
        new(sqlState, mensagem, detalhe, dica);

    /// <summary>Aceita e fecha a conexao imediatamente (fim de fluxo inesperado).</summary>
    public static ServidorPostgresFalso QueFecha() => new(null, string.Empty, string.Empty, string.Empty);

    /// <summary>Porta de loopback garantidamente fechada (listener aberto e encerrado).</summary>
    public static int PortaFechada()
    {
        var l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        var porta = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return porta;
    }

    public async ValueTask DisposeAsync()
    {
        await _parar.CancelAsync();
        _ouvinte.Stop();
        try
        {
            await _laco;
        }
        catch (Exception)
        {
            // encerramento do servidor de teste
        }

        _parar.Dispose();
    }

    private async Task AceitarAsync()
    {
        while (!_parar.IsCancellationRequested)
        {
            TcpClient cliente;
            try
            {
                cliente = await _ouvinte.AcceptTcpClientAsync(_parar.Token);
            }
            catch (Exception)
            {
                return;
            }

            Conexoes++;
            _ = Task.Run(() => AtenderAsync(cliente));
        }
    }

    private async Task AtenderAsync(TcpClient cliente)
    {
        using (cliente)
        {
            try
            {
                var fluxo = cliente.GetStream();
                if (_sqlState is null)
                {
                    return;
                }

                while (true)
                {
                    var cabecalho = new byte[4];
                    await fluxo.ReadExactlyAsync(cabecalho);
                    var tamanho = BinaryPrimitives.ReadInt32BigEndian(cabecalho);
                    var corpo = new byte[tamanho - 4];
                    await fluxo.ReadExactlyAsync(corpo);
                    var codigo = BinaryPrimitives.ReadInt32BigEndian(corpo);

                    if (codigo is CodigoSsl or CodigoGss)
                    {
                        await fluxo.WriteAsync("N"u8.ToArray());
                        continue;
                    }

                    if (codigo == ProtocoloV3)
                    {
                        await fluxo.WriteAsync(MontarErro());
                        await fluxo.FlushAsync();
                        await Task.Delay(200);
                    }

                    return;
                }
            }
            catch (Exception)
            {
                // cliente desconectou
            }
        }
    }

    private byte[] MontarErro()
    {
        var campos = new MemoryStream();
        void Campo(char tipo, string valor)
        {
            campos.WriteByte((byte)tipo);
            var bytes = Encoding.UTF8.GetBytes(valor);
            campos.Write(bytes);
            campos.WriteByte(0);
        }

        Campo('S', "FATAL");
        Campo('V', "FATAL");
        Campo('C', _sqlState!);
        Campo('M', _mensagem);
        Campo('D', _detalhe);
        Campo('H', _dica);
        campos.WriteByte(0);

        var payload = campos.ToArray();
        var mensagem = new byte[1 + 4 + payload.Length];
        mensagem[0] = (byte)'E';
        BinaryPrimitives.WriteInt32BigEndian(mensagem.AsSpan(1), 4 + payload.Length);
        payload.CopyTo(mensagem, 5);
        return mensagem;
    }
}
