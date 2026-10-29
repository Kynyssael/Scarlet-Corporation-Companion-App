using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Web;
using Dalamud.Utility;

namespace Scarlet.Services;

/// <summary>
/// Keycloak sign-in for the plugin: authorization code + PKCE with a loopback redirect.
/// The browser does the login (Discord via kc_idp_hint), then lands on 127.0.0.1 where we pick up the code.
/// </summary>
/// <remarks>
/// Needs a public Keycloak client "scarlet-plugin" with standard flow and
/// redirect URI http://127.0.0.1:47621/callback. Device-code sign-in is not used
/// because Cloudflare challenges POSTs to /protocol/openid-connect/auth/*.
/// </remarks>
public sealed class KeycloakClient(HttpClient http)
{
    public const string ClientId = "scarlet-plugin";
    public const int RedirectPort = 47621;
    public static readonly string RedirectUri = $"http://127.0.0.1:{RedirectPort}/callback";

    private static readonly TimeSpan SignInTimeout = TimeSpan.FromMinutes(5);

    /// <summary>Realm address from the site's /api/portal/config, e.g. https://auth.example.com/realms/scarlet.</summary>
    public async Task<string> FetchIssuerAsync(string websiteUrl, CancellationToken ct)
    {
        var config = await http.GetFromJsonAsync<PortalConfig>($"{websiteUrl.TrimEnd('/')}/api/portal/config", ct)
                     ?? throw new InvalidOperationException("Empty portal config");
        if (!config.Configured)
            throw new InvalidOperationException("Staff sign-in is not configured on the website.");
        return $"{config.Url.TrimEnd('/')}/realms/{config.Realm}";
    }

    public async Task<TokenResponse> SignInAsync(string issuer, string idpHint, CancellationToken ct)
    {
        var verifier = RandomUrlSafe(32);
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = RandomUrlSafe(16);

        var listener = new TcpListener(IPAddress.Loopback, RedirectPort);
        try
        {
            listener.Start();
        }
        catch (SocketException)
        {
            throw new InvalidOperationException($"Port {RedirectPort} is in use; close whatever is using it and try again.");
        }

        try
        {
            var query = HttpUtility.ParseQueryString(string.Empty);
            query["client_id"] = ClientId;
            query["response_type"] = "code";
            query["scope"] = "openid offline_access";
            query["redirect_uri"] = RedirectUri;
            query["state"] = state;
            query["code_challenge"] = challenge;
            query["code_challenge_method"] = "S256";
            if (!string.IsNullOrEmpty(idpHint))
                query["kc_idp_hint"] = idpHint;
            Util.OpenLink($"{issuer}/protocol/openid-connect/auth?{query}");

            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(SignInTimeout);
            var code = await WaitForCallbackAsync(listener, state, timeout.Token);

            return await PostTokenAsync(issuer, new()
            {
                ["grant_type"] = "authorization_code",
                ["client_id"] = ClientId,
                ["code"] = code,
                ["redirect_uri"] = RedirectUri,
                ["code_verifier"] = verifier,
            }, ct);
        }
        finally
        {
            listener.Stop();
        }
    }

    public Task<TokenResponse> RefreshAsync(string issuer, string refreshToken, CancellationToken ct) =>
        PostTokenAsync(issuer, new()
        {
            ["grant_type"] = "refresh_token",
            ["client_id"] = ClientId,
            ["refresh_token"] = refreshToken,
        }, ct);

    /// <summary>Ends the Keycloak session for this refresh token. Best effort.</summary>
    public async Task LogoutAsync(string issuer, string refreshToken)
    {
        try
        {
            using var body = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["client_id"] = ClientId,
                ["refresh_token"] = refreshToken,
            });
            await http.PostAsync($"{issuer}/protocol/openid-connect/logout", body);
        }
        catch (Exception ex)
        {
            Plugin.Log.Debug(ex, "Keycloak logout failed");
        }
    }

    private async Task<TokenResponse> PostTokenAsync(string issuer, Dictionary<string, string> form, CancellationToken ct)
    {
        using var body = new FormUrlEncodedContent(form);
        using var response = await http.PostAsync($"{issuer}/protocol/openid-connect/token", body, ct);
        var text = await response.Content.ReadAsStringAsync(ct);

        if (!response.IsSuccessStatusCode)
        {
            var error = TryDeserialize<TokenError>(text);
            // invalid_grant: the refresh token is expired or revoked, so the user has to sign in again.
            throw new KeycloakException(error?.Error ?? $"HTTP {(int)response.StatusCode}", error?.Description ?? string.Empty);
        }

        return TryDeserialize<TokenResponse>(text) ?? throw new KeycloakException("bad_response", "Unreadable token response");
    }

    private static async Task<string> WaitForCallbackAsync(TcpListener listener, string expectedState, CancellationToken ct)
    {
        while (true)
        {
            using var socket = await listener.AcceptTcpClientAsync(ct);
            await using var stream = socket.GetStream();
            using var reader = new StreamReader(stream, Encoding.ASCII, leaveOpen: true);

            // "GET /callback?code=...&state=... HTTP/1.1"
            var requestLine = await reader.ReadLineAsync(ct) ?? string.Empty;
            var target = requestLine.Split(' ').ElementAtOrDefault(1) ?? string.Empty;
            if (!target.StartsWith("/callback", StringComparison.Ordinal))
            {
                await RespondAsync(stream, 404, "Not found.", ct);
                continue;
            }

            var query = HttpUtility.ParseQueryString(target.Contains('?') ? target[(target.IndexOf('?') + 1)..] : string.Empty);
            if (query["error"] is { } error)
            {
                await RespondAsync(stream, 400, "Sign-in was cancelled. You can close this tab.", ct);
                throw new KeycloakException(error, query["error_description"] ?? string.Empty);
            }

            if (query["state"] != expectedState || query["code"] is not { Length: > 0 } code)
            {
                await RespondAsync(stream, 400, "This sign-in link is out of date. Start again from the plugin.", ct);
                continue;
            }

            await RespondAsync(stream, 200, "Signed in to the Scarlet Corporation Companion App. You can close this tab and go back to the game.", ct);
            return code;
        }
    }

    private static async Task RespondAsync(Stream stream, int status, string message, CancellationToken ct)
    {
        var html = $"<!doctype html><meta charset=utf-8><title>Scarlet</title><body style=\"font-family:sans-serif;background:#1a0d10;color:#f3e6e8;display:grid;place-items:center;height:100vh;margin:0\"><p>{WebUtility.HtmlEncode(message)}</p>";
        var bytes = Encoding.UTF8.GetBytes(html);
        var head = $"HTTP/1.1 {status} {(status == 200 ? "OK" : "Error")}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {bytes.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(head), ct);
        await stream.WriteAsync(bytes, ct);
    }

    /// <summary>Reads the claims we need from an access token. The website verifies signatures; here it only drives the UI.</summary>
    public static TokenClaims ReadClaims(string accessToken)
    {
        var parts = accessToken.Split('.');
        if (parts.Length < 2)
            throw new KeycloakException("bad_token", "Access token is not a JWT");

        var payload = parts[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=');
        using var doc = JsonDocument.Parse(Convert.FromBase64String(payload));
        var root = doc.RootElement;

        string? Str(string name) => root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() : null;

        var roles = root.TryGetProperty("realm_access", out var access) && access.TryGetProperty("roles", out var list)
            ? list.EnumerateArray().Select(r => r.GetString() ?? string.Empty).Where(r => r.Length > 0).ToList()
            : [];

        return new TokenClaims(Str("preferred_username") ?? Str("name") ?? "staff", roles);
    }

    private static T? TryDeserialize<T>(string json) where T : class
    {
        try
        {
            return JsonSerializer.Deserialize<T>(json);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string RandomUrlSafe(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private class PortalConfig
    {
        [JsonPropertyName("configured")] public bool Configured { get; set; }
        [JsonPropertyName("url")] public string Url { get; set; } = string.Empty;
        [JsonPropertyName("realm")] public string Realm { get; set; } = string.Empty;
    }

    private class TokenError
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
        [JsonPropertyName("error_description")] public string? Description { get; set; }
    }
}

public class TokenResponse
{
    [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
    [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
    [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
}

public record TokenClaims(string Name, IReadOnlyList<string> Roles);

public class KeycloakException(string error, string description)
    : Exception(description.Length > 0 ? $"{error}: {description}" : error)
{
    public string Error { get; } = error;
}
