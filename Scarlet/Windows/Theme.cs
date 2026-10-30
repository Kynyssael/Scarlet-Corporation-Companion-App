using System.Numerics;
using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using Dalamud.Interface.GameFonts;
using Dalamud.Interface.ManagedFontAtlas;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;

namespace Scarlet.Windows;

/// <summary>
/// The Scarlet Corporation look: a dark wine background, scarlet (#b3121f) tabs and buttons, rounded
/// cards, and the game's own fonts for headings and big gil amounts. Every page draws with these helpers.
/// </summary>
public static class Theme
{
    // Brand colours.
    public static readonly Vector4 Scarlet = new(0.70f, 0.07f, 0.12f, 1f);
    public static readonly Vector4 ScarletHover = new(0.82f, 0.15f, 0.20f, 1f);
    public static readonly Vector4 ScarletActive = new(0.58f, 0.04f, 0.09f, 1f);
    public static readonly Vector4 Rose = new(1f, 0.62f, 0.66f, 1f);
    public static readonly Vector4 Gold = new(0.95f, 0.80f, 0.52f, 1f);

    // Status colours.
    public static readonly Vector4 Green = new(0.45f, 0.85f, 0.55f, 1f);
    public static readonly Vector4 Red = new(1f, 0.45f, 0.45f, 1f);
    public static readonly Vector4 Amber = new(1f, 0.75f, 0.3f, 1f);
    public static readonly Vector4 Muted = new(0.62f, 0.55f, 0.57f, 1f);

    // Surfaces, darkest first.
    private static readonly Vector4 WindowBg = new(0.075f, 0.052f, 0.060f, 0.97f);
    public static readonly Vector4 CardBg = new(0.125f, 0.082f, 0.092f, 1f);
    public static readonly Vector4 CardTint = new(0.70f, 0.07f, 0.12f, 0.16f);
    private static readonly Vector4 FrameBg = new(0.17f, 0.105f, 0.115f, 1f);
    private static readonly Vector4 Line = new(0.42f, 0.13f, 0.17f, 0.55f);

    private static IFontHandle? title;
    private static IFontHandle? big;
    private static IFontHandle? name;

    /// <summary>Loads the heading fonts. Called once by the plugin; <see cref="Dispose"/> frees them.</summary>
    public static void LoadFonts(IFontAtlas atlas)
    {
        title = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic23));
        big = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.TrumpGothic34));
        name = atlas.NewGameFontHandle(new GameFontStyle(GameFontFamilyAndSize.Axis18));
    }

    public static void Dispose()
    {
        title?.Dispose();
        big?.Dispose();
        name?.Dispose();
    }

    // Fonts fall back to the normal one until the game fonts are built.
    public static IDisposable? TitleFont() => title is { Available: true } f ? f.Push() : null;
    public static IDisposable? BigFont() => big is { Available: true } f ? f.Push() : null;
    public static IDisposable? NameFont() => name is { Available: true } f ? f.Push() : null;

    // ---- Window style -------------------------------------------------------------------------

    /// <summary>Pushes the theme for a whole window: call in PreDraw, then <see cref="Scope.Pop"/> in PostDraw.</summary>
    public sealed class Scope
    {
        private int colors;
        private int styles;

        public void Push()
        {
            Color(ImGuiCol.WindowBg, WindowBg);
            Color(ImGuiCol.ChildBg, Vector4.Zero);
            Color(ImGuiCol.PopupBg, new Vector4(0.10f, 0.065f, 0.075f, 0.98f));
            Color(ImGuiCol.Border, Line);
            Color(ImGuiCol.Separator, Line);
            Color(ImGuiCol.TitleBg, new Vector4(0.11f, 0.045f, 0.055f, 1f));
            Color(ImGuiCol.TitleBgActive, new Vector4(0.36f, 0.045f, 0.08f, 1f));
            Color(ImGuiCol.TitleBgCollapsed, new Vector4(0.11f, 0.045f, 0.055f, 0.8f));

            Color(ImGuiCol.FrameBg, FrameBg);
            Color(ImGuiCol.FrameBgHovered, new Vector4(0.24f, 0.12f, 0.14f, 1f));
            Color(ImGuiCol.FrameBgActive, new Vector4(0.30f, 0.13f, 0.16f, 1f));
            Color(ImGuiCol.Button, new Vector4(0.21f, 0.12f, 0.135f, 1f));
            Color(ImGuiCol.ButtonHovered, new Vector4(0.33f, 0.14f, 0.17f, 1f));
            Color(ImGuiCol.ButtonActive, new Vector4(0.42f, 0.15f, 0.19f, 1f));
            Color(ImGuiCol.Header, new Vector4(0.45f, 0.07f, 0.11f, 0.55f));
            Color(ImGuiCol.HeaderHovered, new Vector4(0.58f, 0.10f, 0.15f, 0.75f));
            Color(ImGuiCol.HeaderActive, Scarlet);
            Color(ImGuiCol.CheckMark, Rose);
            Color(ImGuiCol.SliderGrab, Scarlet);
            Color(ImGuiCol.SliderGrabActive, ScarletHover);
            Color(ImGuiCol.TextSelectedBg, Scarlet with { W = 0.4f });

            Color(ImGuiCol.Tab, new Vector4(0.15f, 0.08f, 0.09f, 1f));
            Color(ImGuiCol.TabHovered, ScarletHover);
            Color(ImGuiCol.TabActive, Scarlet);
            Color(ImGuiCol.TabUnfocused, new Vector4(0.12f, 0.07f, 0.08f, 1f));
            Color(ImGuiCol.TabUnfocusedActive, ScarletActive);

            Color(ImGuiCol.TableHeaderBg, new Vector4(0.20f, 0.085f, 0.10f, 1f));
            Color(ImGuiCol.TableBorderStrong, Line);
            Color(ImGuiCol.TableBorderLight, Line with { W = 0.3f });
            Color(ImGuiCol.TableRowBgAlt, new Vector4(1f, 1f, 1f, 0.025f));

            Color(ImGuiCol.ScrollbarBg, Vector4.Zero);
            Color(ImGuiCol.ScrollbarGrab, new Vector4(0.32f, 0.13f, 0.16f, 1f));
            Color(ImGuiCol.ScrollbarGrabHovered, new Vector4(0.45f, 0.14f, 0.18f, 1f));
            Color(ImGuiCol.ScrollbarGrabActive, Scarlet);
            Color(ImGuiCol.ResizeGrip, Scarlet with { W = 0.25f });
            Color(ImGuiCol.ResizeGripHovered, Scarlet with { W = 0.6f });
            Color(ImGuiCol.ResizeGripActive, Scarlet);

            var scale = ImGuiHelpers.GlobalScale;
            Style(ImGuiStyleVar.WindowRounding, 8 * scale);
            Style(ImGuiStyleVar.ChildRounding, 8 * scale);
            Style(ImGuiStyleVar.FrameRounding, 5 * scale);
            Style(ImGuiStyleVar.PopupRounding, 6 * scale);
            Style(ImGuiStyleVar.GrabRounding, 4 * scale);
            Style(ImGuiStyleVar.TabRounding, 5 * scale);
            Style(ImGuiStyleVar.ScrollbarRounding, 6 * scale);
            Style(ImGuiStyleVar.WindowPadding, new Vector2(12, 10) * scale);
            Style(ImGuiStyleVar.FramePadding, new Vector2(8, 5) * scale);
            Style(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6) * scale);
            Style(ImGuiStyleVar.CellPadding, new Vector2(6, 4) * scale);
        }

        public void Pop()
        {
            ImGui.PopStyleColor(colors);
            ImGui.PopStyleVar(styles);
            colors = styles = 0;
        }

        private void Color(ImGuiCol col, Vector4 value)
        {
            ImGui.PushStyleColor(col, value);
            colors++;
        }

        private void Style(ImGuiStyleVar var, float value)
        {
            ImGui.PushStyleVar(var, value);
            styles++;
        }

        private void Style(ImGuiStyleVar var, Vector2 value)
        {
            ImGui.PushStyleVar(var, value);
            styles++;
        }
    }

    // ---- Pieces -------------------------------------------------------------------------------

    /// <summary>Scarlet colours for the main action of a page (Start, Lock, End night).</summary>
    public static ImRaii.ColorDisposable Primary(bool condition = true) =>
        ImRaii.PushColor(ImGuiCol.Button, Scarlet, condition)
            .Push(ImGuiCol.ButtonHovered, ScarletHover, condition)
            .Push(ImGuiCol.ButtonActive, ScarletActive, condition);

    /// <summary>
    /// A page's heading in the game's headline font, a muted line under it, and a scarlet rule.
    /// <paramref name="right"/> is drawn on the heading's line, right-aligned in <paramref name="rightWidth"/>.
    /// </summary>
    public static void PageHeader(string heading, string? subtitle = null, float rightWidth = 0, Action? right = null)
    {
        var top = ImGui.GetCursorPosY();
        using (ImRaii.PushColor(ImGuiCol.Text, Rose))
        using (TitleFont())
            ImGui.Text(heading);

        if (right != null)
        {
            var after = ImGui.GetCursorPosY();
            ImGui.SetCursorPos(new Vector2(ImGui.GetContentRegionMax().X - rightWidth, top));
            using (ImRaii.Group())
                right();
            ImGui.SetCursorPosY(Math.Max(after, ImGui.GetCursorPosY()));
        }

        if (!string.IsNullOrEmpty(subtitle))
        {
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() - ImGui.GetStyle().ItemSpacing.Y * 0.7f);
            ImGui.TextDisabled(subtitle);
        }
        Rule();
    }

    /// <summary>A thin scarlet line fading out to the right.</summary>
    public static void Rule()
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var height = Math.Max(1f, 2 * ImGuiHelpers.GlobalScale);
        var solid = ImGui.GetColorU32(Scarlet);
        var clear = ImGui.GetColorU32(Scarlet with { W = 0 });
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(pos, pos + new Vector2(width, height), solid, clear, clear, solid);
        ImGui.Dummy(new Vector2(width, height));
        ImGuiHelpers.ScaledDummy(2);
    }

    /// <summary>Small upper-case label over a group of things ("NEW SESSION", "COCKTAILS").</summary>
    public static void SectionLabel(string text)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, Rose with { W = 0.85f }))
            ImGui.Text(text.ToUpperInvariant());
    }

    /// <summary>A rounded card. Dispose to end it; check Success before drawing inside.</summary>
    public static ImRaii.ChildDisposable Card(string id, Vector2 size, bool highlighted = false)
    {
        using var bg = ImRaii.PushColor(ImGuiCol.ChildBg, highlighted ? Blend(CardBg, CardTint) : CardBg);
        using var border = ImRaii.PushColor(ImGuiCol.Border, highlighted ? Scarlet with { W = 0.8f } : Line with { W = 0.35f });
        return ImRaii.Child(id, size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
    }

    /// <summary>A coloured bar down the left edge of the current card (free / locked, running / over).</summary>
    public static void CardAccent(Vector4 color)
    {
        var min = ImGui.GetWindowPos();
        var max = min + new Vector2(4 * ImGuiHelpers.GlobalScale, ImGui.GetWindowSize().Y);
        ImGui.GetWindowDrawList().AddRectFilled(min, max, ImGui.GetColorU32(color), 8 * ImGuiHelpers.GlobalScale, ImDrawFlags.RoundCornersLeft);
    }

    /// <summary>A rounded tag, e.g. [Photo] or [3 min left], sized to sit on a text line.</summary>
    public static void Pill(string text, Vector4 color)
    {
        var pad = new Vector2(7, 1) * ImGuiHelpers.GlobalScale;
        var size = ImGui.CalcTextSize(text) + pad * 2;
        var pos = ImGui.GetCursorScreenPos() - new Vector2(0, pad.Y);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(pos, pos + size, ImGui.GetColorU32(color with { W = 0.18f }), size.Y / 2);
        draw.AddRect(pos, pos + size, ImGui.GetColorU32(color with { W = 0.45f }), size.Y / 2);
        draw.AddText(pos + pad, ImGui.GetColorU32(color), text);
        ImGui.Dummy(new Vector2(size.X, ImGui.GetTextLineHeight()));
    }

    /// <summary>A filled dot the height of a text line, for at-a-glance status.</summary>
    public static void Dot(Vector4 color)
    {
        var height = ImGui.GetTextLineHeight();
        var radius = height * 0.22f;
        var pos = ImGui.GetCursorScreenPos();
        ImGui.GetWindowDrawList().AddCircleFilled(pos + new Vector2(radius, height / 2), radius, ImGui.GetColorU32(color));
        ImGui.Dummy(new Vector2(radius * 2, height));
    }

    /// <summary>A FontAwesome icon as text, in a colour.</summary>
    public static void Icon(FontAwesomeIcon icon, Vector4 color)
    {
        using (ImRaii.PushColor(ImGuiCol.Text, color))
        using (ImRaii.PushFont(UiBuilder.IconFont))
            ImGui.Text(icon.ToIconString());
    }

    /// <summary>A rounded filter button, scarlet when selected.</summary>
    public static bool Chip(string label, bool active)
    {
        using var colors = Primary(active);
        using var rounding = ImRaii.PushStyle(ImGuiStyleVar.FrameRounding, 12 * ImGuiHelpers.GlobalScale);
        return ImGui.Button(label);
    }

    /// <summary>"You owe the venue" over a big gil amount and a detail line.</summary>
    public static void Owed(string label, string amount, string detail)
    {
        ImGui.TextDisabled(label);
        using (ImRaii.PushColor(ImGuiCol.Text, Gold))
        using (BigFont())
            ImGui.Text(amount);
        if (detail.Length > 0)
            ImGui.TextDisabled(detail);
    }

    /// <summary>Height <see cref="Owed"/> takes, for laying out around it.</summary>
    public static float OwedHeight(bool detail = true)
    {
        float bigLine;
        using (BigFont())
            bigLine = ImGui.GetTextLineHeight();
        var spacing = ImGui.GetStyle().ItemSpacing.Y;
        return ImGui.GetTextLineHeight() * (detail ? 2 : 1) + bigLine + spacing * (detail ? 2 : 1);
    }

    /// <summary>
    /// The bottom card of the Photos and Court tabs: what's owed on the left, the page's buttons
    /// (<paramref name="buttons"/>, <paramref name="buttonsWidth"/> wide) centred on the right.
    /// </summary>
    public static void TotalsCard(string id, string label, string amount, string detail, float buttonsWidth, Action buttons)
    {
        var padding = ImGui.GetStyle().WindowPadding.Y;
        var height = OwedHeight() + padding * 2;
        using var card = Card(id, new Vector2(0, height));
        if (!card.Success)
            return;
        CardAccent(Gold);

        using (ImRaii.Group())
            Owed(label, amount, detail);

        var inner = ImGui.GetContentRegionMax();
        ImGui.SetCursorPos(new Vector2(inner.X - buttonsWidth, (height - ImGui.GetFrameHeight()) / 2));
        using (ImRaii.Group())
            buttons();
    }

    /// <summary>Text in the middle of the space left, for empty states.</summary>
    public static void Empty(string text, FontAwesomeIcon icon = FontAwesomeIcon.InfoCircle)
    {
        ImGuiHelpers.ScaledDummy(6);
        Icon(icon, Muted);
        ImGui.SameLine();
        using (ImRaii.TextWrapPos(0))
            ImGui.TextDisabled(text);
    }

    public static void ErrorText(string text)
    {
        Icon(FontAwesomeIcon.ExclamationTriangle, Red);
        ImGui.SameLine();
        using (ImRaii.PushColor(ImGuiCol.Text, Red))
            ImGui.TextWrapped(text);
    }

    private static Vector4 Blend(Vector4 under, Vector4 over) =>
        new(under.X + (over.X - under.X) * over.W, under.Y + (over.Y - under.Y) * over.W, under.Z + (over.Z - under.Z) * over.W, under.W);
}
