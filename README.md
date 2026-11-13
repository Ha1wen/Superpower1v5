# Superpower 1v5

A CounterStrikeSharp plugin for CS2: the player who chooses **T stays the boss** and receives **two different private random powers** against up to five CT opponents. The boss is never rotated or selected from CT. Players normally start at **100 HP**; Juggernaut gives the boss **500 HP**. All **18 powers have equal selection chances**, without rarity tiers.

This repository contains the plugin, build tools, tests, configuration template and install ZIP. CS2 server files, Metamod, CounterStrikeSharp binaries, credentials, logs and local backups are excluded.

## Install v1.6.0

Download [Superpower1v5-v1.6.0.zip](releases/Superpower1v5-v1.6.0.zip) and extract it into your server's `game/csgo/` directory, merging the `addons/` folder. Hosting panels that open directly in `csgo/` should extract there. Back up an existing plugin/config before replacing it, then restart the server.

## Boss selection and privacy

Choose T to become the boss. You remain T and keep the role across rounds until you leave or switch teams. Only one human can occupy T; another player's T request is rejected while that slot is occupied. A human explicitly choosing T can replace a bot boss. If nobody chooses T, CT players are not automatically promoted. Extra players above the six-player limit become spectators.

Only the boss receives their assigned power names, descriptions, cooldown HUD and activation messages/sounds. Other players receive general round announcements. Client-side admin diagnostics also hide assigned IDs from non-boss players; the server console can still inspect them. Visible gameplay effects naturally reveal what happens, but the plugin does not announce the loadout to opponents.

Requires **Metamod:Source**, **CounterStrikeSharp API 1.0.380.0** and its **.NET 10 runtime**. Tested on Windows; other operating systems have not been verified. The package includes the production DLL, dependencies manifest, debugging symbols, configuration and install instructions. No compilation is required to install it.

Expected paths inside `game/csgo/`:

```text
addons/counterstrikesharp/plugins/Superpower1v5/Superpower1v5.dll
addons/counterstrikesharp/configs/plugins/Superpower1v5/Superpower1v5.json
```

Run `css_plugins list` and `css_sp_debug` in the server console to verify loading.

## Powers

| ID | Default behavior |
|---|---|
| `super_speed` | 2.5x movement speed |
| `vampirism` | Heal 100% of actual health damage dealt, capped at 100 HP |
| `invisibility` | Hide the body, weapons and attached equipment from other clients; keep the boss's own view |
| `teleport` | Aim-directed safe teleport; no configured range cap, 15-second cooldown |
| `explosive_bullets` | 55 base blast damage, radius 180, distance falloff and wall checks |
| `infinite_ammo` | Refill the active firearm's normal magazine |
| `rapid_fire` | 1.6x server firing rate |
| `low_gravity` | 0.35x personal gravity |
| `wall_vision` | Automatically reveal opponents with boss-only outlines, radar and bearings |
| `knockback` | Bullet hits push opponents with an 800-unit horizontal impulse and lift |
| `second_chance` | Once per round, prevent lethal damage and rewind position, facing, velocity, health and armor by three seconds |
| `bhop` | Hold jump to hop automatically on landing |
| `sandevistan` | Freeze opponents' movement and attacks for 10 seconds; cooldown 35 seconds |
| `pyromaniac` | Bullet impacts create fire pools; smoke extinguishes them |
| `random_explosion` | **Random Kill:** immediately kill a random living player, including the boss; cooldown 20 seconds |
| `tiny` | Half-sized body, camera and collision bounds |
| `juggernaut` | 500 starting/max HP while assigned; restores the normal cap on removal |
| `cluster_grenade` | Each thrown HE, flash, smoke, Molotov, incendiary or decoy becomes four projectiles of that type |

Wall Vision, Invisibility and Juggernaut need **no activation**. Cluster Grenade works automatically on the boss's thrown utility, adding three copies for four total; copies cannot split again. Juggernaut can combine with healing/rewind up to its 500 HP cap. Random Kill retains the `random_explosion` ID, creates no explosion, and can kill the boss even with Second Chance. Its legacy Strength field and Invisibility's former alpha field are unused. Yoru Flash and rarity tiers remain removed.

With all 18 powers enabled, each has about a 5.56% chance in either slot and an 11.11% chance of appearing in the pair. Disabled/blacklisted powers are excluded. Configure powers in [Superpower1v5.example.json](Superpower1v5.example.json); keep at least two different powers available. ConfigVersion 6 adds the new powers and three-second rewind and fixes the boss side to T with rotation disabled. Existing unrelated tuning is preserved.

## Commands

Players can use `!power`, `!powers`, `!cooldowns`, `!ability1`, `!ability2` and `!help`. Only the living boss can activate an active power after freeze time. Passive/triggered powers work automatically.

Client key binds:

```text
bind v css_ability1
bind b css_ability2
```

Administrator commands require `@css/generic` in CounterStrikeSharp. The server console can use them directly:

| Command | Action |
|---|---|
| `css_sp_enable` / `css_sp_disable` | Enable the mode or clean up its effects |
| `css_sp_reload` | Reload and validate configuration |
| `css_sp_setboss <player>` | Admin override: move the chosen player to T and restart the challenge |
| `css_sp_reroll` | Reroll both powers, retaining current-round cooldowns/one-use state |
| `css_sp_give <player> <power_id> [1\|2]` | Replace one of the current boss's two powers |
| `css_sp_remove <player>` | Remove boss powers and restart the challenge |
| `css_sp_list` / `css_sp_debug` | List power IDs or inspect the roster/powers |

Player targets accept `#userid`, a quoted exact name or an unambiguous partial name.

## Test with bots

Join **T** to be the boss, then run in the dedicated server console:

```text
bot_quota_mode normal
bot_quota 5
bot_stop 0
mp_warmup_end
mp_restartgame 1
css_sp_debug
```

The chosen T player remains boss against five CT bots. Bot-only games also work, though bots cannot type ability commands. Assign a test pair to the current boss:

```text
css_sp_give "YourPlayerName" wall_vision 1
css_sp_give "YourPlayerName" invisibility 2
```

Both powers enable automatically. Substitute another power ID and use its slot's ability command when needed.

## Build from source

Install the **.NET 10 SDK** and obtain the API directory from a compatible CounterStrikeSharp server. The API is referenced directly; no API/server binaries are committed here.

From a checkout anywhere on disk:

```powershell
.\tools\Build.ps1 -CounterStrikeSharpPath 'C:\your-server\game\csgo\addons\counterstrikesharp\api'
```

`Build.ps1` builds Release, runs the 27 automated checks and regenerates the example config. Output: `bin/Release/net10.0/`. If `dotnet` is not on PATH, pass `-DotnetPath 'C:\path\to\dotnet.exe'`. The existing `server/development/Superpower1v5` layout is also supported without arguments.

To install the built output into a chosen server:

```powershell
.\tools\Deploy.ps1 -ServerRoot 'C:\your-server' -MigrateConfig
```

Deployment backs up and migrates an existing plugin config before replacing the DLL. CounterStrikeSharp's file watcher may hot-reload the plugin and restart the challenge round. `Deploy.ps1` also accepts `-DotnetPath` when needed.

## Verification and limits

See [verification notes](docs/VERIFICATION.md), [native gameplay results](docs/verification/native-gameplay.json) and [actual rifle rewind results](docs/verification/rifle-rewind.json). Checks cover selection, permanent T ownership, privacy routing, configuration, permissions, bounded rewind history, concealment, 500 HP lifecycle, live utility activation and native/actual-weapon damage paths.

Invisibility filters outgoing entity transmission for the boss and attached equipment instead of relying on model transparency. Thrown utility remains visible; detached items return to normal transmission on refresh. Human rendering still needs client playtesting.

Second Chance records up to ten seconds of history, selects a safe location about three seconds before the lethal hit, and makes a small upward adjustment when needed for floor clearance. Short histories use the earliest available safe sample. It zeroes incoming damage before the engine applies it, provides a 0.4-second recovery window and reapplies the teleport after the damage flow to avoid lag-compensation rollback. Unsafe historical locations are skipped; if none are clear, health recovery stays at the current location. Direct forced deaths such as Random Kill bypass it.

Cluster Grenade uses real armed projectile factories and preserves the thrower/type, including incendiaries. It is limited to 32 active copies by default, creates four total per throw, and removes owned copies on cleanup. Native grenade signatures need revalidation after engine updates. Teleport remains bounded by world geometry and a finite 32,768-unit trace. Pyromaniac uses bounded custom pools without native Molotov spreading. Normal objectives and economy continue.

Native secondary damage is pinned to the tested API 380 ABI and needs revalidation after game/framework changes. Some isolated Windows test runs still report an existing access violation on quit after successful plugin unload; completed gameplay checks passed.

API references: [CounterStrikeSharp documentation](https://docs.cssharp.dev/) and [the API source used for this release](https://github.com/roflmuffin/CounterStrikeSharp/tree/2273dd1631a5719f2cacc6e9abbf67ac0bc30bc0).
