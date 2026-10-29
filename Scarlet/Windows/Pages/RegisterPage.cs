using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Models;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>
/// The bartender's register: the menu with a + per drink on the left, tonight's receipt and
/// what they owe the venue on the right.
/// </summary>
public class RegisterPage(Plugin plugin)
{
    // Scarlet Corporation red (#b3121f) and shades of it.
    private static readonly Vector4 Scarlet = new(0.70f, 0.07f, 0.12f, 1f);
    private static readonly Vector4 ScarletHover = new(0.82f, 0.15f, 0.20f, 1f);
    private static readonly Vector4 ScarletActive = new(0.58f, 0.04f, 0.09f, 1f);
    private static readonly Vector4 Rose = new(1f, 0.62f, 0.66f, 1f);
    private static readonly Vector4 SoldRowTint = new(0.70f, 0.07f, 0.12f, 0.16f);
    private static readonly Vector4 Amber = new(1f, 0.75f, 0.3f, 1f);
    private static readonly Vector4 OutOfStock = new(1f, 0.45f, 0.45f, 1f);

    /// <summary>Fewer than this many in bags shows the count in amber.</summary>
    private const int LowStock = 5;

    private const string EndNightPopup = "End the night?##scarletEndNight";

    private string search = string.Empty;
    private string? category;

    // Set by the End night button inside the receipt child. The popup has to be opened from the
    // same ID scope it's drawn in (outside the child), or ImGui never shows it.
    private bool openEndNight;

    public void Draw()
    {
        var receiptWidth = 270 * ImGuiHelpers.GlobalScale;
        var spacing = ImGui.GetStyle().ItemSpacing.X;

        using (var menu = ImRaii.Child("##registerMenu", new Vector2(-receiptWidth - spacing, 0), false))
        {
            if (menu.Success)
                DrawMenuSide();
        }

        ImGui.SameLine();

        using (var receipt = ImRaii.Child("##registerReceipt", Vector2.Zero, true))
        {
            if (receipt.Success)
                DrawReceipt();
        }

        DrawEndNightPopup();
    }

    // ---- Menu ---------------------------------------------------------------------------------

    private void DrawMenuSide()
    {
        var available = plugin.Configuration.Menu.Where(m => m.Available).ToList();
        if (available.Count == 0)
        {
            ImGui.TextDisabled("The menu is empty. Sync it from the website in settings.");
            return;
        }

        var shown = DrawFilters(available);

        using var list = ImRaii.Child("##registerItems", Vector2.Zero, false);
        if (!list.Success)
            return;

        if (shown.Count == 0)
        {
            ImGui.TextDisabled("Nothing matches.");
            return;
        }

        using var table = ImRaii.Table("##registerTable", 4, ImGuiTableFlags.PadOuterX);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Icon", ImGuiTableColumnFlags.WidthFixed, RowHeight());
        ImGui.TableSetupColumn("Item", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 80 * ImGuiHelpers.GlobalScale);
        ImGui.TableSetupColumn("Count", ImGuiTableColumnFlags.WidthFixed, ControlsWidth());

        foreach (var group in shown.GroupBy(m => m.Category))
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(1);
            ImGuiHelpers.ScaledDummy(2);
            using (ImRaii.PushColor(ImGuiCol.Text, Rose))
                ImGui.Text(group.Key.ToUpperInvariant());

            foreach (var item in group)
                DrawItemRow(item);
        }
    }

    /// <summary>Search box and category chips. Returns the items to show.</summary>
    private List<MenuItem> DrawFilters(List<MenuItem> items)
    {
        var categories = items.Select(m => m.Category).Distinct().ToList();
        if (category != null && !categories.Contains(category))
            category = null;

        ImGui.SetNextItemWidth(-1);
        var enter = ImGui.InputTextWithHint("##registerSearch", "Search drinks... (Enter adds the first match)", ref search, 64, ImGuiInputTextFlags.EnterReturnsTrue);

        var shown = items
            .Where(m => category == null || m.Category == category)
            .Where(m => search.Length == 0
                        || m.Name.Contains(search, StringComparison.OrdinalIgnoreCase)
                        || (plugin.GameItems.Get(m.GameItemId)?.Name.Contains(search, StringComparison.OrdinalIgnoreCase) ?? false))
            .ToList();

        if (enter && shown.Count > 0)
        {
            plugin.Pos.Sell(shown[0], 1);
            search = string.Empty;
            ImGui.SetKeyboardFocusHere(-1);
        }

        if (categories.Count > 1)
        {
            Chip("All", category == null, () => category = null);
            foreach (var name in categories)
            {
                ImGui.SameLine();
                // Wrap chips onto a new line when out of room.
                if (ImGui.GetContentRegionAvail().X < ImGui.CalcTextSize(name).X + ImGui.GetStyle().FramePadding.X * 2)
                    ImGui.NewLine();
                Chip(name, category == name, () => category = name);
            }
        }

        ImGui.Separator();
        return shown;
    }

    private static void Chip(string label, bool active, Action select)
    {
        using var colors = ImRaii.PushColor(ImGuiCol.Button, Scarlet, active)
            .Push(ImGuiCol.ButtonHovered, ScarletHover, active)
            .Push(ImGuiCol.ButtonActive, ScarletActive, active);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 10 * ImGuiHelpers.GlobalScale);
        if (ImGui.Button(label))
            select();
    }

    private void DrawItemRow(MenuItem item)
    {
        using var id = ImRaii.PushId(item.Id.ToString());
        var sold = plugin.Pos.CountSold(item);
        var rowHeight = RowHeight();
        var gameItem = plugin.GameItems.Get(item.GameItemId);

        ImGui.TableNextRow(ImGuiTableRowFlags.None, rowHeight);
        if (sold > 0)
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(SoldRowTint));

        // In-game item icon.
        ImGui.TableNextColumn();
        var rightClicked = false;
        if (gameItem != null)
        {
            ImGui.Image(plugin.GameItems.Icon(gameItem).Handle, new Vector2(rowHeight));
            rightClicked = ImGui.IsItemClicked(ImGuiMouseButton.Right);
            if (ImGui.IsItemHovered())
                DrawItemTooltip(item, gameItem);
        }

        // Drink name, and under it the in-game item and how many the bartender carries.
        ImGui.TableNextColumn();
        var lines = gameItem != null ? 2 : 1;
        CenterVertically(rowHeight, ImGui.GetTextLineHeight() * lines + ImGui.GetStyle().ItemSpacing.Y * (lines - 1));
        using (ImRaii.Group())
        {
            ImGui.Text(item.Name);
            if (gameItem != null)
                DrawGameItemLine(item, gameItem);
        }
        if (ImGui.IsItemHovered())
            DrawItemTooltip(item, gameItem);
        rightClicked |= ImGui.IsItemClicked(ImGuiMouseButton.Right);
        if (gameItem != null)
            ItemContextMenu(item, gameItem, rightClicked);

        ImGui.TableNextColumn();
        CenterVertically(rowHeight, ImGui.GetTextLineHeight());
        ImGui.TextDisabled(plugin.FormatGil(item.Price));

        // [-] count [+]
        ImGui.TableNextColumn();
        var button = new Vector2(rowHeight, rowHeight);
        using (ImRaii.Disabled(sold == 0))
        {
            if (ImGuiComponents.IconButton("minus", FontAwesomeIcon.Minus, new Vector2(rowHeight * 0.8f, rowHeight)))
                plugin.Pos.Sell(item, -1);
        }

        ImGui.SameLine();
        var countText = sold.ToString();
        var countWidth = ImGui.CalcTextSize("000").X;
        var cursor = ImGui.GetCursorPos();
        ImGui.SetCursorPos(new Vector2(cursor.X + (countWidth - ImGui.CalcTextSize(countText).X) / 2, cursor.Y + (rowHeight - ImGui.GetTextLineHeight()) / 2));
        if (sold > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, Rose))
                ImGui.Text(countText);
        }
        else
        {
            ImGui.TextDisabled(countText);
        }

        ImGui.SameLine();
        ImGui.SetCursorPos(new Vector2(cursor.X + countWidth + ImGui.GetStyle().ItemSpacing.X, cursor.Y));
        if (ImGuiComponents.IconButton("plus", FontAwesomeIcon.Plus, Scarlet, ScarletActive, ScarletHover, button))
            plugin.Pos.Sell(item, 1);
    }

    private void DrawGameItemLine(MenuItem item, GameItemInfo gameItem)
    {
        // Skip the name when the drink is already called the same as the item.
        if (!gameItem.Name.Equals(item.Name, StringComparison.OrdinalIgnoreCase))
        {
            ImGui.TextDisabled(gameItem.Name);
            ImGui.SameLine();
            ImGui.TextDisabled("·");
            ImGui.SameLine();
        }

        var carried = plugin.GameItems.CountInBags(gameItem.Id);
        using (ImRaii.PushColor(ImGuiCol.Text, carried == 0 ? OutOfStock : carried < LowStock ? Amber : ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]))
            ImGui.Text(carried == 0 ? "none in bags" : $"{carried} in bags");
    }

    private static void DrawItemTooltip(MenuItem item, GameItemInfo? gameItem)
    {
        if (item.Description.Length == 0 && gameItem == null)
            return;

        using var tooltip = ImRaii.Tooltip();
        using var wrap = ImRaii.TextWrapPos(ImGui.GetFontSize() * 28);
        if (gameItem != null)
        {
            ImGui.Text($"Serve: {gameItem.Name}");
            ImGui.TextDisabled("Right-click for an item link in chat.");
        }
        if (item.Description.Length > 0)
        {
            if (gameItem != null)
                ImGui.Separator();
            ImGui.Text(item.Description);
        }
    }

    /// <summary>Right-click menu on the icon or name. Opened and drawn in the same ID scope.</summary>
    private static void ItemContextMenu(MenuItem item, GameItemInfo gameItem, bool open)
    {
        var popupId = $"##itemMenu{item.Id}";
        if (open)
            ImGui.OpenPopup(popupId);

        using var popup = ImRaii.Popup(popupId);
        if (!popup.Success)
            return;

        ImGui.TextDisabled(gameItem.Name);
        if (ImGui.Selectable("Show item link in chat"))
            GameItems.PrintLink(gameItem);
        if (ImGui.Selectable("Copy item name"))
            ImGui.SetClipboardText(gameItem.Name);
    }

    // Tall enough for the drink name plus the in-game item line.
    private static float RowHeight() =>
        Math.Max(ImGui.GetFrameHeight() * 1.25f, ImGui.GetTextLineHeight() * 2 + ImGui.GetStyle().ItemSpacing.Y + 4 * ImGuiHelpers.GlobalScale);

    private static float ControlsWidth()
    {
        var rowHeight = RowHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.X;
        return rowHeight * 0.8f + spacing + ImGui.CalcTextSize("000").X + spacing + rowHeight + 4;
    }

    private static void CenterVertically(float rowHeight, float contentHeight) =>
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + (rowHeight - contentHeight) / 2);

    // ---- Receipt ------------------------------------------------------------------------------

    private void DrawReceipt()
    {
        var night = plugin.Pos.CurrentNight;

        using (ImRaii.PushColor(ImGuiCol.Text, Rose))
            ImGui.Text("Tonight");
        ImGui.SameLine();
        ImGui.TextDisabled(night != null ? $"since {night.StartedAt:t}" : "nothing sold yet");
        ImGui.Separator();

        DrawUnsentNotice();

        var style = ImGui.GetStyle();
        var footer = ImGui.GetTextLineHeight() * 1.8f + ImGui.GetTextLineHeightWithSpacing() * 2
                     + ImGui.GetFrameHeightWithSpacing() * 2 + style.ItemSpacing.Y * 3 + style.WindowPadding.Y;

        using (var lines = ImRaii.Child("##receiptLines", new Vector2(0, -footer), false))
        {
            if (lines.Success)
                DrawReceiptLines(night);
        }

        ImGui.Separator();
        DrawTotals(night);
    }

    /// <summary>
    /// Only shown when an ended night hasn't reached the website, so the bartender can resend it.
    /// Bartenders don't get the full History; management does.
    /// </summary>
    private void DrawUnsentNotice()
    {
        var refused = plugin.Pos.Nights.Where(n => n.UploadError != null).ToList();
        var waiting = plugin.Pos.Nights.Count(n => n.UploadError == null && n.UploadedAt == null);
        if (refused.Count == 0 && waiting == 0)
            return;

        if (refused.Count > 0)
        {
            using (ImRaii.PushColor(ImGuiCol.Text, new Vector4(1f, 0.45f, 0.45f, 1f)))
                ImGui.TextWrapped(refused.Count == 1 ? "A night report couldn't be sent." : $"{refused.Count} night reports couldn't be sent.");
            if (ImGui.IsItemHovered())
            {
                using var tooltip = ImRaii.Tooltip();
                foreach (var night in refused)
                    ImGui.Text($"{night.StartedAt:g}: {night.UploadError}");
            }
        }
        else
        {
            ImGui.TextDisabled(waiting == 1 ? "Last night's report is waiting to send." : $"{waiting} night reports are waiting to send.");
            if (plugin.Reports.Status.Length > 0 && ImGui.IsItemHovered())
                ImGui.SetTooltip(plugin.Reports.Status);
        }

        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.PaperPlane, refused.Count > 0 ? "Try again" : "Send now", new Vector2(ImGui.GetContentRegionAvail().X, 0)))
            plugin.Reports.RetryRefused();
        ImGui.Separator();
    }

    private void DrawReceiptLines(Night? night)
    {
        if (night == null || night.Lines.Count == 0)
        {
            ImGui.TextDisabled("Tap + next to a drink");
            ImGui.TextDisabled("each time you sell one.");
            return;
        }

        using var table = ImRaii.Table("##receiptTable", 2);
        if (!table.Success)
            return;

        ImGui.TableSetupColumn("Line", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableSetupColumn("Total", ImGuiTableColumnFlags.WidthFixed);

        foreach (var line in night.Lines)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            using (ImRaii.PushColor(ImGuiCol.Text, Rose))
                ImGui.Text($"{line.Quantity}x");
            ImGui.SameLine();
            ImGui.TextWrapped(line.Name);
            ImGui.TableNextColumn();
            RightAlignedText(plugin.FormatGil(line.Total), disabled: true);
        }
    }

    private void DrawTotals(Night? night)
    {
        ImGui.TextDisabled("You owe the venue");

        using (ImRaii.PushColor(ImGuiCol.Text, Rose))
        {
            ImGui.SetWindowFontScale(1.8f);
            ImGui.Text(plugin.FormatGil(night?.Total ?? 0));
            ImGui.SetWindowFontScale(1f);
        }

        ImGui.TextDisabled($"{night?.ItemCount ?? 0} items sold");

        var width = ImGui.GetContentRegionAvail().X;
        var last = plugin.Pos.LastSold;
        using (ImRaii.Disabled(last == null))
        {
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Undo, last != null ? $"Undo {Shorten(last.Name, 18)}" : "Undo last", new Vector2(width, 0)))
                plugin.Pos.UndoLast();
        }

        using (ImRaii.Disabled(night == null || night.Lines.Count == 0))
        {
            using var colors = ImRaii.PushColor(ImGuiCol.Button, Scarlet)
                .Push(ImGuiCol.ButtonHovered, ScarletHover)
                .Push(ImGuiCol.ButtonActive, ScarletActive);
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Moon, "End night", new Vector2(width, 0)))
                openEndNight = true;
        }
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

        var night = plugin.Pos.CurrentNight;
        ImGui.Text($"You sold {night?.ItemCount ?? 0} items tonight.");
        ImGui.Text("You owe the venue");
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Rose))
            ImGui.Text(plugin.FormatGil(night?.Total ?? 0));
        ImGui.TextDisabled("The night is saved to History and sent to the website.");
        ImGuiHelpers.ScaledDummy(4);

        var button = ImGuiHelpers.ScaledVector2(120, 0);
        using (ImRaii.PushColor(ImGuiCol.Button, Scarlet).Push(ImGuiCol.ButtonHovered, ScarletHover).Push(ImGuiCol.ButtonActive, ScarletActive))
        {
            if (ImGui.Button("End night", button))
            {
                plugin.Pos.EndNight();
                ImGui.CloseCurrentPopup();
            }
        }

        ImGui.SameLine();
        if (ImGui.Button("Keep going", button) || ImGui.IsKeyPressed(ImGuiKey.Escape))
            ImGui.CloseCurrentPopup();
    }

    private static void RightAlignedText(string text, bool disabled)
    {
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X);
        if (disabled)
            ImGui.TextDisabled(text);
        else
            ImGui.Text(text);
    }

    private static string Shorten(string text, int max) => text.Length <= max ? text : text[..(max - 3)] + "...";
}
