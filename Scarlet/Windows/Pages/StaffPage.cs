using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Models;
using Scarlet.Services;

namespace Scarlet.Windows.Pages;

/// <summary>The selected venue's staff, and who is on site tonight.</summary>
public class StaffPage(Plugin plugin)
{
    private string newName = string.Empty;

    public void Draw()
    {
        var config = plugin.Configuration;
        var venue = config.VenueSlug;
        var staff = config.Staff.Where(s => s.Venue == venue).ToList();
        var changed = false;

        Theme.PageHeader($"{ScarletWebClient.VenueName(venue)} staff", $"{staff.Count(s => s.OnShift)} of {staff.Count} on site tonight");

        ImGui.SetNextItemWidth(220 * ImGuiHelpers.GlobalScale);
        var submitted = ImGui.InputTextWithHint("##newStaff", "Character name", ref newName, 64, ImGuiInputTextFlags.EnterReturnsTrue);
        ImGui.SameLine();
        if ((ImGuiComponents.IconButtonWithText(FontAwesomeIcon.UserPlus, "Add") || submitted) && newName.Trim().Length > 0)
        {
            changed |= Add(staff, newName.Trim(), venue);
            newName = string.Empty;
        }
        ImGui.SameLine();
        if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.User, "Add me") && Plugin.LocalPlayerName() is { } me)
            changed |= Add(staff, me, venue);

        using (var table = ImRaii.Table("##staff", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.PadOuterX))
        {
            if (table.Success)
            {
                ImGui.TableSetupColumn("On site", ImGuiTableColumnFlags.WidthFixed, 60);
                ImGui.TableSetupColumn("Name");
                ImGui.TableSetupColumn("Role");
                ImGui.TableSetupColumn("##del", ImGuiTableColumnFlags.WidthFixed, 50);
                ImGui.TableHeadersRow();

                StaffMember? toRemove = null;
                foreach (var member in staff)
                {
                    using var id = ImRaii.PushId(member.Name);
                    ImGui.TableNextRow();

                    ImGui.TableNextColumn();
                    var onSite = member.OnShift;
                    if (ImGui.Checkbox("##onSite", ref onSite)) { member.OnShift = onSite; changed = true; }

                    ImGui.TableNextColumn();
                    Theme.Dot(member.OnShift ? Theme.Green : Theme.Muted with { W = 0.5f });
                    ImGui.SameLine();
                    ImGui.Text(member.Name);

                    ImGui.TableNextColumn();
                    var role = member.Role;
                    ImGui.SetNextItemWidth(-1);
                    if (ImGui.InputText("##role", ref role, 32)) { member.Role = role; changed = true; }

                    ImGui.TableNextColumn();
                    if (member.FromWebsite)
                    {
                        Theme.Pill("web", Theme.Muted);
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("From the venue's Staff page on the website; remove them there.");
                    }
                    else
                    {
                        if (ImGuiComponents.IconButton("remove", FontAwesomeIcon.Trash) && ImGui.GetIO().KeyCtrl)
                            toRemove = member;
                        if (ImGui.IsItemHovered())
                            ImGui.SetTooltip("Ctrl+click to remove");
                    }
                }

                if (toRemove != null)
                {
                    config.Staff.Remove(toRemove);
                    changed = true;
                }
            }
        }

        if (staff.Count == 0)
            Theme.Empty("No staff yet. Sync from the website, or add people above.", FontAwesomeIcon.Users);

        if (changed)
            plugin.SaveConfig();
    }

    private bool Add(List<StaffMember> venueStaff, string name, string venue)
    {
        if (venueStaff.Any(s => s.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            return false;
        plugin.Configuration.Staff.Add(new StaffMember { Name = name, Venue = venue });
        return true;
    }
}
