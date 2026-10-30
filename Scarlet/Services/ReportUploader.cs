using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Scarlet.Models;

namespace Scarlet.Services;

/// <summary>
/// Sends ended nights to the website (POST /api/portal/{venue}/nights) so management has the history too.
/// Nights wait locally until the site accepts them; the site ignores a night it already has, so retries are safe.
/// </summary>
public sealed class ReportUploader : IDisposable
{
    private static readonly TimeSpan RetryInterval = TimeSpan.FromMinutes(5);

    private readonly Plugin plugin;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(20) };
    private readonly CancellationTokenSource disposing = new();
    private readonly SemaphoreSlim wake = new(0);
    private readonly SemaphoreSlim running = new(1, 1);

    public string Status { get; private set; } = string.Empty;

    public ReportUploader(Plugin plugin)
    {
        this.plugin = plugin;
        http.DefaultRequestHeaders.UserAgent.ParseAdd("Scarlet-Dalamud/1.0");
        plugin.Auth.Authenticated += Kick;
        _ = LoopAsync();
    }

    public void Dispose()
    {
        plugin.Auth.Authenticated -= Kick;
        disposing.Cancel();
        http.Dispose();
    }

    /// <summary>Try sending now (after a night ends, a void, or a sign-in).</summary>
    public void Kick() => wake.Release();

    /// <summary>Gives refused nights another go (e.g. after management fixed your roles). Call on the UI thread.</summary>
    public void RetryRefused()
    {
        foreach (var night in plugin.Pos.Nights.Where(n => n.UploadError != null))
            night.UploadError = null;
        plugin.Pos.Save();
        Kick();
    }

    private async Task LoopAsync()
    {
        try
        {
            while (true)
            {
                await SendPendingAsync();
                await wake.WaitAsync(RetryInterval, disposing.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Plugin unloading.
        }
    }

    private async Task SendPendingAsync()
    {
        if (!await running.WaitAsync(0))
            return;

        try
        {
            // Night objects belong to the UI thread; copy what we need there.
            var work = await Plugin.Framework.RunOnFrameworkThread(() => plugin.Pos.Nights
                .Where(n => n.UploadError == null && (n.UploadedAt == null || n.VoidPending))
                .Select(n => (Night: n, Body: ToBody(n), n.VenueSlug, NeedsUpload: n.UploadedAt == null, n.Voided))
                .ToList());
            if (work.Count == 0)
            {
                Status = string.Empty;
                return;
            }

            var token = await plugin.Auth.GetAccessTokenAsync(disposing.Token);
            if (token == null)
            {
                Status = $"{work.Count} night(s) waiting: sign in to send them to the website.";
                return;
            }

            var baseUrl = plugin.Configuration.WebsiteUrl.TrimEnd('/');
            string? problem = null;
            foreach (var item in work)
            {
                // One night failing doesn't hold up the others.
                try
                {
                    if (item.NeedsUpload)
                        await UploadAsync(baseUrl, token, item.Night, item.VenueSlug, item.Body);
                    else
                        await SendVoidAsync(baseUrl, token, item.Night, item.VenueSlug, item.Body.Id, item.Voided);
                }
                catch (WebsiteException ex)
                {
                    Plugin.Log.Warning($"Night {item.Body.Id} not sent: {ex.Message}");
                    problem ??= ex.Message;
                }
            }

            var left = await Plugin.Framework.RunOnFrameworkThread(() => plugin.Pos.Nights.Count(n => n.UploadError == null && n.UploadedAt == null));
            Status = left == 0 ? $"All nights sent to the website ({DateTime.Now:t})."
                : problem != null ? $"{problem} Will retry in a few minutes."
                : $"{left} night(s) waiting to be sent.";
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !disposing.IsCancellationRequested)
        {
            // No answer at all (offline, DNS, timeout); the next pass tries every night again.
            Plugin.Log.Warning(ex, "Website unreachable");
            Status = $"Can't reach the website ({ex.Message}); will retry in a few minutes.";
        }
        catch (Exception ex) when (!disposing.IsCancellationRequested)
        {
            Plugin.Log.Warning(ex, "Night upload failed");
            Status = $"Upload failed: {ex.Message}";
        }
        finally
        {
            running.Release();
        }
    }

    private async Task UploadAsync(string baseUrl, string token, Night night, string venue, NightBody body)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{baseUrl}/api/portal/{Uri.EscapeDataString(venue)}/nights")
        {
            Content = JsonContent.Create(body),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, disposing.Token);

        if (response.IsSuccessStatusCode)
        {
            await Update(night, n =>
            {
                n.UploadedAt = DateTime.Now;
                // Voided before it was ever sent: tell the site on the next pass.
                n.VoidPending = n.Voided;
            });
            // Show it in management's History straight away, not on the next 2-minute reload.
            plugin.VenueNights.MarkStale();
            return;
        }

        // 400/403/409 won't fix themselves by retrying; keep the night locally and say why.
        // 404 is retried: it's what a site without the nights API (not deployed yet) answers.
        var reason = await ErrorText(response);
        if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Forbidden or HttpStatusCode.Conflict)
        {
            await Update(night, n => n.UploadError = reason);
            return;
        }

        // The site turned the token down; get a fresh one on the next pass.
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            plugin.Auth.ForgetAccessToken();

        throw new WebsiteException($"Website answered {(int)response.StatusCode} ({reason}) for {venue}.");
    }

    private async Task SendVoidAsync(string baseUrl, string token, Night night, string venue, string id, bool voided)
    {
        using var request = new HttpRequestMessage(HttpMethod.Patch, $"{baseUrl}/api/portal/{Uri.EscapeDataString(venue)}/nights/{id}")
        {
            Content = JsonContent.Create(new { voided }),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using var response = await http.SendAsync(request, disposing.Token);

        // A refusal (not management any more) isn't retried; the admin can void it on the site.
        if (response.IsSuccessStatusCode || (int)response.StatusCode is >= 400 and < 500)
            await Update(night, n => n.VoidPending = false);
    }

    private Task Update(Night night, Action<Night> change) =>
        Plugin.Framework.RunOnFrameworkThread(() =>
        {
            change(night);
            plugin.Pos.Save();
        });

    private static async Task<string> ErrorText(HttpResponseMessage response)
    {
        try
        {
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("error", out var error) && error.GetString() is { } text)
                return text;
        }
        catch (JsonException)
        {
            // Not JSON (e.g. a Cloudflare or nginx page); fall through.
        }
        return response.ReasonPhrase ?? $"HTTP {(int)response.StatusCode}";
    }

    /// <summary>The website answered, but not with success; the night is retried later.</summary>
    private class WebsiteException(string message) : Exception(message);

    private static NightBody ToBody(Night night) => new()
    {
        Id = night.Id.ToString(),
        Kind = night.Kind,
        Bartender = night.Bartender,
        StartedAt = new DateTimeOffset(night.StartedAt),
        EndedAt = new DateTimeOffset(night.EndedAt ?? night.StartedAt),
        Lines = night.Lines.Select(l => new LineBody
        {
            Name = l.Name,
            UnitPrice = l.UnitPrice,
            Quantity = l.Quantity,
            Buyer = l.Buyer,
            Room = l.Room,
            StartedAt = l.StartedAt is { } start ? new DateTimeOffset(start) : null,
            EndedAt = l.EndedAt is { } end ? new DateTimeOffset(end) : null,
            Rate = l.Rate,
            Minutes = l.Minutes,
        }).ToList(),
    };

    private class NightBody
    {
        [JsonPropertyName("id")] public string Id { get; set; } = string.Empty;
        [JsonPropertyName("kind")] public string Kind { get; set; } = Night.Bar;
        [JsonPropertyName("bartender")] public string Bartender { get; set; } = string.Empty;
        [JsonPropertyName("started_at")] public DateTimeOffset StartedAt { get; set; }
        [JsonPropertyName("ended_at")] public DateTimeOffset EndedAt { get; set; }
        [JsonPropertyName("lines")] public List<LineBody> Lines { get; set; } = [];
    }

    private class LineBody
    {
        [JsonPropertyName("name")] public string Name { get; set; } = string.Empty;
        [JsonPropertyName("unit_price")] public int UnitPrice { get; set; }
        [JsonPropertyName("quantity")] public int Quantity { get; set; }

        // Court sessions: the site works the price out again from the rate and the minutes bought.
        [JsonPropertyName("buyer")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Buyer { get; set; }
        [JsonPropertyName("room")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public string? Room { get; set; }
        [JsonPropertyName("started_at")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? StartedAt { get; set; }
        [JsonPropertyName("ended_at")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public DateTimeOffset? EndedAt { get; set; }
        [JsonPropertyName("rate")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Rate { get; set; }
        [JsonPropertyName("minutes")] [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] public int? Minutes { get; set; }
    }
}
