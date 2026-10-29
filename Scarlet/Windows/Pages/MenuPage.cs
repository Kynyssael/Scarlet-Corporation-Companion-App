using Dalamud.Bindings.ImGui;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Models;

namespace Scarlet.Windows.Pages;

/// <summary>
/// The drink menu. Items from the website are read-only here (edit them in the site's admin);
/// items added in the plugin are local extras and stay through syncs.
/// </summary>
public class MenuPage(Plugin plugin)
{
    public void Draw()
    {
        var menu = plugin.Configuration.Menu;
        var changed = false;

        SyncControls.Draw(plugin);

        if (ImGui.Button("Add local item"))
        {
            menu.Add(new MenuItem { Name = "New item", Category = "Local" });
            changed = true;
        }

        using (var table = ImRaii.Table("##menu", 6, ImGuiTableFlags.RowBg | ImGuiTableFlags.Resizable | ImGuiTableFlags.ScrollY))
        {
            if (table.Success)
            {
                ImGui.TableSetupScrollFreeze(0, 1);
                ImGui.TableSetupColumn("On", ImGuiTableColumnFlags.WidthFixed, 30);
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("Category");
                ImGui.TableSetupColumn("Price", ImGuiTableColumnFlags.WidthFixed, 110);
                ImGui.TableSetupColumn("Description");
                ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 40);
                ImGui.TableHeadersRow();

                MenuItem? toRemove = null;
                foreach (var item in menu)
                {
                    using var id = ImRaii.PushId(item.Id.ToString());
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    var available = item.Available;
                    if (ImGui.Checkbox("##on", ref available)) { item.Available = available; changed = true; }
                    if (ImGui.IsItemHovered())
                        ImGui.SetTooltip("Show on the register");

                    if (item.RemoteId != null)
                        DrawRemoteRow(item);
                    else if (DrawLocalRow(item, ref toRemove))
                        changed = true;
                }

                if (toRemove != null)
                {
                    menu.Remove(toRemove);
                    changed = true;
                }
            }
        }

        if (changed)
            plugin.SaveConfig();
    }

    private void DrawRemoteRow(MenuItem item)
    {
        ImGui.TableNextColumn();
        ImGui.Text(item.Name);
        if (plugin.GameItems.Get(item.GameItemId) is { } gameItem)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"({gameItem.Name})");
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("In-game item, set in the website admin");
        }
        else if (item.GameItemId == 0 && ImGui.IsItemHovered())
        {
            ImGui.SetTooltip("No in-game item set. Pick one in the website admin (Menu > Edit).");
        }
        ImGui.TableNextColumn();
        ImGui.Text(item.Category);
        ImGui.TableNextColumn();
        ImGui.Text(plugin.FormatGil(item.Price));
        if (item.Price == 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip($"Could not read a price from \"{item.PriceText}\"");
        ImGui.TableNextColumn();
        ImGui.TextDisabled(FirstLine(item.Description));
        if (item.Description.Length > 0 && ImGui.IsItemHovered())
            ImGui.SetTooltip(item.Description);
        ImGui.TableNextColumn();
        ImGui.TextDisabled("web");
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("From the website. Edit it in the site's admin, then sync.");
    }

    private static bool DrawLocalRow(MenuItem item, ref MenuItem? toRemove)
    {
        var changed = false;

        ImGui.TableNextColumn();
        changed |= EditText("##name", item.Name, 64, v => item.Name = v);

        ImGui.TableNextColumn();
        changed |= EditText("##cat", item.Category, 32, v => item.Category = v);

        ImGui.TableNextColumn();
        var price = item.Price;
        ImGui.SetNextItemWidth(-1);
        if (ImGui.InputInt("##price", ref price, 0, 0)) { item.Price = Math.Max(0, price); changed = true; }

        ImGui.TableNextColumn();
        changed |= EditText("##desc", item.Description, 256, v => item.Description = v);

        ImGui.TableNextColumn();
        if (ImGui.SmallButton("X") && ImGui.GetIO().KeyCtrl)
            toRemove = item;
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Ctrl+click to delete");

        return changed;
    }

    private static string FirstLine(string text)
    {
        var line = text.Split('\n', 2)[0];
        return line.Length > 80 ? line[..80] + "..." : line;
    }

    private static bool EditText(string label, string value, int max, Action<string> set)
    {
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.InputText(label, ref value, max))
            return false;
        set(value);
        return true;
    }
}
