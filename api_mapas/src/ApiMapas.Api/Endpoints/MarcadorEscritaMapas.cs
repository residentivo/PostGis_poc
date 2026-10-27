namespace ApiMapas.Api.Endpoints;

/// <summary>
/// Metadado dos endpoints de escrita (PUT e POST). O filtro de origem (403, RS-22) so atua nos
/// endpoints com este marcador, e o rate limiter o usa para aplicar os limites de escrita
/// compartilhados por PUT e POST: por IP (D-P4), global sem particao (RS-25) e simultaneas (R-2),
/// alem do limite global de requisicoes.
/// </summary>
public sealed class MarcadorEscritaMapas
{
    public static readonly MarcadorEscritaMapas Instancia = new();

    private MarcadorEscritaMapas()
    {
    }
}
