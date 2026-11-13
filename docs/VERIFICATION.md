# Verification: v1.7.0

Tested on 2026-11-13 with Windows CS2, CounterStrikeSharp API 1.0.380.0, .NET 10 and SDK 10.0.404.

## Behavior

- `css_sp_random_boss [0|1|toggle]` controls next-round promotion. Off preserves the chosen T player. On selects from eligible active players, prefers humans and changes the boss when possible. The selected player moves to T. An administrator's explicit selection overrides the next roll.
- `css_sp_showpowers [0|1|toggle]` controls public loadout announcements during rounds. Off sends assigned details/HUD/ability notices only to the owner. End-of-round loadouts are always public, including dead/disconnected players and removed powers.
- `css_sp_everyone [0|1|toggle]` assigns independent pairs, instances, cooldowns and one-use flags to all active T/CT players. Respawns and joins are supported. Spectators have no active effects. Disabling it removes CT effects and preserves the boss's loadout.
- All switches default off and persist to configuration. Public visibility and everyone mode take effect immediately; random promotion applies next round.
- Invisibility reveals immediately on noisy actions and fades after 0.5 seconds of silence over 1 second. Running/footsteps, shots, reloads, jumps/landings, thrown grenades, audible pickups, scope sounds, damage and bomb actions are covered. Reload/defuse/plant states sustain visibility. Shift and crouch walking remain quiet. Full concealment filters body and attached equipment from other clients, while leaving the owner's view usable.
- Wall Vision, movement effects and overlapping freezes clean up independently across owners. Reveal history survives death/effect cleanup until the round ends.
- A rifle regression exposed unscaled damage in API 380's pre-damage callback: `HitGroupId` can be INVALID while `GetHitGroup()` returns the actual trace group. Second Chance now considers head/stomach/leg scaling, weapon headshot/armor data and team damage scales before deciding whether a bullet is lethal. This closes the observed headshot bypass.

## Build and automated checks

Production Release builds with zero warnings/errors. **31/31 automated checks pass**, including selection/50,000-round distribution samples, roster rules, permissions, config migration and persistent switches, fade timing, separate round state, bounded rewind history and lethal bullet estimation. Diagnostic probes and damage injectors are excluded from production.

## Native checks

**50/50 native gameplay checks pass**: 32 round-options/multiplayer checks, 16 regressions and two rifle checks. Final scenario results are copied into [verification/](verification/). They exercise real engine entities and callbacks on a separate localhost dedicated server with six bots. Sound events and reload state are driven by the probe; visual checks inspect model alpha and immutable transmission decisions, not a human client's screen.

The new scenario covers server commands and persistence, six independent pairs/cooldowns, CT Vampirism/Juggernaut/Second Chance/Cluster Grenade/activated abilities, movement/outline cleanup, spectator return, sound reveal/fade/recovery, quiet walking, public/private announcements, round-end reveal after an actual boss death, random promotion and overlapping freezes. See [round-options.json](verification/round-options.json).

Existing regression checks cover all 18 apply paths, roster/health, Vampirism and damage-event deduplication, slot/cooldown state, movement restoration, long teleport, initial invisibility state, ammo, actual Knockback displacement, outlines, measured gravity and lethal protection. See [regression.json](verification/regression.json).

The focused rifle scenario begins at 100 HP, moves the boss away from historical coordinates and uses actual AK-47 `DMG_BULLET` hits. See [rifle-rewind.json](verification/rifle-rewind.json). The headshot bypass was reproduced before correction; failed exploratory results remain local and are not presented as passes.

Earlier v1.6 utility coverage for HE, flash, smoke, Molotov, incendiary and decoy remains in [native-gameplay.json](verification/native-gameplay.json); those historical results are separate from this release's new checks.

Some isolated Windows processes reproduce the previously documented access violation during shutdown after successful plugin unload. Gameplay checks are recorded before shutdown; this issue remains unresolved. The user's live server and CS2 client are not stopped by the private test runner.

## Limits

Human rendering of invisibility's fade and through-wall outlines still needs client playtesting. Full concealment decisions and cleanup are tested on the server. Visible gameplay effects can naturally reveal an ability even when loadout announcements are private.

Second Chance uses the earliest safe history sample if a full three-second history is unavailable, with small upward clearance adjustments. If no historical position is safe, it restores health at the current position. Random Kill is a forced death and bypasses damage-based protection. Bullet lethality is predicted before engine scaling; custom damage plugins or future engine changes can require revalidation.

Cluster copies remain capped (32 by default), and native factories/secondary damage ABI require revalidation after engine/framework updates. Windows is tested; Linux has not been exercised here. Teleport remains limited by world geometry and a finite 32,768-unit trace. Pyromaniac uses custom bounded pools.

Implementation references: [CounterStrikeSharp API source](https://github.com/roflmuffin/CounterStrikeSharp/tree/2273dd1631a5719f2cacc6e9abbf67ac0bc30bc0), including its `CTakeDamageInfo.GetHitGroup` helper; [MatchZy grenade factories](https://github.com/shobhit-pathak/MatchZy/blob/main/GrenadeProjectiles.cs), [SwiftlyS2 grenade signatures](https://github.com/swiftly-solution/swiftlys2/blob/master/plugin_files/gamedata/cs2/core/signatures.jsonc), and [NadeSystem factory bindings](https://github.com/ed0ard/CS2-Bot-NadeSystem/blob/main/NadeSystem.cs).

## Deployment

ConfigVersion 7 adds the switches and fade timing while preserving existing power tuning. Deployment backs up the previous JSON before migrating it. The install ZIP contains only the plugin, configuration, manifest and instructions; it excludes the server and framework binaries.

Live deployment confirmed v1.7.0 with 18 powers at 22:40:26 JST. Source and installed production DLL SHA256: `d582712c6ed773999055686c7c6d38933dabe172123ddf6dfca3e41c7d671e08`. The seven ZIP entries were validated against their staged bytes; ZIP SHA256: `7808bf653e1386576a2f941a63d8d2f7c2499d13d9f85b856144e68bde801bec`. All pre-existing power tuning was compared with the backup and preserved. The three switches remain off. Both original live server/client processes remained running.
