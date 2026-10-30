using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Scarlet.Services;

namespace Scarlet.Windows;

/// <summary>Staff sign-in status and buttons, shared by the settings window and the locked main window.</summary>
public static class AccountControls
{
    public static void Draw(Plugin plugin)
    {
        var auth = plugin.Auth;
        var state = auth.State;

        if (state.SignedIn)
        {
            Theme.Icon(FontAwesomeIcon.UserCheck, Theme.Green);
            ImGui.SameLine();
            ImGui.Text($"Signed in as {state.Name}");
            foreach (var job in state.Jobs)
            {
                ImGui.SameLine();
                Theme.Pill(job, Theme.Rose);
            }

            var venues = string.Join(", ", state.Venues.Select(ScarletWebClient.VenueName));
            ImGui.TextDisabled(venues.Length > 0 ? $"Staff at: {venues}" : "Not staff at any venue yet.");

            using (ImRaii.Disabled(auth.Busy))
            {
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SyncAlt, "Refresh roles"))
                    auth.Refresh();
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Re-read your Discord roles, e.g. after being given a new one.");

            ImGui.SameLine();
            if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SignOutAlt, "Sign out"))
                auth.SignOut();
        }
        else if (auth.WaitingForBrowser)
        {
            Theme.Icon(FontAwesomeIcon.Hourglass, Theme.Amber);
            ImGui.SameLine();
            ImGui.Text("Waiting for your browser...");
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                auth.CancelSignIn();
        }
        else
        {
            using (ImRaii.Disabled(auth.Busy))
            {
                using var colors = Theme.Primary();
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.SignInAlt, "Sign in with Discord", ImGuiHelpers.ScaledVector2(200, 0) with { Y = ImGui.GetFrameHeight() * 1.3f }))
                    auth.SignIn();
            }
        }

        if (auth.Status.Length > 0)
            ImGui.TextWrapped(auth.Status);
        else if (state is { Offline: true, CheckedAt: { } checkedAt })
            ImGui.TextDisabled($"Offline, roles from {checkedAt:g}");
    }

    /// <summary>Shown instead of the POS when the user can't use the selected venue: a card in the middle of the window.</summary>
    public static void DrawLocked(Plugin plugin)
    {
        var state = plugin.Auth.State;
        var venue = ScarletWebClient.VenueName(plugin.Configuration.VenueSlug);

        var width = Math.Min(ImGui.GetContentRegionAvail().X, 460 * ImGuiHelpers.GlobalScale);
        var height = 230 * ImGuiHelpers.GlobalScale;
        var avail = ImGui.GetContentRegionAvail();
        ImGui.SetCursorPos(ImGui.GetCursorPos() + new Vector2((avail.X - width) / 2, Math.Max(0, (avail.Y - height) / 3)));

        using var card = Theme.Card("##locked", new Vector2(width, height));
        if (!card.Success)
            return;
        Theme.CardAccent(Theme.Scarlet);

        if (!state.SignedIn)
        {
            Theme.PageHeader("Scarlet Corporation", $"Companion app for {venue} staff");
            ImGui.TextWrapped("Sign in with your staff Discord account to open the register, photos, court timers and rooms.");
            ImGuiHelpers.ScaledDummy(4);
            Draw(plugin);
            return;
        }

        var staff = state.IsStaffOf(plugin.Configuration.VenueSlug);
        Theme.PageHeader(staff ? "No role for this app yet" : $"Not staff at {venue}", $"Signed in as {state.Name}");
        ImGui.TextWrapped(staff
            ? $"The app is for bartenders, photographers and courts. You're staff at {venue}, but you don't have the Bartender, Photographer or Court role on Discord. Ask management if this is wrong, then refresh."
            : $"Staff access comes from your Discord roles; ask management if this is wrong, then refresh.");
        ImGuiHelpers.ScaledDummy(2);

        var mine = state.Venues.Where(v => v != plugin.Configuration.VenueSlug && state.AccessFor(v).Any).ToList();
        if (mine.Count > 0)
        {
            ImGui.AlignTextToFramePadding();
            ImGui.TextDisabled("Switch to:");
            foreach (var slug in mine)
            {
                ImGui.SameLine();
                using var colors = Theme.Primary();
                if (ImGui.Button(ScarletWebClient.VenueName(slug)))
                {
                    plugin.Configuration.VenueSlug = slug;
                    plugin.SaveConfig();
                    plugin.WebSync.Sync();
                }
            }
            ImGuiHelpers.ScaledDummy(2);
        }

        Draw(plugin);
    }
}
