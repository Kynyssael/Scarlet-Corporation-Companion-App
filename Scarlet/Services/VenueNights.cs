using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Scarlet.Services;

/// <summary>
/// Every bartender's nights for the selected venue, as stored on the website, for management:
/// GET /api/portal/{venue}/nights, and PATCH .../{id} to mark one paid or void it.
/// </summary>
public sealed class VenueNights : IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource disposing = new();

    private string? loadedVenue;
    private DateTime loadedAt;

    public VenueNights(Plugin plugin)
    {
        this.plugin = plugin;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Dalamud/1.0");
    }

    /// <summary>Null until the first load. Replaced as a whole, so the UI can read it any time.</summary>
    public IReadOnlyList<NightReportDto>? Nights { get; private set; }

    public bool Loading { get; private set; }
    public string? Error { get; private set; }
    public DateTime? UpdatedAt => Nights != null ? loadedAt : null;

    public void Dispose()
    {
        disposing.Cancel();
        http.Dispose();
    }

    /// <summary>Loads when nothing is loaded for the selected venue yet, or the copy is a few minutes old.</summary>
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
            Nights = null; // Don't show another venue's nights while loading.
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
                var list = await response.Content.ReadFromJsonAsync<List<NightReportDto>>(disposing.Token) ?? [];
                if (venue == plugin.Configuration.VenueSlug)
                {
                    Nights = list;
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

    /// <summary>Marks a night paid / not paid, or voids / restores it. Shown straight away, then confirmed by the site.</summary>
    public void Update(NightReportDto night, bool? paid = null, bool? voided = null)
    {
        var venue = loadedVenue ?? plugin.Configuration.VenueSlug;
        var changed = night.With(paid, voided);
        Replace(changed);

        _ = Task.Run(async () =>
        {
            try
            {
                using var request = await AuthorizedAsync(HttpMethod.Patch, $"{Url(venue)}/{Uri.EscapeDataString(night.ClientId)}");
                request.Content = JsonContent.Create(new PatchBody { Paid = paid, Voided = voided });
                using var response = await http.SendAsync(request, disposing.Token);
                await EnsureOk(response);
                if (await response.Content.ReadFromJsonAsync<NightReportDto>(disposing.Token) is { } saved)
                    Replace(saved);
                Error = null;
            }
            catch (Exception ex) when (!disposing.IsCancellationRequested)
            {
                Replace(night); // Put it back as it was.
                Error = $"Couldn't save: {ex.Message}";
            }
        });
    }

    private void Replace(NightReportDto night)
    {
        if (Nights is { } list)
            Nights = list.Select(n => n.Id == night.Id ? night : n).ToList();
    }

    private string Url(string venue) =>
        $"{plugin.Configuration.WebsiteUrl.TrimEnd('/')}/api/portal/{Uri.EscapeDataString(venue)}/nights";

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url)
    {
        var token = await plugin.Auth.GetAccessTokenAsync(disposing.Token)
                    ?? throw new InvalidOperationException("Sign in to see the venue's nights.");
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

    private class PatchBody
    {
        [JsonPropertyName("paid")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Paid { get; set; }
        [JsonPropertyName("voided")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public bool? Voided { get; set; }
    }

    private class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}

/// <summary>A night as the website stores it (see the site's nightReports.js viewReport).</summary>
public record NightReportDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("client_id")] public string ClientId { get; init; } = string.Empty;
    [JsonPropertyName("staff_name")] public string StaffName { get; init; } = string.Empty;
    [JsonPropertyName("bartender")] public string Bartender { get; init; } = string.Empty;
    [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; init; }
    [JsonPropertyName("ended_at")] public DateTimeOffset EndedAt { get; init; }
    [JsonPropertyName("lines")] public List<NightLineDto> Lines { get; init; } = [];
    [JsonPropertyName("item_count")] public int ItemCount { get; init; }
    [JsonPropertyName("total")] public long Total { get; init; }
    [JsonPropertyName("voided")] public bool Voided { get; init; }
    [JsonPropertyName("paid")] public bool Paid { get; init; }

    /// <summary>Who to show: the character name, or the Discord/Keycloak name when missing.</summary>
    public string Who => Bartender.Length > 0 ? Bartender : StaffName;

    public NightReportDto With(bool? paid, bool? voided) =>
        this with { Paid = paid ?? Paid, Voided = voided ?? Voided };
}

public record NightLineDto
{
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("unit_price")] public long UnitPrice { get; init; }
    [JsonPropertyName("quantity")] public int Quantity { get; init; }
}
