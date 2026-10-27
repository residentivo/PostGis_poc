namespace ApiMapas.Aplicacao;

/// <summary>Resultado da consulta, traduzido pela API em 200 / 400 / 404 (o 500 vira excecao).</summary>
public abstract record ResultadoConsulta
{
    private ResultadoConsulta()
    {
    }

    /// <summary>Id fora da regra (400). Nao carrega o valor recebido (RA-10/RA-11).</summary>
    public sealed record IdInvalido : ResultadoConsulta
    {
        public static readonly IdInvalido Instancia = new();
    }

    /// <summary>Sem linha para o id, ou com a coluna NULL (404).</summary>
    public sealed record NaoEncontrado(int Id, string Coluna) : ResultadoConsulta;

    /// <summary>Geometria encontrada (200).</summary>
    public sealed record Sucesso(RespostaGeometriaDto Resposta) : ResultadoConsulta;
}
