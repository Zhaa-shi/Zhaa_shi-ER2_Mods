ER2 Universal Generation v1.0.0
================================

Spawn any unit or vehicle anywhere, right from Battlefield Commander's RTS god view.
A sandbox/cheat tool: no balance gating, just clean spawning through the game's own native pipelines.

Requirements
--------------------------------
- Easy Red 2 (BepInEx IL2CPP)
- ER2 Battlefield Commander v1.0.1 or newer (this mod only works inside its RTS god view; it stays dormant otherwise)

Installation instructions
--------------------------------
1. Extract and drop ER2_UniversalGeneration.dll into <game>/BepInEx/plugins/
2. Make sure ER2_BattlefieldCommander.dll (v1.0.1+) is present
3. Start a battle, press F9 to enter the god view, then press G (or click the bottom-left "Spawn" button)

Main features
--------------------------------
- Three clicks to a tank: G -> pick entry -> click the battlefield
- Everything spawnable: ~50 official squad types (auto-probed per battle) and every vehicle from the game + DLC + content mods (auto-enumerated)
- Faction picker: Allies / Enemy / Neutral (Civilian) — spawned units fight accordingly
- Vehicles spawn with a proper crew: per-nation tank crews, exact seat count, full native boarding (they obey RTS orders)
- Crew customization: dedicated tankers, any infantry squad type, or an empty vehicle
- Spawned units hold position and engage on sight — they never wander off to objectives, and they obey your orders
- Favorites: star any entry, persisted across sessions, shown in the Favs tab
- One-click cleanup of everything you spawned
- Bilingual (EN/CN), built-in Chinese localization

How to use
--------------------------------
- G: toggle the spawn panel (inside the god view)
- Panel: pick faction -> category -> entry -> the panel collapses into placement mode
- Placement: left-click to place, Shift+left-click to keep placing, right-click/ESC/G to cancel
- "Clear" button in the panel title removes everything the mod spawned

Known limitations
--------------------------------
- Singleplayer verified; multiplayer only as host
- Planes spawn on the ground and may crash — handle with care
- Spawned units are not guaranteed to survive phase transitions

Shout outs
--------------------------------
- Corvostudio: Easy Red 2 and its open attitude towards modding
- BepInEx / Il2CppInterop / Harmony teams
- The Battlefield Commander mod — this tool is built on top of its RTS view
