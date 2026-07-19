# Changelog

## 1.5.10

- Thunderstore package includes race `Sounds/` (start, countdown, checkpoint, finish, false_start)

## 1.5.9

- Finish announces place (e.g. 1st / 2nd) on MessageHud for everyone; finisher also gets a personal "You finished…" alert
- Delayed "all runners finished" so it doesn't overwrite the place message
- Richer Thunderstore README (client mod required, race pitch)

## 1.5.8

- F6: world records, status, admin check, join/leave use yellow MessageHud
- Close registration announces to all clients via MessageHud

## 1.5.7

- Race-critical events now use yellow center MessageHud (`Announce`) as well as chat
- Removed unused helpers and dead gear/hand validation leftovers
- Wired `EnableHud` config to the live race HUD
- Thunderstore packaging script (`pack-thunderstore.ps1`)

## 1.5.6

- Removed Frost resistance mead requirement (Feather cape covers frost)
- RULES sign font size increased

## 1.5.5

- Required race foods: Salad, Blood Pudding, Mushroom Omelette
- Illegal food mid-race disqualifies

## 1.5.0 – 1.5.4

- WR / RULES bulletin Signs
- Admin F6 fixes for dedicated servers
- Non-admin F6 limited to join/leave/gear/HUD

## 1.0.0

- Initial release
- Server-authoritative marathon tracking with start/finish gates and torch checkpoint pairs
- Split times, personal bests, leaderboard, and JSON export
- Admin registration and auto-detection commands
- Optional client HUD
