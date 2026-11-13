# Verification: v1.5.0

Tested on 2026-11-13 with Windows CS2, CounterStrikeSharp API 1.0.380.0, .NET 10 and SDK 10.0.404.

- Production Release build: zero warnings/errors.
- **24/24 automated checks passed**, covering valid/uniform power selection over 50,000 rounds, roster/rotation and human priority, permission attributes, replacements, cooldown preservation, configuration migration, retired powers, passive powers and damage bookkeeping. Diagnostic entry points are excluded from the production assembly.
- **5/5 native bot checks passed** in a separate private server, including a direct kill from a six-player living pool, armor/Vampirism behavior, blocking at 19 seconds, reuse after 20 seconds with dead players excluded, boss self-selection with Second Chance, and recovery into the next round.

The checked-in [native results](verification/native-gameplay.json) came from the running-engine test. Production uses Random.Shared; the diagnostic build supplies deterministic random choices to cover both opponent and boss selection through the same code.

The install ZIP contains the tested production DLL, SHA256:

```text
6A7C5A7AA848C106767D462A874B70F3DF9C683038B827887F9FA7C553467F3F
```

Server tests establish state, death handling, timing and cleanup. Client rendering of translucent players and wall outlines still needs human playtesting. The isolated Windows host reported an existing 0xC0000005 during quit after successful plugin unload; no gameplay access violation occurred in the completed checks.

The CS2 server, private diagnostic server, API binaries, raw logs and local backups are not part of this repository.
