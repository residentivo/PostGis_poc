namespace ApiMapas.Dominio.Geometrias;

/// <summary>
/// Value object de uma geometria recebida no PUT ou no POST, ja validada localmente para a coluna (D-P5,
/// D-P7). So pode ser criada por <see cref="TentarCriar"/>.
/// </summary>
public sealed class GeometriaEntrada
{
    private GeometriaEntrada(string wkt, int srid, string tipo)
    {
        Wkt = wkt;
        Srid = srid;
        Tipo = tipo;
    }

    /// <summary>WKT recebido (sem os espacos ASCII das pontas). NUNCA deve ir para log nem resposta.</summary>
    public string Wkt { get; }

    /// <summary>SRID aceito para a coluna.</summary>
    public int Srid { get; }

    /// <summary>Tipo raiz lido localmente (ex.: POINT), comparado com o GeometryType do banco.</summary>
    public string Tipo { get; }

    /// <summary>
    /// Valida uma geometria para a coluna. Ordem (RP-14): primeiro o SRID contra a politica da
    /// coluna; so depois o WKT (caracteres, tipo, 2D, faixas do SRID).
    /// </summary>
    /// <param name="wkt">WKT recebido.</param>
    /// <param name="srid">SRID recebido (obrigatorio no corpo).</param>
    /// <param name="coluna">Coluna fixada pelo endpoint.</param>
    /// <param name="geometria">Geometria valida, ou null.</param>
    /// <param name="motivo">Motivo fixo da rejeicao, ou null.</param>
    public static bool TentarCriar(string? wkt, int srid, ColunaMapa coluna, out GeometriaEntrada? geometria, out string? motivo)
    {
        geometria = null;

        var faixa = PoliticaSridColuna.Aceita(coluna, srid) ? LimitesSrid.Para(srid) : null;
        if (faixa is null)
        {
            motivo = MotivosGeometria.SridNaoPermitido(coluna);
            return false;
        }

        var resultado = WktValidator.Validar(wkt, faixa);
        if (!resultado.Valido)
        {
            motivo = resultado.Motivo;
            return false;
        }

        motivo = null;
        geometria = new GeometriaEntrada(wkt!.Trim(' '), srid, resultado.Tipo!);
        return true;
    }
}
