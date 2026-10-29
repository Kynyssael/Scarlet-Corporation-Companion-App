using Dalamud.Configuration;
using Scarlet.Models;
using Scarlet.Services;

namespace Scarlet;

[Serializable]
public class Configuration : IPluginConfiguration
{
    public int Version { get; set; } = 1;

    public string VenueName { get; set; } = "Scarlet Steel Tavern";
    public string CurrencySuffix { get; set; } = "gil";

    /// <summary>Print the night's total to your own chat log when you end the night.</summary>
    public bool EchoNightTotal { get; set; } = true;

    /// <summary>Scarlet Corporation site the menu and staff are read from.</summary>
    public string WebsiteUrl { get; set; } = ScarletWebClient.DefaultBaseUrl;

    /// <summary>Venue slug on the site, e.g. "scarlet-steel".</summary>
    public string VenueSlug { get; set; } = "scarlet-steel";

    public bool SyncOnStartup { get; set; } = true;
    public DateTime? LastSyncedAt { get; set; }

    public List<MenuItem> Menu { get; set; } = [];
    public List<StaffMember> Staff { get; set; } = [];

    /// <summary>
    /// Staff saved before entries had a venue: website ones are dropped (the next sync adds them back
    /// under the right venue), hand-added ones go to the selected venue. Returns true if anything changed.
    /// </summary>
    public bool MigrateStaffVenues()
    {
        var removed = Staff.RemoveAll(s => s.Venue.Length == 0 && s.FromWebsite);
        var moved = 0;
        foreach (var member in Staff.Where(s => s.Venue.Length == 0))
        {
            member.Venue = VenueSlug;
            moved++;
        }
        return removed + moved > 0;
    }
}
