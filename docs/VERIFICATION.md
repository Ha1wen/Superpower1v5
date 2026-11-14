# Verification: v1.8.0

Verified locally on Windows CS2 with CounterStrikeSharp API 1.0.380.0 and .NET SDK 10.0.404 on 2026-11-15 (Asia/Tokyo).

## Production and automated checks

The production Release build has zero warnings/errors. **35/35 automated checks pass**, including the 21-power catalog/factory, equal selection probability, 1v5 roster, permissions, version-8 config migration, restricted deathmatch pools, different respawn pairs, fresh life state, mode detection, crouch/eye-height arithmetic, bounded rewind history and lethal hitgroup scaling. Diagnostic probes and native damage injectors are excluded from the production assembly.

## Native engine checks

**24/24 focused gameplay checks pass** on private servers bound to `127.0.0.1:27016`, using the installed game/framework binaries:

- [20 ability checks](verification/ability-update-v1.8.json): 1v5 roster and all 21 implementations; Tiny's body/client scale, standing/crouched firing offset and cleanup; equipped-rifle recoil/camera cleanup and shortened firing delay; random clear destination and cooldown enforcement; concealment/shadow/radar suppression; planted C4 exclusion; repeated noise transitions and weapon changes; Wall Vision respecting concealed targets; lethal protection immediately after Teleport, deferred historical movement and overlapping-blink rejection; explicitly populated head hitgroups; Forcefield priority, duration, cooldown and cleanup; repeated air jumps with preserved horizontal velocity; native Molotov spreading/damage, Cluster Grenade recursion suppression, smoke extinguishing and entity cleanup.
- [Two deathmatch checks](verification/deathmatch-v1.8.json): native deathmatch has no T boss, gives all active players separate pairs, clears death effects and automatically rolls a different pair on the next life, with fresh cooldowns and one-use state. The fixture initially spawns its test bots through the native Respawn API; the tested death is followed by the engine's automatic deathmatch respawn.
- [Two rifle/teleport checks](verification/rifle-teleport-v1.8.json): the owner activates the actual Teleport ability, then a bot fires a real AK-47. Second Chance prevents lethal damage, rewinds to pre-teleport history and keeps the round live.

The full logs and earlier exploratory runs remain in ignored `artifacts/` directories. Initial test assumptions about exact engine eye height and an equipped firearm were corrected. An early deathmatch fixture incorrectly used competitive restart commands; the final fixture uses native deathmatch behavior. Failed exploratory runs are not counted as passes.

## Remaining manual checks and known limit

These native tests use bots and engine entities, not a human client's rendered screen. Tiny crosshair/bullet alignment and the first-person/spectator appearance of Invisibility need client playtesting. No human client crash was reproduced or captured. Invisibility now avoids broad entity casts and live-pawn/equipment transmission removal, excludes planted C4, preserves viewmodels, and clears spotting while fully concealed; this addresses the likely unsafe paths without claiming a proven reproduction of the reported client crash.

Some isolated Windows runs still reproduce the previously documented access violation during shutdown after successful plugin unload. The final native deathmatch and actual-rifle runs exited normally; the full ability run recorded all passing gameplay checks before that shutdown fault. This existing shutdown issue remains separate from the client invisibility report.

Pyromaniac uses the existing native Molotov factory binding. Native fire controls damage ramp, radius/spreading, audio and natural lifetime; the plugin caps ignition rate, pool count and maximum duration and removes its fire on cleanup. Random Teleport samples the loaded navigation mesh, with player-spawn fallback, validates floor/body clearance and avoids occupied destinations. No safe destination means no cooldown charge. Forcefield blocks damage; explicit administrative suicides bypass damage hooks.

The [v1.7 report](VERIFICATION-v1.7.md) and older native JSON files are historical evidence, not new v1.8 results.
