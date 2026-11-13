# Superpower 1v5

A CounterStrikeSharp plugin for CS2: one rotating player receives **two different random powers** against up to five normal opponents. Everyone starts at **100 HP**. All **16 powers have equal selection chances**, without rarity tiers. Humans have boss priority over bots.

This repository contains the plugin, build tools, tests, configuration template and install ZIP. CS2 server files, Metamod, CounterStrikeSharp binaries, credentials, logs and local backups are excluded.

## Install v1.5.0

Download [Superpower1v5-v1.5.0.zip](releases/Superpower1v5-v1.5.0.zip) and extract it into your server's `game/csgo/` directory, merging the `addons/` folder. Hosting panels that open directly in `csgo/` should extract there. Back up an existing plugin/config before replacing it, then restart the server.

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
| `invisibility` | Automatically fade player and owned weapons while assigned |
| `teleport` | Aim-directed safe teleport; no configured range cap, 15-second cooldown |
| `explosive_bullets` | 55 base blast damage, radius 180, distance falloff and wall checks |
| `infinite_ammo` | Refill the active firearm's normal magazine |
| `rapid_fire` | 1.6x server firing rate |
| `low_gravity` | 0.35x personal gravity |
| `wall_vision` | Automatically reveal opponents with boss-only outlines, radar and bearings |
| `knockback` | Bullet hits push opponents with an 800-unit horizontal impulse and lift |
| `second_chance` | Prevent one lethal damage hit per round and restore 100 HP |
| `bhop` | Hold jump to hop automatically on landing |
| `sandevistan` | Freeze opponents' movement and attacks for 10 seconds; cooldown 35 seconds |
| `pyromaniac` | Bullet impacts create fire pools; smoke extinguishes them |
| `random_explosion` | **Random Kill:** immediately kill a random living player, including the boss; cooldown 20 seconds |
| `tiny` | Half-sized body, camera and collision bounds |

Wall Vision and Invisibility need **no activation** and last while assigned. Random Kill retains the `random_explosion` ID for command/config compatibility, but creates no explosion. It uses the engine's forced death flow and can kill the boss even with Second Chance. Its legacy Strength setting is unused. Yoru Flash and all rarity tiers have been removed.

With all 16 powers enabled, each has a 6.25% chance in either slot and a 12.5% chance of appearing in the round's pair. Disabled/blacklisted powers are excluded. Configure strengths, cooldowns and availability in [Superpower1v5.example.json](Superpower1v5.example.json); keep at least two different compatible powers available. ConfigVersion 5 migrates older configurations.

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
| `css_sp_setboss <player>` | Select the boss for the next round |
| `css_sp_reroll` | Reroll both powers, retaining current-round cooldowns/one-use state |
| `css_sp_give <player> <power_id> [1\|2]` | Replace one of the current boss's two powers |
| `css_sp_remove <player>` | Remove boss powers and restart the challenge |
| `css_sp_list` / `css_sp_debug` | List power IDs or inspect the roster/powers |

Player targets accept `#userid`, a quoted exact name or an unambiguous partial name.

## Test with bots

Join T or CT, then run in the dedicated server console:

```text
bot_quota_mode normal
bot_quota 5
bot_stop 0
mp_warmup_end
mp_restartgame 1
css_sp_debug
```

The only human receives boss priority against five bots. Bot-only games also work, though bots cannot type ability commands. Assign a deliberate test pair to the current boss:

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

`Build.ps1` builds Release, runs the 24 automated checks and regenerates the example config. Output: `bin/Release/net10.0/`. If `dotnet` is not on PATH, pass `-DotnetPath 'C:\path\to\dotnet.exe'`. The existing `server/development/Superpower1v5` layout is also supported without arguments.

To install the built output into a chosen server:

```powershell
.\tools\Deploy.ps1 -ServerRoot 'C:\your-server' -MigrateConfig
```

Deployment backs up and migrates an existing plugin config before replacing the DLL. CounterStrikeSharp's file watcher may hot-reload the plugin and restart the challenge round. `Deploy.ps1` also accepts `-DotnetPath` when needed.

## Verification and limits

See [verification notes](docs/VERIFICATION.md) and [native gameplay results](docs/verification/native-gameplay.json). Automated checks cover selection, roster, configuration, permissions, cooldown state and damage bookkeeping. Native bot tests cover direct opponent deaths, boss self-selection, cooldown timing and round recovery.

Teleport is bounded by world geometry and a finite 32,768-unit engine trace. Pyromaniac uses custom bounded pools with stock Molotov particles, without native Molotov spreading. Client rendering/prediction affects perceived Invisibility, Wall Vision, Rapid Fire and Tiny. The mode restores temporary effects and forced ConVars on cleanup; normal objectives and economy continue.

Native secondary damage is pinned to the tested API 380 ABI and needs revalidation after game/framework changes. The isolated Windows test host still reports an existing access violation on quit after successful plugin unload; completed gameplay checks passed.

API references: [CounterStrikeSharp documentation](https://docs.cssharp.dev/) and [the API source used for this release](https://github.com/roflmuffin/CounterStrikeSharp/tree/2273dd1631a5719f2cacc6e9abbf67ac0bc30bc0).
