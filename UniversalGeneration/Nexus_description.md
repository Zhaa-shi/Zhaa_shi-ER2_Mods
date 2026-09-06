# Universal Generation

Spawn any unit or vehicle anywhere, right from Battlefield Commander's RTS god view. A sandbox/cheat tool built on the game's own native spawn pipelines — no balance gating, no hacked units, everything spawns exactly like the game does it.

## Description

Universal Generation adds a spawn panel to Battlefield Commander's RTS god view. Pick a faction, pick an entry, click the battlefield — and it's there, fully functional and commandable.

- **Infantry**: ~50 official squad types (rifle squads, marines, rangers, paratroopers, SS, assault teams…), auto-probed from the game's own squad database each battle. Full squads with proper loadouts.
- **Vehicles**: every tank, truck, plane and gun from the game, DLCs and content mods — auto-enumerated at runtime (390+ entries). Spawned through the game's own vehicle spawner.
- **Factions**: Allies / Enemy / Neutral (civilians). Enemy units fight you, neutral units behave like civilians.
- **Vehicles come with a real crew**: nation-specific tank crews, filled to the exact seat count, boarded through the game's native boarding flow — so spawned tanks are immediately commandable through Battlefield Commander's RTS orders.
- **No objective wandering**: spawned units hold position and engage on sight, and always obey your RTS orders.
- **Favorites**: star the entries you use often; persisted across sessions.
- **One-click cleanup**: remove everything you spawned at any time.

Works as a standalone BepInEx plugin and only activates inside Battlefield Commander's RTS god view — it sleeps everywhere else.

## Installation instructions

1. Install [BepInEx](https://bepinex.dev) (IL2CPP) and **ER2 Battlefield Commander v1.0.1+**
2. Extract `ER2_UniversalGeneration.dll` into `<game>/BepInEx/plugins/`
3. Start a battle → press F9 (god view) → press G

## Main features

- Three clicks to a tank: `G` → pick entry → click the battlefield
- Full vehicle catalog (game + DLC + mods) with live category tabs and favorites
- ~50 official squad types, validated per battle
- Allies / Enemy / Neutral faction spawning
- Crewed vehicles: per-nation tanker crews, exact seat count, natively commandable
- Crew composition: dedicated tankers / any infantry squad type / empty vehicle
- Hold-position behavior: fight anything in sight, never wander, always obey orders
- Shift+click continuous placement
- One-click despawn of everything spawned
- English + Chinese (中文) localization

## Requirements

- Easy Red 2 (BepInEx IL2CPP)
- [ER2 Battlefield Commander](https://www.nexusmods.com/easyred2/mods/…) v1.0.1 or newer (required — the mod only works inside its RTS view)

## Shout outs

- **Corvostudio** — for Easy Red 2 and an engine that welcomes modding
- **BepInEx / Il2CppInterop / Harmony** teams
- The Battlefield Commander mod — this tool lives inside its god view
