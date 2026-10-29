using Dalamud.Bindings.ImGui;
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
            ImGui.Text($"Signed in as {state.Name}");
            var jobs = string.Join(", ", state.Jobs);
            if (jobs.Length > 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled($"({jobs})");
            }

            var venues = string.Join(", ", state.Venues.Select(ScarletWebClient.VenueName));
            ImGui.TextDisabled(venues.Length > 0 ? $"Staff at: {venues}" : "Not staff at any venue yet.");

            using (ImRaii.Disabled(auth.Busy))
            {
                if (ImGui.Button("Refresh roles"))
                    auth.Refresh();
            }
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                ImGui.SetTooltip("Re-read your Discord roles, e.g. after being given a new one.");

            ImGui.SameLine();
            if (ImGui.Button("Sign out"))
                auth.SignOut();
        }
        else if (auth.WaitingForBrowser)
        {
            ImGui.Text("Waiting for your browser...");
            ImGui.SameLine();
            if (ImGui.Button("Cancel"))
                auth.CancelSignIn();
        }
        else
        {
            using (ImRaii.Disabled(auth.Busy))
            {
                if (ImGui.Button("Sign in with Discord"))
                    auth.SignIn();
            }
        }

        if (auth.Status.Length > 0)
            ImGui.TextWrapped(auth.Status);
        else if (state is { Offline: true, CheckedAt: { } checkedAt })
            ImGui.TextDisabled($"Offline, roles from {checkedAt:g}");
    }

    /// <summary>Shown instead of the POS when the user can't use the selected venue.</summary>
    public static void DrawLocked(Plugin plugin)
    {
        var state = plugin.Auth.State;
        var venue = ScarletWebClient.VenueName(plugin.Configuration.VenueSlug);

        if (!state.SignedIn)
        {
            ImGui.TextWrapped($"Sign in with your staff Discord account to use the {venue} companion app.");
            ImGui.Spacing();
            Draw(plugin);
            return;
        }

        ImGui.TextWrapped(state.IsStaffOf(plugin.Configuration.VenueSlug)
            ? $"The app is for bartenders and photographers. You're staff at {venue}, but you don't have the Bartender or Photographer role on Discord. Ask management if this is wrong, then refresh."
            : $"You are not staff at {venue}. Staff access comes from your Discord roles; ask management if this is wrong, then refresh.");
        ImGui.Spacing();

        var mine = state.Venues.Where(v => v != plugin.Configuration.VenueSlug && state.AccessFor(v).Any).ToList();
        if (mine.Count > 0)
        {
            ImGui.Text("Switch to:");
            foreach (var slug in mine)
            {
                ImGui.SameLine();
                if (ImGui.Button(ScarletWebClient.VenueName(slug)))
                {
                    plugin.Configuration.VenueSlug = slug;
                    plugin.SaveConfig();
                    plugin.WebSync.Sync();
                }
            }
            ImGui.Spacing();
        }

        Draw(plugin);
    }
}
