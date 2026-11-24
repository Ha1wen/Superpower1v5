# Verification for v1.9.0

Tested on Windows CS2 build 26879581 with CounterStrikeSharp API 1.0.380.0 and .NET 10.

## Build and automated checks

The production build passed with zero warnings and zero errors. All 37 automated checks passed, including the 24-power catalog, two-power loadout rules, config migration through version 11, and exclusion of native test probes from the production assembly.

## Dedicated server checks

The isolated Windows server ran the following checks with real CS2 engine entities and bots.

| Suite | Passed | Coverage |
|---|---:|---|
| Ability updates | 30/30 | C4 transmission during Invisibility and reveal, planted C4, cluster projectile layout, Random Swap, Unlimited Wallbang, and existing abilities |
| Decoy Clone and grenade refunds | 11/11 | Model and weapon attachment, walking poses and footsteps, bullet-triggered flash, timed expiry, grenade refunds, and Blood Pact |
| Thunder God | 13/13 | Zeus range and accuracy, native firing paths, cover, and ten-second recharge |
| Tiny | 8/8 | Standing and crouched firing height, actual bullets and player hitgroups, and cleanup |
| Native deathmatch | 2/2 | Independent loadouts, automatic respawn, and fresh cooldowns |

## Client and platform coverage

Human-client checks for C4 charm and light rendering, Decoy Clone appearance, and Tiny crosshair alignment remain open. Decoy Clone's AnimGraph2 locomotion is bound to Windows CS2 build 26879581. The standard moving prop is used on other builds.

Some isolated Windows runs reproduced the known access violation on shutdown after the gameplay results were recorded and the plugin unloaded.

See the [v1.8.0 verification record](https://github.com/Ha1wen/Superpower1v5/blob/v1.8.0/docs/VERIFICATION.md) for historical results.
