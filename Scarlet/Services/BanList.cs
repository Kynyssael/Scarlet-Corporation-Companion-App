using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Scarlet.Services;

/// <summary>
/// The selected venue's blacklist from the website. Management only, to read and to change. GET/POST /api/portal/{venue}/blacklist, PATCH/DELETE .../blacklist/{id}.
/// </summary>
public sealed class BanList : IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource disposing = new();

    private string? loadedVenue;
    private DateTime loadedAt;

    public BanList(Plugin plugin)
    {
        this.plugin = plugin;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Dalamud/1.0");
    }

    /// <summary>Null until the first load. Replaced as a whole, so the UI can read it any time.</summary>
    public IReadOnlyList<BanDto>? Bans { get; private set; }

    public bool Loading { get; private set; }

    /// <summary>A change is on its way to the website.</summary>
    public bool Saving { get; private set; }

    public string? Error { get; private set; }
    public DateTime? UpdatedAt => Bans != null ? loadedAt : null;

    public void Dispose()
    {
        disposing.Cancel();
        http.Dispose();
    }

    public void RefreshIfStale()
    {
        if (!Loading && (loadedVenue != plugin.Configuration.VenueSlug || DateTime.Now - loadedAt > StaleAfter))
            Refresh();
    }

    public void Refresh()
    {
        if (Loading)
            return;

        var venue = plugin.Configuration.VenueSlug;
        if (loadedVenue != venue)
            Bans = null; // Don't show another venue's bans while loading.
        loadedVenue = venue;
        loadedAt = DateTime.Now;
        Loading = true;

        _ = Task.Run(async () =>
        {
            try
            {
                using var request = await AuthorizedAsync(HttpMethod.Get, Url(venue));
                using var response = await http.SendAsync(request, disposing.Token);
                await EnsureOk(response);
                var list = await response.Content.ReadFromJsonAsync<List<BanDto>>(disposing.Token) ?? [];
                if (venue == plugin.Configuration.VenueSlug)
                {
                    Bans = list;
                    Error = null;
                }
            }
            catch (Exception ex) when (!disposing.IsCancellationRequested)
            {
                Error = ex.Message;
            }
            finally
            {
                loadedAt = DateTime.Now;
                Loading = false;
            }
        });
    }

    /// <param name="until">When the ban ends; null for permanent.</param>
    public void Add(string name, string reason, DateTimeOffset bannedAt, DateTimeOffset? until) =>
        Send(HttpMethod.Post, string.Empty, new BanBody { Name = name, Reason = reason, BannedAt = bannedAt, Until = until });

    public void Update(BanDto ban, string name, string reason, DateTimeOffset bannedAt, DateTimeOffset? until) =>
        Send(HttpMethod.Patch, $"/{ban.Id}", new BanBody { Name = name, Reason = reason, BannedAt = bannedAt, Until = until });

    /// <summary>Lifts a ban now but keeps it on the list as ended.</summary>
    public void EndNow(BanDto ban) => Update(ban, ban.Name, ban.Reason, ban.BannedAt, DateTimeOffset.Now);

    public void Remove(BanDto ban) => Send(HttpMethod.Delete, $"/{ban.Id}", null);

    /// <summary>Sends one change, then reloads the list so everyone's view matches the website.</summary>
    private void Send(HttpMethod method, string path, BanBody? body)
    {
        var venue = loadedVenue ?? plugin.Configuration.VenueSlug;
        Saving = true;
        _ = Task.Run(async () =>
        {
            try
            {
                using var request = await AuthorizedAsync(method, Url(venue) + path);
                if (body != null)
                    request.Content = JsonContent.Create(body);
                using var response = await http.SendAsync(request, disposing.Token);
                await EnsureOk(response);
                Error = null;
            }
            catch (Exception ex) when (!disposing.IsCancellationRequested)
            {
                Error = $"Couldn't save: {ex.Message}";
            }
            finally
            {
                Saving = false;
                loadedAt = DateTime.MinValue; // Reload on the next draw.
            }
        });
    }

    private string Url(string venue) =>
        $"{plugin.Configuration.WebsiteUrl.TrimEnd('/')}/api/portal/{Uri.EscapeDataString(venue)}/blacklist";

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url)
    {
        var token = await plugin.Auth.GetAccessTokenAsync(disposing.Token)
                    ?? throw new InvalidOperationException("Sign in to see the venue's blacklist.");
        var request = new HttpRequestMessage(method, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return request;
    }

    private static async Task EnsureOk(HttpResponseMessage response)
    {
        if (response.IsSuccessStatusCode)
            return;
        string? reason = null;
        try
        {
            reason = (await response.Content.ReadFromJsonAsync<ErrorBody>())?.Error;
        }
        catch (Exception)
        {
            // Not JSON.
        }
        throw new HttpRequestException(reason ?? $"Website answered {(int)response.StatusCode}");
    }

    private class BanBody
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("reason")] public string Reason { get; set; } = string.Empty;
        [JsonPropertyName("banned_at")] public DateTimeOffset BannedAt { get; set; }

        // Sent as null for a permanent ban, so it has to be written even when null.
        [JsonPropertyName("until")] public DateTimeOffset? Until { get; set; }
    }

    private class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}

/// <summary>A ban as the website stores it (see the site's blacklist.js viewBan).</summary>
public record BanDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("reason")] public string Reason { get; init; } = string.Empty;
    [JsonPropertyName("banned_at")] public DateTimeOffset BannedAt { get; init; }
    [JsonPropertyName("until")] public DateTimeOffset? Until { get; init; }
    [JsonPropertyName("added_by")] public string AddedBy { get; init; } = string.Empty;

    // Worked out here rather than read from the site's status, so it stays right while the page is open.
    public bool Permanent => Until == null;
    public bool Ended => Until is { } end && end <= DateTimeOffset.Now;
    public int? DaysLeft => Until is { } end ? (int)Math.Ceiling((end - DateTimeOffset.Now).TotalDays) : null;
}
