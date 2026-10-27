using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace ApiMapas.Tests.Suporte;

/// <summary>Comando recebido pelo servidor falso de escrita.</summary>
/// <param name="Conexao">Numero da conexao (1, 2, ...).</param>
/// <param name="Sql">SQL como enviado pelo Npgsql (parametros posicionais $1, $2...).</param>
/// <param name="Oids">OIDs declarados no Parse.</param>
/// <param name="Parametros">Valores decodificados (int, string, int[], string[], bool ou null).</param>
/// <param name="Simples">True para Query simples ('Q': BEGIN/COMMIT/ROLLBACK do Npgsql).</param>
public sealed record ComandoPg(int Conexao, string Sql, IReadOnlyList<int> Oids, IReadOnlyList<object?> Parametros, bool Simples)
{
    public bool Contem(string trecho) => Sql.Contains(trecho, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Coluna de RowDescription.</summary>
public sealed record ColunaPg(string Nome, int Oid)
{
    public const int Bool = 16;
    public const int Int4 = 23;
    public const int Text = 25;
}

/// <summary>Resposta configurada para um comando.</summary>
public abstract record RespostaPg
{
    /// <summary>Linhas (RowDescription + DataRow + CommandComplete).</summary>
    public sealed record Linhas(IReadOnlyList<ColunaPg> Colunas, IReadOnlyList<object?[]> Valores, string? Tag = null) : RespostaPg;

    /// <summary>Sem linhas (NoData + CommandComplete com a tag, ex.: "UPDATE 2").</summary>
    public sealed record Comando(string Tag) : RespostaPg;

    /// <summary>ErrorResponse com o SqlState (e, opcionalmente, o nome da constraint - campo 'n').</summary>
    public sealed record Erro(string SqlState, string Mensagem = "erro simulado com SEGREDO_DO_SERVIDOR", string? Constraint = null) : RespostaPg;

    /// <summary>
    /// Segura a resposta ate chegar um CancelRequest desta conexao (responde 57014) ou ate
    /// <paramref name="Maximo"/> (responde <paramref name="DepoisDoPrazo"/>).
    /// </summary>
    public sealed record Pendurar(TimeSpan Maximo, RespostaPg DepoisDoPrazo) : RespostaPg;

    /// <summary>Fecha o socket no meio do comando (conexao perdida).</summary>
    public sealed record Derrubar : RespostaPg;

    public static Linhas Booleano(bool? valor) => new(new[] { new ColunaPg("exists", ColunaPg.Bool) }, new[] { new object?[] { valor } });

    public static Linhas Validacao(bool? valida, string? tipo) =>
        new(new[] { new ColunaPg("valida", ColunaPg.Bool), new ColunaPg("tipo", ColunaPg.Text) }, new[] { new object?[] { valida, tipo } });
}

/// <summary>
/// Mock do protocolo PostgreSQL v3 em 127.0.0.1 (porta efemera) para a ESCRITA: protocolo
/// estendido com varios comandos por Sync (NpgsqlBatch), Query simples (BEGIN/COMMIT/ROLLBACK do
/// Npgsql), estado de transacao no ReadyForQuery (I/T/E), CancelRequest (57014), erro com SqlState
/// e queda de conexao. Registra todos os comandos. Exige no cliente "SSL Mode=Disable;Server
/// Compatibility Mode=NoTypeLoading".
/// </summary>
public sealed class ServidorPostgresEscritaFalso : IAsyncDisposable
{
    private readonly TcpListener _ouvinte;
    private readonly Func<ComandoPg, RespostaPg?> _responder;
    private readonly CancellationTokenSource _parar = new();
    private readonly Task _laco;
    private readonly ConcurrentDictionary<int, TaskCompletionSource> _cancelamentos = new();
    private int _proximaConexao;
    private int _terminacoes;
    private int _cancelRequests;

    public ServidorPostgresEscritaFalso(Func<ComandoPg, RespostaPg?> responder)
    {
        _responder = responder;
        _ouvinte = new TcpListener(IPAddress.Loopback, 0);
        _ouvinte.Start();
        Porta = ((IPEndPoint)_ouvinte.LocalEndpoint).Port;
        _laco = Task.Run(AceitarAsync);
    }

    public int Porta { get; }

    public ConcurrentQueue<ComandoPg> Comandos { get; } = new();

    public ConcurrentQueue<Exception> ErrosInternos { get; } = new();

    /// <summary>Conexoes que concluiram o startup.</summary>
    public int Conexoes => Volatile.Read(ref _proximaConexao);

    /// <summary>Mensagens Terminate ('X') recebidas (conexao fechada pelo cliente, ex.: DataSource.Clear()).</summary>
    public int Terminacoes => Volatile.Read(ref _terminacoes);

    public int CancelRequests => Volatile.Read(ref _cancelRequests);

    public string ConnectionString(int maxPool = 5, int timeout = 5, bool pooling = true) =>
        $"Host=127.0.0.1;Port={Porta};Username=u_escrita_falso;Password=p_escrita_falsa;SSL Mode=Disable;" +
        $"Server Compatibility Mode=NoTypeLoading;Pooling={(pooling ? "true" : "false")};Maximum Pool Size={maxPool};" +
        $"Timeout={timeout};Command Timeout=5;Cancellation Timeout=3000";

    /// <summary>SQLs (sem os ';' finais) na ordem de chegada.</summary>
    public IReadOnlyList<string> Sqls => Comandos.Select(c => c.Sql).ToList();

    public async ValueTask DisposeAsync()
    {
        await _parar.CancelAsync();
        _ouvinte.Stop();
        foreach (var tcs in _cancelamentos.Values)
        {
            tcs.TrySetResult();
        }

        try
        {
            await _laco;
        }
        catch (Exception ex) when (ex is OperationCanceledException or ObjectDisposedException or SocketException)
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
                        await AtenderAsync(cliente);
                    }
                    catch (Exception ex) when (ex is IOException or SocketException or EndOfStreamException or OperationCanceledException or ObjectDisposedException)
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

    private async Task AtenderAsync(TcpClient cliente)
    {
        var rede = cliente.GetStream();
        var ct = _parar.Token;

        while (true)
        {
            var tamanho = BinaryPrimitives.ReadInt32BigEndian(await LerAsync(rede, 4, ct));
            var corpo = await LerAsync(rede, tamanho - 4, ct);
            var codigo = BinaryPrimitives.ReadInt32BigEndian(corpo);
            if (codigo is 80877103 or 80877104)
            {
                await rede.WriteAsync("N"u8.ToArray(), ct);
                continue;
            }

            if (codigo == 80877102)
            {
                // CancelRequest: pid e chave da conexao alvo.
                Interlocked.Increment(ref _cancelRequests);
                var pid = BinaryPrimitives.ReadInt32BigEndian(corpo.AsSpan(4));
                if (_cancelamentos.TryGetValue(pid, out var tcs))
                {
                    tcs.TrySetResult();
                }

                return;
            }

            break;
        }

        var conexao = Interlocked.Increment(ref _proximaConexao);
        var saida = new MemoryStream();
        Escrever(saida, 'R', b => b.Int32(0));
        foreach (var (nome, valor) in new[]
        {
            ("server_version", "17.2"), ("server_encoding", "UTF8"), ("client_encoding", "UTF8"),
            ("DateStyle", "ISO, MDY"), ("integer_datetimes", "on"), ("standard_conforming_strings", "on"),
            ("TimeZone", "UTC"), ("IntervalStyle", "postgres"), ("is_superuser", "off"),
        })
        {
            Escrever(saida, 'S', b => b.Texto(nome).Texto(valor));
        }

        Escrever(saida, 'K', b => b.Int32(conexao).Int32(777));
        Escrever(saida, 'Z', b => b.Byte((byte)'I'));
        await EnviarAsync(rede, saida, ct);

        var estado = 'I';
        var ignorarAteSync = false;
        string? sql = null;
        int[] oids = Array.Empty<int>();
        short[] formatosResultado = Array.Empty<short>();
        RespostaPg? resposta = null;

        while (true)
        {
            var tipo = (char)(await LerAsync(rede, 1, ct))[0];
            var tamanho = BinaryPrimitives.ReadInt32BigEndian(await LerAsync(rede, 4, ct));
            var corpo = await LerAsync(rede, tamanho - 4, ct);
            var leitor = new LeitorMensagem(corpo);

            switch (tipo)
            {
                case 'X':
                    Interlocked.Increment(ref _terminacoes);
                    return;

                case 'Q':
                {
                    var texto = leitor.Texto();
                    var comando = new ComandoPg(conexao, texto.TrimEnd(';', ' '), Array.Empty<int>(), Array.Empty<object?>(), Simples: true);
                    Comandos.Enqueue(comando);
                    var r = _responder(comando) ?? Padrao(comando);
                    if (r is RespostaPg.Derrubar)
                    {
                        return;
                    }

                    if (r is RespostaPg.Erro erro)
                    {
                        EscreverErro(saida, erro);
                        estado = estado == 'T' ? 'E' : estado;
                    }
                    else
                    {
                        Escrever(saida, 'C', b => b.Texto(TagDe(r)));
                        estado = NovoEstado(comando.Sql, estado);
                    }

                    Escrever(saida, 'Z', b => b.Byte((byte)estado));
                    await EnviarAsync(rede, saida, ct);
                    break;
                }

                case 'P':
                    if (ignorarAteSync)
                    {
                        break;
                    }

                    leitor.Texto();
                    sql = leitor.Texto();
                    var n = leitor.Int16();
                    oids = Enumerable.Range(0, n).Select(_ => leitor.Int32()).ToArray();
                    Escrever(saida, '1', _ => { });
                    break;

                case 'B':
                {
                    if (ignorarAteSync)
                    {
                        break;
                    }

                    leitor.Texto();
                    leitor.Texto();
                    var nFormatos = leitor.Int16();
                    var formatos = Enumerable.Range(0, nFormatos).Select(_ => leitor.Int16()).ToArray();
                    var nParam = leitor.Int16();
                    var valores = new object?[nParam];
                    for (var i = 0; i < nParam; i++)
                    {
                        var len = leitor.Int32();
                        var bytes = len < 0 ? null : leitor.Bytes(len);
                        var binario = formatos.Length != 0 && formatos[Math.Min(i, formatos.Length - 1)] == 1;
                        valores[i] = bytes is null ? null : Decodificar(i < oids.Length ? oids[i] : 0, bytes, binario);
                    }

                    var nRes = leitor.Int16();
                    formatosResultado = Enumerable.Range(0, nRes).Select(_ => leitor.Int16()).ToArray();
                    Escrever(saida, '2', _ => { });

                    var comando = new ComandoPg(conexao, (sql ?? string.Empty).TrimEnd(';', ' '), oids, valores, Simples: false);
                    Comandos.Enqueue(comando);
                    resposta = _responder(comando) ?? Padrao(comando);
                    break;
                }

                case 'D':
                    if (ignorarAteSync)
                    {
                        break;
                    }

                    if (resposta is RespostaPg.Linhas linhasDesc)
                    {
                        EscreverDescricao(saida, linhasDesc.Colunas, formatosResultado);
                    }
                    else
                    {
                        Escrever(saida, 'n', _ => { });
                    }

                    break;

                case 'E':
                {
                    if (ignorarAteSync)
                    {
                        break;
                    }

                    var r = resposta;
                    if (r is RespostaPg.Pendurar pendurar)
                    {
                        await EnviarAsync(rede, saida, ct);
                        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                        _cancelamentos[conexao] = tcs;
                        var venceu = await Task.WhenAny(tcs.Task, Task.Delay(pendurar.Maximo, ct));
                        _cancelamentos.TryRemove(conexao, out _);
                        r = venceu == tcs.Task
                            ? new RespostaPg.Erro("57014", "canceling statement due to user request")
                            : pendurar.DepoisDoPrazo;
                    }

                    if (r is RespostaPg.Derrubar)
                    {
                        await EnviarAsync(rede, saida, ct);
                        return;
                    }

                    if (r is RespostaPg.Erro erro)
                    {
                        EscreverErro(saida, erro);
                        ignorarAteSync = true;
                        estado = estado == 'T' ? 'E' : estado;
                    }
                    else if (r is RespostaPg.Linhas linhas)
                    {
                        foreach (var valoresLinha in linhas.Valores)
                        {
                            EscreverLinha(saida, linhas.Colunas, valoresLinha, formatosResultado);
                        }

                        Escrever(saida, 'C', b => b.Texto(linhas.Tag ?? $"SELECT {linhas.Valores.Count.ToString(CultureInfo.InvariantCulture)}"));
                    }
                    else
                    {
                        Escrever(saida, 'C', b => b.Texto(TagDe(r!)));
                        estado = NovoEstado(sql ?? string.Empty, estado);
                    }

                    break;
                }

                case 'S':
                    ignorarAteSync = false;
                    resposta = null;
                    sql = null;
                    Escrever(saida, 'Z', b => b.Byte((byte)estado));
                    await EnviarAsync(rede, saida, ct);
                    break;

                case 'H':
                    await EnviarAsync(rede, saida, ct);
                    break;

                case 'C':
                    if (!ignorarAteSync)
                    {
                        Escrever(saida, '3', _ => { });
                    }

                    break;

                default:
                    break;
            }
        }
    }

    /// <summary>Respostas padrao: controle de transacao e SET; o resto devolve "SELECT 0".</summary>
    private static RespostaPg.Comando Padrao(ComandoPg comando)
    {
        var sql = comando.Sql.TrimStart().ToUpperInvariant();
        if (sql.StartsWith("BEGIN", StringComparison.Ordinal))
        {
            return new RespostaPg.Comando("BEGIN");
        }

        if (sql.StartsWith("COMMIT", StringComparison.Ordinal))
        {
            return new RespostaPg.Comando("COMMIT");
        }

        if (sql.StartsWith("ROLLBACK", StringComparison.Ordinal))
        {
            return new RespostaPg.Comando("ROLLBACK");
        }

        if (sql.StartsWith("SET", StringComparison.Ordinal))
        {
            return new RespostaPg.Comando("SET");
        }

        if (sql.StartsWith("DISCARD", StringComparison.Ordinal))
        {
            return new RespostaPg.Comando("DISCARD ALL");
        }

        return new RespostaPg.Comando("SELECT 0");
    }

    private static string TagDe(RespostaPg r) => r switch
    {
        RespostaPg.Comando c => c.Tag,
        RespostaPg.Linhas l => l.Tag ?? "SELECT " + l.Valores.Count.ToString(CultureInfo.InvariantCulture),
        _ => "SELECT 0",
    };

    private static char NovoEstado(string sql, char atual)
    {
        var s = sql.TrimStart().ToUpperInvariant();
        if (s.StartsWith("BEGIN", StringComparison.Ordinal) || s.StartsWith("START TRANSACTION", StringComparison.Ordinal))
        {
            return 'T';
        }

        if (s.StartsWith("COMMIT", StringComparison.Ordinal) || s.StartsWith("ROLLBACK", StringComparison.Ordinal))
        {
            return 'I';
        }

        return atual;
    }

    private static object? Decodificar(int oid, byte[] bytes, bool binario)
    {
        switch (oid)
        {
            case ColunaPg.Int4:
                return binario ? BinaryPrimitives.ReadInt32BigEndian(bytes) : int.Parse(Encoding.UTF8.GetString(bytes), CultureInfo.InvariantCulture);
            case ColunaPg.Bool:
                return binario ? bytes[0] != 0 : Encoding.UTF8.GetString(bytes) == "t";
            case 1007:
            case 1009:
                if (!binario)
                {
                    return Encoding.UTF8.GetString(bytes);
                }

                var ndim = BinaryPrimitives.ReadInt32BigEndian(bytes);
                var pos = 12;
                var total = 0;
                if (ndim > 0)
                {
                    total = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos));
                    pos += 8 * ndim;
                }

                var textos = new List<string?>();
                var inteiros = new List<int>();
                for (var i = 0; i < total; i++)
                {
                    var len = BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos));
                    pos += 4;
                    if (len < 0)
                    {
                        textos.Add(null);
                        continue;
                    }

                    if (oid == 1007)
                    {
                        inteiros.Add(BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(pos)));
                    }
                    else
                    {
                        textos.Add(Encoding.UTF8.GetString(bytes, pos, len));
                    }

                    pos += len;
                }

                return oid == 1007 ? inteiros.ToArray() : textos.ToArray();
            default:
                return Encoding.UTF8.GetString(bytes);
        }
    }

    private static short Formato(short[] formatos, int coluna) => formatos.Length switch
    {
        0 => (short)0,
        1 => formatos[0],
        _ => formatos[coluna],
    };

    private static void EscreverDescricao(MemoryStream saida, IReadOnlyList<ColunaPg> colunas, short[] formatos) =>
        Escrever(saida, 'T', b =>
        {
            b.Int16((short)colunas.Count);
            for (var i = 0; i < colunas.Count; i++)
            {
                var tam = colunas[i].Oid switch { ColunaPg.Bool => (short)1, ColunaPg.Int4 => (short)4, _ => (short)-1 };
                b.Texto(colunas[i].Nome).Int32(0).Int16(0).Int32(colunas[i].Oid).Int16(tam).Int32(-1).Int16(Formato(formatos, i));
            }
        });

    private static void EscreverLinha(MemoryStream saida, IReadOnlyList<ColunaPg> colunas, object?[] valores, short[] formatos) =>
        Escrever(saida, 'D', b =>
        {
            b.Int16((short)colunas.Count);
            for (var i = 0; i < colunas.Count; i++)
            {
                var valor = valores[i];
                var binario = Formato(formatos, i) == 1;
                if (valor is null)
                {
                    b.Int32(-1);
                    continue;
                }

                byte[] dados = (colunas[i].Oid, valor) switch
                {
                    (ColunaPg.Bool, bool v) => binario ? new[] { (byte)(v ? 1 : 0) } : Encoding.UTF8.GetBytes(v ? "t" : "f"),
                    (ColunaPg.Int4, int v) => binario ? Int32Be(v) : Encoding.UTF8.GetBytes(v.ToString(CultureInfo.InvariantCulture)),
                    _ => Encoding.UTF8.GetBytes(Convert.ToString(valor, CultureInfo.InvariantCulture)!),
                };
                b.Int32(dados.Length).Raw(dados);
            }
        });

    private static byte[] Int32Be(int v)
    {
        var b = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(b, v);
        return b;
    }

    private static void EscreverErro(MemoryStream saida, RespostaPg.Erro erro) =>
        Escrever(saida, 'E', b =>
        {
            b.Byte((byte)'S').Texto("ERROR").Byte((byte)'V').Texto("ERROR")
                .Byte((byte)'C').Texto(erro.SqlState).Byte((byte)'M').Texto(erro.Mensagem)
                .Byte((byte)'D').Texto("detalhe SEGREDO_DETALHE POINT(1 2)").Byte((byte)'H').Texto("hint SEGREDO_HINT");
            if (erro.Constraint is not null)
            {
                b.Byte((byte)'n').Texto(erro.Constraint);
            }

            b.Byte(0);
        });

    private static async Task EnviarAsync(NetworkStream rede, MemoryStream saida, CancellationToken ct)
    {
        if (saida.Length == 0)
        {
            return;
        }

        await rede.WriteAsync(saida.ToArray(), ct);
        saida.SetLength(0);
    }

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
