using Dalamud.Plugin.Services;
using FFXIVClientStructs.FFXIV.Client.UI;
using Scarlet.Models;

namespace Scarlet.Services;

/// <summary>
/// Rings a chat chime (the game's &lt;se.N&gt; sounds, heard only by this player) when a running court
/// session is near the end of the time bought, and again when it runs out. Also prints a chat line and
/// shows a toast, in case the game's sound is off. Works with the window closed.
/// </summary>
public sealed class CourtBell : IDisposable
{
    /// <summary>Don't ring "time's up" for a session that ran out long before the plugin loaded.</summary>
    private static readonly TimeSpan LateLimit = TimeSpan.FromMinutes(2);

    private readonly Plugin plugin;

    // The end time each session was last rung for, so +30 (a new end time) rings again.
    private readonly Dictionary<Guid, DateTime> warned = [];
    private readonly Dictionary<Guid, DateTime> timeUp = [];
    private DateTime nextCheck;

    public CourtBell(Plugin plugin)
    {
        this.plugin = plugin;
        Plugin.Framework.Update += OnUpdate;
    }

    public void Dispose() => Plugin.Framework.Update -= OnUpdate;

    /// <summary>Plays the chime chosen in settings, for the Test button.</summary>
    public void Ring() => UIGlobals.PlayChatSoundEffect((uint)Math.Clamp(plugin.Configuration.CourtBellSound, 1, 16));

    private void OnUpdate(IFramework framework)
    {
        var now = DateTime.Now;
        if (now < nextCheck)
            return;
        nextCheck = now.AddSeconds(1);

        var config = plugin.Configuration;
        var running = plugin.Pos.RunningCourtSessions;
        foreach (var session in running)
        {
            var left = session.EndsAt - now;

            if (config.CourtBellWarning && left > TimeSpan.Zero && left <= TimeSpan.FromMinutes(config.CourtBellMinutes)
                && warned.GetValueOrDefault(session.Id) != session.EndsAt)
            {
                warned[session.Id] = session.EndsAt;
                Notify(session, $"{(int)Math.Ceiling(left.TotalMinutes)} minutes left");
            }

            if (config.CourtBellAtEnd && left <= TimeSpan.Zero && -left < LateLimit
                && timeUp.GetValueOrDefault(session.Id) != session.EndsAt)
            {
                timeUp[session.Id] = session.EndsAt;
                Notify(session, "time's up");
            }
        }

        // Forget stopped sessions.
        if (warned.Count + timeUp.Count > running.Count * 2)
        {
            var ids = running.Select(s => s.Id).ToHashSet();
            foreach (var id in warned.Keys.Where(id => !ids.Contains(id)).ToList())
                warned.Remove(id);
            foreach (var id in timeUp.Keys.Where(id => !ids.Contains(id)).ToList())
                timeUp.Remove(id);
        }
    }

    private void Notify(CourtSession session, string what)
    {
        var text = $"{session.Service} with {session.Buyer}: {what}{(session.Room.Length > 0 ? $" ({session.Room})" : "")}.";
        Plugin.ChatGui.Print(text, "Scarlet");
        Plugin.ToastGui.ShowQuest(text);
        if (plugin.Configuration.CourtBellSound > 0)
            Ring();
    }
}
