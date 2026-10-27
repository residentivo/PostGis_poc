using System.Globalization;

namespace ApiMapas.Dominio.Geometrias;

/// <summary>Resultado da validacao local de um WKT.</summary>
public sealed class ResultadoValidacaoWkt
{
    private ResultadoValidacaoWkt(bool valido, string? motivo, string? tipo)
    {
        Valido = valido;
        Motivo = motivo;
        Tipo = tipo;
    }

    /// <summary>True quando o WKT passou em todas as regras locais.</summary>
    public bool Valido { get; }

    /// <summary>
    /// Motivo da rejeicao, da lista FIXA do validador (so posicao, U+XXXX e constantes; nunca o
    /// WKT nem trecho dele - RP-14). Null quando valido.
    /// </summary>
    public string? Motivo { get; }

    /// <summary>Tipo da geometria raiz em maiusculas (ex.: POINT) quando valido.</summary>
    public string? Tipo { get; }

    internal static ResultadoValidacaoWkt Ok(string tipo) => new(true, null, tipo);

    internal static ResultadoValidacaoWkt Falha(string motivo) => new(false, motivo, null);
}

/// <summary>
/// Validacao LOCAL de WKT, sem banco e sem log (D-P5, D-P7, RP-14). Tokenizador manual, sem Regex.
/// Origem: teste_metry/src/TesteMetry/Validacao/WktValidator.cs (copia adaptada: estatica, sem
/// ILog, SRID/faixa recebidos por chamada e mensagens fixas; o dominio NAO referencia teste_metry).
/// Regras: nao vazio; ate 10.000 caracteres; somente caracteres da lista permitida (letras dos
/// tipos, digitos, '.', '-', '+', espaco ASCII, parenteses e virgula), o que rejeita EWKT (';' e
/// '='), HEX/WKB, controles e espacos Unicode; tipos da lista permitida (sem diferenciar
/// maiusculas); somente 2D (sem Z/M/ZM) e sem EMPTY; parenteses balanceados com profundidade
/// maxima 10; coordenadas InvariantCulture finitas e dentro da faixa do SRID em TODOS os niveis;
/// nada depois da geometria. A validacao definitiva (ST_IsValid/GeometryType) e feita no banco.
/// </summary>
public static class WktValidator
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

    /// <summary>
    /// Valida o WKT com a faixa do SRID JA ACEITO para a coluna (o SRID e verificado antes, em
    /// <see cref="GeometriaEntrada.TentarCriar"/> - RP-14). Nunca lanca excecao por WKT invalido.
    /// </summary>
    public static ResultadoValidacaoWkt Validar(string? wkt, FaixaSrid faixa)
    {
        ArgumentNullException.ThrowIfNull(faixa);

        if (string.IsNullOrWhiteSpace(wkt))
        {
            return ResultadoValidacaoWkt.Falha("WKT nulo ou vazio");
        }

        if (wkt.Length > TamanhoMaximo)
        {
            return ResultadoValidacaoWkt.Falha($"WKT excede o limite de {TamanhoMaximo.ToString(CultureInfo.InvariantCulture)} caracteres");
        }

        // Sem Trim() padrao: so o espaco ASCII e removido; espacos Unicode sao rejeitados abaixo.
        var texto = wkt.Trim(' ');

        if (texto.StartsWith("SRID", StringComparison.OrdinalIgnoreCase))
        {
            return ResultadoValidacaoWkt.Falha("prefixo SRID= (EWKT) não permitido; informe o SRID no campo srid");
        }

        // Lista permitida de caracteres. Rejeita EWKT (';', '=') mesmo sem "SRID=" no inicio.
        var erroCaractere = VerificarCaracteres(texto);
        if (erroCaractere is not null)
        {
            return ResultadoValidacaoWkt.Falha(erroCaractere);
        }

        var erroParenteses = VerificarParenteses(texto);
        if (erroParenteses is not null)
        {
            return ResultadoValidacaoWkt.Falha(erroParenteses);
        }

        try
        {
            var leitor = new Leitor(texto, faixa);
            var tipo = leitor.LerGeometria();
            leitor.PularEspacos();
            if (!leitor.Fim)
            {
                throw new FormatoWktException($"conteúdo inesperado após a geometria na posição {Pos(leitor.Posicao)}");
            }

            return ResultadoValidacaoWkt.Ok(tipo);
        }
        catch (FormatoWktException ex)
        {
            return ResultadoValidacaoWkt.Falha(ex.Message);
        }
    }

    private static string Pos(int posicao) => posicao.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Aceita somente letras dos tipos permitidos (maiusculas ou minusculas), digitos ASCII, '.',
    /// '-', '+', espaco ASCII, '(', ')' e ','. A mensagem cita a posicao e o codigo do caractere
    /// (U+XXXX), nunca o caractere em si nem o WKT.
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
                'Z' or 'z' => " (apenas geometrias 2D são aceitas: Z, M e ZM não são permitidos)",
                ';' or '=' => " (EWKT não permitido; informe o SRID no campo srid)",
                _ when char.IsControl(c) => " (caractere de controle)",
                _ when char.IsWhiteSpace(c) => " (somente o espaço comum é aceito)",
                _ => string.Empty,
            };

            return $"caractere não permitido (U+{codigo}) na posição {Pos(i + 1)}{dica}";
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
                    return $"profundidade de parênteses excede o máximo de {ProfundidadeMaxima.ToString(CultureInfo.InvariantCulture)}";
                }
            }
            else if (c == ')')
            {
                profundidade--;
                if (profundidade < 0)
                {
                    return $"parênteses desbalanceados (')' sem abertura na posição {Pos(i + 1)})";
                }
            }
        }

        if (profundidade != 0)
        {
            return "parênteses desbalanceados (falta ')')";
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
                throw new FormatoWktException($"tipo de geometria esperado na posição {Pos(inicio)}");
            }

            var tipo = nome.ToUpperInvariant();
            if (!TiposPermitidos.Contains(tipo))
            {
                throw new FormatoWktException(
                    $"tipo de geometria não permitido na posição {Pos(inicio)} (aceitos: {string.Join(", ", TiposPermitidos)})");
            }

            PularEspacos();
            if (!Fim && char.IsAsciiLetter(texto[_pos]))
            {
                var posModificador = Posicao;
                var modificador = LerPalavra().ToUpperInvariant();
                throw modificador switch
                {
                    "Z" or "M" or "ZM" => new FormatoWktException("apenas geometrias 2D são aceitas (Z, M e ZM não são permitidos)"),
                    "EMPTY" => new FormatoWktException("geometria EMPTY não é permitida"),
                    _ => new FormatoWktException($"palavra inesperada na posição {Pos(posModificador)}"),
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
                throw new FormatoWktException($"esperado '(' na posição {Pos(Posicao)}");
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
                    throw new FormatoWktException($"apenas 2 coordenadas (X Y) por ponto são aceitas (posição {Pos(Posicao)})");
                }

                throw new FormatoWktException($"esperado ')' ou ',' na posição {Pos(Posicao)}");
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
                    $"coordenada deve ter X e Y separados por espaço na posição {Pos(Posicao)} (vírgula decimal não é aceita; use ponto)");
            }

            var posY = Posicao;
            var y = LerNumero();

            // D-P5: faixa do SRID em toda coordenada lida (qualquer nivel), inclusiva.
            var srid = faixa.Srid.ToString(CultureInfo.InvariantCulture);
            if (!faixa.XDentro(x))
            {
                throw new FormatoWktException(
                    $"{faixa.NomeX} fora da faixa {faixa.DescreverX()} do SRID {srid} antes da posição {Pos(posY)}");
            }

            if (!faixa.YDentro(y))
            {
                throw new FormatoWktException(
                    $"{faixa.NomeY} fora da faixa {faixa.DescreverY()} do SRID {srid} na posição {Pos(posY)}");
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
                // Letra (inclusive NaN/Infinity por extenso) na posicao de numero.
                throw new FormatoWktException($"número esperado na posição {Pos(inicio + 1)}");
            }

            var token = texto.AsSpan(inicio, _pos - inicio);
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var valor))
            {
                throw new FormatoWktException($"número inválido na posição {Pos(inicio + 1)}");
            }

            // Estouro de expoente (ex.: 1e999); NaN/Infinity por extenso ja sao barrados pela lista de caracteres.
            if (!double.IsFinite(valor))
            {
                throw new FormatoWktException($"número não finito (NaN/Infinity) na posição {Pos(inicio + 1)}");
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
                throw new FormatoWktException(
                    $"{contexto} exige ao menos {minimo.ToString(CultureInfo.InvariantCulture)} pontos (posição {Pos(Posicao)})");
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
                    throw new FormatoWktException($"anel de POLYGON deve ser fechado (primeiro ponto = último) (posição {Pos(Posicao)})");
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
