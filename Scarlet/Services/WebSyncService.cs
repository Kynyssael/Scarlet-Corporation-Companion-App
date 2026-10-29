using Scarlet.Models;

namespace Scarlet.Services;

/// <summary>Pulls the selected venue's menu and staff from the website into the local config.</summary>
public sealed class WebSyncService(Plugin plugin) : IDisposable
{
    private readonly ScarletWebClient client = new();
    private CancellationTokenSource? cts;

    public bool Syncing { get; private set; }
    public string Status { get; private set; } = string.Empty;

    public void Dispose()
    {
        cts?.Cancel();
        client.Dispose();
    }

    // A sync asked for while one was running (e.g. the venue was switched); run it after.
    private bool resyncRequested;

    public void Sync()
    {
        if (Syncing)
        {
            resyncRequested = true;
            return;
        }

        var config = plugin.Configuration;
        var baseUrl = config.WebsiteUrl;
        var slug = config.VenueSlug;

        Syncing = true;
        Status = "Syncing...";
        cts = new CancellationTokenSource();
        var token = cts.Token;

        Task.Run(async () =>
        {
            try
            {
                var venue = await client.FetchVenueAsync(baseUrl, slug, token);
                // Config lists are read by the UI every frame; change them on the main thread.
                await Plugin.Framework.RunOnFrameworkThread(() => Apply(venue, slug));
            }
            catch (Exception ex) when (!token.IsCancellationRequested)
            {
                Plugin.Log.Warning(ex, "Website sync failed");
                Status = $"Sync failed: {ex.Message}";
            }
            finally
            {
                Syncing = false;
                if (resyncRequested && !token.IsCancellationRequested)
                {
                    resyncRequested = false;
                    await Plugin.Framework.RunOnFrameworkThread(Sync);
                }
            }
        }, token);
    }

    private void Apply(VenueDto venue, string slug)
    {
        var config = plugin.Configuration;
        // The venue was switched while this was downloading; the queued sync fetches the right one.
        if (slug != config.VenueSlug)
            return;

        var baseUrl = config.WebsiteUrl.TrimEnd('/');

        // Keep the local "available" toggle for items that still exist on the site.
        var previous = config.Menu.Where(m => m.RemoteId != null).ToDictionary(m => m.RemoteId!.Value);
        var local = config.Menu.Where(m => m.RemoteId == null).ToList();

        var remote = venue.Menu.OrderBy(m => m.Sort).ThenBy(m => m.Id).Select(dto =>
        {
            var item = previous.GetValueOrDefault(dto.Id) ?? new MenuItem { RemoteId = dto.Id };
            item.Name = dto.Name;
            item.Category = string.IsNullOrWhiteSpace(dto.Section) ? "Uncategorized" : dto.Section;
            item.Description = dto.Description;
            item.PriceText = dto.Price;
            item.Price = ScarletWebClient.ParsePrice(dto.Price);
            item.ImageUrl = dto.ImageUrl.StartsWith('/') ? baseUrl + dto.ImageUrl : dto.ImageUrl;
            item.GameItemId = (uint)Math.Max(0, dto.GameItemId);
            return item;
        }).ToList();

        config.Menu = [.. remote, .. local];

        // Staff belong to the venue being synced. People taken off that venue's Staff page on the
        // site are dropped here too; entries added by hand in the plugin stay.
        var remoteStaff = venue.Staff.Where(s => !string.IsNullOrWhiteSpace(s.Name)).ToList();
        config.Staff.RemoveAll(s => s.FromWebsite && s.Venue == slug
                                    && !remoteStaff.Any(r => r.Name.Equals(s.Name, StringComparison.OrdinalIgnoreCase)));

        var added = 0;
        foreach (var dto in remoteStaff)
        {
            var member = config.Staff.FirstOrDefault(s => s.Venue == slug && s.Name.Equals(dto.Name, StringComparison.OrdinalIgnoreCase));
            if (member == null)
            {
                config.Staff.Add(member = new StaffMember { Name = dto.Name, Venue = slug });
                added++;
            }
            member.FromWebsite = true;
            if (!string.IsNullOrWhiteSpace(dto.Role))
                member.Role = dto.Role;
        }

        config.VenueName = ScarletWebClient.VenueName(config.VenueSlug);
        config.LastSyncedAt = DateTime.Now;
        plugin.SaveConfig();

        Status = $"Synced {remote.Count} menu items, {added} new staff at {DateTime.Now:t}.";
    }
}
