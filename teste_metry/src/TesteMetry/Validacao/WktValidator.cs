using System.Globalization;
using TesteMetry.Diagnostico;

namespace TesteMetry.Validacao;

/// <summary>
/// Validacao LOCAL de WKT, sem banco (D-M7, RQ-3, RQ-5). Tokenizador manual, sem Regex.
/// Regras: nao vazio; ate 10.000 caracteres; somente caracteres da lista permitida
/// (letras dos tipos, digitos, '.', '-', '+', espaco ASCII, parenteses e virgula), o que
/// rejeita EWKT (';' e '='), HEX/WKB, controles e espacos Unicode; tipos da lista
/// permitida (case-insensitive); somente 2D (sem Z/M/ZM) e sem EMPTY; parenteses
/// balanceados com profundidade maxima 10; coordenadas InvariantCulture finitas e dentro
/// da faixa do SRID configurado (<see cref="LimitesSrid"/>) em TODOS os niveis; nada apos
/// a geometria. A validacao definitiva (ST_IsValid) e feita no banco em seguida.
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

    /// <summary>Letras usadas pelos nomes dos tipos permitidos (unicas letras aceitas no WKT).</summary>
    private static readonly string LetrasPermitidas =
        new(TiposPermitidos.SelectMany(t => t).Distinct().Order().ToArray());

    private readonly FaixaSrid _faixa;
    private readonly ILog _log;

    /// <summary>Cria o validador para o SRID ja validado pela configuracao (RQ-1/RQ-5).</summary>
    /// <exception cref="ArgumentOutOfRangeException">SRID fora da lista permitida.</exception>
    public WktValidator(int srid, ILog? log = null)
    {
        _faixa = LimitesSrid.Para(srid);
        _log = log ?? LogNulo.Instancia;
    }

    /// <summary>SRID cujas faixas sao aplicadas.</summary>
    public int Srid => _faixa.Srid;

    /// <summary>Valida o WKT e devolve o resultado; nunca lanca excecao por WKT invalido.</summary>
    public ResultadoValidacao Validar(string? wkt)
    {
        _log.Passo($"Validacao local do WKT (SRID {Srid.ToString(CultureInfo.InvariantCulture)}): inicio.");

        var resultado = ValidarInterno(wkt, _faixa);

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

    private static ResultadoValidacao ValidarInterno(string? wkt, FaixaSrid faixa)
    {
        if (string.IsNullOrWhiteSpace(wkt))
        {
            return ResultadoValidacao.Falha("WKT nulo ou vazio");
        }

        if (wkt.Length > TamanhoMaximo)
        {
            return ResultadoValidacao.Falha($"WKT excede o limite de {TamanhoMaximo} caracteres");
        }

        // Sem Trim() padrao: so o espaco ASCII e removido; espacos Unicode sao rejeitados abaixo.
        var texto = wkt.Trim(' ');

        if (texto.StartsWith("SRID", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoValidacao.Falha("prefixo SRID= (EWKT) nao permitido; o SRID e aplicado pelo programa (PG_SRID)");
        }

        // RQ-3: lista permitida de caracteres. Rejeita EWKT (';', '=') mesmo sem "SRID=" no inicio.
        var erroCaractere = VerificarCaracteres(texto);
        if (erroCaractere is not null)
        {
            return ResultadoValidacao.Falha(erroCaractere);
        }

        var erroParenteses = VerificarParenteses(texto);
        if (erroParenteses is not null)
        {
            return ResultadoValidacao.Falha(erroParenteses);
        }

        try
        {
            var leitor = new Leitor(texto, faixa);
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

    /// <summary>
    /// RQ-3: aceita somente letras dos tipos permitidos (maiusculas ou minusculas), digitos
    /// ASCII, '.', '-', '+', espaco ASCII, '(', ')' e ','. A mensagem cita a posicao e o
    /// codigo do caractere (U+XXXX), nunca o caractere em si nem o WKT.
    /// </summary>
    private static string? VerificarCaracteres(string texto)
    {
        for (var i = 0; i < texto.Length; i++)
        {
            var c = texto[i];
            if (char.IsAsciiDigit(c) || c is '.' or '-' or '+' or ' ' or '(' or ')' or ',')
            {
                continue;
            }

            if (char.IsAsciiLetter(c) && LetrasPermitidas.Contains(char.ToUpperInvariant(c), StringComparison.Ordinal))
            {
                continue;
            }

            var codigo = ((int)c).ToString("X4", CultureInfo.InvariantCulture);
            var dica = c switch
            {
                'Z' or 'z' => " (apenas geometrias 2D sao aceitas: Z, M e ZM nao sao permitidos)",
                ';' or '=' => " (EWKT nao permitido; o SRID e aplicado pelo programa)",
                _ when char.IsControl(c) => " (caractere de controle)",
                _ when char.IsWhiteSpace(c) => " (somente o espaco comum e aceito)",
                _ => string.Empty,
            };

            return $"caractere nao permitido (U+{codigo}) na posicao {i + 1}{dica}";
        }

        return null;
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
    private sealed class Leitor(string texto, FaixaSrid faixa)
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
            while (!Fim && texto[_pos] == ' ')
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

            // RQ-5: faixa do SRID configurado em toda coordenada lida (qualquer nivel), inclusiva.
            var srid = faixa.Srid.ToString(CultureInfo.InvariantCulture);
            if (!faixa.XDentro(x))
            {
                throw new FormatoWktException(
                    $"{faixa.NomeX} fora da faixa {faixa.DescreverX()} do SRID {srid} antes da posicao {posY}");
            }

            if (!faixa.YDentro(y))
            {
                throw new FormatoWktException(
                    $"{faixa.NomeY} fora da faixa {faixa.DescreverY()} do SRID {srid} na posicao {posY}");
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
                // MF-1: letra (inclusive NaN/Infinity por extenso) na posicao de numero.
                throw new FormatoWktException($"numero esperado na posicao {inicio + 1}");
            }

            var token = texto.AsSpan(inicio, _pos - inicio);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor))
            {
                throw new FormatoWktException($"numero invalido na posicao {inicio + 1}");
            }

            // R-1: cobre estouro de expoente (ex.: 1e999); NaN/Infinity por extenso ja sao
            // barrados pela lista de caracteres (RQ-3).
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
