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

    /// <summary>The selected venue's photo sessions and prices, from the website (Admin › Photo prices).</summary>
    public List<PhotoPackage> PhotoPackages { get; set; } = [];

    public const int DefaultPhotoCut = 30;

    /// <summary>The selected venue's house cut of photo sales, in percent (Admin › Photo prices; read when signed in).</summary>
    public int PhotoCut { get; set; } = DefaultPhotoCut;

    /// <summary>The selected venue's court services and hourly rates, from the website (Admin › Court rates).</summary>
    public List<CourtService> CourtServices { get; set; } = [];

    public const int DefaultCourtCut = 30;

    /// <summary>The selected venue's house cut of court sales, in percent (Admin › Court rates; read when signed in).</summary>
    public int CourtCut { get; set; } = DefaultCourtCut;

    /// <summary>Ring when a court session is <see cref="CourtBellMinutes"/> from the end of the time bought.</summary>
    public bool CourtBellWarning { get; set; } = true;

    public int CourtBellMinutes { get; set; } = 10;

    /// <summary>Ring again when the time bought runs out.</summary>
    public bool CourtBellAtEnd { get; set; } = true;

    /// <summary>Which chat chime plays (the game's &lt;se.1&gt; to &lt;se.16&gt;); 0 shows the chat line and toast without a sound.</summary>
    public int CourtBellSound { get; set; } = 6;

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
