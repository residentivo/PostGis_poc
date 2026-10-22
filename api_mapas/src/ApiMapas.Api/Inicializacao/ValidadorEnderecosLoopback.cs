using System.Net;
using Microsoft.AspNetCore.Http;

namespace ApiMapas.Api.Inicializacao;

/// <summary>
/// RA-5: a API nao tem autenticacao, portanto so pode ouvir em loopback. Aceita apenas
/// "localhost", IPs de loopback (127.x.x.x, [::1]) e unix sockets. Recusa 0.0.0.0, [::], *, +,
/// IPs de LAN e qualquer outro nome de host (o Kestrel ouviria em todas as interfaces).
/// </summary>
public static class ValidadorEnderecosLoopback
{
    /// <summary>
    /// Valida uma lista de URLs separadas por ';' (formato de "urls"/ASPNETCORE_URLS).
    /// </summary>
    /// <returns>null se todas forem loopback; senao, a primeira URL recusada.</returns>
    public static string? PrimeiraNaoLoopback(string? urls)
    {
        if (string.IsNullOrWhiteSpace(urls))
        {
            return null;
        }

        foreach (var url in urls.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!EhLoopback(url))
            {
                return url;
            }
        }

        return null;
    }

    /// <summary>True se a URL de escuta e loopback (ou unix socket).</summary>
    public static bool EhLoopback(string url)
    {
        BindingAddress endereco;
        try
        {
            endereco = BindingAddress.Parse(url);
        }
        catch (FormatException)
        {
            return false;
        }

        if (endereco.IsUnixPipe)
        {
            return true;
        }

        var host = endereco.Host;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var semColchetes = host.StartsWith('[') && host.EndsWith(']') ? host[1..^1] : host;
        return IPAddress.TryParse(semColchetes, out var ip) && IPAddress.IsLoopback(ip);
    }
}
