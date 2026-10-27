namespace ApiMapas.Dominio;

/// <summary>Resultado fechado da leitura de uma geometria por id (Q1/Q2; D-N7, RS-23).</summary>
public abstract record LeituraMapa
{
    private LeituraMapa()
    {
    }

    /// <summary>Nenhuma linha para o id (404).</summary>
    public sealed record NaoEncontrada : LeituraMapa
    {
        public static readonly NaoEncontrada Instancia = new();
    }

    /// <summary>Uma linha, mas a coluna da rota e NULL (404).</summary>
    public sealed record ColunaVazia : LeituraMapa
    {
        public static readonly ColunaVazia Instancia = new();
    }

    /// <summary>Duas ou mais linhas para o id: a migracao para PRIMARY KEY nao foi aplicada (500 + log Error).</summary>
    public sealed record Duplicada : LeituraMapa
    {
        public static readonly Duplicada Instancia = new();
    }

    /// <summary>Uma linha com a coluna preenchida (200).</summary>
    public sealed record Encontrada(GeometriaMapa Geometria) : LeituraMapa;
}
