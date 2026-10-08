using System.Text.Json;

namespace SyncPlayer;

static class RelayConfiguration
{
    public static string Load(string? path = null)
    {
        try {
            var settings = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(path ?? Path.Combine(AppContext.BaseDirectory, "relay.json")));
            string server = settings?.GetValueOrDefault("server")?.Trim().TrimEnd('/') ?? "";
            if (!Uri.TryCreate(server, UriKind.Absolute, out var uri) || uri.Scheme != "https" || string.IsNullOrEmpty(uri.Host) || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0 || uri.AbsolutePath != "/") throw new JsonException();
            return server;
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) {
            throw new IOException(Localization.T("请检查程序旁 relay.json 中的 server，须为有效的 HTTPS 中转网址"), ex);
        }
    }
}
