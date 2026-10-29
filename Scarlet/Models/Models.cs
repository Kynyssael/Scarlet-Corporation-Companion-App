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
}

/// <summary>
/// One bartender's tally for a night: every drink sold, so they know what they owe the venue.
/// The current night is open (EndedAt null) until the bartender ends it.
/// </summary>
public class Night
{
    public Guid Id { get; set; } = Guid.NewGuid();
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

    public long Total => Lines.Sum(l => l.Total);
    public int ItemCount => Lines.Sum(l => l.Quantity);
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
    public List<Night> Nights { get; set; } = [];
}
