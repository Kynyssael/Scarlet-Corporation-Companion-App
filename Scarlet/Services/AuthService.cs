using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text.Json;

namespace Scarlet.Services;

/// <summary>
/// What the signed-in person can open for one venue. Matches the website's checks:
/// bartenders and management send nights, photographers and management lock rooms.
/// </summary>
/// <param name="Register">The register and night reports ("bartender" or "management").</param>
/// <param name="Rooms">Locking service rooms ("photographer" or "management").</param>
/// <param name="Manage">History, Menu, Staff ("management").</param>
public readonly record struct Access(bool Register, bool Rooms, bool Manage)
{
    public static readonly Access None = default;

    public bool Any => Register || Rooms || Manage;
}

/// <summary>Snapshot of who is signed in. Replaced as a whole so the UI never sees a half-updated state.</summary>
public record AuthState(string? Name, IReadOnlyList<string> Roles, DateTime? CheckedAt, bool Offline)
{
    public static readonly AuthState SignedOut = new(null, [], null, false);

    public bool SignedIn => Name != null;

    public IEnumerable<string> Venues => ScarletWebClient.Venues.Select(v => v.Slug).Where(v => Roles.Contains($"staff-{v}"));

    /// <summary>Job roles copied from Discord (see the site's keycloak.js STAFF_JOBS).</summary>
    public IEnumerable<string> Jobs => Roles.Where(r => StaffJobs.Contains(r));

    public static readonly string[] StaffJobs =
        ["waitstaff", "performer", "artist", "bartender", "bouncer", "greeter", "court", "gamba", "management", "photographer"];

    public bool IsStaffOf(string venue) => Roles.Contains($"staff-{venue}");

    public Access AccessFor(string venue)
    {
        if (!IsStaffOf(venue))
            return Access.None;
        var manager = Roles.Contains("management");
        return new Access(
            Register: manager || Roles.Contains("bartender"),
            Rooms: manager || Roles.Contains("photographer"),
            Manage: manager);
    }
}

/// <summary>
/// Staff sign-in through the website's Keycloak realm. Roles come from Discord via the site's role sync;
/// after each sign-in or refresh we ask the site to re-sync (POST /api/portal/sync-me) so changes show up quickly.
/// </summary>
public sealed class AuthService : IDisposable
{
    private static readonly TimeSpan RecheckInterval = TimeSpan.FromMinutes(15);

    /// <summary>How long saved roles are trusted when Keycloak can't be reached.</summary>
    private static readonly TimeSpan OfflineGrace = TimeSpan.FromDays(3);

    private static readonly byte[] Entropy = "Scarlet.Session"u8.ToArray();

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly KeycloakClient keycloak;
    private readonly string sessionPath;
    private readonly CancellationTokenSource disposing = new();
    private readonly SemaphoreSlim gate = new(1, 1);

    private StoredSession? session;
    private CancellationTokenSource? signInCts;
    private string? accessToken;
    private DateTime accessExpiresUtc;

    /// <summary>Raised (on a background thread) after a sign-in or role check succeeds.</summary>
    public event Action? Authenticated;

    public AuthState State { get; private set; } = AuthState.SignedOut;
    public bool Busy { get; private set; }
    public bool WaitingForBrowser { get; private set; }
    public string Status { get; private set; } = string.Empty;

    public AuthService(Plugin plugin, string configDirectory)
    {
        this.plugin = plugin;
        keycloak = new KeycloakClient(http);
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Dalamud/1.0");
        sessionPath = Path.Combine(configDirectory, "session.dat");

        session = LoadSession();
        if (session != null)
        {
            // Show the saved roles straight away; Recheck confirms them.
            State = new AuthState(session.Name, session.Roles, session.CheckedAt, true);
            Run(RecheckAsync);
        }

        _ = RecheckLoopAsync();
    }

    public void Dispose()
    {
        disposing.Cancel();
        signInCts?.Cancel();
        http.Dispose();
    }

    public Access CurrentAccess => State.AccessFor(plugin.Configuration.VenueSlug);

    public void SignIn()
    {
        if (Busy)
            return;

        signInCts = CancellationTokenSource.CreateLinkedTokenSource(disposing.Token);
        var ct = signInCts.Token;
        Run(async () =>
        {
            WaitingForBrowser = true;
            Status = "Finish signing in in your browser...";
            try
            {
                var issuer = await keycloak.FetchIssuerAsync(plugin.Configuration.WebsiteUrl, ct);
                var tokens = await keycloak.SignInAsync(issuer, "discord", ct);
                WaitingForBrowser = false;
                await AcceptTokensAsync(issuer, tokens, ct);
                Status = $"Signed in as {State.Name}.";
            }
            finally
            {
                WaitingForBrowser = false;
            }
        });
    }

    public void CancelSignIn() => signInCts?.Cancel();

    /// <summary>
    /// A current access token for calling the website, refreshing it if needed.
    /// Null when signed out. Throws HttpRequestException when Keycloak can't be reached.
    /// </summary>
    public async Task<string?> GetAccessTokenAsync(CancellationToken ct)
    {
        await gate.WaitAsync(ct);
        try
        {
            if (accessToken != null && DateTime.UtcNow < accessExpiresUtc)
                return accessToken;
            if (session is not { } current)
                return null;

            try
            {
                var tokens = await keycloak.RefreshAsync(current.Issuer, current.RefreshToken, ct);
                await AcceptTokensAsync(current.Issuer, tokens, ct, syncRoles: false);
                return accessToken;
            }
            catch (KeycloakException ex) when (ex.Error == "invalid_grant")
            {
                SignOut();
                Status = "Your sign-in expired. Please sign in again.";
                return null;
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Drops the cached access token so the next call fetches a new one.</summary>
    public void ForgetAccessToken() => accessToken = null;

    public void SignOut()
    {
        var old = session;
        session = null;
        accessToken = null;
        State = AuthState.SignedOut;
        Status = "Signed out.";
        try
        {
            File.Delete(sessionPath);
        }
        catch (IOException ex)
        {
            Plugin.Log.Warning(ex, "Could not delete saved session");
        }

        if (old != null)
            _ = keycloak.LogoutAsync(old.Issuer, old.RefreshToken);
    }

    /// <summary>Re-reads roles now (e.g. after someone got a Discord role).</summary>
    public void Refresh() => Run(RecheckAsync);

    private async Task RecheckLoopAsync()
    {
        try
        {
            while (true)
            {
                await Task.Delay(RecheckInterval, disposing.Token);
                if (session != null)
                    Run(RecheckAsync);
            }
        }
        catch (OperationCanceledException)
        {
            // Plugin unloading.
        }
    }

    private async Task RecheckAsync()
    {
        if (session is not { } current)
            return;

        try
        {
            var tokens = await keycloak.RefreshAsync(current.Issuer, current.RefreshToken, disposing.Token);
            await AcceptTokensAsync(current.Issuer, tokens, disposing.Token);
            Status = string.Empty;
        }
        catch (KeycloakException ex) when (ex.Error == "invalid_grant")
        {
            SignOut();
            Status = "Your sign-in expired. Please sign in again.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !disposing.IsCancellationRequested)
        {
            // Network trouble: keep the saved roles for a while so a Keycloak outage doesn't close the bar.
            var fresh = DateTime.Now - current.CheckedAt < OfflineGrace;
            State = fresh ? State with { Offline = true } : AuthState.SignedOut;
            Status = fresh ? "Offline: using your saved roles." : "Can't reach the sign-in server and your saved roles are too old.";
        }
    }

    private async Task AcceptTokensAsync(string issuer, TokenResponse tokens, CancellationToken ct, bool syncRoles = true)
    {
        // Ask the site to copy Discord roles into Keycloak. If anything changed,
        // the token we hold is stale, so fetch one with the new roles.
        if (syncRoles && await SyncDiscordRolesAsync(tokens.AccessToken, ct))
            tokens = await keycloak.RefreshAsync(issuer, tokens.RefreshToken, ct);

        accessToken = tokens.AccessToken;
        // Renew a little early so a request never goes out with a token that expires on the way.
        accessExpiresUtc = DateTime.UtcNow.AddSeconds(Math.Max(0, tokens.ExpiresIn - 30));

        var claims = KeycloakClient.ReadClaims(tokens.AccessToken);
        session = new StoredSession
        {
            Issuer = issuer,
            RefreshToken = tokens.RefreshToken,
            Name = claims.Name,
            Roles = claims.Roles.ToList(),
            CheckedAt = DateTime.Now,
        };
        SaveSession(session);
        State = new AuthState(claims.Name, claims.Roles, session.CheckedAt, false);
        Authenticated?.Invoke();
    }

    /// <summary>Returns true when the site added or removed any of our roles.</summary>
    private async Task<bool> SyncDiscordRolesAsync(string accessToken, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, $"{plugin.Configuration.WebsiteUrl.TrimEnd('/')}/api/portal/sync-me");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            using var response = await http.SendAsync(request, ct);
            if (!response.IsSuccessStatusCode)
                return false;

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
            int Count(string name) => doc.RootElement.TryGetProperty(name, out var list) && list.ValueKind == JsonValueKind.Array ? list.GetArrayLength() : 0;
            return Count("added") + Count("removed") > 0;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            // The token's current roles are still usable.
            Plugin.Log.Warning(ex, "Discord role sync failed");
            return false;
        }
    }

    private void Run(Func<Task> work) => Task.Run(async () =>
    {
        await gate.WaitAsync(disposing.Token);
        Busy = true;
        try
        {
            await work();
        }
        catch (OperationCanceledException)
        {
            if (!disposing.IsCancellationRequested)
                Status = "Sign-in cancelled.";
        }
        catch (Exception ex)
        {
            Plugin.Log.Warning(ex, "Sign-in failed");
            Status = $"Sign-in failed: {ex.Message}";
        }
        finally
        {
            Busy = false;
            gate.Release();
        }
    });

    private StoredSession? LoadSession()
    {
        try
        {
            if (!File.Exists(sessionPath))
                return null;
            var json = ProtectedData.Unprotect(File.ReadAllBytes(sessionPath), Entropy, DataProtectionScope.CurrentUser);
            return JsonSerializer.Deserialize<StoredSession>(json);
        }
        catch (Exception ex) when (ex is CryptographicException or JsonException or IOException)
        {
            Plugin.Log.Warning(ex, "Saved session unreadable; sign in again");
            return null;
        }
    }

    private void SaveSession(StoredSession stored)
    {
        try
        {
            var json = JsonSerializer.SerializeToUtf8Bytes(stored);
            File.WriteAllBytes(sessionPath, ProtectedData.Protect(json, Entropy, DataProtectionScope.CurrentUser));
        }
        catch (Exception ex) when (ex is CryptographicException or IOException)
        {
            Plugin.Log.Error(ex, "Could not save session");
        }
    }

    private class StoredSession
    {
        public string Issuer { get; set; } = string.Empty;
        public string RefreshToken { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public List<string> Roles { get; set; } = [];
        public DateTime CheckedAt { get; set; }
    }
}
