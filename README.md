# Scarlet Corporation Companion App

A Dalamud plugin (internal name `Scarlet`, command `/scarlet`): the staff app for Scarlet Corporation venues in FFXIV. Staff sign in with Discord and get the tabs for their role: the bar register, photos, court timers, rooms and VIPs, plus the blacklist, history, attendance and pay for management.

## Install (staff)

1. In game, `/xlsettings` → **Experimental** → **Custom Plugin Repositories**, paste
   `https://raw.githubusercontent.com/Kynyssael/Scarlet-Corporation-Companion-App/main/repo.json`,
   tick **Enabled**, then **Save**.
2. `/xlplugins` → search **Scarlet Corporation Companion App** → **Install**.

Dalamud updates it like any other plugin when a new version is released.

## Releasing a new version

Push a version tag; [the Release workflow](.github/workflows/release.yml) does the rest:

```bash
git tag v0.2.0
git push origin v0.2.0
```

It builds against the current Dalamud, attaches `latest.zip` to a GitHub release, and
commits the new version to `repo.json`. Tags are `vMAJOR.MINOR.PATCH`; the plugin version
becomes `MAJOR.MINOR.PATCH.0`, and it must go up for Dalamud to offer the update.

## Build

Requires the .NET 10 SDK and XIVLauncher/Dalamud installed (the SDK finds Dalamud in `%APPDATA%\XIVLauncher\addon\Hooks\dev`).

```bash
dotnet build Scarlet.slnx
```

Output: `Scarlet/bin/Debug/Scarlet.dll`.

## Load in game

1. `/xlsettings` → Experimental → Dev Plugin Locations → add the full path to `Scarlet/bin/Debug/Scarlet.dll`, save.
2. `/xlplugins` → Dev Tools → Installed Dev Plugins → enable Scarlet.

## Commands

| Command | Action |
| --- | --- |
| `/scarlet` | Toggle the POS window |
| `/scarlet config` | Settings |
| `/scarlet sync` | Re-read the menu and staff from the website |

## Website sync

The menu and staff come from the Scarlet Corporation site's public API,
`GET https://scarletcorporation.com/api/venues/<slug>` (`scarlet-steel`,
`scarlet-den`, `scarlet-unholy-water`). Pick the venue in settings; it syncs on load
and from the **Sync from website** button.

- Menu sections become categories; the text price (`"15,000 Gil"`, `"15k"`) is parsed to gil.
- Website items are read-only in the plugin: edit them in the site's admin, then sync.
  Their on/off toggle is kept between syncs.
- Items added with **Add local item** are plugin-only and survive syncs.
- Staff are per venue: the Staff page only lists the selected venue's people (tick **On site**). People removed from the venue's Staff page on the site are removed on the next sync; people added by hand stay.

## In-game items

In the site's **Admin › Menu**, each drink can be linked to the real FFXIV item it's
served as (searched on XIVAPI; stored as `game_item_id`, not shown on the venue pages).
After a sync the Register shows, per drink:

- the item's icon and real name, read from the game's own data (client language);
- how many are in the bartender's bags (amber under 5, red when out);
- right-click: an item link in your chat log, or copy the item name.

Search on the Register matches the in-game name too.

## Rooms (photographers)

The **Rooms** tab lists the venue's service rooms from the website (Admin › Service
rooms) and who has each one locked. Photographers only see (and can only lock) photo
rooms; management sees every room, photo rooms first. Photographers and management
**Lock** a room while they use it, with an optional note; it shows as locked to all
staff until **Unlock**. Only whoever locked it, or management, can unlock it.
Locks go through `GET /api/portal/<venue>/rooms` and
`POST`/`DELETE /api/portal/<venue>/rooms/<id>/lock`; the public venue pages don't
show them. The list refreshes every 30 seconds while the tab is open.

## Photos (photographers)

The **Photos** tab is the photographer's register: one card per photo session (e.g.
Single Photo, Couples Photo) with **Sold one** / minus buttons and what they owe the
venue. **End night** sends it to the website like a bar night, with `kind: "photo"`; the
site only takes photo nights from photographers and management (bar nights from bartenders
and management). It's a separate tally from the Register, so management can run both the
same night.

The sessions and prices are per venue, set by management in the site's
**Admin › Photo prices** (name, price in whole gil, optional note shown on hover). They
come with the menu on each sync (`photos` in `GET /api/venues/<slug>`) and aren't shown on
the venue pages. A price change mid-night starts a new line, so earlier photos keep their
price.

Photographers owe the venue the **house cut** of their sales (30% by default), set per
venue at the top of Admin › Photo prices. Staff read it from
`GET /api/portal/<venue>/settings` (signed in; not in the public payload) on each sync, and
the Photos tab shows sales, the cut and what's owed. The site works out what's owed itself
when the night arrives, using the cut at that moment and saving it on the night
(`house_cut`, `owed`), so changing the cut later doesn't change nights already in. Bar
nights owe their whole total. History and Admin › Nights total what's owed.

## Court (companions)

The **Court** tab is the court's Companion/Room Timers sheet. Type the **buyer**, set the
**time bought** (half-hour steps), pick the **service** and a **private room** (or none),
and **Start**: the room is locked on the website first (the session only starts once it
is), and **Stop** ends the session and unlocks it. The price is the hourly rate × the
time bought (1h30 at 500,000/h is 750,000), shown before you start. The timer is only a
guide: OUT shows when the time bought runs out and how many minutes are left (red once
over), and **+30** adds time the client buys mid-session. Ctrl+click the bin to remove a
session started by mistake.

**Bell:** 10 minutes before a running session's time bought runs out, and again when it
does, the plugin plays a chat chime (one of the game's `<se.1>`–`<se.16>`, heard only by the
court), prints a line in their chat log and shows a toast, even with the window closed.
Buying more time (+30) rings again before the new end. Courts change it in settings
(**Court**: on/off, how many minutes before, ring at the end, which sound or none, **Test**).

Services and their **hourly rates** are per venue in the site's **Admin › Court rates**,
with the **house cut of court sales** (30% by default) at the top; they sync like the
photo prices. **End night** (once no session is running) sends the sessions as a `court`
night with each one's rate, minutes bought and in/out times; the site works each price
out again from the rate and the minutes bought, keeps how long it really ran
(`actual_minutes`), takes court nights only from courts and management, and shows them
in Admin › Nights in the same columns as the sheet (⚠ when a session ran past its time).

## VIP (all staff, management edits)

The **VIP** tab keeps the venue's VIP members. Every role sees the list, so staff know who's
a VIP; only management sees the form and the Renew / edit / remove buttons. Management adds
VIPs as **Monthly**, **Yearly** or **Lifetime**, with
when they bought it (your local time; the clock button fills in now) and an optional note.
The list shows when each one needs renewing, soonest first, with a tag: green (days
left), amber (renews within 7 days), red (expired); for management the tab title counts the amber
and red ones, e.g. **VIP (2)**. **Renew** records another payment, optionally for another tier;
renewing before it runs out adds the time on top of what's left (a monthly VIP due
Nov 5 who renews on Oct 30 is good until Dec 5), after it ran out the time starts from the
payment. The pen fixes a name, tier, purchase date or note (the renewal date is worked
out again from the date), and Ctrl+click on the bin removes a VIP. Hover a name for every
purchase and renewal, and who recorded it.

VIPs are kept on the website per venue, so every manager sees the same list:
`GET`/`POST /api/portal/<venue>/vips`, `PATCH`/`DELETE .../vips/<id>`,
`POST .../vips/<id>/renew` (`GET` for any staff of the venue, the rest management only;
tables `vip_members` and `vip_purchases`).

## Blacklist (management only)

The **Blacklist** tab lists who's banned from the selected venue, why, since when and until
when. Only management sees the tab; ended bans are greyed out under **Ended**. Management bans someone with a name, reason, when the ban
started (defaults to now) and how long: **Permanent**, 1 week, 1 month, 3 months, 6 months,
1 year, or until a typed date. The pen changes a ban (including making it permanent or
setting an end date), Ctrl+click on the unlock icon lifts it now (it stays on the list as
ended), and Ctrl+click on the bin deletes it entirely.

Bans are kept on the website per venue: `GET`/`POST /api/portal/<venue>/blacklist` and
`PATCH`/`DELETE .../blacklist/<id>`, all management only (table
`blacklist`; `until` is null for a permanent ban).

## Attendance (management)

The **Attendance** tab is where management records who worked and works out their pay.
**Record a shift** takes who (type it or pick from the venue's staff), the role, **In** and
**Out** (your local time; the clock buttons fill in now) and an optional note:

- Leave **Out** empty and press **Start shift** when someone comes on: they show under
  **On shift now** with the time and pay so far, and **End shift** ends it.
- Fill in both times to add a past shift (24 hours at most; the Out column shows "+1"
  for shifts that end the next day).

Pay is the role's hourly rate × the **whole hours** worked: a part hour of 45 minutes or
more counts as an hour, less doesn't (3h44 pays 3 hours, 3h45 pays 4). The Hours column
shows paid hours (hover for the time really worked), and **On shift now** shows when the
next hour starts counting. Unpaid shifts recorded while pay went by the minute are worked
out again when the site starts; paid ones keep what they paid. Rates are set per role in the site's
**Admin › Pay rates** (private: not in the public venue payload, and only admins can list
them). Each shift keeps the rate it was recorded at, so a rate change only applies to new
shifts, or to a shift whose role you change. **Pay owed to staff** totals each person's
ended, unpaid shifts; tick **Paid** on a shift, or Ctrl+click **Paid** next to a person to
mark all of theirs. The pen fixes a shift, Ctrl+click on the bin deletes one.

Kept on the website per venue, management only: `GET /api/portal/<venue>/pay-rates`,
`GET`/`POST .../shifts`, `PATCH`/`DELETE .../shifts/<id>` (tables `pay_rates`, `shifts`).

## Night reports on the website

When a bartender ends the night, the plugin sends it to
`POST /api/portal/<venue>/nights` (Keycloak token, staff of that venue): the night's
id, bartender, start/end time and the lines (name, unit price, quantity). The site
works out the total itself and ignores a night it already has, so retries are safe.

- Unsent nights wait locally and retry every 5 minutes, after sign-in, or with
  **Send now**. Bartenders only see a notice on the Register when a night is waiting or
  was refused (hover for the reason; **Try again** resends).
- Management's **History** tab shows every bartender's nights for the selected venue,
  loaded from the website (`GET /api/portal/<venue>/nights`), with a "Still owed" total
  per bartender. **Paid** and **Void** are saved to the website
  (`PATCH /api/portal/<venue>/nights/<id>`, management only).
- The same list is in the site's **Admin › venue › Nights**; both stay in step.

## Staff sign-in

The plugin uses the same Keycloak realm and Discord-role sync as the site's Staff
Portal. The realm address comes from `GET /api/portal/config`.

| Roles | Can use |
| --- | --- |
| not signed in, no `staff-<venue>`, or none of `bartender`/`photographer`/`management` | nothing (sign-in or "bartenders and photographers only" screen) |
| `staff-<venue>` + `bartender` | Register, VIP (read only), plus a resend notice for unsent nights |
| `staff-<venue>` + `photographer` | Photos, Rooms (lock and unlock photo rooms only), VIP (read only) |
| `staff-<venue>` + `court` | Court, Rooms (lock and unlock private rooms only), VIP (read only) |
| `staff-<venue>` + `management` | Register, Photos, Court, Rooms, VIP, Blacklist, History, Attendance, Menu, Staff; voiding nights, unlocking anyone's room, editing VIPs and the blacklist |

**Sign in with Discord** opens the browser (authorization code + PKCE). Keycloak
redirects back to `http://127.0.0.1:47621/callback`, where the plugin picks up the
code. After each sign-in or refresh the plugin calls `POST /api/portal/sync-me`, so
Discord role changes show up at once. Roles are re-checked every 15 minutes, or
with **Refresh roles**. The refresh token is stored encrypted (Windows DPAPI) in
`session.dat` in the plugin config folder. If Keycloak can't be reached, the saved
roles are used for up to 3 days.

**Testing roles (dev only):** when the plugin is loaded from a Dev Plugin Location, the
main window's top bar has a **View as** dropdown (My roles, Bartender, Photographer,
Management, No role) that shows the tabs that role gets. It isn't saved and doesn't exist
in the released plugin. The website still checks your real roles, so a preview only shows
the UI; anything your account can't really do is refused by the site.

Device-code sign-in isn't used because Cloudflare challenges POSTs to
`/protocol/openid-connect/auth/*` on the Keycloak host.

### One-time Keycloak setup

In the `scarlet` realm, **Clients › Create client**:

1. Client ID `scarlet-plugin`, OpenID Connect.
2. *Client authentication* **off**, *Standard flow* **on** (nothing else needed).
3. **Valid redirect URIs**: `http://127.0.0.1:47621/callback`.
4. **Advanced › Proof Key for Code Exchange Code Challenge Method**: `S256`.
5. **Client scopes**: make sure `offline_access` is listed (it is by default) so
   staff stay signed in between sessions.

Hiding pages in the plugin is a convenience, not security: the plugin runs on the
player's PC. Anything the plugin later sends to the site must be checked by the API.

## Layout

- `Plugin.cs`: entry point, services, commands
- `Configuration.cs`: settings, menu, staff (saved by Dalamud)
- `Models/`: menu items, order lines, nights
- `Services/PosService.cs`: tonight's tally and past nights; `PosStore.cs` saves them to `pos-data.json` in the plugin config folder
- `Windows/`: main window with Register / Photos / Court / Rooms / History / Menu / Staff pages, plus settings; `Theme.cs` holds the colours, fonts and shared pieces (cards, tags, totals) every page draws with
