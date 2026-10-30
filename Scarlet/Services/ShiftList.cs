using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Scarlet.Services;

/// <summary>
/// Management only: who worked at the selected venue and when, and the hourly pay per role set in the
/// website admin (Pay rates). GET /api/portal/{venue}/pay-rates, GET/POST .../shifts, PATCH/DELETE .../shifts/{id}.
/// </summary>
public sealed class ShiftList : IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource disposing = new();

    private string? loadedVenue;
    private DateTime loadedAt;

    public ShiftList(Plugin plugin)
    {
        this.plugin = plugin;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Dalamud/1.0");
    }

    /// <summary>Null until the first load. Replaced as a whole, so the UI can read them any time.</summary>
    public IReadOnlyList<ShiftDto>? Shifts { get; private set; }

    public IReadOnlyList<PayRateDto>? Rates { get; private set; }

    public bool Loading { get; private set; }

    /// <summary>A change is on its way to the website.</summary>
    public bool Saving => saving > 0;
    private int saving;

    public string? Error { get; private set; }
    public DateTime? UpdatedAt => Shifts != null ? loadedAt : null;

    public void Dispose()
    {
        disposing.Cancel();
        http.Dispose();
    }

    /// <summary>A part hour of at least this many minutes is paid as a whole hour (same as the website).</summary>
    public const int CountsAsHour = 45;

    /// <summary>Whole hours paid for <paramref name="minutes"/> worked: 3h44 is 3, 3h45 is 4.</summary>
    public static int PaidHours(int minutes) => minutes / 60 + (minutes % 60 >= CountsAsHour ? 1 : 0);

    /// <summary>Pay for <paramref name="minutes"/> at an hourly rate, like the website works it out.</summary>
    public static long Pay(long rate, int minutes) => rate * PaidHours(minutes);

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
        {
            // Don't show another venue's shifts while loading.
            Shifts = null;
            Rates = null;
        }
        loadedVenue = venue;
        loadedAt = DateTime.Now;
        Loading = true;

        _ = Task.Run(async () =>
        {
            try
            {
                var rates = await GetAsync<List<PayRateDto>>($"{Url(venue)}/pay-rates");
                var shifts = await GetAsync<List<ShiftDto>>($"{Url(venue)}/shifts");
                if (venue == plugin.Configuration.VenueSlug)
                {
                    Rates = rates;
                    Shifts = shifts;
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

    /// <param name="end">Null while they're still on shift.</param>
    public void Add(string name, string role, DateTimeOffset start, DateTimeOffset? end, string note) =>
        Send(HttpMethod.Post, "/shifts", new { staff_name = name, role, started_at = start, ended_at = end, note });

    public void Update(ShiftDto shift, string name, string role, DateTimeOffset start, DateTimeOffset? end, string note)
    {
        var body = new Dictionary<string, object?> { ["staff_name"] = name, ["started_at"] = start, ["ended_at"] = end, ["note"] = note };
        // Only a new role is looked up again, so a shift whose role was since removed from Pay rates can still be fixed.
        if (role != shift.Role)
            body["role"] = role;
        Send(HttpMethod.Patch, $"/shifts/{shift.Id}", body);
    }

    public void EndNow(ShiftDto shift) => Send(HttpMethod.Patch, $"/shifts/{shift.Id}", new { ended_at = DateTimeOffset.Now });

    public void SetPaid(ShiftDto shift, bool paid)
    {
        // Shown straight away; the reload confirms it.
        if (Shifts is { } list)
            Shifts = list.Select(s => s.Id == shift.Id ? s with { Paid = paid } : s).ToList();
        Send(HttpMethod.Patch, $"/shifts/{shift.Id}", new { paid });
    }

    public void Remove(ShiftDto shift) => Send(HttpMethod.Delete, $"/shifts/{shift.Id}", null);

    /// <summary>Sends one change, then reloads so every manager's view matches the website.</summary>
    private void Send(HttpMethod method, string path, object? body)
    {
        var venue = loadedVenue ?? plugin.Configuration.VenueSlug;
        Interlocked.Increment(ref saving);
        _ = Task.Run(async () =>
        {
            try
            {
                using var request = await AuthorizedAsync(method, Url(venue) + path);
                if (body != null)
                    request.Content = JsonContent.Create(body, body.GetType());
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
                Interlocked.Decrement(ref saving);
                loadedAt = DateTime.MinValue; // Reload on the next draw.
            }
        });
    }

    private async Task<T?> GetAsync<T>(string url)
    {
        using var request = await AuthorizedAsync(HttpMethod.Get, url);
        using var response = await http.SendAsync(request, disposing.Token);
        await EnsureOk(response);
        return await response.Content.ReadFromJsonAsync<T>(disposing.Token);
    }

    private string Url(string venue) =>
        $"{plugin.Configuration.WebsiteUrl.TrimEnd('/')}/api/portal/{Uri.EscapeDataString(venue)}";

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url)
    {
        var token = await plugin.Auth.GetAccessTokenAsync(disposing.Token)
                    ?? throw new InvalidOperationException("Sign in to see attendance.");
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

    private class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}

/// <summary>A role's hourly pay from the website admin (Pay rates).</summary>
public record PayRateDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("rate")] public long Rate { get; init; }
}

/// <summary>A shift as the website stores it (see the site's shifts.js viewShift).</summary>
public record ShiftDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("staff_name")] public string StaffName { get; init; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; init; } = string.Empty;

    /// <summary>The role's hourly rate when the shift was recorded.</summary>
    [JsonPropertyName("rate")] public long Rate { get; init; }
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("ended_at")] public DateTimeOffset? EndedAt { get; init; }
    [JsonPropertyName("minutes")] public int Minutes { get; init; }
    [JsonPropertyName("pay")] public long Pay { get; init; }
    [JsonPropertyName("note")] public string Note { get; init; } = string.Empty;
    [JsonPropertyName("paid")] public bool Paid { get; init; }
    [JsonPropertyName("recorded_by")] public string RecordedBy { get; init; } = string.Empty;

    public bool OnShift => EndedAt == null;

    /// <summary>Minutes so far for someone still on shift; the worked minutes otherwise.</summary>
    public int MinutesSoFar => EndedAt == null ? Math.Max(0, (int)(DateTimeOffset.Now - StartedAt).TotalMinutes) : Minutes;

    public int PaidHours => ShiftList.PaidHours(MinutesSoFar);

    /// <summary>For someone on shift: when their next hour starts counting.</summary>
    public DateTimeOffset NextPaidHourAt => StartedAt.AddMinutes(PaidHours * 60 + ShiftList.CountsAsHour);
}
