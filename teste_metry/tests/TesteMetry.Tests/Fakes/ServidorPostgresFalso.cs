using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace TesteMetry.Tests.Fakes;

/// <summary>
/// Servidor TCP minimo em 127.0.0.1 (porta efemera) que fala apenas parte do protocolo
/// do PostgreSQL. Nunca acessa a rede externa. Modos:
/// <list type="bullet">
/// <item><see cref="ComErro"/>: responde ao StartupMessage com ErrorResponse FATAL (erro na ABERTURA).</item>
/// <item><see cref="QueFecha"/>: aceita e fecha a conexao.</item>
/// <item><see cref="ComErroNaExecucao"/>: conclui o startup (AuthenticationOk, ParameterStatus,
/// BackendKeyData, ReadyForQuery), responde ao carregamento de tipos do Npgsql e devolve
/// ErrorResponse (ERROR) no lote estendido (Parse/Bind/Execute/Sync) do comando - erro na EXECUCAO.</item>
/// <item><see cref="SemLinhas"/>: conclui o startup e responde aos comandos com zero linhas.</item>
/// <item><see cref="ComLinhas"/>: conclui o startup e responde com uma linha binaria (e NOTICE opcional).</item>
/// </list>
/// </summary>
internal sealed class ServidorPostgresFalso : IAsyncDisposable
{
    private const int CodigoSsl = 80877103;
    private const int CodigoGss = 80877104;
    private const int ProtocoloV3 = 196608;

    private const uint OidBool = 16;
    private const uint OidInt4 = 23;
    private const uint OidText = 25;

    /// <summary>Tipos minimos devolvidos ao carregamento de tipos do Npgsql: (oid, nome, typtype).</summary>
    private static readonly (uint Oid, string Nome, char Tipo)[] Tipos =
    [
        (16, "bool", 'b'), (17, "bytea", 'b'), (18, "char", 'b'), (19, "name", 'b'), (20, "int8", 'b'),
        (21, "int2", 'b'), (23, "int4", 'b'), (25, "text", 'b'), (26, "oid", 'b'), (700, "float4", 'b'),
        (701, "float8", 'b'), (1043, "varchar", 'b'), (705, "unknown", 'p'), (2249, "record", 'p'),
        (2278, "void", 'p'),
    ];

    private readonly TcpListener _ouvinte;
    private readonly CancellationTokenSource _parar = new();
    private readonly Task _laco;
    private readonly Modo _modo;
    private readonly string? _sqlState;
    private readonly string _mensagem;
    private readonly string _detalhe;
    private readonly string _dica;
    private readonly ConcurrentQueue<string> _comandos = new();
    private Respostas? _respostas;

    private ServidorPostgresFalso(Modo modo, string? sqlState, string mensagem, string detalhe, string dica)
    {
        _modo = modo;
        _sqlState = sqlState;
        _mensagem = mensagem;
        _detalhe = detalhe;
        _dica = dica;
        _ouvinte = new TcpListener(IPAddress.Loopback, 0);
        _ouvinte.Start();
        _laco = Task.Run(AceitarAsync);
    }

    private enum Modo
    {
        Fecha,
        ErroNoStartup,
        ErroNaExecucao,
        SemLinhas,
    }

    public int Porta => ((IPEndPoint)_ouvinte.LocalEndpoint).Port;

    public int Conexoes { get; private set; }

    /// <summary>SQL recebidos em mensagens Parse (protocolo estendido), na ordem.</summary>
    public IReadOnlyList<string> ComandosRecebidos => [.. _comandos];

    /// <summary>Responde ao startup com ErrorResponse FATAL.</summary>
    public static ServidorPostgresFalso ComErro(string sqlState, string mensagem, string detalhe, string dica) =>
        new(Modo.ErroNoStartup, sqlState, mensagem, detalhe, dica);

    /// <summary>Conclui o startup e devolve ErrorResponse (ERROR) na execucao do comando.</summary>
    public static ServidorPostgresFalso ComErroNaExecucao(string sqlState, string mensagem, string detalhe, string dica) =>
        new(Modo.ErroNaExecucao, sqlState, mensagem, detalhe, dica);

    /// <summary>Conclui o startup e responde a cada comando com zero linhas.</summary>
    public static ServidorPostgresFalso SemLinhas() => new(Modo.SemLinhas, null, string.Empty, string.Empty, string.Empty);

    /// <summary>
    /// Conclui o startup e responde a cada comando com UMA linha (formato binario): validacao
    /// (valido, tipo), INSERT (id) ou leitura (id, wkt, srid). Valores nulos viram NULL. Se
    /// <see cref="Respostas.Aviso"/> for informado, envia um NoticeResponse antes das linhas.
    /// </summary>
    public static ServidorPostgresFalso ComLinhas(Respostas respostas) =>
        new(Modo.SemLinhas, null, string.Empty, string.Empty, string.Empty) { _respostas = respostas };

    /// <summary>Valores devolvidos pelo modo <see cref="ComLinhas"/>.</summary>
    public sealed record Respostas(bool? Valido = true, string? Tipo = "POINT", int? Id = 1, string? Wkt = "POINT(0 0)", int? Srid = 4326, string? Aviso = null);

    /// <summary>Aceita e fecha a conexao imediatamente (fim de fluxo inesperado).</summary>
    public static ServidorPostgresFalso QueFecha() => new(Modo.Fecha, null, string.Empty, string.Empty, string.Empty);

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
                if (_modo == Modo.Fecha)
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

                    if (codigo != ProtocoloV3)
                    {
                        return;
                    }

                    if (_modo == Modo.ErroNoStartup)
                    {
                        await fluxo.WriteAsync(MontarErro("FATAL"));
                        await fluxo.FlushAsync();
                        await Task.Delay(200);
                        return;
                    }

                    await fluxo.WriteAsync(MontarStartupOk());
                    await fluxo.FlushAsync();
                    await AtenderComandosAsync(fluxo);
                    return;
                }
            }
            catch (Exception)
            {
                // cliente desconectou
            }
        }
    }

    /// <summary>Laco de mensagens apos o startup: Query simples (tipos) e lote estendido ate Sync.</summary>
    private async Task AtenderComandosAsync(NetworkStream fluxo)
    {
        string? sqlDoLote = null;
        while (true)
        {
            var tipo = new byte[1];
            await fluxo.ReadExactlyAsync(tipo);
            var cabecalho = new byte[4];
            await fluxo.ReadExactlyAsync(cabecalho);
            var corpo = new byte[BinaryPrimitives.ReadInt32BigEndian(cabecalho) - 4];
            await fluxo.ReadExactlyAsync(corpo);

            switch ((char)tipo[0])
            {
                case 'X':
                    return;

                case 'Q':
                    await fluxo.WriteAsync(ResponderQuerySimples(LerCString(corpo, 0)));
                    await fluxo.FlushAsync();
                    break;

                case 'P':
                    // Parse: nome do statement (cstring) + SQL (cstring) + parametros.
                    var nome = LerCString(corpo, 0);
                    var sql = LerCString(corpo, Encoding.UTF8.GetByteCount(nome) + 1);
                    _comandos.Enqueue(sql);
                    sqlDoLote = sql;
                    break;

                case 'S':
                    await fluxo.WriteAsync(ResponderLote(sqlDoLote));
                    await fluxo.FlushAsync();
                    sqlDoLote = null;
                    break;

                default:
                    // Bind, Describe, Execute, Close, Flush: respondidos em bloco no Sync.
                    break;
            }
        }
    }

    private byte[] ResponderLote(string? sql)
    {
        var saida = new MemoryStream();
        if (_modo == Modo.ErroNaExecucao)
        {
            saida.Write(MontarErro("ERROR"));
            saida.Write(Mensagem('Z', "I"u8.ToArray()));
            return saida.ToArray();
        }

        // SemLinhas/ComLinhas: ParseComplete, BindComplete, [NoticeResponse], RowDescription (binario),
        // zero ou uma DataRow, CommandComplete e ReadyForQuery.
        var insert = sql is not null && sql.Contains("INSERT", StringComparison.Ordinal);
        var validacao = sql is not null && sql.Contains("ST_IsValid", StringComparison.Ordinal);
        saida.Write(Mensagem('1', []));
        saida.Write(Mensagem('2', []));
        if (_respostas?.Aviso is { } aviso)
        {
            saida.Write(Mensagem('N', [.. "SNOTICE\0"u8, .. "VNOTICE\0"u8, .. "C00000\0"u8, (byte)'M', .. CString(aviso), 0]));
        }

        var colunas = insert ? new[] { ("id", OidInt4, (short)1) }
            : validacao ? new[] { ("valido", OidBool, (short)1), ("tipo", OidText, (short)1) }
            : new[] { ("id", OidInt4, (short)1), ("wkt", OidText, (short)1), ("srid", OidInt4, (short)1) };
        saida.Write(DescricaoLinhas(colunas));

        var linhas = 0;
        if (_respostas is { } r)
        {
            var valores = insert ? [Int4(r.Id)]
                : validacao ? [r.Valido is null ? null : [r.Valido.Value ? (byte)1 : (byte)0], Texto(r.Tipo)]
                : new[] { Int4(r.Id), Texto(r.Wkt), Int4(r.Srid) };
            saida.Write(LinhaBinaria(valores));
            linhas = 1;
        }

        saida.Write(Mensagem('C', CString(insert ? $"INSERT 0 {linhas}" : $"SELECT {linhas}")));
        saida.Write(Mensagem('Z', "I"u8.ToArray()));
        return saida.ToArray();
    }

    private static byte[]? Int4(int? valor)
    {
        if (valor is null)
        {
            return null;
        }

        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(bytes, valor.Value);
        return bytes;
    }

    private static byte[]? Texto(string? valor) => valor is null ? null : Encoding.UTF8.GetBytes(valor);

    private static byte[] LinhaBinaria(byte[]?[] valores)
    {
        var corpo = new MemoryStream();
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(buffer, (short)valores.Length);
        corpo.Write(buffer, 0, 2);
        foreach (var valor in valores)
        {
            BinaryPrimitives.WriteInt32BigEndian(buffer, valor?.Length ?? -1);
            corpo.Write(buffer, 0, 4);
            if (valor is not null)
            {
                corpo.Write(valor);
            }
        }

        return Mensagem('D', corpo.ToArray());
    }

    /// <summary>Respostas ao carregamento de tipos do Npgsql (protocolo simples, formato texto).</summary>
    private static byte[] ResponderQuerySimples(string sql)
    {
        // O Npgsql envia versao, tipos, campos de composites e rotulos de enums numa unica Query:
        // um conjunto de resultados por instrucao, na ordem, e um unico ReadyForQuery no final.
        var secoes = new[] { "version()", "typnotnull", "-- Load field definitions", "-- Load enum fields" }
            .Select(marca => (Marca: marca, Indice: sql.IndexOf(marca, StringComparison.Ordinal)))
            .Where(s => s.Indice >= 0)
            .OrderBy(s => s.Indice)
            .ToList();

        var saida = new MemoryStream();
        if (secoes.Count == 0)
        {
            saida.Write(Mensagem('I', []));
        }

        foreach (var (marca, _) in secoes)
        {
            saida.Write(ResponderSecao(marca));
        }

        saida.Write(Mensagem('Z', "I"u8.ToArray()));
        return saida.ToArray();
    }

    private static byte[] ResponderSecao(string marca)
    {
        var saida = new MemoryStream();
        if (marca == "version()")
        {
            saida.Write(DescricaoLinhas([("version", OidText, 0)]));
            saida.Write(Linha(["PostgreSQL 16.0 (servidor falso)"]));
            saida.Write(Mensagem('C', CString("SELECT 1")));
        }
        else if (marca == "typnotnull")
        {
            saida.Write(DescricaoLinhas([
                ("nspname", OidText, 0), ("oid", OidText, 0), ("typname", OidText, 0),
                ("typtype", OidText, 0), ("typnotnull", OidText, 0), ("elemtypoid", OidText, 0),
            ]));
            foreach (var (oid, nome, tipo) in Tipos)
            {
                saida.Write(Linha(["pg_catalog", oid.ToString(System.Globalization.CultureInfo.InvariantCulture), nome, tipo.ToString(), "f", null]));
            }

            saida.Write(Mensagem('C', CString($"SELECT {Tipos.Length}")));
        }
        else if (marca == "-- Load enum fields")
        {
            saida.Write(DescricaoLinhas([("oid", OidText, 0), ("enumlabel", OidText, 0)]));
            saida.Write(Mensagem('C', CString("SELECT 0")));
        }
        else
        {
            saida.Write(DescricaoLinhas([("oid", OidText, 0), ("attname", OidText, 0), ("atttypid", OidText, 0)]));
            saida.Write(Mensagem('C', CString("SELECT 0")));
        }

        return saida.ToArray();
    }

    private static byte[] MontarStartupOk()
    {
        var saida = new MemoryStream();
        var autenticacao = new byte[4];
        saida.Write(Mensagem('R', autenticacao));
        foreach (var (nome, valor) in new[]
                 {
                     ("server_version", "16.0"), ("server_encoding", "UTF8"), ("client_encoding", "UTF8"),
                     ("DateStyle", "ISO, MDY"), ("integer_datetimes", "on"), ("standard_conforming_strings", "on"),
                     ("TimeZone", "UTC"),
                 })
        {
            saida.Write(Mensagem('S', [.. CString(nome), .. CString(valor)]));
        }

        var chave = new byte[8];
        BinaryPrimitives.WriteInt32BigEndian(chave, 4242);
        BinaryPrimitives.WriteInt32BigEndian(chave.AsSpan(4), 1234);
        saida.Write(Mensagem('K', chave));
        saida.Write(Mensagem('Z', "I"u8.ToArray()));
        return saida.ToArray();
    }

    private static byte[] DescricaoLinhas((string Nome, uint Oid, short Formato)[] colunas)
    {
        var corpo = new MemoryStream();
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(buffer, (short)colunas.Length);
        corpo.Write(buffer, 0, 2);
        foreach (var (nome, oid, formato) in colunas)
        {
            corpo.Write(CString(nome));
            BinaryPrimitives.WriteInt32BigEndian(buffer, 0);
            corpo.Write(buffer, 0, 4);                 // tabela
            BinaryPrimitives.WriteInt16BigEndian(buffer, 0);
            corpo.Write(buffer, 0, 2);                 // atributo
            BinaryPrimitives.WriteUInt32BigEndian(buffer, oid);
            corpo.Write(buffer, 0, 4);                 // tipo
            BinaryPrimitives.WriteInt16BigEndian(buffer, oid == OidText ? (short)-1 : (short)4);
            corpo.Write(buffer, 0, 2);                 // tamanho
            BinaryPrimitives.WriteInt32BigEndian(buffer, -1);
            corpo.Write(buffer, 0, 4);                 // typmod
            BinaryPrimitives.WriteInt16BigEndian(buffer, formato);
            corpo.Write(buffer, 0, 2);                 // formato (0 texto, 1 binario)
        }

        return Mensagem('T', corpo.ToArray());
    }

    private static byte[] Linha(string?[] valores)
    {
        var corpo = new MemoryStream();
        var buffer = new byte[4];
        BinaryPrimitives.WriteInt16BigEndian(buffer, (short)valores.Length);
        corpo.Write(buffer, 0, 2);
        foreach (var valor in valores)
        {
            if (valor is null)
            {
                BinaryPrimitives.WriteInt32BigEndian(buffer, -1);
                corpo.Write(buffer, 0, 4);
                continue;
            }

            var bytes = Encoding.UTF8.GetBytes(valor);
            BinaryPrimitives.WriteInt32BigEndian(buffer, bytes.Length);
            corpo.Write(buffer, 0, 4);
            corpo.Write(bytes);
        }

        return Mensagem('D', corpo.ToArray());
    }

    private byte[] MontarErro(string severidade)
    {
        var campos = new MemoryStream();
        void Campo(char tipo, string valor)
        {
            campos.WriteByte((byte)tipo);
            campos.Write(CString(valor));
        }

        Campo('S', severidade);
        Campo('V', severidade);
        Campo('C', _sqlState!);
        Campo('M', _mensagem);
        Campo('D', _detalhe);
        Campo('H', _dica);
        campos.WriteByte(0);
        return Mensagem('E', campos.ToArray());
    }

    private static byte[] Mensagem(char tipo, byte[] payload)
    {
        var mensagem = new byte[1 + 4 + payload.Length];
        mensagem[0] = (byte)tipo;
        BinaryPrimitives.WriteInt32BigEndian(mensagem.AsSpan(1), 4 + payload.Length);
        payload.CopyTo(mensagem, 5);
        return mensagem;
    }

    private static byte[] CString(string valor) => [.. Encoding.UTF8.GetBytes(valor), 0];

    private static string LerCString(byte[] dados, int inicio)
    {
        var fim = Array.IndexOf(dados, (byte)0, inicio);
        return Encoding.UTF8.GetString(dados, inicio, (fim < 0 ? dados.Length : fim) - inicio);
    }
}
