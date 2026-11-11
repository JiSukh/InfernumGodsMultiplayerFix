
# WORK IN PROGRESS, NOT FINISHED

# Disclosures

This project is *HEAVILY* AI coded, and mostly made as an experiment with a friend. 

We know that neither Infernum nor WoTG isn't meant to be played in multiplayer. We might also split this into different mods, depending on demand and/or needs.

Don't bother the original mod creators if you find issues with this mod.

# InfernumGodsMultiplayerFix

Fix for Calamity Infernum and Wrath of The Gods bosses and general bugs.

A tModLoader patch mod (internal name `CalWotGMPFix`) that fixes multiplayer bugs in **Calamity: Infernum Mode** and **Calamity: Wrath of the Gods**, and adds a few co-op features. It doesn't modify or redistribute either mod: it hooks them at runtime. Everyone on the server needs it installed.

Built for Infernum 2.0.1.35, Wrath of the Gods 1.2.31 and Calamity 2.2.4 (tModLoader 2026.09). If a hook target can't be found in another version, that fix switches itself off and says so in chat and the log.

## Bug fixes

| Fix | What was wrong |
|---|---|
| Infernum duplicate projectiles | ~30 Infernum attacks spawned projectiles on every client as well as the server, so they fired 2-3x in multiplayer (Plantera, Queen Bee, Empress, Hive Mind, Twins, Moon Lord, SCal, ...) |
| Infernum ghost enemies | King Slime's death slimes/Ninja, Duke Fishron's Sharkrons and the Profaned Guardians' crystal were also spawned unsynced on each client |
| Infernum crash guard | Errors in Infernum boss/projectile AI are caught and logged (`[InfernumGuard]`) instead of crashing the game or server |
| Golem | Clients read `Main.npc[-1]` before the fist/head links synced; links are now repaired |
| Providence | Arena walls only worked in singleplayer |
| Avatar of Emptiness | `ReceiveExtraAI` never read the telegraph list, misreading every field after it |
| Avatar of Emptiness | Frost Screen Smash was disabled in multiplayer; now the server picks the telegraphs |
| Avatar of Emptiness | Antishadow Onslaught weapons and Rubble Gravity Slam portals spawned once per client |
| Mars | Electric Shockwave didn't push anyone in multiplayer |
| Nameless Deity | Client crash drawing constellations before their shape was set |
| Avatar / Nameless / Mars | Server syncs rewound the client's attack timer, replaying frame-exact sounds/effects |

## Multiplayer features (host settings)

- **Shared hazards** - Cultist/Ravager/Guardian borders, Brimstone's rose ring, Calamitas Clone and Polterghast's Madness circles, Crabulon's claw slam and SCal's leave-arena kill affect every player, not just the boss's target.
- **Boss targeting** - ~20 Infernum bosses that stuck to one player all fight re-pick each attack: weighted by class (default melee 7 / ranged 5 / rogue 5 / magic 4 / summoner 3 / other 5) or alternating.
- **Boss fight lives** - out of lives = spectate until the fight ends (0 = unlimited).
- **Boss health scaling** - single-player health x (1 + X% per extra player), default 50%.

Per-player settings: spectate a teammate while dead (`[` / `]` to switch), boss target marker over whoever each boss is targeting.

## Testing commands

| Command | |
|---|---|
| `/mptest list` | Boss list with where to fight each one |
| `/mptest next` / `/mptest boss <n or name>` | Summon the next / a specific boss on you |
| `/mptest kill` | Remove all bosses, enemies and enemy projectiles (no loot) |
| `/mptest gather` | Teleport everyone to you |
| `/mpgod` | Toggle taking no damage (your game only) |

`[BossTrace]` lines in `client.log` / `server.log` log each boss's position, life and attack state once a second, so host and client logs can be compared for desyncs.

## Building

Clone into `Documents/My Games/Terraria/tModLoader/ModSources/CalWotGMPFix`, then in tModLoader: Workshop -> Develop Mods -> CalWotGMPFix -> Build + Reload. Requires Calamity, Wrath of the Gods and Luminance (Infernum optional).

## Credits

Calamity Mod is by Azafure, LLC; Infernum Mode and Calamity: Wrath of the Gods are by Lucille Karma and their teams. This mod only references them; several fixes re-implement small pieces of their boss logic (noted in the code comments) so the same rule applies to every player.
