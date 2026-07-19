# RunningMan

A Valheim BepInEx mod that tracks marathon races on dedicated servers.

## Features

- Start/finish line detection via registered gate coordinates
- Sequential checkpoint pairs (Standing Iron Torches)
- Split times, personal bests, and leaderboard
- Server-wide race broadcasts
- JSON data export
- Optional in-game HUD with elapsed time and checkpoint progress
- Admin commands to register track layout and auto-detect torch pairs

## Installation

### Thunderstore (recommended)

1. Install with [Thunderstore Mod Manager](https://www.overwolf.com/app/Thunderstore-Thunderstore_Mod_Manager) / r2modman for Valheim.
2. Add **RunningMan** (team **Uthenaria**) — depends on [BepInExPack Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/).
3. Enable the mod on both the **dedicated server** and **clients** (HUD, sounds, WR/RULES signs need the client DLL).

### Manual

1. Install [BepInExPack for Valheim](https://valheim.thunderstore.io/package/denikson/BepInExPack_Valheim/).
2. Copy `RunningMan.dll` to `BepInEx/plugins/RunningMan/` (or `BepInEx/plugins/`).
3. Launch the server once to generate config files.

**Server + clients:** Race logic is server-authoritative. Clients need the mod for HUD, MessageHud alerts, sounds, and bulletin Signs.

## Building

1. Copy `Config.Build.user.props.template` to `Config.Build.user.props`.
2. Set `VALHEIM_INSTALL` to your Valheim folder.
3. Publicize `assembly_valheim.dll` using [AssemblyPublicizer](https://github.com/CabbageCrow/AssemblyPublicizer) if needed.
4. Run `dotnet build RunningMan.sln -c Release`.

## Config

- **Client:** `%APPDATA%\Thunderstore Mod Manager\DataFolder\Valheim\profiles\Default\BepInEx\config\RunningMan.cfg`
- **Server:** `BepInEx/config/RunningMan.cfg` on the dedicated server
- Track/race JSON: `BepInEx/config/RunningMan/`
- Detection tick rate: `[Detection] UpdateInterval` (default `0.05` on new installs)

## Track Setup

Stand at each gate corner and register triggers (admin required):

```
/run register start
/run register finish
/run register checkpoint
```

Or auto-detect torch checkpoint pairs near your position:

```
/run autodetect checkpoints
```

Auto-detect start/finish Grausten arch structures:

```
/run autodetect start
/run autodetect finish
```

## World Records / Rules bulletins

Uses normal Hammer **Signs** (no floating HUD):

1. Build a Sign with the Hammer (Misc tab).
2. Look at it and run `/run wrboard` **or** `/run rulesboard` (also on F6).
3. Walk up — WR Signs show track name, **track length**, and top records; RULES Signs show gear rules.

`/run wrboard remove|clear` and `/run rulesboard remove|clear` manage marks. Marked Signs cannot be rewritten by hand.

World records on the board show **5** places by default (`WorldRecordsLimit` in config). The old on-screen World Records HUD panel has been removed — use the WR Sign instead.

## Player Commands

| Command | Description |
|---------|-------------|
| `/run join` | Register for the next race event |
| `/run leave` | Unregister from the event |
| `/run status` | Current race status, place, and time |
| `/run pb` | Personal best |
| `/run last` | Last completed run |

Press **F6** to open the RunningMan GUI panel (same actions + live standings).

## Marathon loadout (anti-cheat)

Participants must use the approved gear only. The server validates at **join**, **race start**, and **every tick during the run**.

| Slot | Required |
|------|----------|
| Helmet | Troll Leather Hood (`HelmetTrollLeather`) |
| Chest | Troll Leather Tunic (`ArmorTrollLeatherChest`) |
| Legs | Troll Leather Trousers (`ArmorTrollLeatherLegs`) |
| Inventory | 1× Anti-Sting (`MeadBugRepellent`) |
| Inventory | 2× Tonic of Ratatosk (`MeadHasty`) |
| Food | 2× Salad, 2× Blood Pudding, 2× Mushroom Omelette |
| Cape | `CapeFeather` only (or no cape) — Troll Hide not allowed; Feather covers frost |
| Hands | Any (not checked) |

Commands:

- `/run gearcheck` — verify your loadout before the race
- `/run loadout` — show required items

If a runner swaps to better armor or a non-Feather cape, they are **disqualified**. During the race, only the approved meads and the three race foods may be eaten — anything else is a DQ.

## Admin Commands — Event Flow

1. Register the track (`/run register ...` or F6 admin buttons)
2. `/run debug` — toggle gate marker lines (green start, red finish, colored checkpoints)
3. `/run open` — open registration
4. Players `/run join` (or F6 **Join race**)
5. `/run start` — countdown (default 5 seconds, configurable)
6. At **GO!** registered runners' timers start and live standings sync to all clients

| Command | Description |
|---------|-------------|
| `/run open` | Open registration |
| `/run close` | Close registration |
| `/run start` | Start countdown |
| `/run cancel` | Cancel event |
| `/run debug` | Toggle debug gate markers |
| `/run leaderboard` | Fastest times |
| `/run reset <player>` | Cancel active run |
| `/run export` | Export all data to JSON |
| `/run reload` | Reload configuration |

## Configuration

Generated at `BepInEx/config/com.runningman.valheim.cfg`.

Race data is stored under `BepInEx/config/RunningMan/` by default.

## Compatibility

Designed to coexist with ValheimPlus, ConfigurationManager, ServerCharacters, and Server_devcommands without Harmony conflicts.
