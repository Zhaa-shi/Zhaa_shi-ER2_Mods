ER2 Universal Generation v2.4.1
================================

Spawn any unit, vehicle, or item anywhere, right from Battlefield Commander's RTS god view.
A sandbox/cheat tool: no balance gating, just clean spawning through the game's own native pipelines.

Requirements
--------------------------------
- Easy Red 2 (BepInEx IL2CPP)
- ER2 Battlefield Commander v1.2.19 or newer (this mod only works inside its RTS god view; it stays dormant otherwise)

Installation instructions
--------------------------------
1. Extract and drop ER2_UniversalGeneration.dll into <game>/BepInEx/plugins/
2. Make sure ER2_BattlefieldCommander.dll (v1.2.8+) is present
3. Start a battle, press F9 to enter the god view, then press G (or click the "Spawn [G]" button on the left edge)

Main features
--------------------------------
- Three clicks to a tank: G -> pick entry -> click the battlefield
- Zero-lag panel: the whole catalog (squad library, vehicles, firepoints, items) is built in the background at game startup, time-sliced across frames
- Everything spawnable: ~50 base squad types + 400+ era/theater variants straight from the game's squad library (winter, D-Day, early-war…), plus every vehicle from the game + DLC (auto-enumerated), firepoints included
- Firepoints tab: every fixed machine gun in the game — MG34/MG42/Browning/Maxim/Type92/Vickers and more, ground/tripod/bunker mounts, AA machine guns and the M45 Quadmount — spawned with a gunner by default
- Item spawning (new in 2.0.0, catalog rewritten in 2.0.2): the item list is now **enumerated directly from the game's live item database**, so every listed item is guaranteed to spawn — no more "clicked it, says invalid". Categories come from the game's own item types (Weapons / Ammo / Throwables / Gear / Medical & Food / Misc), and entries show their real in-game icon where available. Click an item to pick it up, then drag it onto a soldier to put it in their backpack, or onto the ground to drop it as a real physical item
- Faction picker: Allies / Enemy / Neutral (Civilian) — spawned units fight accordingly
- Allied & neutral spawns hold position and engage on sight, always obeying your RTS orders; enemy spawns use their native AI — they advance and attack on their own like any other enemy force (configurable)
- Vehicles spawn with a proper crew already seated: per-nation tank crews, exact seat count, native spawn-on-vehicle (no boarding wait; they obey RTS orders)
- Crew customization: dedicated tankers, any infantry squad type, or an empty vehicle
- Placement preview: a real translucent ghost model of the unit, rotatable by hold-dragging the left mouse button
- Favorites: star any entry, persisted across sessions, shown in the Favs tab
- One-click cleanup of everything you spawned
- Bilingual (EN/CN), built-in Chinese localization

How to use
--------------------------------
- G: toggle the spawn panel (inside the god view)
- Panel: pick faction -> category -> entry -> the panel collapses into placement mode
- Placement: left-click to place, Shift+left-click to keep placing, right-click/ESC/G to cancel
- Items: open the item tabs -> click an item to pick it up -> drag onto a soldier (green ring) to fill their backpack, or onto the ground (amber ring) to drop it. Right-click / ESC / G cancels the carry
- "Clear" button in the panel title removes everything the mod spawned

Changelog
--------------------------------
2.4.1
- **The "Mod Vehicles" tab no longer overlaps its neighbours.** Category tabs are a fixed-width grid (~72 px per cell) with a fixed 12 px font, so a long label such as "Mod Vehicles" (or the "Medical/Food" item tab) was wider than its button — IMGUI does not clip button text, so it spilled over the adjacent tabs. Every tab now picks the largest font size (12 down to 8) that actually fits its cell, using a per-character width estimate (CJK ≈ 1.0 em, Latin ≈ 0.56 em).
- **The "Mod Vehicles" / "Mod Items" tabs are no longer permanently empty.** Third-party content was discovered through `ModsLoader.mods_installed`, which is still empty on the main menu; the probe waited 30 s, saw zero mods, then flagged itself "ready" forever — so mod vehicles/items never appeared even after entering a battle (where the game actually loads mod content). Discovery now scans the disk directly (`<SteamLibrary>/steamapps/workshop/content/<appid>/*/index.xml`, plus `<game>/Mods` and the runtime mod list as fallback), caches the parsed candidates, and only defers the runtime validation until the item database is ready — re-scanning periodically (15 s while there is pending work, 60 s idle) so newly subscribed mods appear without a restart. No more terminal "ready with zero mods" state.
- Vehicle candidates that keep failing validation (mod bundle not loaded / prefab name mismatch) are dropped after 5 attempts, with a log line saying exactly which id was dropped.

2.4.0
- **The spawn panel no longer overflows its background.** The panel height was a fixed sum that only accounted for the unit-category layout — item tabs, favourite sub-tabs, the item sub-category row and the letter-index rows were not included, so on item tabs the lower list rows, the pagination line and the help text were drawn past the bottom of the panel. The height is now computed from the same row counts the panel actually draws, in lockstep with the layout.
- **Item entries now use the game's own display-name mapping.** Names are resolved through the game's `GetMappedResourcesName()` (the same lookup its own UI uses), falling back to the internal object name. Vanilla items that showed raw internal names (e.g. "arisaka t38carbine") now display their registered names.
- Still seeing Chinese item names with the game set to English? Those entries are **third-party workshop weapons whose authors registered them under Chinese names** — that is mod data, not this mod's UI text, so it follows the mod author's wording in every language.
2.3.0
- **The item 3D ghost preview is back** (a 2.2.0 bug made it invisible): the ghost was registered with a wrong anchor offset — it was teleported away from the cursor every frame (to near the world origin), so you never saw it. The offset is now recorded after positioning, so the model sits 25 cm above the ground point and follows the cursor like unit/vehicle previews.
- **Mouse wheel and middle-mouse camera controls work again while placing or carrying** (needs ER2 Battlefield Commander 1.4.16+). The gesture-mutex full-screen block used to also freeze the camera; the host now distinguishes "gesture mutex" from "UI panel" via a new `externalCameraPass` hook, so wheel zoom and MMB rotate stay live during placement/drag while clicks are still swallowed.
- **English build: the "Wearable" sub-tab no longer shows Chinese** — the label helper returned raw Chinese without going through the translation lookup.
- Failed item-ghost creation is now logged unconditionally (missing prefab / clone failure / ghostify refusal), so "no preview" can always be diagnosed from the log.
- Note: item / squad / vehicle **names** come from the game's own database and from each mod's own `index.xml`, so they follow the game's language setting and the mod author's wording — those are not mod UI text and cannot be translated.
2.2.2
- **English build: fixed leftover Chinese text.** The EN dictionary keys had drifted out of sync with the strings in the code (two config descriptions were reworded but the dictionary was not), so those entries silently fell back to Chinese. Keys now match the code verbatim, the missing "Item spawn failed:" entry was added, and `Ui.Tr` now logs a one-time warning naming any key it cannot translate (with `debugLog` on) so future drift is caught immediately.
  Note: item / squad / vehicle **names** come from the game's own database and from each mod's own `index.xml`, so they follow the game's language setting and the mod author's wording — those are not mod UI text and cannot be translated.
2.2.1
- **Fixed the item list rendering completely blank** (a 2.2.0 regression): the "All" button of the letter-index row was drawn at grid cell 0 but the loop read `letters[i - 2]` starting at i = 1 — index -1 threw `ArgumentOutOfRangeException`, which aborted the whole OnGUI frame, so the letters and the entire item list vanished (symptom: sub-category tabs + a lone "All" button, nothing else). Letters now start at i >= 2.
2.2.0
- **Item carry preview is now a real 3D ghost model** (replacing the ground ring + cursor icon, as requested). When you carry an item over open ground, a translucent model of the actual item follows the cursor — the same ghost visual used for unit/vehicle placement. The green ring under a hovered soldier stays (it marks who will receive the item in their backpack).
- **Mod content support is back (units, squads, items, vehicles).** Workshop / local mods are discovered from each mod's own `index.xml` registration list; entry ids are computed with the game's own `ModsLoader.GenerateModItemId`, then **validated at runtime** — items only appear after `GetItemObject(id)` confirms them, vehicles only after their prefab loads. Validated mod vehicles get a "Mod Vehicles" tab (placed like any other vehicle); validated mod items get a "Mod Items" tab (carried/dropped like any other item). Mod-defined squads and squads from custom factions already flow into the "Infantry" tab automatically (they live in the same squad archive the mod enumerates).
- Fixed overlapping buttons in the letter-index row: the "All" button now occupies two grid cells instead of overrunning its neighbour.
2.1.1
- **Initial-letter index** replaces the 2.1.0 keyword box. `GUI.TextField` is stripped from the game's IL2CPP build → `Method unstripping failed` → the entire OnGUI frame aborted → item list rendered blank. The index row is click-only (letters that actually occur + "All"), 24 px buttons that wrap.
2.1.0
- **Items can now be favourited.** Every item row has a ★ toggle on the right (as units always had); favourites persist to the config file alongside unit favourites (`t:<item_id>` entries).
- **Favourites are now grouped by category.** Selecting the "Favs" tab shows a second row of category sub-tabs (Infantry / MGs / Tanks / Wheeled / Planes / Artillery + Weapons / Ammo / Throwables / Gear / Medical-Food / Misc), listing only the categories you have actually favourited. Unit categories and item categories each use their own list renderer, so a unit row still means "place it" and an item row still means "pick it up" — no mixed list, no accidental clicks.
- **Item sub-categories**, because a single "Weapons" bucket holds ~700 entries. Sub-tabs appear automatically per bucket and are derived from **the game's own data, not name guessing**: `TryCast<Weapon>` → `weaponPose` gives Rifles / Pistols, and `Interagible.IsWerable()` gives Wearables.
- **Initial-letter index** on item lists. A click-only row of the letters that actually occur in the current bucket/sub-category (plus "All"); buttons are 24 px wide and wrap automatically, so even 30+ letters stay clickable. This replaced the 2.1.0 keyword box on purpose: `GUI.TextField` is **stripped out of this game's IL2CPP build**, so calling it throws `Method unstripping failed`, which aborts the whole OnGUI frame — the visible symptom was "sub-category tabs render, but the list is completely blank". Click-only input has zero keyboard dependency and cannot hit a stripped method.
- Switching category resets the sub-category and letter filter, so you never land on an empty list.

2.0.7
- **Fixed the actual root cause of "no item tabs at all"** (the real one; 2.0.3–2.0.6 were all treating symptoms).
  Two defects, both now fixed:
  1. **The readiness gate was removed entirely.** Every previous version waited for a condition before enumerating: "the item database reports ready AND the `items` category is non-empty". 2.0.6's diagnostics proved that condition can **never** be true — `ItemsDatabase.Loaded` is always true, but `GetAllItemsOfType(PropType.items)` **always returns an empty array** (measured for 154 s continuously, across Menu → LoadingScene → Aberdeen). So the gate never opened and the enumeration code never ran, no matter how healthy the coroutine was. The mod now **enumerates unconditionally** and simply retries when it gets nothing, removing this whole class of failure instead of guessing a better condition.
  2. **It was asking for the wrong type.** The enumerator was called as `GetAllItemsOfType<PropData>`, which is almost certainly filtered by the generic argument — and the database stores **`ItemObject`** (the type returned by `GetItemObject(id)`, which has `item_id` and `icon`). `ItemObject` is now the primary source and `PropData` is kept as a fallback; duplicates are de-duplicated by id.
- Diagnostics now print the real counts for **both** types and all four categories, e.g. `物品库可枚举(IO=ItemObject, PD=PropData): items=IO:12/PD:0 weapons=...`, so the log states plain facts instead of requiring guesses.
- Added a one-time top-up sweep 20 s after the catalog is built, in case the database loads in batches and the first pass only captured part of it.

2.0.6
- **Fixed "no item tabs at all"**: 2.0.5's watchdog logic was **inverted**. It began with "if the probe is running, do nothing" — but when the coroutine is killed by a scene change, **nothing resets the state** (the thing that would reset it is the dead coroutine itself). So the state stayed at "running" forever, the watchdog **assumed everything was fine and never restarted it**, and the probe went permanently silent right after printing "not ready, polling every frame". Hence no item tabs.
- **Liveness is now determined by counting the coroutine's own frames, not by time.** The coroutine increments a counter on every frame it runs; the watchdog compares that counter between its own invocations — **if it has not advanced, the coroutine genuinely is not running**, and it is restarted immediately. This test is time-independent, so scene loads and clock drift cannot fool it.
  Every previous round failed for the same underlying reason: using *elapsed time* as the criterion for coroutine death (150 s in 2.0.3, a 20 s heartbeat in 2.0.4). During a scene load the main thread is held and the coroutine stalls **while the clock keeps advancing**, which guarantees a misjudgement. That assumption is now gone entirely.

2.0.5
- Fixed "still no item options". 2.0.4's per-frame polling was the right direction, but the watchdog misread the **normal stall during a scene load** as "coroutine dead" and restarted it; each restart incremented the retry counter, and **after 6 restarts the probe marked itself permanently abandoned** — so even once the scene had loaded and the database was ready, it never retried.
  The auto-abandon path is removed: the probe has only three states (not started / running / done), plus readiness diagnostics explaining why the gate has not opened.

2.0.4
- Fixed item spawning still being unavailable. 2.0.3 corrected the readiness test but polled it only **once every 2 seconds**, so its wait window landed exactly on the scene change and was killed. It now **polls every frame** and begins enumerating the moment the database is ready; the enumeration budget starts only after readiness.

2.0.3
- **Fixed item spawning being completely unavailable**: 2.0.2 rewrote the item catalog to enumerate the game's live item database, but the readiness gate was too weak — it accepted a list that was merely non-null. Before the database finishes loading, `GetAllItemsOfType` returns an **empty but non-null** array, so the probe passed the gate immediately, collected 0 entries, marked itself permanently failed and never retried. The item tabs therefore never appeared and **the whole item-spawn feature was dead**.
  The probe now waits for the game's own `ItemsDatabase.Loaded` flag *and* requires a genuinely non-empty result, retries for up to ~2 minutes instead of giving up on the first try, and the watchdog no longer mistakes a legitimate wait for a killed coroutine.
- Diagnostics: startup now logs `item catalog ready (source=runtime ItemsDatabase enumeration)` with a per-category count once it succeeds.

2.0.2
- **Fixed "only items with an icon can spawn, everything else is invalid"**: the item list now comes from a different source.
  The old version guessed item ids from the prefab manifest on disk, but **those filenames are not the same key space as the game's live item database** — ArisakaT38, Carcano, Syringe and Thompson_M1928 all really exist on disk, yet the database lookup for those ids returns nothing; only entries whose names happened to match (bar_1918, bandages, ToolBox) could spawn. The icon comes from the item object itself, so "has an icon" accidentally became a marker for "actually works".
  The list is now **enumerated directly from the game's live item database**, with categories taken from the game's own item types instead of guesswork. Only items that genuinely spawn are listed.
- **All uniform entries removed**: uniforms are not pickable items in this game — they are loadout fields, and were never spawnable as items. The 757 uniform entries the old build listed were all dead weight.
- The item catalog is now built by a **background time-sliced enumeration at startup** (max 3 ms per frame), so opening the panel stays instant.

2.0.1
- Fixed: clicking an item placed it instantly instead of picking it up. The mouse-release that ends the panel click was being consumed and then still evaluated as a drop in the same frame — you can now click an item and drag it properly.
- Fixed: many items reported "invalid item". Two causes: (a) 61 firearms (Gewehr43, Springfield_1903, MAS_36, VZ24…) were mis-sorted into the Misc tab because the classifier only knew English weapon names — they are now under Weapons; (b) the manifest lists every prefab on disk, but not all of them exist in the game's runtime item database, so entries are now verified at startup and unusable ones are dropped from the list.
- Fixed: dragging onto a soldier often failed, because the camera ray usually hit the ground in front of them rather than the soldier. The target search now scales with camera distance instead of a fixed 1.6 m radius, and dropping on the ground no longer refuses when a soldier is in the way.
- Added failure logging for every item path (previously silent), so "invalid item" now always tells you why in the log.

Known limitations
--------------------------------
- Singleplayer verified; multiplayer only as host
- Planes spawn on the ground and may crash — handle with care
- Spawned units are not guaranteed to survive phase transitions
- Custom squads created in the mission editor cannot be spawned: the game does not expose their member data at runtime (the squad objects attached to spawn points are empty shells)
- Item icons are decoded lazily in the background as you scroll; a freshly opened item tab may briefly show names before icons appear

Shout outs
--------------------------------
- Corvostudio: Easy Red 2 and its open attitude towards modding
- BepInEx / Il2CppInterop / Harmony teams
- The Battlefield Commander mod — this tool is built on top of its RTS view
