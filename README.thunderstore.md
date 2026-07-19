# RunningMan

Server-authoritative **marathon races** for Valheim: checkpoints, gear rules, live standings, WR/RULES Signs, and an F6 race panel.

**Install on the dedicated server and on every player's client.** Everyone who races needs the mod — otherwise they won't get the HUD, yellow announcements, sounds, or join UI.

Want some great fun with your friends? Spice up Valheim with a fair race: shared gear rules, and Running skill normalized to **50** for the event only. Add your Steam ID to the server `adminlist.txt`, open the menu with **F6**, and place a start line, checkpoints, and a finish line.

Source & full docs: [github.com/Abengs84/RunningMan](https://github.com/Abengs84/RunningMan)

## Quick start

1. Admins set up the track (F6 or `/run register` / `/run autodetect`).
2. `/run open` — open registration (announced in-game).
3. Players **Join race** (F6) or `/run join`.
4. `/run start` — countdown, then **GO!**

Press **F6** for the race GUI.

## Players

| Command | What it does |
|---------|----------------|
| `/run join` / `/run leave` | Register / unregister |
| `/run status` | Place, time, next checkpoint |
| `/run gearcheck` | Check marathon loadout |
| `/run pb` / `/run last` | Personal best / last run |

**Default loadout:** Troll leather set, Feather cape (or none), Anti-Sting + Ratatosk meads, Salad / Blood Pudding / Mushroom Omelette. Swapping gear or eating other food mid-race = DQ.

## Admins

| Command | What it does |
|---------|----------------|
| `/run open` / `/run close` | Registration |
| `/run start` / `/run cancel` | Start countdown / cancel event |
| `/run debug` | Gate marker lines |
| `/run wrboard` / `/run rulesboard` | Mark a Hammer **Sign** as WR or RULES board |
| `/run worldrecords` | Show top times |

Config: `BepInEx/config/com.runningman.valheim.cfg`  
Race data: `BepInEx/config/RunningMan/`
