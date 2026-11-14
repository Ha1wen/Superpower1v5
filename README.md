# Superpower 1v5

A CounterStrikeSharp plugin for CS2 with **21 equally likely powers** and two different powers per loadout. In 1v5, the human who chooses T stays the boss against up to five CT opponents. Native deathmatch gives every active player a new pair on each respawn. Normal health is 100; Juggernaut grants 500.

## v1.8.0 changes

- Tiny scales the body and eye height once, follows crouch interpolation, and refreshes the firing height when shooting.
- Rapid Fire retains its 1.6x firing rate and clears aim punch, camera kick and weapon recoil index for its owner. Normal spread remains. Client recoil settings are replicated only to that player when supported and restored on cleanup.
- Second Chance suppresses the lethal hit first, then rewinds after the damage/lag-compensation stack unwinds. It handles explicitly populated bullet hitgroups, provides a short recovery window and prevents overlapping teleports during recovery.
- Invisibility fades known model entities and shadows instead of removing a live player's transmission. Planted C4 and unrelated world entities are excluded. Planting reveals the player; fully invisible players have radar spotting cleared, and Wall Vision cannot silently re-spot them.
- Deathmatch cleans up effects on death and rolls a different pair on respawn with fresh cooldowns and one-use state. A configuration with only two available powers necessarily keeps that pair.
- Pyromaniac now ignites real Molotovs with native spreading fire, damage ramp, sounds and smoke extinguishing. Generated Molotovs cannot trigger Cluster Grenade recursively.
- New powers: Infinite Jump, Random Teleport and Forcefield.

## Requirements and installation

Requires a CS2 dedicated server, Metamod:Source, **CounterStrikeSharp API 1.0.380.0** and its .NET 10 runtime. Windows is tested. Native grenade and damage bindings are pinned to the tested engine/API and need revalidation after updates.

Download [Superpower1v5-v1.8.0.zip](releases/Superpower1v5-v1.8.0.zip) and extract it into the server's `game/csgo/` directory, merging `addons/`. Back up an existing plugin/config first; the package contains default configuration. Restart the server after installation. Older ZIPs in `releases/` are historical packages.

Alternatively, build and deploy from this checkout using the commands below. `tools/Deploy.ps1 -MigrateConfig` backs up and migrates an existing config to version 8 while preserving existing tuning.

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
| `vampirism` | Heal 100% of actual health damage dealt, up to the owner's health cap |
| `invisibility` | Sound reveals you; after 0.5 seconds of silence, fade out over 1 second. Shift/crouch walking stays quiet |
| `teleport` | Aim-directed safe blink, 15-second cooldown |
| `explosive_bullets` | 55 base blast damage, radius 180, distance falloff and wall checks |
| `infinite_ammo` | Refill the active firearm's normal magazine |
| `rapid_fire` | 1.6x server firing rate without recoil; normal spread remains |
| `low_gravity` | 0.35x personal gravity |
| `wall_vision` | Opponent outlines, radar and bearings for the owner; silent fully invisible targets stay hidden |
| `knockback` | Bullet hits push opponents with an 800-unit horizontal impulse and lift |
| `second_chance` | Once per round/life, block lethal damage and rewind position, health, armor and velocity about three seconds |
| `bhop` | Hold jump to hop automatically on landing |
| `sandevistan` | Freeze opponents' movement and attacks for 10 seconds, 35-second cooldown |
| `pyromaniac` | Bullet impacts ignite native Molotov fire; smoke extinguishes it |
| `random_explosion` | Random Kill: kill a random unshielded living player, possibly yourself; 20-second cooldown |
| `tiny` | Half-sized body, camera and collision with synchronized firing height |
| `juggernaut` | 500 starting/max HP while assigned |
| `cluster_grenade` | Each thrown utility becomes four projectiles of its type |
| `infinite_jump` | Press jump again in midair for unlimited additional jumps; preserves horizontal momentum |
| `random_teleport` | Random clear nav-mesh destination with spawn-point fallback; 20-second cooldown |
| `forcefield` | Block all damage for 10 seconds; 35-second cooldown from activation |

Infinite Jump is passive: release and press jump for each additional air jump. Random Teleport and Forcefield use the normal ability-slot commands. Failed teleport searches do not spend cooldown. Forcefield takes precedence over Second Chance and also excludes its owner from Random Kill selection. Forced suicides and administrative deaths are not damage events.

Invisibility uses model alpha and shadow strength for the body, carried weapons and directly parented model attachments. Viewmodels, planted C4, thrown utility and unrelated owner-linked world entities remain transmitted. Reloading, planting, defusing, running, jumps, shots and other noisy actions reveal the player. Radar spotting is suppressed only while fully concealed; ordinary spotting resumes when visible. Cleanup restores model color, render mode and shadows.

Pyromaniac's `Interval` limits ignition frequency, `MaxEntities` limits simultaneous owned pools, and `Duration` caps their lifetime. Native Molotov convars govern spreading, damage and natural lifetime; the old custom-pool `Strength`/`Radius` fields are retained for config compatibility and do not override native fire. Owned projectiles/fire are removed on cleanup. Cluster Grenade copies cannot split again and are bounded separately.

All 21 enabled powers have a 4.76% chance per slot and a 9.52% chance of appearing in a pair. Disabled/blacklisted powers are excluded; keep at least two compatible powers available. Defaults are in [Superpower1v5.example.json](Superpower1v5.example.json).

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

See [verification notes](docs/VERIFICATION.md) for exact test scope, recorded native checks and remaining client playtests. The historical v1.7 results under `docs/verification/` are separate from the v1.8 checks. Native test probes and damage injectors are excluded from production.

Human client testing is still needed for Tiny's crosshair alignment and Invisibility's visual behavior/crash regression. A previously documented Windows access violation on server shutdown after successful plugin unload also remains reproducible; gameplay checks complete before that shutdown.
