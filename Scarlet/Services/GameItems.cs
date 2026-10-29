using Dalamud.Game.Inventory;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Textures.TextureWraps;
using Lumina.Excel.Sheets;

namespace Scarlet.Services;

public record GameItemInfo(uint Id, string Name, uint Icon);

/// <summary>
/// The real FFXIV items menu entries are served as: name and icon from the game's own data
/// (so they match the client's language), and how many the bartender carries.
/// Use from the UI thread only.
/// </summary>
public sealed class GameItems
{
    private static readonly TimeSpan CountRefresh = TimeSpan.FromSeconds(1);

    private static readonly GameInventoryType[] Bags =
    [
        GameInventoryType.Inventory1,
        GameInventoryType.Inventory2,
        GameInventoryType.Inventory3,
        GameInventoryType.Inventory4,
    ];

    private readonly Dictionary<uint, GameItemInfo?> items = [];
    private readonly Dictionary<uint, int> counts = [];
    private DateTime countsReadAt;

    public GameItemInfo? Get(uint id)
    {
        if (id == 0)
            return null;
        if (items.TryGetValue(id, out var cached))
            return cached;

        GameItemInfo? info = null;
        if (Plugin.DataManager.GetExcelSheet<Item>().GetRowOrDefault(id) is { } row)
            info = new GameItemInfo(id, row.Name.ExtractText(), row.Icon);
        items[id] = info;
        return info;
    }

    public IDalamudTextureWrap Icon(GameItemInfo item) =>
        Plugin.TextureProvider.GetFromGameIcon(new GameIconLookup(item.Icon)).GetWrapOrEmpty();

    /// <summary>How many of the item (NQ and HQ) are in the main inventory bags.</summary>
    public int CountInBags(uint id)
    {
        if (DateTime.UtcNow - countsReadAt > CountRefresh)
            ReadCounts();
        return counts.GetValueOrDefault(id);
    }

    /// <summary>Prints a clickable item link to the bartender's own chat log.</summary>
    public static void PrintLink(GameItemInfo item) =>
        Plugin.ChatGui.Print(new SeStringBuilder().AddText("[Scarlet] ").Append(SeString.CreateItemLink(item.Id, false)).Build());

    private void ReadCounts()
    {
        counts.Clear();
        countsReadAt = DateTime.UtcNow;
        foreach (var bag in Bags)
        {
            foreach (var slot in Plugin.GameInventory.GetInventoryItems(bag))
            {
                if (slot.BaseItemId != 0)
                    counts[slot.BaseItemId] = counts.GetValueOrDefault(slot.BaseItemId) + (int)slot.Quantity;
            }
        }
    }
}
