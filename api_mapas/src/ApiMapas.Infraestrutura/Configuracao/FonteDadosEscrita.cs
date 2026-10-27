using Npgsql;

namespace ApiMapas.Infraestrutura.Configuracao;

/// <summary>
/// Pool de conexoes de ESCRITA (role api_mapas_escrita). Tipo proprio para que o container de DI
/// nunca confunda com o <see cref="NpgsqlDataSource"/> de leitura (CP-10: o PUT usa so a
/// conexao de escrita e o GET so a de leitura).
/// </summary>
public sealed class FonteDadosEscrita : IAsyncDisposable, IDisposable
{
    public FonteDadosEscrita(NpgsqlDataSource dataSource)
    {
        ArgumentNullException.ThrowIfNull(dataSource);
        DataSource = dataSource;
    }

    public NpgsqlDataSource DataSource { get; }

    public ValueTask DisposeAsync() => DataSource.DisposeAsync();

    public void Dispose() => DataSource.Dispose();
}
