# Scarlet Corporation Companion App

A Dalamud plugin (internal name `Scarlet`, command `/scarlet`) for bartenders in FFXIV: tap + next to each drink as you sell it, and at the end of the night it tells you how much you owe the venue. Keeps a history of nights and a staff roster.

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
| `staff-<venue>` + `bartender` | Register (plus a resend notice for unsent nights) |
| `staff-<venue>` + `photographer` | Rooms (lock and unlock photo rooms only) |
| `staff-<venue>` + `management` | Register, Rooms, History, Menu, Staff; voiding nights, unlocking anyone's room |

**Sign in with Discord** opens the browser (authorization code + PKCE). Keycloak
redirects back to `http://127.0.0.1:47621/callback`, where the plugin picks up the
code. After each sign-in or refresh the plugin calls `POST /api/portal/sync-me`, so
Discord role changes show up at once. Roles are re-checked every 15 minutes, or
with **Refresh roles**. The refresh token is stored encrypted (Windows DPAPI) in
`session.dat` in the plugin config folder. If Keycloak can't be reached, the saved
roles are used for up to 3 days.

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
- `Windows/`: main window with Register / Menu / History / Staff pages, plus settings
