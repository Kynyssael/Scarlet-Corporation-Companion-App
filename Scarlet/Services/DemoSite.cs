using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Scarlet.Models;

namespace Scarlet.Services;

/// <summary>
/// Dev plugins only: a pretend website inside the plugin, so every tab can be tried without staff roles
/// or a sign-in. While <see cref="On"/> is set, the website requests of the plugin's services are
/// answered here from sample data instead of being sent; nothing reaches the real site. Changes last
/// until the demo is left. Sign-in (Keycloak) never goes through here.
/// </summary>
public static class DemoSite
{
    public const string UserName = "Demo Manager";

    /// <summary>Answer website requests here instead of sending them.</summary>
    public static volatile bool On;

    /// <summary>The handler every website HttpClient sends through (with disposeHandler: false).</summary>
    public static readonly HttpMessageHandler Handler = new Gate();

    private static readonly object Sync = new();
    private static Data data = new();

    /// <summary>Puts the sample data back as it started.</summary>
    public static void Reset()
    {
        lock (Sync)
            data = new Data();
    }

    private sealed class Gate() : DelegatingHandler(new SocketsHttpHandler())
    {
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (!On)
                return await base.SendAsync(request, ct);

            var text = request.Content == null ? null : await request.Content.ReadAsStringAsync(ct);
            using var body = string.IsNullOrWhiteSpace(text) ? null : JsonDocument.Parse(text);
            await Task.Delay(150, ct); // Feels like a website, so "Saving..." states show.

            var path = request.RequestUri!.AbsolutePath.Trim('/').Split('/').Select(Uri.UnescapeDataString).ToArray();
            (HttpStatusCode Status, object? Body) answer;
            lock (Sync)
                answer = data.Answer(request.Method, path, body?.RootElement);

            return new HttpResponseMessage(answer.Status)
            {
                RequestMessage = request,
                Content = new StringContent(answer.Body == null ? string.Empty : JsonSerializer.Serialize(answer.Body, answer.Body.GetType()),
                                            Encoding.UTF8, "application/json"),
            };
        }
    }

    /// <summary>The sample venue, the same for every venue slug. Only touched under <see cref="Sync"/>.</summary>
    private sealed class Data
    {
        private static readonly (HttpStatusCode, object?) NotFound = (HttpStatusCode.NotFound, new { error = "Not in the demo." });

        private int nextId = 100;

        private readonly object venue = new
        {
            menu = new object[]
            {
                new { id = 1, section = "Drinks", name = "Scarlet Sunset", description = "House cocktail", price = "15,000 gil", image_url = "", game_item_id = 0, sort = 1 },
                new { id = 2, section = "Drinks", name = "Red Wine", description = "By the glass", price = "8k", image_url = "", game_item_id = 0, sort = 2 },
                new { id = 3, section = "Drinks", name = "Water", description = "", price = "Free", image_url = "", game_item_id = 0, sort = 3 },
                new { id = 4, section = "Food", name = "Cheese Platter", description = "For two", price = "25,000 gil", image_url = "", game_item_id = 0, sort = 4 },
                new { id = 5, section = "Food", name = "Chocolate Cake", description = "", price = "12,000 gil", image_url = "", game_item_id = 0, sort = 5 },
            },
            staff = new object[]
            {
                new { name = "Aria Vale", role = "Bartender" },
                new { name = "Bram Holt", role = "Photographer" },
                new { name = "Cyra Moon", role = "Court" },
                new { name = UserName, role = "Management" },
            },
            photos = new object[]
            {
                new { id = 1, name = "Single portrait", price = 50_000, description = "One edited shot", sort = 1 },
                new { id = 2, name = "Couple set", price = 120_000, description = "Three edited shots", sort = 2 },
            },
            court = new object[]
            {
                new { id = 1, name = "Private dance", rate = 200_000, description = "", sort = 1 },
                new { id = 2, name = "Companion", rate = 300_000, description = "", sort = 2 },
            },
        };

        private readonly List<PayRateDto> rates =
        [
            new() { Id = 1, Name = "Bartender", Rate = 100_000 },
            new() { Id = 2, Name = "Photographer", Rate = 120_000 },
            new() { Id = 3, Name = "Court", Rate = 150_000 },
            new() { Id = 4, Name = "Management", Rate = 200_000 },
        ];

        private readonly List<RoomDto> rooms =
        [
            new() { Id = 1, Name = "Studio A", Kind = "photo", Description = "Backdrop and lights" },
            new() { Id = 2, Name = "Studio B", Kind = "photo", Locked = true, LockedBy = "Bram Holt", LockedNote = "Couple set", LockedAt = DateTimeOffset.Now.AddMinutes(-20) },
            new() { Id = 3, Name = "Velvet Room", Kind = "private" },
            new() { Id = 4, Name = "Rose Suite", Kind = "private" },
        ];

        private readonly List<VipDto> vips;
        private readonly List<BanDto> bans;
        private readonly List<ShiftDto> shifts;
        private readonly List<NightReportDto> nights;

        public Data()
        {
            var now = DateTimeOffset.Now;
            vips =
            [
                Vip(1, "Lyra Dawn", VipList.Monthly, now.AddDays(-27), "Likes the window booth"),
                Vip(2, "Kael Storm", VipList.Yearly, now.AddMonths(-3), ""),
                Vip(3, "Sera Bloom", VipList.Lifetime, now.AddMonths(-8), "Founding VIP"),
                Vip(4, "Old Tom", VipList.Monthly, now.AddDays(-40), ""),
            ];
            bans =
            [
                new() { Id = 1, Name = "Grim Shade", Reason = "Harassing staff", BannedAt = now.AddMonths(-2), AddedBy = UserName },
                new() { Id = 2, Name = "Loud Larry", Reason = "Fighting at the bar", BannedAt = now.AddDays(-5), Until = now.AddDays(25), AddedBy = UserName },
                new() { Id = 3, Name = "Past Pete", Reason = "Spamming chat", BannedAt = now.AddMonths(-2), Until = now.AddDays(-10), AddedBy = UserName },
            ];
            shifts =
            [
                Shift(1, "Aria Vale", "Bartender", now.AddMinutes(-95), null, false),
                Shift(2, "Bram Holt", "Photographer", now.AddDays(-1).AddHours(-4), now.AddDays(-1).AddMinutes(-10), false),
                Shift(3, "Cyra Moon", "Court", now.AddDays(-2).AddHours(-3), now.AddDays(-2), false),
                Shift(4, "Aria Vale", "Bartender", now.AddDays(-3).AddHours(-5), now.AddDays(-3), true),
            ];
            nights =
            [
                Night(1, Models.Night.Bar, "Aria Vale", now.AddDays(-1), 100, [("Scarlet Sunset", 15_000, 6), ("Red Wine", 8_000, 4)]),
                Night(2, Models.Night.Photo, "Bram Holt", now.AddDays(-1), 30, [("Couple set", 120_000, 2)]),
                Night(3, Models.Night.Court, "Cyra Moon", now.AddDays(-2), 30, [("Private dance", 200_000, 1)]),
            ];
        }

        public (HttpStatusCode, object?) Answer(HttpMethod method, string[] path, JsonElement? body)
        {
            // /api/venues/{venue}: the public menu, staff and prices.
            if (path is ["api", "venues", _] && method == HttpMethod.Get)
                return Ok(venue);
            if (path is not ["api", "portal", _, var what, ..])
                return NotFound;
            var id = path.Length > 4 ? path[4] : null;
            var rest = path.Skip(5).ToArray();

            return (what, method.Method, id, rest) switch
            {
                ("settings", "GET", null, _) => Ok(new { photo_cut = 30, court_cut = 30 }),
                ("nights", "GET", null, _) => Ok(nights.OrderByDescending(n => n.EndedAt)),
                ("nights", "POST", null, _) => AddNight(body),
                ("nights", "PATCH", { } clientId, []) => ChangeNight(clientId, body),
                ("rooms", "GET", null, _) => Ok(rooms),
                ("rooms", "POST", { } room, ["lock"]) => Lock(Number(room), body),
                ("rooms", "DELETE", { } room, ["lock"]) => Unlock(Number(room)),
                ("vips", "GET", null, _) => Ok(vips.OrderBy(v => v.ExpiresAt ?? DateTimeOffset.MaxValue)),
                ("vips", "POST", null, _) => AddVip(body),
                ("vips", "PATCH", { } vip, []) => ChangeVip(Number(vip), body),
                ("vips", "POST", { } vip, ["renew"]) => RenewVip(Number(vip), body),
                ("vips", "DELETE", { } vip, []) => Remove(vips, v => v.Id == Number(vip)),
                ("blacklist", "GET", null, _) => Ok(bans.OrderBy(b => b.Ended).ThenByDescending(b => b.BannedAt)),
                ("blacklist", "POST", null, _) => SaveBan(null, body),
                ("blacklist", "PATCH", { } ban, []) => SaveBan(Number(ban), body),
                ("blacklist", "DELETE", { } ban, []) => Remove(bans, b => b.Id == Number(ban)),
                ("pay-rates", "GET", null, _) => Ok(rates),
                ("shifts", "GET", null, _) => Ok(shifts.OrderBy(s => s.EndedAt != null).ThenByDescending(s => s.StartedAt)),
                ("shifts", "POST", null, _) => SaveShift(null, body),
                ("shifts", "PATCH", { } shift, []) => SaveShift(Number(shift), body),
                ("shifts", "DELETE", { } shift, []) => Remove(shifts, s => s.Id == Number(shift)),
                _ => NotFound,
            };
        }

        // ---- Nights ---------------------------------------------------------------------------

        private (HttpStatusCode, object?) AddNight(JsonElement? body)
        {
            var clientId = Text(body, "id");
            var kind = Text(body, "kind") is { Length: > 0 } k ? k : Models.Night.Bar;
            var lines = body?.TryGetProperty("lines", out var list) == true ? list.Deserialize<List<NightLineDto>>() ?? [] : [];
            if (nights.Any(n => n.ClientId == clientId))
                return Ok(new { });
            var total = lines.Sum(l => l.UnitPrice * l.Quantity);
            var cut = kind == Models.Night.Bar ? 100 : 30;
            nights.Add(new NightReportDto
            {
                Id = nextId++,
                ClientId = clientId,
                Kind = kind,
                StaffName = UserName,
                Bartender = Text(body, "bartender"),
                StartedAt = Time(body, "started_at") ?? DateTimeOffset.Now,
                EndedAt = Time(body, "ended_at") ?? DateTimeOffset.Now,
                Lines = lines,
                ItemCount = lines.Sum(l => l.Quantity),
                Total = total,
                HouseCut = cut,
                OwedOnSite = total * cut / 100,
            });
            return (HttpStatusCode.Created, new { });
        }

        private (HttpStatusCode, object?) ChangeNight(string clientId, JsonElement? body)
        {
            var index = nights.FindIndex(n => n.ClientId == clientId);
            if (index < 0)
                return NotFound;
            var night = nights[index];
            if (Bool(body, "paid") is { } paid)
                night = night with { Paid = paid };
            if (Bool(body, "voided") is { } voided)
                night = night with { Voided = voided };
            return Ok(nights[index] = night);
        }

        // ---- Rooms ----------------------------------------------------------------------------

        private (HttpStatusCode, object?) Lock(int id, JsonElement? body)
        {
            var index = rooms.FindIndex(r => r.Id == id);
            if (index < 0)
                return NotFound;
            if (rooms[index] is { Locked: true, Mine: false } taken && Bool(body, "force") != true)
                return Error(HttpStatusCode.Conflict, $"{taken.LockedBy} already has {taken.Name}.");
            return Ok(rooms[index] = rooms[index] with
            {
                Locked = true, Mine = true, LockedBy = Text(body, "name") is { Length: > 0 } name ? name : UserName,
                LockedNote = Text(body, "note"), LockedAt = DateTimeOffset.Now,
            });
        }

        private (HttpStatusCode, object?) Unlock(int id)
        {
            var index = rooms.FindIndex(r => r.Id == id);
            if (index < 0)
                return NotFound;
            return Ok(rooms[index] = rooms[index] with { Locked = false, Mine = false, LockedBy = "", LockedNote = "", LockedAt = null });
        }

        // ---- VIPs -----------------------------------------------------------------------------

        private static VipDto Vip(int id, string name, string tier, DateTimeOffset bought, string note)
        {
            var expires = VipList.ExpiryFrom(tier, bought);
            return new VipDto
            {
                Id = id, Name = name, Tier = tier, BoughtAt = bought, ExpiresAt = expires, Note = note, AddedBy = UserName,
                Purchases = [new VipPurchaseDto { Tier = tier, BoughtAt = bought, ExpiresAt = expires, SoldBy = UserName }],
            };
        }

        private (HttpStatusCode, object?) AddVip(JsonElement? body)
        {
            var name = Text(body, "name");
            var tier = Text(body, "tier");
            if (name.Length == 0)
                return Error(HttpStatusCode.BadRequest, "Name is required.");
            if (tier is not (VipList.Monthly or VipList.Yearly or VipList.Lifetime))
                return Error(HttpStatusCode.BadRequest, "Pick monthly, yearly or lifetime.");
            var vip = Vip(nextId++, name, tier, Time(body, "bought_at") ?? DateTimeOffset.Now, Text(body, "note"));
            vips.Add(vip);
            return (HttpStatusCode.Created, vip);
        }

        private (HttpStatusCode, object?) ChangeVip(int id, JsonElement? body)
        {
            var index = vips.FindIndex(v => v.Id == id);
            if (index < 0)
                return NotFound;
            var vip = vips[index];
            var tier = Text(body, "tier") is { Length: > 0 } t ? t : vip.Tier;
            var bought = Time(body, "bought_at") ?? vip.BoughtAt;
            return Ok(vips[index] = vip with
            {
                Name = Text(body, "name") is { Length: > 0 } name ? name : vip.Name,
                Tier = tier, BoughtAt = bought, ExpiresAt = VipList.ExpiryFrom(tier, bought),
                Note = Has(body, "note") ? Text(body, "note") : vip.Note,
            });
        }

        private (HttpStatusCode, object?) RenewVip(int id, JsonElement? body)
        {
            var index = vips.FindIndex(v => v.Id == id);
            if (index < 0)
                return NotFound;
            var vip = vips[index];
            var tier = Text(body, "tier") is { Length: > 0 } t ? t : vip.Tier;
            var paid = Time(body, "bought_at") ?? DateTimeOffset.Now;
            var expires = VipList.RenewedExpiry(vip, tier, paid);
            return Ok(vips[index] = vip with
            {
                Tier = tier, BoughtAt = paid, ExpiresAt = expires,
                Purchases = [.. vip.Purchases, new VipPurchaseDto { Tier = tier, BoughtAt = paid, ExpiresAt = expires, SoldBy = UserName }],
            });
        }

        // ---- Blacklist ------------------------------------------------------------------------

        private (HttpStatusCode, object?) SaveBan(int? id, JsonElement? body)
        {
            var index = id is { } existing ? bans.FindIndex(b => b.Id == existing) : -1;
            if (id != null && index < 0)
                return NotFound;
            var name = Text(body, "name");
            if (name.Length == 0)
                return Error(HttpStatusCode.BadRequest, "Name is required.");
            var bannedAt = Time(body, "banned_at") ?? DateTimeOffset.Now;
            var until = Time(body, "until");
            if (until <= bannedAt)
                return Error(HttpStatusCode.BadRequest, "The ban has to end after it starts.");
            var ban = new BanDto { Id = id ?? nextId++, Name = name, Reason = Text(body, "reason"), BannedAt = bannedAt, Until = until, AddedBy = UserName };
            if (index < 0)
                bans.Add(ban);
            else
                bans[index] = ban;
            return (index < 0 ? HttpStatusCode.Created : HttpStatusCode.OK, ban);
        }

        // ---- Attendance -----------------------------------------------------------------------

        private ShiftDto Shift(int id, string name, string role, DateTimeOffset start, DateTimeOffset? end, bool paid)
        {
            var rate = rates.First(r => r.Name == role).Rate;
            var minutes = end is { } e ? (int)Math.Round((e - start).TotalMinutes) : 0;
            return new ShiftDto
            {
                Id = id, StaffName = name, Role = role, Rate = rate, StartedAt = start, EndedAt = end,
                Minutes = minutes, Pay = ShiftList.Pay(rate, minutes), Paid = paid, RecordedBy = UserName,
            };
        }

        private (HttpStatusCode, object?) SaveShift(int? id, JsonElement? body)
        {
            var index = id is { } existing ? shifts.FindIndex(s => s.Id == existing) : -1;
            if (id != null && index < 0)
                return NotFound;
            var current = index >= 0 ? shifts[index] : null;

            var name = Has(body, "staff_name") ? Text(body, "staff_name") : current?.StaffName ?? "";
            if (name.Length == 0)
                return Error(HttpStatusCode.BadRequest, "Who worked is required.");
            var roleName = Has(body, "role") ? Text(body, "role") : current!.Role;
            var rate = rates.FirstOrDefault(r => r.Name.Equals(roleName, StringComparison.OrdinalIgnoreCase));
            if (rate == null)
                return Error(HttpStatusCode.BadRequest, $"No pay rate called \"{roleName}\".");
            var start = Has(body, "started_at") ? Time(body, "started_at") ?? DateTimeOffset.Now : current!.StartedAt;
            var end = Has(body, "ended_at") ? Time(body, "ended_at") : current?.EndedAt;
            if (start > DateTimeOffset.Now.AddMinutes(5) || end > DateTimeOffset.Now.AddMinutes(5))
                return Error(HttpStatusCode.BadRequest, "A shift can't be in the future.");
            var minutes = end is { } e ? (int)Math.Round((e - start).TotalMinutes) : 0;
            if (end != null && minutes <= 0)
                return Error(HttpStatusCode.BadRequest, "The shift has to end after it starts.");
            if (minutes > 24 * 60)
                return Error(HttpStatusCode.BadRequest, "A shift can be 24 hours at most.");
            var paid = Bool(body, "paid") ?? current?.Paid ?? false;
            if (paid && end == null)
                return Error(HttpStatusCode.BadRequest, "A shift that hasn't ended can't be paid yet.");
            if (end == null && shifts.Any(s => s.Id != id && s.OnShift && s.StaffName.Equals(name, StringComparison.OrdinalIgnoreCase)))
                return Error(HttpStatusCode.Conflict, $"{name} is already on shift.");

            // Same role kept: keep the rate it was recorded at.
            var keptRate = current != null && current.Role.Equals(rate.Name, StringComparison.OrdinalIgnoreCase) ? current.Rate : rate.Rate;
            var shift = new ShiftDto
            {
                Id = id ?? nextId++, StaffName = name, Role = rate.Name, Rate = keptRate, StartedAt = start, EndedAt = end,
                Minutes = minutes, Pay = ShiftList.Pay(keptRate, minutes), Paid = paid, RecordedBy = UserName,
                Note = Has(body, "note") ? Text(body, "note") : current?.Note ?? "",
            };
            if (index < 0)
                shifts.Add(shift);
            else
                shifts[index] = shift;
            return (index < 0 ? HttpStatusCode.Created : HttpStatusCode.OK, shift);
        }

        // ---- Helpers --------------------------------------------------------------------------

        private static NightReportDto Night(int id, string kind, string who, DateTimeOffset ended, int cut, (string Name, long Price, int Qty)[] lines)
        {
            var total = lines.Sum(l => l.Price * l.Qty);
            return new NightReportDto
            {
                Id = id, ClientId = $"demo-{id}", Kind = kind, StaffName = who, Bartender = who,
                StartedAt = ended.AddHours(-4), EndedAt = ended,
                Lines = lines.Select(l => new NightLineDto { Name = l.Name, UnitPrice = l.Price, Quantity = l.Qty }).ToList(),
                ItemCount = lines.Sum(l => l.Qty), Total = total, HouseCut = cut, OwedOnSite = total * cut / 100,
            };
        }

        private static (HttpStatusCode, object?) Remove<T>(List<T> list, Predicate<T> match) =>
            list.RemoveAll(match) > 0 ? (HttpStatusCode.NoContent, null) : NotFound;

        private static (HttpStatusCode, object?) Ok(object body) => (HttpStatusCode.OK, body);

        private static (HttpStatusCode, object?) Error(HttpStatusCode status, string error) => (status, new { error });

        private static int Number(string text) => int.TryParse(text, out var n) ? n : -1;

        private static bool Has(JsonElement? body, string name) => body?.ValueKind == JsonValueKind.Object && body.Value.TryGetProperty(name, out _);

        private static JsonElement? Field(JsonElement? body, string name) =>
            body?.ValueKind == JsonValueKind.Object && body.Value.TryGetProperty(name, out var value) && value.ValueKind != JsonValueKind.Null ? value : null;

        private static string Text(JsonElement? body, string name) =>
            Field(body, name) is { ValueKind: JsonValueKind.String } value ? value.GetString()!.Trim() : string.Empty;

        private static DateTimeOffset? Time(JsonElement? body, string name) =>
            Field(body, name) is { ValueKind: JsonValueKind.String } value && value.TryGetDateTimeOffset(out var at) ? at : null;

        private static bool? Bool(JsonElement? body, string name) =>
            Field(body, name) is { ValueKind: JsonValueKind.True or JsonValueKind.False } value ? value.GetBoolean() : null;
    }
}
