using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;

namespace DropSort.Infrastructure.Trakt;

internal static class TraktRequestHeaders
{
    // Trakt security can reject unidentified clients. Use the entry application's numeric version;
    // never include account information or copy a browser User-Agent.
    internal static void IdentifyApplication(HttpRequestMessage request)
    {
        var version = Assembly.GetEntryAssembly()?.GetName().Version?.ToString() ?? "0.0.0";
        request.Headers.UserAgent.Add(new ProductInfoHeaderValue("DropSort", version));
    }
}
