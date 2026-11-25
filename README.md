# Superpower 1v5

A CounterStrikeSharp plugin for CS2 with **24 equally likely powers** and two different powers per loadout. In 1v5, the human who chooses T stays the boss against up to five CT opponents. Native deathmatch gives every active player a new pair on each respawn. Normal health is 100; Juggernaut grants 500.

## v1.9.1 changes

- Invisibility suppresses custom gloves, weapon nameplates, and StatTrak displays while the player fades, and restores their appearance on reveal.

## v1.9.0 changes

- Added Decoy Clone, Thunder God, Unlimited Wallbang, and Random Swap.
- Invisibility hides carried C4 cosmetics from opponents as the fade begins. Planting reveals the carrier and the planted bomb renders normally.
- Tiny aligns the server's shot origin with the scaled standing and crouched view, including real player hitboxes.
- Cluster Grenade throws five total grenades along a centered spread and replaces the thrown grenade after 30 seconds. Infinite Ammo also replenishes grenades.
- Explosive Bullets defaults to 80 base damage and a 250-unit radius. Blood Pact can heal past normal maximum health and grants 10% movement speed after the first enemy kill.
- Removed Low Gravity and Sandevistan's screen effect.

## Requirements and installation

Requires a CS2 dedicated server, Metamod:Source, **CounterStrikeSharp API 1.0.380.0** and its .NET 10 runtime. Windows is tested. Native grenade and damage bindings are pinned to the tested engine/API and need revalidation after updates.

Decoy Clone's walking animation uses AnimGraph2 on Windows CS2 build 26879581. On other builds, the decoy still moves, shows the held weapon, and plays footsteps using the standard prop behavior.

Back up the existing plugin and config. Download [Superpower1v5-v1.9.1.zip](releases/Superpower1v5-v1.9.1.zip), stop the server, and extract it into `game/csgo/`, merging `addons/`. The ZIP includes default configuration. Restart the server after installation.

To keep an existing config, build from this checkout and use `tools/Deploy.ps1 -MigrateConfig`. The script backs up the config and migrates it to version 11.

Installed paths under `game/csgo/`:

```text
addons/counterstrikesharp/plugins/Superpower1v5/Superpower1v5.dll
addons/counterstrikesharp/configs/plugins/Superpower1v5/Superpower1v5.json
```

Run `css_plugins list` and `css_sp_debug` in the server console to verify loading.

## Modes and privacy

In 1v5, `css_sp_random_boss 0` keeps the chosen T player as boss. Only one human may occupy T; a human may replace a bot boss. `css_sp_random_boss 1` instead promotes an eligible player each round, preferring humans and a different person when possible. Extra players beyond the one-T/five-CT limit become spectators.

`css_sp_everyone 1` gives every active 1v5 player their own pair, effects, cooldowns and one-use state. Disabling it removes CT powers and keeps the boss's pair. `css_sp_showpowers 0` keeps assigned powers and HUD private during the round; `1` announces loadouts publicly. Round-end loadouts are always revealed. These three options accept `0`, `1` or `toggle` and persist to the JSON config.

Native CS2 deathmatch (`game_type 1`, `game_mode 2`, or `+game_alias deathmatch`) is detected automatically. It has no boss or 1v5 team limit and always powers every active player. Each respawn rerolls the pair and resets cooldowns/one-use state. Normally leave the plugin's `GameMode` at `1v5` and use native detection; an explicit `GameMode: deathmatch` also supports a separately configured custom respawn mode.

## Powers

| ID | Default behavior |
|---|---|
| `super_speed` | 2.5x movement speed |
| `blood_pact` | Heal 100% of actual health damage dealt, including health above your normal maximum. Your first enemy kill gives +10% movement speed for the rest of your life or round |
| `invisibility` | Sound reveals you; after 0.5 seconds of silence, fade out over 1 second. Shift/crouch walking stays quiet |
| `teleport` | Aim-directed safe blink, 15-second cooldown |
| `explosive_bullets` | 80 base blast damage, radius 250, distance falloff and wall checks |
| `infinite_ammo` | Refill the active firearm’s magazine and replace each thrown grenade after the throw animation |
| `rapid_fire` | 1.6x server firing rate without recoil; normal spread remains |
| `decoy_clone` | Send your player model walking in your facing direction. A bullet triggers a flashbang, or the clone disappears after 10 seconds. It carries a cosmetic copy of your equipped weapon and emits footsteps while moving. Default cooldown is 20 seconds |
| `wall_vision` | Opponent outlines, radar and bearings for the owner; silent fully invisible targets stay hidden |
| `knockback` | Bullet hits push opponents with an 800-unit horizontal impulse and lift |
| `second_chance` | Once per round/life, block lethal damage and rewind position, health, armor and velocity about three seconds |
| `bhop` | Hold jump to hop automatically on landing |
| `sandevistan` | Freeze opponents' movement and attacks for 10 seconds, 35-second cooldown |
| `pyromaniac` | Bullet impacts ignite native Molotov fire; smoke extinguishes it |
| `random_explosion` | Random Kill: kill a random unshielded living player, possibly yourself; 20-second cooldown |
| `tiny` | Half-sized body and collision, with a matching server firing height when standing or crouching |
| `juggernaut` | 500 starting/max HP while assigned |
| `cluster_grenade` | Four copies fan out from each throw while the original follows its usual path. The thrown grenade returns after 30 seconds, or immediately after the throw animation with Infinite Ammo |
| `infinite_jump` | Press jump again in midair for unlimited additional jumps; preserves horizontal momentum |
| `random_teleport` | Random clear nav-mesh destination with spawn-point fallback; 20-second cooldown |
| `forcefield` | Block all damage for 10 seconds; 35-second cooldown from activation |
| `unlimited_wallbang` | Fire through cover with no penetration damage penalty. Normal weapon range and armor still apply |
| `thunder_god` | An equipped Zeus fires exactly along your crosshair across the map. Cover blocks the shot, and the Zeus recharges in 10 seconds. |
| `random_swap` | Trade positions with another randomly selected living player, 20-second cooldown |

Infinite Jump is passive: release and press jump for each additional air jump. Random Teleport and Forcefield use the normal ability-slot commands. Failed teleport searches do not spend cooldown. Forcefield takes precedence over Second Chance and also excludes its owner from Random Kill selection. Forced suicides and administrative deaths are not damage events.

Invisibility fades the player, their carried weapons and their attached equipment models. Full concealment hides those models and their shadows while preserving the first-person viewmodel. Planted C4, dropped equipment and thrown utility keep their ordinary rendering. Reloading, planting, defusing, running, jumps, shots and other noisy actions reveal the player. Radar spotting is suppressed while fully concealed. Cleanup restores model colors, flags, render modes and shadows.

Pyromaniac's `Interval` limits ignition frequency, `MaxEntities` limits simultaneous owned pools, and `Duration` caps their lifetime. Native Molotov convars govern spreading, damage and natural lifetime; the old custom-pool `Strength`/`Radius` fields are retained for config compatibility and do not override native fire. Owned projectiles/fire are removed on cleanup. Cluster Grenade copies cannot split again and are bounded separately.

All 24 enabled powers have a 4.17% chance per slot and an 8.33% chance of appearing in a pair. Disabled/blacklisted powers are excluded; keep at least two compatible powers available. Defaults are in [Superpower1v5.example.json](Superpower1v5.example.json).

## Commands

Players: `!power`, `!powers`, `!cooldowns`, `!ability1`, `!ability2`, `!help`.

```text
bind v css_ability1
bind b css_ability2
```

Administrator commands require `@css/generic`; the dedicated server console can use them directly:

| Command | Action |
|---|---|
| `css_sp_enable` / `css_sp_disable` | Enable the mode or clean up its effects |
| `css_sp_reload` | Reload and validate config |
| `css_sp_random_boss [0\|1\|toggle]` | Control 1v5 random promotion |
| `css_sp_showpowers [0\|1\|toggle]` | Public/private loadouts during a round |
| `css_sp_everyone [0\|1\|toggle]` | Powers for all active players in 1v5 |
| `css_sp_setboss <player>` | Select the 1v5 boss |
| `css_sp_reroll` | Reroll the boss while retaining current-round cooldown/use state |
| `css_sp_give <player> <power_id> [1\|2]` | Replace one of a living powered player's powers |
| `css_sp_remove <player>` | Clear boss effects and restart the 1v5 challenge |
| `css_sp_list` / `css_sp_debug` | List powers or inspect mode/roster/loadouts |

Targets accept `#userid`, a quoted exact name or an unambiguous partial name.

## Building and development

Install the .NET 10 SDK and use a compatible server's API directory:

```powershell
.\tools\Build.ps1 -CounterStrikeSharpPath 'C:\your-server\game\csgo\addons\counterstrikesharp\api'
.\tools\Deploy.ps1 -ServerRoot 'C:\your-server' -MigrateConfig
```

The `server/development/Superpower1v5` layout also works without arguments. Pass `-DotnetPath` if the SDK is outside PATH. Build runs the production Release build, automated checks and config generation; output is `bin/Release/net10.0/`. Deployment can trigger CounterStrikeSharp hot reload and restart the challenge round.

See the [verification notes](docs/VERIFICATION.md) for the test scope and remaining client playtests. Native test probes and damage injectors are excluded from production.

Human client testing is still needed for Tiny's crosshair alignment and the appearance of Invisibility and Decoy Clone. Some isolated Windows servers still encounter an access violation during shutdown after the gameplay checks and plugin unload complete.
