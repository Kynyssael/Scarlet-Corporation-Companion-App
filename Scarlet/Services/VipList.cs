using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Scarlet.Services;

/// <summary>
/// The selected venue's VIP members from the website, and when each needs to renew. Every staff
/// member can read it; only management changes it.
/// GET/POST /api/portal/{venue}/vips, PATCH/DELETE .../vips/{id}, POST .../vips/{id}/renew.
/// </summary>
public sealed class VipList : IDisposable
{
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(2);

    public const string Monthly = "monthly";
    public const string Yearly = "yearly";
    public const string Lifetime = "lifetime";
    public static readonly string[] Tiers = [Monthly, Yearly, Lifetime];

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource disposing = new();

    private string? loadedVenue;
    private DateTime loadedAt;

    public VipList(Plugin plugin)
    {
        this.plugin = plugin;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Dalamud/1.0");
    }

    /// <summary>Null until the first load. Replaced as a whole, so the UI can read it any time.</summary>
    public IReadOnlyList<VipDto>? Vips { get; private set; }

    public bool Loading { get; private set; }

    /// <summary>A change is on its way to the website.</summary>
    public bool Saving { get; private set; }

    public string? Error { get; private set; }
    public DateTime? UpdatedAt => Vips != null ? loadedAt : null;

    public void Dispose()
    {
        disposing.Cancel();
        http.Dispose();
    }

    public static string TierLabel(string tier) => tier switch
    {
        Monthly => "Monthly",
        Yearly => "Yearly",
        Lifetime => "Lifetime",
        _ => tier,
    };

    /// <summary>When a tier bought at <paramref name="from"/> runs out, like the website works it out.</summary>
    public static DateTimeOffset? ExpiryFrom(string tier, DateTimeOffset from) => tier switch
    {
        Monthly => from.AddMonths(1),
        Yearly => from.AddMonths(12),
        _ => null,
    };

    /// <summary>When a renewal paid at <paramref name="paidAt"/> runs out: on top of the time left, if any.</summary>
    public static DateTimeOffset? RenewedExpiry(VipDto vip, string tier, DateTimeOffset paidAt) =>
        ExpiryFrom(tier, vip.ExpiresAt is { } current && current > paidAt ? current : paidAt);

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
            Vips = null; // Don't show another venue's VIPs while loading.
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
                var list = await response.Content.ReadFromJsonAsync<List<VipDto>>(disposing.Token) ?? [];
                if (venue == plugin.Configuration.VenueSlug)
                {
                    Vips = list;
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

    public void Add(string name, string tier, DateTimeOffset boughtAt, string note) =>
        Send(HttpMethod.Post, string.Empty, new VipBody { Name = name, Tier = tier, BoughtAt = boughtAt, Note = note });

    /// <summary>Fixes a VIP's details; a new tier or purchase date works the renewal date out again.</summary>
    public void Update(VipDto vip, string name, string tier, DateTimeOffset boughtAt, string note) =>
        Send(HttpMethod.Patch, $"/{vip.Id}", new VipBody { Name = name, Tier = tier, BoughtAt = boughtAt, Note = note });

    /// <summary>The VIP paid again, for the same or another tier.</summary>
    public void Renew(VipDto vip, string tier, DateTimeOffset paidAt) =>
        Send(HttpMethod.Post, $"/{vip.Id}/renew", new VipBody { Tier = tier, BoughtAt = paidAt });

    public void Remove(VipDto vip) => Send(HttpMethod.Delete, $"/{vip.Id}", null);

    /// <summary>Sends one change, then reloads the list so everyone's view matches the website.</summary>
    private void Send(HttpMethod method, string path, VipBody? body)
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
        $"{plugin.Configuration.WebsiteUrl.TrimEnd('/')}/api/portal/{Uri.EscapeDataString(venue)}/vips";

    private async Task<HttpRequestMessage> AuthorizedAsync(HttpMethod method, string url)
    {
        var token = await plugin.Auth.GetAccessTokenAsync(disposing.Token)
                    ?? throw new InvalidOperationException("Sign in to see the venue's VIPs.");
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

    private class VipBody
    {
        [JsonPropertyName("name")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Name { get; set; }
        [JsonPropertyName("tier")] public string Tier { get; set; } = Monthly;
        [JsonPropertyName("bought_at")] public DateTimeOffset BoughtAt { get; set; }
        [JsonPropertyName("note")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Note { get; set; }
    }

    private class ErrorBody
    {
        [JsonPropertyName("error")] public string? Error { get; set; }
    }
}

/// <summary>A VIP as the website stores it (see the site's vips.js viewVip).</summary>
public record VipDto
{
    [JsonPropertyName("id")] public int Id { get; init; }
    [JsonPropertyName("name")] public string Name { get; init; } = string.Empty;
    [JsonPropertyName("tier")] public string Tier { get; init; } = VipList.Monthly;
    [JsonPropertyName("bought_at")] public DateTimeOffset BoughtAt { get; init; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; init; }
    [JsonPropertyName("note")] public string Note { get; init; } = string.Empty;
    [JsonPropertyName("added_by")] public string AddedBy { get; init; } = string.Empty;
    [JsonPropertyName("purchases")] public List<VipPurchaseDto> Purchases { get; init; } = [];

    // Worked out here rather than read from the site's status, so it stays right while the page is open.
    public int? DaysLeft => ExpiresAt is { } end ? (int)Math.Ceiling((end - DateTimeOffset.Now).TotalDays) : null;
    public bool Expired => ExpiresAt is { } end && end <= DateTimeOffset.Now;
    public bool DueSoon => !Expired && DaysLeft <= DueSoonDays;

    /// <summary>Same as the website's DUE_SOON_DAYS.</summary>
    public const int DueSoonDays = 7;
}

public record VipPurchaseDto
{
    [JsonPropertyName("tier")] public string Tier { get; init; } = VipList.Monthly;
    [JsonPropertyName("bought_at")] public DateTimeOffset BoughtAt { get; init; }
    [JsonPropertyName("expires_at")] public DateTimeOffset? ExpiresAt { get; init; }
    [JsonPropertyName("sold_by")] public string SoldBy { get; init; } = string.Empty;
}
