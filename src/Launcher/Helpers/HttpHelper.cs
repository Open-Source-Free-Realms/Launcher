using Launcher.Handlers;
using Launcher.Models;
using NLog;
using System.Net.Http;
using System.Net.Mime;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace Launcher.Helpers;

public static class HttpHelper
{
    private static readonly Logger _logger = LogManager.GetCurrentClassLogger();
    private static readonly HttpClient _httpClient = CreateHttpClient();

    public static HttpClient CreateHttpClient()
    {
        var httpClient = new HttpClient(new HttpLoggingHandler(new HttpClientHandler()
        {
            AllowAutoRedirect = true
        }));

        var userAgent = $"{App.GetText("Text.Title")} v{App.CurrentVersion}";

        httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);

        return httpClient;
    }

    public static async Task<(bool Success, string Error, ServerManifest? ServerManifest)> GetServerManifestAsync(string serverUrl)
    {
        var serverManifestUri = UriHelper.JoinUriPaths(serverUrl, ServerManifest.FileName.ToLower());

        var response = await _httpClient.GetAsync(serverManifestUri);

        if (!response.IsSuccessStatusCode)
        {
            var error = $"""
                         Failed to get server manifest.
                         Http Error: {response.ReasonPhrase}
                         """;

            _logger.Error(error);

            return (false, error, null);
        }

        if (response.Content.Headers.ContentType?.MediaType is not MediaTypeNames.Text.Xml and not MediaTypeNames.Application.Xml)
        {
            var error = $"""
                         Failed to get server manifest, invalid format.
                         Content Type: {response.Content.Headers.ContentType}
                         """;

            _logger.Error(error);

            return (false, error, null);
        }

        using var contentStream = await response.Content.ReadAsStreamAsync();

        var xmlDocument = XDocument.Load(contentStream);

        if (!int.TryParse(xmlDocument.Root?.Attribute("version")?.Value, out int version) || version != ServerManifest.ManifestVersion)
        {
            var error = $"""
                         Failed to get server manifest, invalid version.
                         """;

            _logger.Error(error);

            return (false, error, null);
        }

        contentStream.Position = 0;

        if (!XmlHelper.TryDeserialize<ServerManifest>(contentStream, ServerManifest.SchemaName, out var serverManifest, out var xmlError))
        {
            var error = $"""
                         Failed to get server manifest, invalid data.
                         Xml Error: {xmlError}
                         """;

            _logger.Error(error);

            return (false, error, null);
        }

        return (true, string.Empty, serverManifest);
    }

    public static async Task<(bool Success, string Error, ClientManifest? ClientManifest)> GetClientManifestAsync(string serverUrl)
    {
        var clientManifestUri = UriHelper.JoinUriPaths(serverUrl, ClientManifest.FileName.ToLower());

        var response = await _httpClient.GetAsync(clientManifestUri);

        if (!response.IsSuccessStatusCode)
        {
            var error = $"""
                         Failed to get client manifest.
                         Http Error: {response.ReasonPhrase}
                         """;

            _logger.Error(error);

            return (false, error, null);
        }

        if (response.Content.Headers.ContentType?.MediaType is not MediaTypeNames.Text.Xml or MediaTypeNames.Application.Xml)
        {
            var error = $"""
                         Failed to get client manifest, invalid format.
                         Content Type: {response.Content.Headers.ContentType}
                         """;

            _logger.Error(error);

            return (false, error, null);
        }

        using var contentStream = await response.Content.ReadAsStreamAsync();

        var xmlDocument = XDocument.Load(contentStream);

        if (!int.TryParse(xmlDocument.Root?.Attribute("version")?.Value, out int version) || version != ClientManifest.ManifestVersion)
        {
            var error = $"""
                         Failed to get client manifest, invalid version.
                         """;

            _logger.Error(error);

            return (false, error, null);
        }

        contentStream.Position = 0;

        if (!XmlHelper.TryDeserialize<ClientManifest>(contentStream, ClientManifest.SchemaName, out var clientManifest, out var xmlError))
        {
            var error = $"""
                         Failed to get client manifest, invalid data.
                         Xml Error: {xmlError}
                         """;

            _logger.Error(error);

            return (false, error, null);
        }


        return (true, string.Empty, clientManifest);
    }
}