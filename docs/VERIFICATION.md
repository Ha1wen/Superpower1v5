# Verification: v1.6.0

Tested on 2026-11-13 with Windows CS2, CounterStrikeSharp API 1.0.380.0, .NET 10 and SDK 10.0.404.

## Changes verified

- The chosen T player remains boss across rounds; CT players are never automatically promoted. The T slot is limited to one boss, and a human choosing T can replace a bot.
- Assigned power announcements, descriptions, cooldown HUD, ability notices and sounds are addressed only to the boss. Non-boss client admin diagnostics redact assigned IDs. General round messages remain public.
- Invisibility filters body, weapon and attached-equipment entity indices out of other clients' transmissions. The boss retains their own view. Detached items are pruned from concealment; cleanup releases the filter. This replaces incomplete model-alpha fading.
- Juggernaut applies 500 health/max health once on assignment/spawn, supports damage and healing to the new cap, and restores the normal cap on removal.
- Second Chance records bounded position/facing/velocity/health/armor history and rewinds about three seconds on lethal damage. It uses the player-specific pre-damage callback, zeroes incoming damage, allows brief recovery, validates landing clearance and reapplies teleport after the damage flow to avoid lag-compensation rollback. One use per round remains enforced.
- Cluster Grenade makes four total real projectiles per utility throw. Copies preserve thrower/type and cannot recursively split. HE, flash, smoke, Molotov, incendiary and decoy activation were all checked.

## Production build and offline checks

Release build: zero warnings/errors. **27/27 automated checks pass**, including 50,000-round pair validity and uniform distribution samples, permanent T ownership, no CT promotion, bot replacement rules, spectator/roster limits, permissions, config migration, cooldown preservation, passive powers, bounded rewind history and timing.

Diagnostic code remains excluded from production. The repository does not contain the CS2 server, API binaries, private test server, raw console logs or backups.

## Native gameplay checks

**46/46 native checks pass** across three private localhost bot scenarios:

1. **28 checks** for all 18 powers applying, concealment recipients/body/equipment/detachment, Juggernaut damage/healing/removal, recorded rewind and lethal interception, one-use protection, permanent T ownership, four-projectile splitting and actual activation for all six utility types, real bot weapon damage and final round cleanup.
2. **Two focused rifle checks** with a boss starting at 100 HP, moved away from the historical position before combat. An AK-47 bot fires actual `DMG_BULLET` hits; the fatal hit restores health/position and the round remains live. The pre-damage and deferred-teleport path are exercised together.
3. **16 regression checks** for roster/health, all 18 apply paths, Vampirism, null native damage buffers/event fallback, slot/cooldown preservation, Speed/Gravity restoration, long teleport, automatic concealment, Infinite Ammo, real Knockback movement, Wall Vision lifecycle, measured gravity, Second Chance and retaining the T boss across the next round.

Evidence checked into this repository: [gameplay results](verification/native-gameplay.json), [rifle rewind](verification/rifle-rewind.json), and [regression results](verification/regression.json). The broad scenario's historical test label says "bot-fired lethal bullet" for a hit whose diagnostic damage type was actually `DMG_SLASH`; it verifies real bot weapon damage. The separate rifle scenario explicitly confirms `DMG_BULLET` and supplies the gunfire evidence.

The final broad scenario used private PID 9560, the focused moving/rifle case PID 39700, and the regression run PID 37184. All completed gameplay checks. The broad and moving/rifle cases exited cleanly after unload; the regression run and some exploratory runs reproduced the previously recorded Windows shutdown access violation after unload. No live user server was terminated.

## Remaining limits

Human rendering of invisibility and through-wall outlines still needs client playtesting. Tests establish the server's transmission decisions and cleanup, rather than a screenshot from every client. Visible gameplay effects may reveal an ability naturally even though assignment information is private.

Unsafe rewind positions are skipped, with small upward floor-clearance adjustments. Short histories use the earliest safe sample; if no safe sample exists, health recovery stays at the current location. Forced death such as Random Kill bypasses damage-based Second Chance.

Cluster copies are capped (32 by default). The factories use engine signatures and require revalidation after game updates. Windows is tested; Linux signatures have not been exercised here. API 380's native secondary damage ABI also needs revalidation after framework/engine changes.

Implementation references: [CounterStrikeSharp API source](https://github.com/roflmuffin/CounterStrikeSharp/tree/2273dd1631a5719f2cacc6e9abbf67ac0bc30bc0), [MatchZy grenade factories](https://github.com/shobhit-pathak/MatchZy/blob/main/GrenadeProjectiles.cs), [SwiftlyS2 grenade signatures](https://github.com/swiftly-solution/swiftlys2/blob/master/plugin_files/gamedata/cs2/core/signatures.jsonc), and [the NadeSystem author's factory bindings](https://github.com/ed0ard/CS2-Bot-NadeSystem/blob/main/NadeSystem.cs). The plugin implements its own ownership, splitting, cleanup and recipient filtering.

## Release

The release manifest inside the install ZIP records the production DLL checksum. ConfigVersion 6 preserves unrelated settings while adding Juggernaut/Cluster Grenade, the three-second rewind and fixed T ownership. Deployment backs up the previous configuration.

The live server confirmed **v1.6.0 with 18 powers loaded at 21:36:09 JST**. All unrelated existing power settings were compared with the backup and preserved. Production source/deployed DLL SHA256 match: `8B4086DB8A4106559604D5F6D944759EF2E96CC3E02BA4EBAF032104F9C43795`. The seven-file install ZIP was checked entry-by-entry against its staged source. The user's running dedicated server and CS2 client stayed running.
