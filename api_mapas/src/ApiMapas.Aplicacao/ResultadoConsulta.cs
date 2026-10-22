namespace ApiMapas.Aplicacao;

/// <summary>Resultado da consulta, traduzido pela API em 200 / 400 / 404.</summary>
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

    /// <summary>Nenhuma linha nao nula para o id na coluna (404).</summary>
    public sealed record NaoEncontrado(int Id, string Coluna) : ResultadoConsulta;

    /// <summary>Geometrias encontradas (200).</summary>
    public sealed record Sucesso(RespostaGeometriasDto Resposta) : ResultadoConsulta;
}
