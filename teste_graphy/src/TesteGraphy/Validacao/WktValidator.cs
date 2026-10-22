using System.Globalization;
using TesteGraphy.Diagnostico;

namespace TesteGraphy.Validacao;

/// <summary>
/// Validacao LOCAL de WKT, sem banco (R4). Tokenizador manual, sem Regex.
/// Regras: nao vazio; ate 10.000 caracteres; sem prefixo SRID=; tipos da lista
/// permitida (case-insensitive); somente 2D (sem Z/M/ZM) e sem EMPTY; parenteses
/// balanceados com profundidade maxima 10; coordenadas InvariantCulture finitas;
/// longitude em [-180,180] e latitude em [-90,90]; nada apos a geometria.
/// A validacao definitiva de geometria (ST_IsValid) e feita no banco em seguida.
/// </summary>
public sealed class WktValidator
{
    public const int TamanhoMaximo = 10_000;
    public const int ProfundidadeMaxima = 10;

    public const string Point = "POINT";
    public const string LineString = "LINESTRING";
    public const string Polygon = "POLYGON";
    public const string MultiPoint = "MULTIPOINT";
    public const string MultiLineString = "MULTILINESTRING";
    public const string MultiPolygon = "MULTIPOLYGON";
    public const string GeometryCollection = "GEOMETRYCOLLECTION";

    /// <summary>Tipos de geometria aceitos.</summary>
    public static readonly IReadOnlyList<string> TiposPermitidos =
    [
        Point, LineString, Polygon, MultiPoint, MultiLineString, MultiPolygon, GeometryCollection,
    ];

    private readonly ILog _log;

    public WktValidator(ILog? log = null)
    {
        _log = log ?? LogNulo.Instancia;
    }

    /// <summary>Valida o WKT e devolve o resultado; nunca lanca excecao por WKT invalido.</summary>
    public ResultadoValidacao Validar(string? wkt)
    {
        _log.Passo("Validacao local do WKT: inicio.");

        var resultado = ValidarInterno(wkt);

        if (resultado.Valido)
        {
            _log.Passo($"Validacao local do WKT: ok (tipo {resultado.Tipo}).");
        }
        else
        {
            _log.Passo($"Validacao local do WKT: rejeitado ({resultado.Erro}).");
        }

        return resultado;
    }

    private static ResultadoValidacao ValidarInterno(string? wkt)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return ResultadoValidacao.Falha("WKT nulo ou vazio");
        }

        if (wkt.Length > TamanhoMaximo)
        {
            return ResultadoValidacao.Falha($"WKT excede o limite de {TamanhoMaximo} caracteres");
        }

        var texto = wkt.Trim();

        if (texto.StartsWith("SRID=", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoValidacao.Falha("prefixo SRID= (EWKT) nao permitido; o SRID 4326 e aplicado pelo programa");
        }

        var erroParenteses = VerificarParenteses(texto);
        if (erroParenteses is not null)
        {
            return ResultadoValidacao.Falha(erroParenteses);
        }

        try
        {
            var leitor = new Leitor(texto);
            var tipo = leitor.LerGeometria();
            leitor.PularEspacos();
            if (!leitor.Fim)
            {
                throw new FormatoWktException($"conteudo inesperado apos a geometria na posicao {leitor.Posicao}");
            }

            return ResultadoValidacao.Ok(tipo);
        }
        catch (FormatoWktException ex)
        {
            return ResultadoValidacao.Falha(ex.Message);
        }
    }

    /// <summary>Confere balanceamento e profundidade maxima antes da analise recursiva.</summary>
    private static string? VerificarParenteses(string texto)
    {
        var profundidade = 0;

        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (c == '(')
            {
                profundidade++;
                if (profundidade > ProfundidadeMaxima)
                {
                    return $"profundidade de parenteses excede o maximo de {ProfundidadeMaxima}";
                }
            }
            else if (c == ')')
            {
                profundidade--;
                if (profundidade < 0)
                {
                    return $"parenteses desbalanceados (')' sem abertura na posicao {i + 1})";
                }
            }
        }

        if (profundidade != 0)
        {
            return "parenteses desbalanceados (falta ')')";
        }

        return null;
    }

    /// <summary>Erro interno de formato; a mensagem nunca contem o WKT de entrada.</summary>
    private sealed class FormatoWktException(string mensagem) : Exception(mensagem);

    /// <summary>Analisador descendente recursivo; recursao limitada pela profundidade ja verificada.</summary>
    private sealed class Leitor(string texto)
    {
        private int _pos;

        public bool Fim => _pos >= texto.Length;

        /// <summary>Posicao atual (base 1) para mensagens de erro.</summary>
        public int Posicao => _pos + 1;

        public string LerGeometria()
        {
            PularEspacos();
            var inicio = Posicao;
            var nome = LerPalavra();
            if (nome.Length == 0)
            {
                throw new FormatoWktException($"tipo de geometria esperado na posicao {inicio}");
            }

            var tipo = nome.ToUpperInvariant();
            if (!TiposPermitidos.Contains(tipo))
            {
                throw new FormatoWktException(
                    $"tipo de geometria nao permitido na posicao {inicio} (aceitos: {string.Join(", ", TiposPermitidos)})");
            }

            PularEspacos();
            if (!Fim && char.IsAsciiLetter(texto[_pos]))
            {
                var posModificador = Posicao;
                var modificador = LerPalavra().ToUpperInvariant();
                throw modificador switch
                {
                    "Z" or "M" or "ZM" => new FormatoWktException("apenas geometrias 2D sao aceitas (Z, M e ZM nao sao permitidos)"),
                    "EMPTY" => new FormatoWktException("geometria EMPTY nao e permitida"),
                    _ => new FormatoWktException($"palavra inesperada na posicao {posModificador}"),
                };
            }

            switch (tipo)
            {
                case Point:
                    Abrir();
                    LerCoordenada();
                    Fechar();
                    break;

                case LineString:
                    LerListaCoordenadas(2, "LINESTRING");
                    break;

                case Polygon:
                    LerPoligono();
                    break;

                case MultiPoint:
                    Abrir();
                    do
                    {
                        LerPontoDeMultiPoint();
                    }
                    while (Virgula());
                    Fechar();
                    break;

                case MultiLineString:
                    Abrir();
                    do
                    {
                        LerListaCoordenadas(2, "LINESTRING");
                    }
                    while (Virgula());
                    Fechar();
                    break;

                case MultiPolygon:
                    Abrir();
                    do
                    {
                        LerPoligono();
                    }
                    while (Virgula());
                    Fechar();
                    break;

                case GeometryCollection:
                    Abrir();
                    do
                    {
                        LerGeometria();
                    }
                    while (Virgula());
                    Fechar();
                    break;
            }

            return tipo;
        }

        /// <summary>Avanca espacos em branco; retorna true se avancou ao menos um.</summary>
        public bool PularEspacos()
        {
            var inicio = _pos;
            while (!Fim && char.IsWhiteSpace(texto[_pos]))
            {
                _pos++;
            }

            return _pos > inicio;
        }

        private string LerPalavra()
        {
            var inicio = _pos;
            while (!Fim && char.IsAsciiLetter(texto[_pos]))
            {
                _pos++;
            }

            return texto[inicio.._pos];
        }

        private void Abrir()
        {
            PularEspacos();
            if (Fim || texto[_pos] != '(')
            {
                throw new FormatoWktException($"esperado '(' na posicao {Posicao}");
            }

            _pos++;
        }

        private void Fechar()
        {
            PularEspacos();
            if (Fim || texto[_pos] != ')')
            {
                if (!Fim && EhCaractereNumerico(texto[_pos]))
                {
                    throw new FormatoWktException($"apenas 2 coordenadas (X Y) por ponto sao aceitas (posicao {Posicao})");
                }

                throw new FormatoWktException($"esperado ')' ou ',' na posicao {Posicao}");
            }

            _pos++;
        }

        private bool Virgula()
        {
            PularEspacos();
            if (!Fim && texto[_pos] == ',')
            {
                _pos++;
                return true;
            }

            return false;
        }

        private (double X, double Y) LerCoordenada()
        {
            PularEspacos();
            var x = LerNumero();
            if (!PularEspacos())
            {
                throw new FormatoWktException(
                    $"coordenada deve ter X e Y separados por espaco na posicao {Posicao} (virgula decimal nao e aceita; use ponto)");
            }

            var posY = Posicao;
            var y = LerNumero();

            if (x < -180d || x > 180d)
            {
                throw new FormatoWktException($"longitude fora da faixa [-180, 180] antes da posicao {posY}");
            }

            if (y < -90d || y > 90d)
            {
                throw new FormatoWktException($"latitude fora da faixa [-90, 90] na posicao {posY}");
            }

            return (x, y);
        }

        private double LerNumero()
        {
            var inicio = _pos;
            while (!Fim && EhCaractereNumerico(texto[_pos]))
            {
                _pos++;
            }

            if (_pos == inicio)
            {
                if (!Fim && char.IsAsciiLetter(texto[_pos]))
                {
                    var palavra = LerPalavra();
                    if (palavra.Equals("NaN", StringComparison.OrdinalIgnoreCase)
                        || palavra.StartsWith("Inf", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new FormatoWktException($"numero nao finito (NaN/Infinity) na posicao {inicio + 1}");
                    }
                }

                throw new FormatoWktException($"numero esperado na posicao {inicio + 1}");
            }

            var token = texto.AsSpan(inicio, _pos - inicio);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor))
            {
                throw new FormatoWktException($"numero invalido na posicao {inicio + 1}");
            }

            if (!double.IsFinite(valor))
            {
                throw new FormatoWktException($"numero nao finito (NaN/Infinity) na posicao {inicio + 1}");
            }

            return valor;
        }

        private static bool EhCaractereNumerico(char c) =>
            char.IsAsciiDigit(c) || c is '+' or '-' or '.' or 'e' or 'E';

        private (int Quantidade, (double X, double Y) Primeiro, (double X, double Y) Ultimo) LerListaCoordenadas(int minimo, string contexto)
        {
            Abrir();
            var primeiro = LerCoordenada();
            var ultimo = primeiro;
            var quantidade = 1;
            while (Virgula())
            {
                ultimo = LerCoordenada();
                quantidade++;
            }

            Fechar();

            if (quantidade < minimo)
            {
                throw new FormatoWktException($"{contexto} exige ao menos {minimo} pontos (posicao {Posicao})");
            }

            return (quantidade, primeiro, ultimo);
        }

        private void LerPoligono()
        {
            Abrir();
            do
            {
                var anel = LerListaCoordenadas(4, "anel de POLYGON");
                if (anel.Primeiro != anel.Ultimo)
                {
                    throw new FormatoWktException($"anel de POLYGON deve ser fechado (primeiro ponto = ultimo) (posicao {Posicao})");
                }
            }
            while (Virgula());
            Fechar();
        }

        private void LerPontoDeMultiPoint()
        {
            PularEspacos();
            if (!Fim && texto[_pos] == '(')
            {
                _pos++;
                LerCoordenada();
                Fechar();
            }
            else
            {
                LerCoordenada();
            }
        }
    }
}
