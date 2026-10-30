using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Models;

namespace Scarlet.Windows.Pages;

/// <summary>
/// The photographer's register: a + per photo session sold, and what they owe the venue at the
/// end of the night. Sent to the website like a bar night, marked as photos.
/// </summary>
public class PhotosPage(Plugin plugin)
{
    private const string EndNightPopup = "End the photo night?##scarletEndPhotos";

    private bool openEndNight;

    public void Draw()
    {
        var night = plugin.Pos.CurrentPhotoNight;

        Theme.PageHeader("Tonight's photos", night != null ? $"since {night.StartedAt:t}" : "tap Sold one each time you sell a photo");

        UnsentNotice.Draw(plugin);

        var packages = plugin.Configuration.PhotoPackages;
        if (packages.Count == 0)
        {
            Theme.Empty(plugin.WebSync.Syncing
                ? "Loading photo prices from the website..."
                : "No photo prices for this venue yet. Management adds them in the website admin (Photo prices), then sync.", FontAwesomeIcon.Camera);
        }

        // The cards scroll if there are many; the totals stay at the bottom.
        var footer = Theme.OwedHeight() + ImGui.GetStyle().WindowPadding.Y * 2 + ImGui.GetStyle().ItemSpacing.Y;
        using (var list = ImRaii.Child("##photoCards", new Vector2(0, -footer), false))
        {
            if (list.Success)
            {
                // One card per package, up to three to a row.
                const int perRow = 3;
                var spacing = ImGui.GetStyle().ItemSpacing.X;
                var columns = Math.Clamp(packages.Count, 1, perRow);
                var cardWidth = (ImGui.GetContentRegionAvail().X - spacing * (columns - 1)) / columns;
                for (var i = 0; i < packages.Count; i++)
                {
                    if (i % perRow != 0)
                        ImGui.SameLine();
                    DrawPackage(packages[i], cardWidth);
                }
            }
        }

        DrawTotals(night);
        DrawEndNightPopup();
    }

    private void DrawPackage(PhotoPackage photo, float width)
    {
        using var id = ImRaii.PushId(photo.Id.ToString());
        var sold = plugin.Pos.CountSold(photo);
        var button = ImGui.GetFrameHeight() * 1.6f;
        var style = ImGui.GetStyle();

        float nameLine, countLine;
        using (Theme.NameFont())
            nameLine = ImGui.GetTextLineHeight();
        using (Theme.BigFont())
            countLine = ImGui.GetTextLineHeight();
        var height = nameLine + ImGui.GetTextLineHeight() + countLine + button + style.ItemSpacing.Y * 4 + style.WindowPadding.Y * 2;

        using var card = Theme.Card("##photo", new Vector2(width, height), sold > 0);
        if (!card.Success)
            return;
        Theme.CardAccent(sold > 0 ? Theme.Scarlet : Theme.Muted with { W = 0.35f });

        // Name and price, the count big under them.
        using (Theme.NameFont())
            ImGui.Text(photo.Name);
        if (photo.Description.Length > 0 && ImGui.IsItemHovered())
        {
            using var tooltip = ImRaii.Tooltip();
            using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 28);
            ImGui.Text(photo.Description);
        }
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
            ImGui.Text(plugin.FormatGil(photo.Price));

        using (ImRaii.PushColor(ImGuiCol.Text, sold > 0 ? Theme.Rose : style.Colors[(int)ImGuiCol.TextDisabled]))
        using (Theme.BigFont())
            ImGui.Text(sold.ToString());
        ImGui.SameLine();
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + countLine - ImGui.GetTextLineHeight() - style.FramePadding.Y);
        ImGui.TextDisabled("sold");
        if (sold > 0)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"· {plugin.FormatGil(photo.Price * sold)}");
        }

        // [-] and a wide Sold one.
        using (ImRaii.Disabled(sold == 0))
        {
            if (ImGuiComponents.IconButton("minus", FontAwesomeIcon.Minus, new Vector2(button * 0.8f, button)))
                plugin.Pos.Sell(photo, -1);
        }
        if (sold > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip("Take one off (sold by mistake)");

        ImGui.SameLine();
        using var colors = Theme.Primary();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Plus, "Sold one", new Vector2(ImGui.GetContentRegionAvail().X, button)))
            plugin.Pos.Sell(photo, 1);
    }

    private void DrawTotals(Night? night)
    {
        var cut = plugin.Configuration.PhotoCut;
        var total = night?.Total ?? 0;
        var buttonWidth = 160 * ImGuiHelpers.GlobalScale;
        Theme.TotalsCard("##photoTotals", $"You owe the venue ({cut}% house cut)", plugin.FormatGil(Owed(night)),
            $"{PhotoCount(night)} · {plugin.FormatGil(total)} in sales · you keep {plugin.FormatGil(total - Owed(night))}",
            buttonWidth, () =>
            {
                using var disabled = ImRaii.Disabled(night == null || night.Lines.Count == 0);
                using var colors = Theme.Primary();
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Moon, "End night", new Vector2(buttonWidth, 0)))
                    openEndNight = true;
            });
    }

    private void DrawEndNightPopup()
    {
        if (openEndNight)
        {
            ImGui.OpenPopup(EndNightPopup);
            openEndNight = false;
        }

        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f));
        using var popup = ImRaii.PopupModal(EndNightPopup, ImGuiWindowFlags.AlwaysAutoResize);
        if (!popup.Success)
            return;

        var night = plugin.Pos.CurrentPhotoNight;
        ImGui.Text($"{PhotoCount(night)} tonight, {plugin.FormatGil(night?.Total ?? 0)} in sales.");
        ImGui.Text($"You owe the venue ({plugin.Configuration.PhotoCut}%)");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Theme.Gold))
            ImGui.Text(plugin.FormatGil(Owed(night)));
        ImGui.TextDisabled("The night is sent to the website for management.");
        ImGuiHelpers.ScaledDummy(4);

        var button = ImGuiHelpers.ScaledVector2(120, 0);
        using (Theme.Primary())
        {
            if (ImGui.Button("End night", button))
            {
                plugin.Pos.EndPhotoNight();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Keep going", button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    // Tonight's night takes the venue's current cut when it ends, so show that one.
    private long Owed(Night? night) => Night.OwedFrom(night?.Total ?? 0, plugin.Configuration.PhotoCut);

    private static string PhotoCount(Night? night) =>
        (night?.ItemCount ?? 0) switch
        {
            1 => "1 photo",
            var n => $"{n} photos",
        };
}
