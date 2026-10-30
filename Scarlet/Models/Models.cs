namespace Scarlet.Models;

public class MenuItem
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = "Uncategorized";
    public int Price { get; set; }
    public string Description { get; set; } = string.Empty;
    public bool Available { get; set; } = true;

    /// <summary>Id on the Scarlet Corporation site; null for items added only in the plugin.</summary>
    public int? RemoteId { get; set; }

    /// <summary>Price as written on the site, e.g. "15,000 gil".</summary>
    public string PriceText { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;

    /// <summary>The real FFXIV item (Item sheet row) this drink is served as; 0 when not set on the site.</summary>
    public uint GameItemId { get; set; }
}

public class OrderLine
{
    public Guid MenuItemId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int UnitPrice { get; set; }
    public int Quantity { get; set; } = 1;

    public long Total => (long)UnitPrice * Quantity;

    // Court sessions only (one line per session, Quantity 1, Name = the service): who it was
    // with, where, when, and the hourly rate and minutes bought UnitPrice was worked out from.
    public string? Buyer { get; set; }
    public string? Room { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? EndedAt { get; set; }
    public int? Rate { get; set; }
    public int? Minutes { get; set; }
}

/// <summary>A court session still running: the timer started, not stopped yet.</summary>
public class CourtSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Service { get; set; } = string.Empty;
    public int Rate { get; set; }
    public string Buyer { get; set; } = string.Empty;

    /// <summary>The private room locked for the session on the website; null when none.</summary>
    public int? RoomId { get; set; }
    public string Room { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;

    /// <summary>The time the client bought, in minutes: what the session costs. The timer is only a guide.</summary>
    public int BoughtMinutes { get; set; } = 60;

    public DateTime EndsAt => StartedAt.AddMinutes(BoughtMinutes);

    public int Cost => Price(Rate, BoughtMinutes);

    /// <summary>Same as the website (nightReports.js sessionPrice): the hourly rate for the minutes bought.</summary>
    public static int Price(int rate, int minutes) =>
        (int)Math.Clamp(Math.Round((long)rate * minutes / 60.0, MidpointRounding.AwayFromZero), 0, int.MaxValue);

    /// <summary>Whole minutes between two times (how long a session really ran).</summary>
    public static int Minutes(DateTime from, DateTime to) =>
        Math.Max(0, (int)Math.Round((to - from).TotalMinutes, MidpointRounding.AwayFromZero));
}

/// <summary>A court service and its hourly rate, from the website (Admin › Court rates).</summary>
public class CourtService
{
    public int RemoteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Rate { get; set; }
    public string Description { get; set; } = string.Empty;
}

/// <summary>
/// One bartender's tally for a night: every drink sold, so they know what they owe the venue.
/// The current night is open (EndedAt null) until the bartender ends it.
/// </summary>
public class Night
{
    public const string Bar = "bar";
    public const string Photo = "photo";
    public const string Court = "court";

    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary><see cref="Bar"/> (drinks) or <see cref="Photo"/> (photo sessions). Nights saved before photo nights are bar nights.</summary>
    public string Kind { get; set; } = Bar;
    public string Bartender { get; set; } = string.Empty;
    public string Venue { get; set; } = string.Empty;
    public string VenueSlug { get; set; } = string.Empty;
    public DateTime StartedAt { get; set; } = DateTime.Now;
    public DateTime? EndedAt { get; set; }
    public List<OrderLine> Lines { get; set; } = [];
    public bool Voided { get; set; }

    /// <summary>When the website accepted this night; null while it still has to be sent.</summary>
    public DateTime? UploadedAt { get; set; }

    /// <summary>Why the website refused this night; it isn't retried.</summary>
    public string? UploadError { get; set; }

    /// <summary>Voided or restored after upload; the website still has to be told.</summary>
    public bool VoidPending { get; set; }

    /// <summary>The house's percent of the total: 100 for a bar night, the venue's photo cut for a photo night.</summary>
    public int HouseCut { get; set; } = 100;

    public long Total => Lines.Sum(l => l.Total);
    public int ItemCount => Lines.Sum(l => l.Quantity);

    /// <summary>What the seller owes the venue. The website works this out again when the night arrives.</summary>
    public long Owed => OwedFrom(Total, HouseCut);

    // Same rounding as the site (Math.round).
    public static long OwedFrom(long total, int cut) => (long)Math.Round(total * cut / 100.0, MidpointRounding.AwayFromZero);
}

/// <summary>A photo session a photographer sells, from the website. Management sets the names and prices there.</summary>
public class PhotoPackage
{
    /// <summary>Kept across syncs so tonight's lines stay matched to the package.</summary>
    public Guid Id { get; set; } = Guid.NewGuid();

    /// <summary>Id on the Scarlet Corporation site.</summary>
    public int RemoteId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public string Description { get; set; } = string.Empty;
}

public class StaffMember
{
    public string Name { get; set; } = string.Empty;
    public string Role { get; set; } = "Bartender";

    /// <summary>Venue slug this entry belongs to; someone working two venues has one entry per venue.</summary>
    public string Venue { get; set; } = string.Empty;

    /// <summary>On site tonight at this venue.</summary>
    public bool OnShift { get; set; }
    public bool FromWebsite { get; set; }
}

/// <summary>Runtime POS state persisted separately from the plugin config.</summary>
public class PosData
{
    public Night? CurrentNight { get; set; }

    /// <summary>The photographer's tally, kept apart so management can run the bar and take photos the same night.</summary>
    public Night? CurrentPhotoNight { get; set; }

    /// <summary>The court's finished sessions tonight, one line each.</summary>
    public Night? CurrentCourtNight { get; set; }

    /// <summary>Court sessions whose timer is still running.</summary>
    public List<CourtSession> RunningCourtSessions { get; set; } = [];
    public List<Night> Nights { get; set; } = [];
}
