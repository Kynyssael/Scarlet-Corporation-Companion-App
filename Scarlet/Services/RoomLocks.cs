using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Scarlet.Services;

/// <summary>
/// The selected venue's service rooms and who has them locked, from the website:
/// GET /api/portal/{venue}/rooms, POST/DELETE .../rooms/{id}/lock.
/// </summary>
public sealed class RoomLocks : IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromSeconds(30);

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource disposing = new();

    private string? loadedVenue;
    private DateTime loadedAt;

    public RoomLocks(Plugin plugin)
    {
        this.plugin = plugin;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Dalamud/1.0");
    }

    /// <summary>Null until the first load. Replaced as a whole, so the UI can read it any time.</summary>
    public IReadOnlyList<RoomDto>? Rooms { get; private set; }

    public bool Loading { get; private set; }

    /// <summary>Room being locked or unlocked right now.</summary>
    public int? Busy { get; private set; }

    public string? Error { get; private set; }
    public DateTime? UpdatedAt => Rooms != null ? loadedAt : null;

    public void Dispose()
    {
        disposing.Cancel();
        http.Dispose();
    }

    /// <summary>Reloads every 30 seconds while the page is open, so others' locks show up.</summary>
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
            Rooms = null;
        loadedVenue = venue;
        loadedAt = DateTime.Now;
        Loading = true;

        _ = Run(async () =>
        {
            try
            {
                using var request = await AuthorizedAsync(HttpMethod.Get, RoomsUrl(venue));
                using var response = await http.SendAsync(request, disposing.Token);
                await EnsureOk(response);
                var rooms = await response.Content.ReadFromJsonAsync<List<RoomDto>>(disposing.Token) ?? [];
                if (venue == plugin.Configuration.VenueSlug)
                    Rooms = rooms;
            }
            finally
            {
                loadedAt = DateTime.Now;
                Loading = false;
            }
        });
    }

    /// <summary>Locks a room under the bartender's character name. Management can take over a locked room with force.</summary>
    public void Lock(RoomDto room, string note, bool force = false) => _ = LockAsync(room.Id, note, force);

    public void Unlock(RoomDto room) => _ = UnlockAsync(room.Id);

    /// <summary>
    /// True once the website has the room locked for us; false (with <see cref="Error"/> set) if not.
    /// Call on the main thread: it reads the character name.
    /// </summary>
    public Task<bool> LockAsync(int roomId, string note, bool force = false) =>
        Change(roomId, HttpMethod.Post, new LockBody { Name = Plugin.LocalPlayerName() ?? string.Empty, Note = note, Force = force });

    public Task<bool> UnlockAsync(int roomId) => Change(roomId, HttpMethod.Delete, null);

    private Task<bool> Change(int roomId, HttpMethod method, LockBody? body)
    {
        if (Busy != null)
        {
            Error = "Another room is being locked or unlocked; try again in a moment.";
            return Task.FromResult(false);
        }
        Busy = roomId;
        var venue = loadedVenue ?? plugin.Configuration.VenueSlug;

        return Run(async () =>
        {
            try
            {
                using var request = await AuthorizedAsync(method, $"{RoomsUrl(venue)}/{roomId}/lock");
                if (body != null)
                    request.Content = JsonContent.Create(body);
                using var response = await http.SendAsync(request, disposing.Token);
                // A 409/403 usually means someone else got there first; reload to show who.
                if (!response.IsSuccessStatusCode)
                    loadedAt = DateTime.MinValue;
                await EnsureOk(response);
                if (await response.Content.ReadFromJsonAsync<RoomDto>(disposing.Token) is { } saved && Rooms is { } list)
                    Rooms = list.Select(r => r.Id == saved.Id ? saved : r).ToList();
            }
            finally
            {
                Busy = null;
            }
        });
    }

    /// <summary>Runs work in the background; true when it succeeded, otherwise the reason is in <see cref="Error"/>.</summary>
    private Task<bool> Run(Func<Task> work) => Task.Run(async () =>
    {
        try
        {
            await work();
            Error = null;
            return true;
        }
        catch (Exception ex) when (!disposing.IsCancellationRequested)
        {
            Error = ex.Message;
            return false;
        }
    });

    private string RoomsUrl(string venue) =>
        $"{plugin.Configuration.WebsiteUrl.TrimEnd('/')}/api/portal/{Uri.EscapeDataString(venue)}/rooms";

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url)
    {
        var token = await plugin.Auth.GetAccessTokenAsync(disposing.Token)
                    ?? throw new InvalidOperationException("Sign in to see the rooms.");
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

    private class LockBody
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("note")] public string Note { get; set; } = string.Empty;
        [JsonPropertyName("force")] public bool Force { get; set; }
    }

    private class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}

/// <summary>A service room as staff see it (see the site's roomLocks.js staffRoom).</summary>
public record RoomDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("kind")] public string Kind { get; init; } = "private";
    [JsonPropertyName("description")] public string Description { get; init; } = string.Empty;
    [JsonPropertyName("locked")] public bool Locked { get; init; }
    [JsonPropertyName("locked_by")] public string LockedBy { get; init; } = string.Empty;
    [JsonPropertyName("locked_note")] public string LockedNote { get; init; } = string.Empty;
    [JsonPropertyName("locked_at")] public DateTimeOffset? LockedAt { get; init; }
    [JsonPropertyName("mine")] public bool Mine { get; init; }

    public string KindLabel => Kind == "photo" ? "Photo room" : "Private room";
}
