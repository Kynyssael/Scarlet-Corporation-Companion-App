using System.Globalization;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Scarlet.Services;

/// <summary>
/// Reads a venue's public data from the Scarlet Corporation site.
/// Endpoint: GET {base}/api/venues/{slug} → { info, menu, staff, ... }.
/// </summary>
public sealed partial class ScarletWebClient : IDisposable
{
    public const string DefaultBaseUrl = "https://scarletcorporation.com";

    /// <summary>Venue slugs the site accepts (mirrors server/src/venues.js) with display names.</summary>
    public static readonly (string Slug, string Name)[] Venues =
    [
        ("scarlet-steel", "Scarlet Steel Tavern"),
        ("scarlet-den", "Scarlet Den"),
        ("scarlet-unholy-water", "Scarlet Unholy Water"),
    ];

    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(15) };

    public void Dispose() => http.Dispose();

    public async Task<VenueDto> FetchVenueAsync(string baseUrl, string slug, CancellationToken ct = default)
    {
        var url = $"{baseUrl.TrimEnd('/')}/api/venues/{Uri.EscapeDataString(slug)}";
        return await http.GetFromJsonAsync<VenueDto>(url, ct) ?? throw new InvalidOperationException("Empty response");
    }

    /// <summary>
    /// The site stores prices as free text ("15,000 gil", "15k", "Free").
    /// Returns the amount in gil, or 0 when no number is found.
    /// </summary>
    public static int ParsePrice(string text)
    {
        var match = PriceRegex().Match(text.Replace(",", string.Empty).Replace(" ", string.Empty));
        if (!match.Success || !double.TryParse(match.Groups[1].Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            return 0;

        value *= match.Groups[2].Value.ToLowerInvariant() switch
        {
            "k" => 1_000,
            "m" => 1_000_000,
            _ => 1,
        };
        return (int)Math.Clamp(value, 0, int.MaxValue);
    }

    [GeneratedRegex(@"(\d+(?:\.\d+)?)([kKmM]?)")]
    private static partial Regex PriceRegex();

    public static string VenueName(string slug) =>
        Venues.FirstOrDefault(v => v.Slug == slug).Name ?? slug;
}

public class VenueDto
{
    [JsonPropertyName("menu")] public List<MenuItemDto> Menu { get; set; } = [];
    [JsonPropertyName("staff")] public List<StaffDto> Staff { get; set; } = [];
}

public class MenuItemDto
{
    [JsonPropertyName("id")] public int Id { get; set; }
    [JsonPropertyName("section")] public string Section { get; set; } = string.Empty;
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("description")] public string Description { get; set; } = string.Empty;
    [JsonPropertyName("price")] public string Price { get; set; } = string.Empty;
    [JsonPropertyName("image_url")] public string ImageUrl { get; set; } = string.Empty;
    [JsonPropertyName("game_item_id")] public int GameItemId { get; set; }
    [JsonPropertyName("sort")] public int Sort { get; set; }
}

public class StaffDto
{
    [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
    [JsonPropertyName("role")] public string Role { get; set; } = string.Empty;
}
