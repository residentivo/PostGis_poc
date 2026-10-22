using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;
using ApiMapas.Dominio;

namespace ApiMapas.Tests.Suporte;

/// <summary>Consulta recebida pelo servidor falso (texto SQL, OIDs dos parametros e valor binario de $1).</summary>
public sealed record ConsultaRecebida(string Sql, IReadOnlyList<int> OidsParametros, int? ValorId);

/// <summary>Resposta que o servidor falso deve devolver para a consulta.</summary>
public abstract record RespostaFalsa
{
    public sealed record Linhas(IReadOnlyList<GeometriaMapa> Itens) : RespostaFalsa;

    /// <summary>ErrorResponse logo na execucao (antes de qualquer linha).</summary>
    public sealed record ErroNaExecucao(string SqlState, string Mensagem) : RespostaFalsa;

    /// <summary>Algumas linhas e depois ErrorResponse (falha durante a leitura).</summary>
    public sealed record ErroDuranteLeitura(IReadOnlyList<GeometriaMapa> Antes, string SqlState, string Mensagem) : RespostaFalsa;

    /// <summary>Primeira coluna declarada como int4 (o repositorio faz GetString -> InvalidCastException).</summary>
    public sealed record TipoErrado : RespostaFalsa;
}

/// <summary>
/// Mock minimo do protocolo PostgreSQL v3 em 127.0.0.1 (porta efemera), usado para exercitar o
/// MapasRepository real (Npgsql) sem servidor de banco. Autentica com AuthenticationOk, responde
/// Parse/Bind/Describe/Execute/Sync com linhas configuradas e registra o SQL e os tipos dos parametros.
/// Exige no cliente: "SSL Mode=Disable;Server Compatibility Mode=NoTypeLoading".
/// </summary>
public sealed class ServidorPostgresFalso : IAsyncDisposable
{
    private const int OidText = 25;
    private const int OidInt4 = 23;
    private const int OidFloat8 = 701;

    private readonly TcpListener _ouvinte;
    private readonly Func<ConsultaRecebida, RespostaFalsa> _responder;
    private readonly CancellationTokenSource _parar = new();
    private readonly Task _laco;

    public ServidorPostgresFalso(Func<ConsultaRecebida, RespostaFalsa> responder)
    {
        _responder = responder;
        _ouvinte = new TcpListener(IPAddress.Loopback, 0);
        _ouvinte.Start();
        Porta = ((IPEndPoint)_ouvinte.LocalEndpoint).Port;
        _laco = Task.Run(AceitarAsync);
    }

    public int Porta { get; }

    public ConcurrentQueue<ConsultaRecebida> Consultas { get; } = new();

    public ConcurrentQueue<Exception> ErrosInternos { get; } = new();

    public string ConnectionString =>
        $"Host=127.0.0.1;Port={Porta};Username=u_falso;Password=p_falsa;SSL Mode=Disable;" +
        "Server Compatibility Mode=NoTypeLoading;Pooling=false;Timeout=5;Command Timeout=5";

    public async ValueTask DisposeAsync()
    {
        await _parar.CancelAsync();
        _ouvinte.Stop();
        try
        {
            await _laco;
        }
        catch (OperationCanceledException)
        {
        }
        catch (ObjectDisposedException)
        {
        }
        catch (SocketException)
        {
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
            catch (Exception) when (_parar.IsCancellationRequested)
            {
                return;
            }

            _ = Task.Run(async () =>
            {
                using (cliente)
                {
                    try
                    {
                        await AtenderAsync(cliente.GetStream());
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException or OperationCanceledException)
                    {
                        // cliente fechou
                    }
                    catch (Exception ex)
                    {
                        ErrosInternos.Enqueue(ex);
                    }
                }
            });
        }
    }

    private async Task AtenderAsync(NetworkStream rede)
    {
        var ct = _parar.Token;

        // ---- inicializacao (SSLRequest/GSSENCRequest/CancelRequest/StartupMessage) ----
        while (true)
        {
            var tamanho = BinaryPrimitives.ReadInt32BigEndian(await LerAsync(rede, 4, ct));
            var corpo = await LerAsync(rede, tamanho - 4, ct);
            var codigo = BinaryPrimitives.ReadInt32BigEndian(corpo);
            if (codigo is 80877103 or 80877104)
            {
                await rede.WriteAsync("N"u8.ToArray(), ct); // sem SSL/GSS
                continue;
            }

            if (codigo == 80877102)
            {
                return; // CancelRequest
            }

            break;
        }

        var saida = new MemoryStream();
        Escrever(saida, 'R', b => b.Int32(0)); // AuthenticationOk
        foreach (var (nome, valor) in new[]
        {
            ("server_version", "16.4"), ("server_encoding", "UTF8"), ("client_encoding", "UTF8"),
            ("DateStyle", "ISO, MDY"), ("integer_datetimes", "on"), ("standard_conforming_strings", "on"),
            ("TimeZone", "UTC"), ("IntervalStyle", "postgres"), ("is_superuser", "off"),
        })
        {
            Escrever(saida, 'S', b => b.Texto(nome).Texto(valor));
        }

        Escrever(saida, 'K', b => b.Int32(4242).Int32(1234));
        Escrever(saida, 'Z', b => b.Byte((byte)'I'));
        await rede.WriteAsync(saida.ToArray(), ct);

        // ---- protocolo estendido ----
        string? sql = null;
        int[] oids = Array.Empty<int>();
        int? valorId = null;
        short[] formatos = Array.Empty<short>();
        var houveExecute = false;

        while (true)
        {
            var tipo = (char)(await LerAsync(rede, 1, ct))[0];
            var tamanho = BinaryPrimitives.ReadInt32BigEndian(await LerAsync(rede, 4, ct));
            var corpo = await LerAsync(rede, tamanho - 4, ct);
            var leitor = new LeitorMensagem(corpo);

            switch (tipo)
            {
                case 'X':
                    return;
                case 'P':
                    leitor.Texto();
                    sql = leitor.Texto();
                    var n = leitor.Int16();
                    oids = Enumerable.Range(0, n).Select(_ => leitor.Int32()).ToArray();
                    break;
                case 'B':
                    leitor.Texto();
                    leitor.Texto();
                    var nFormatosParam = leitor.Int16();
                    var formatosParam = Enumerable.Range(0, nFormatosParam).Select(_ => leitor.Int16()).ToArray();
                    var nParam = leitor.Int16();
                    for (var i = 0; i < nParam; i++)
                    {
                        var len = leitor.Int32();
                        var bytes = leitor.Bytes(len);
                        if (i == 0 && len >= 0)
                        {
                            var binario = formatosParam.Length == 0 ? false : formatosParam[Math.Min(i, formatosParam.Length - 1)] == 1;
                            valorId = binario && len == 4
                                ? BinaryPrimitives.ReadInt32BigEndian(bytes)
                                : int.Parse(Encoding.UTF8.GetString(bytes), CultureInfo.InvariantCulture);
                        }
                    }

                    var nRes = leitor.Int16();
                    formatos = Enumerable.Range(0, nRes).Select(_ => leitor.Int16()).ToArray();
                    break;
                case 'E':
                    houveExecute = true;
                    break;
                case 'S':
                    await ResponderAsync(rede, sql, oids, valorId, formatos, houveExecute, ct);
                    sql = null;
                    houveExecute = false;
                    break;
                default:
                    break; // Describe/Flush/Close: tratados no Sync
            }
        }
    }

    private async Task ResponderAsync(NetworkStream rede, string? sql, int[] oids, int? valorId, short[] formatos, bool houveExecute, CancellationToken ct)
    {
        var saida = new MemoryStream();
        if (sql is null || !houveExecute)
        {
            Escrever(saida, 'Z', b => b.Byte((byte)'I'));
            await rede.WriteAsync(saida.ToArray(), ct);
            return;
        }

        var consulta = new ConsultaRecebida(sql, oids, valorId);
        Consultas.Enqueue(consulta);
        var resposta = _responder(consulta);

        short Formato(int coluna) => formatos.Length switch
        {
            0 => (short)0,
            1 => formatos[0],
            _ => formatos[coluna],
        };

        if (resposta is RespostaFalsa.ErroNaExecucao erroExec)
        {
            EscreverErro(saida, erroExec.SqlState, erroExec.Mensagem);
            Escrever(saida, 'Z', b => b.Byte((byte)'I'));
            await rede.WriteAsync(saida.ToArray(), ct);
            return;
        }

        Escrever(saida, '1', _ => { });
        Escrever(saida, '2', _ => { });

        var tipoErrado = resposta is RespostaFalsa.TipoErrado;
        var colunas = new (string Nome, int Oid, short Tam)[]
        {
            ("tipo", tipoErrado ? OidInt4 : OidText, (short)(tipoErrado ? 4 : -1)), ("srid", OidInt4, 4), ("wkt", OidText, -1), ("geojson", OidText, -1),
            ("x", OidFloat8, 8), ("y", OidFloat8, 8), ("longitude", OidFloat8, 8), ("latitude", OidFloat8, 8),
        };
        Escrever(saida, 'T', b =>
        {
            b.Int16((short)colunas.Length);
            for (var i = 0; i < colunas.Length; i++)
            {
                b.Texto(colunas[i].Nome).Int32(0).Int16(0).Int32(colunas[i].Oid).Int16(colunas[i].Tam).Int32(-1).Int16(Formato(i));
            }
        });

        IReadOnlyList<GeometriaMapa> linhas = resposta switch
        {
            RespostaFalsa.Linhas l => l.Itens,
            RespostaFalsa.ErroDuranteLeitura e => e.Antes,
            _ => new[] { Geometrias.Ponto() },
        };

        foreach (var g in linhas)
        {
            Escrever(saida, 'D', b =>
            {
                b.Int16(8);
                if (tipoErrado)
                {
                    b.Celula(BitConverterBE(1), Formato(0) == 1 ? null : "1");
                }
                else
                {
                    b.CelulaTexto(g.Tipo);
                }

                b.Celula(BitConverterBE(g.Srid), Formato(1) == 1 ? null : g.Srid.ToString(CultureInfo.InvariantCulture));
                b.CelulaTexto(g.Wkt).CelulaTexto(g.GeoJson);
                for (var i = 0; i < 4; i++)
                {
                    var v = i switch { 0 => g.X, 1 => g.Y, 2 => g.Longitude, _ => g.Latitude };
                    if (v is null)
                    {
                        b.Int32(-1);
                    }
                    else if (Formato(4 + i) == 1)
                    {
                        var bytes = new byte[8];
                        BinaryPrimitives.WriteDoubleBigEndian(bytes, v.Value);
                        b.Int32(8).Raw(bytes);
                    }
                    else
                    {
                        b.CelulaTexto(v.Value.ToString("R", CultureInfo.InvariantCulture));
                    }
                }
            });
        }

        if (resposta is RespostaFalsa.ErroDuranteLeitura erroLeitura)
        {
            EscreverErro(saida, erroLeitura.SqlState, erroLeitura.Mensagem);
        }
        else
        {
            Escrever(saida, 'C', b => b.Texto($"SELECT {linhas.Count}"));
        }

        Escrever(saida, 'Z', b => b.Byte((byte)'I'));
        await rede.WriteAsync(saida.ToArray(), ct);
    }

    private static byte[] BitConverterBE(int valor)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, valor);
        return b;
    }

    private static void EscreverErro(MemoryStream saida, string sqlState, string mensagem) =>
        Escrever(saida, 'E', b => b.Byte((byte)'S').Texto("ERROR").Byte((byte)'V').Texto("ERROR")
            .Byte((byte)'C').Texto(sqlState).Byte((byte)'M').Texto(mensagem).Byte(0));

    private static void Escrever(MemoryStream saida, char tipo, Action<Construtor> corpo)
    {
        var c = new Construtor();
        corpo(c);
        var dados = c.ToArray();
        saida.WriteByte((byte)tipo);
        var len = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(len, dados.Length + 4);
        saida.Write(len);
        saida.Write(dados);
    }

    private static async Task<byte[]> LerAsync(NetworkStream rede, int quantidade, CancellationToken ct)
    {
        var buffer = new byte[quantidade];
        await rede.ReadExactlyAsync(buffer, ct);
        return buffer;
    }

    private sealed class Construtor
    {
        private readonly MemoryStream _ms = new();

        public Construtor Byte(byte b)
        {
            _ms.WriteByte(b);
            return this;
        }

        public Construtor Int16(short v)
        {
            Span<byte> b = stackalloc byte[2];
            BinaryPrimitives.WriteInt16BigEndian(b, v);
            _ms.Write(b);
            return this;
        }

        public Construtor Int32(int v)
        {
            Span<byte> b = stackalloc byte[4];
            BinaryPrimitives.WriteInt32BigEndian(b, v);
            _ms.Write(b);
            return this;
        }

        public Construtor Texto(string s)
        {
            _ms.Write(Encoding.UTF8.GetBytes(s));
            _ms.WriteByte(0);
            return this;
        }

        public Construtor Raw(byte[] b)
        {
            _ms.Write(b);
            return this;
        }

        public Construtor CelulaTexto(string s)
        {
            var b = Encoding.UTF8.GetBytes(s);
            return Int32(b.Length).Raw(b);
        }

        /// <summary>Celula binaria (texto nulo) ou texto.</summary>
        public Construtor Celula(byte[] binario, string? texto) =>
            texto is null ? Int32(binario.Length).Raw(binario) : CelulaTexto(texto);

        public byte[] ToArray() => _ms.ToArray();
    }

    private sealed class LeitorMensagem(byte[] dados)
    {
        private int _pos;

        public string Texto()
        {
            var fim = Array.IndexOf(dados, (byte)0, _pos);
            var s = Encoding.UTF8.GetString(dados, _pos, fim - _pos);
            _pos = fim + 1;
            return s;
        }

        public short Int16()
        {
            var v = BinaryPrimitives.ReadInt16BigEndian(dados.AsSpan(_pos));
            _pos += 2;
            return v;
        }

        public int Int32()
        {
            var v = BinaryPrimitives.ReadInt32BigEndian(dados.AsSpan(_pos));
            _pos += 4;
            return v;
        }

        public byte[] Bytes(int n)
        {
            if (n <= 0)
            {
                return Array.Empty<byte>();
            }

            var b = dados.AsSpan(_pos, n).ToArray();
            _pos += n;
            return b;
        }
    }
}
