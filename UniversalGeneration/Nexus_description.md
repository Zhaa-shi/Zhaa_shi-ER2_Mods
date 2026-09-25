# Universal Generation

Spawn any unit, vehicle, or item anywhere, right from Battlefield Commander's RTS god view. A sandbox/cheat tool built on the game's own native spawn pipelines — no balance gating, no hacked units, everything spawns exactly like the game does it.

## Description

Universal Generation adds a spawn panel to Battlefield Commander's RTS god view. Pick a faction, pick an entry, click the battlefield — and it's there, fully functional and commandable.

- **Infantry**: ~50 base squad types + 400+ era/theater variants read straight from the game's squad library (winter, D-Day, early-war…), auto-probed each battle. Full squads with proper loadouts.
- **Firepoints**: every fixed machine gun in the game — MG34, MG42, Browning, Maxim, Type92, Vickers and more, in ground/tripod/bunker mounts, plus AA machine guns and the M45 Quadmount. Spawned with a gunner by default.
- **Vehicles**: every tank, truck, plane and gun from the game and DLCs — auto-enumerated at runtime. Spawned through the game's own vehicle spawner.
- **Items** *(new in 2.0.0, catalog rewritten in 2.0.2)*: the item list is now **enumerated directly from the game's live item database**, so every listed item is guaranteed to spawn, across Weapons / Ammo / Throwables / Gear / Medical & Food / Misc (categories taken from the game's own item types) — each shown with its real in-game icon where available. Click an item to pick it up, then **drag it onto a soldier to fill their backpack, or onto the ground to drop it as a real physical item.**
- **Factions**: Allies / Enemy / Neutral (civilians). Enemy units fight you, neutral units behave like civilians.
- **Vehicles come with a real crew, already seated**: nation-specific tank crews, filled to the exact seat count, spawned straight onto the vehicle through the game's native spawn-on-vehicle path — so spawned tanks are immediately commandable through Battlefield Commander's RTS orders.
- **Allied spawns hold position**: they engage on sight and always obey your RTS orders — never wander off. **Enemy spawns act on their native AI**: they advance and attack like any other enemy force (configurable).
- **Favorites**: star the entries you use often; persisted across sessions.
- **One-click cleanup**: remove everything you spawned at any time.

Works as a standalone BepInEx plugin and only activates inside Battlefield Commander's RTS god view — it sleeps everywhere else.

## Installation instructions

1. Install [BepInEx](https://bepinex.dev) (IL2CPP) and **ER2 Battlefield Commander v1.2.19+**
2. Extract `ER2_UniversalGeneration.dll` into `<game>/BepInEx/plugins/`
3. Start a battle → press F9 (god view) → press G

## Main features

- Three clicks to a tank: `G` → pick entry → click the battlefield
- Zero-lag panel: the catalog is built in the background at game startup (time-sliced, no frame hitches)
- Full vehicle catalog (game + DLC) with live category tabs and favorites
- ~50 base squad types + 400+ era/theater variants from the game's squad library
- Dedicated firepoint tab: all fixed machine guns, crewed by default
- **Item tabs with real icons**: drag an item onto a soldier to fill their backpack, or onto the ground to drop it
- Allies / Enemy / Neutral faction spawning
- Allied spawns hold position & obey orders; enemy spawns use native AI (configurable)
- Crewed vehicles: per-nation tanker crews, exact seat count, natively commandable
- Crew composition: dedicated tankers / any infantry squad type / empty vehicle
- Shift+click continuous placement
- One-click despawn of everything spawned
- English + Chinese (中文) localization

## How to use items

1. Open the panel (`G`) and switch to an item tab (Weapons / Ammo / Throwables / Gear / Medical & Food / Uniforms)
2. Click an item — you're now carrying it, shown as a ghost icon at the cursor
3. Move the cursor over a soldier (**green ring**) and release to put it in their backpack, or over the ground (**amber ring**) and release to drop it as a real item
4. Right-click / `ESC` / `G` cancels the carry without dropping anything

Items go into backpacks as their **proper subclass** — magazines hold the right round count, grenades behave like grenades, ammo stacks like ammo. Weight limits are respected on pickup into a backpack.

## Changelog

**2.5.17**
- No change on this mod's side; the text colour unification and the info-panel / hint-bar exclusivity
  live in the shared toolkit and the commander mod.


**2.5.16**
- **Hover feedback kept working**: all text went pure white, so the hover dim now uses a dedicated
  slightly-darker token instead of the (now equally white) secondary colour.
- No other change on this mod's side; the overlap fix and 50% opacity live in the shared toolkit and
  the commander mod.


**2.5.15**
- **Removed the tooltip box** and restored hover feedback: hovering a tab or a list entry now dims its text.
- No other change.


**2.5.14**
- **Panel opacity migration:** if `uiPanelAlpha` still holds the old default (0.85), it is updated to the current default (0.72). Values you changed yourself are untouched.
- **Stronger text outline** (four directions) and one point larger list text, so labels read clearly against the panel.


**2.5.13**
- **Hover tooltips restored** (the 1.4.30 control refactor had dropped the tooltip channel): tabs, list entries and the panel title carry tooltips again, drawn as a small box near the cursor.
- No other change.


**2.5.12**
- **Shared panel style with the commander mod**: same colour tokens, same leather texture and top highlight edge; HUD button outlines use the shared border token.
- No gameplay change.


**2.5.11**
- **Text is outlined instead of bold.** `FontStyle.Bold` is silently ignored when the font has no bold face (likely the case for the game font), which is why the previous attempt changed nothing. Row text and tab labels now draw a dark offset copy first, then the white text on top.
- **Row text enlarged** to `FontBody + 3`.
- **Panel colour matched to the bottom HUD bar:** all surfaces lost their blue cast and panel opacity now defaults to 0.72 (same as the HUD bar), so the panel picks up terrain colour the same way instead of looking cold.


**2.5.10**
- **Row text is bold and 1 pt larger.** White on #1A1A22 is already ~15:1 contrast - what was missing was stroke weight, not brightness. Bold + larger is the actual fix.
- **Every row draws its separator now.** Lines were previously drawn only between rows, so a single-entry list showed none (which is why favourites appeared to have lost them), and the favourites folder list had none at all.
- **Favourite stars: 14 -> 17 px, bold, brighter gold (#FFE81A).**
- **Surfaces lifted another step** (panel #1A1A22, control #32323C, rows #24242C / #2E2E38).


**2.5.9**
- **Favourites are now a two-level folder view.** Opening *Favourites* shows one **folder per category** (`> Name (count)`); clicking a folder opens its saved entries, with a breadcrumb `< Favourites / Name` to go back. The old row of category tabs is gone, and entering Favourites no longer jumps straight into the first category.
- **Panel lifted one step** (panel #121218, title bar #1C1C24, control #26262E, row #1A1A20) and rows use **zebra striping** so list lines are easy to follow.
- **The bottom help line wraps now** (dedicated style with word-wrap and a two-line row height) - shortening the text alone could never fix a narrow panel plus a long English sentence.


**2.5.8**
- **Translucency is background-only now.** Faces follow the panel-opacity setting; text and lines are fully opaque (previously the border/divider alpha was tied to it too, which killed contrast).
- **Text and lines are white** (#FFFFFF text, white borders and dividers) instead of near-white grey.
- **Favourite stars brightened** - `GUI.contentColor` multiplies with `GUIStyle.normal.textColor`, so a grey star style was darkening the gold; the style is pure white now and the gold is #FFD800.
- **Fixed the bottom help line overflowing the panel** - the cause was horizontal (a ~99-character English string against ~450 px of width), not the vertical padding. Shortened the string and added width-aware font shrinking.


**2.5.7**
- **Marker line width now uses fixed world units** (shared toolkit), matching the marker radii which are also fixed world sizes. Previously width was constant in screen pixels, so its *world* width grew with distance and far-away rings filled into solid discs. Near is now thicker and far thinner, with a constant ratio - natural perspective.
- `[Markers] markerLineWidth` still scales every marker line together.


**2.5.6**
- **Marker lines reverted to the 1.4.23 widths** - the "too thin" impression came from a very close camera: marker radii are fixed world sizes, so up close the ring fills much of the screen while the line stays pixel-constant, dropping the line/hole ratio. Use `[Markers] markerLineWidth` if it still reads thin at close range.
- **Fixed the bottom help text running past the panel edge** (bottom padding 8 -> 18px, help row 34 -> 38px).
- **Contrast pass:** brighter/thicker panel border (#555560, ~2px), brighter selected fill, darker row fills, row separators 10% -> 15%, more opaque list background.


**2.5.5**
- **Marker lines raised about 60%** again (infantry ring 1.5 -> 2.4, selection brackets -> 3.5/4.2, focus ring -> 4.8).
- **Panel is neutral black now, not brown** - all surfaces are R=G=B and the leather noise texture lost its warm tint.
- **Panel opacity is configurable:** `UI/uiPanelAlpha` (default 0.85, range 0.55-1.0); 1.0 = fully opaque. Translucency and darkness pull against each other, so the axis is exposed instead of guessed.


**2.5.4**
- **Marker lines raised about 40%** (they were too thin after the camera-distance fix).
- **Near-black leather panel.** The panel used to read as "completely brown" because translucent panels get tinted by the brown terrain behind them - opacity is now 0.92 over a near-black base (#0C0906), with a 64x64 procedural leather noise overlay and a 1px lit top edge for the leather feel.
- **Fixed the overlapping buttons at the top** (title row 26 -> 34px; the Clear / x buttons no longer cross the divider).
- Config defaults darkened to match.


**2.5.3**
- **Dark-brown translucent UI** - the shared palette moved to warm dark brown with translucency (panel #1E1813 @ 0.82, title bar #2A2119 @ 0.86, control #32271E @ 0.84, selected #584331 @ 0.96), matching Battle Commander 1.4.22.
- **Elements are clearly separated:** tabs, faction buttons and the crew button all get outlines (warm brown when idle, warm white when selected); list rows get hairline separators; the panel border and dividers are warm brown.
- **Text no longer collides with its background:** crew / preview / title / faction / list / pager rows still used hard-coded pixel sizes that ignored the adaptive scale, and row spacing was baked into only some rows - so crew and preview sat flush together. All sizes now follow the scale, and spacing is applied once in the row loop.
- English config description for the UI palette switch.


**2.5.2**
- **Panels got structure instead of flat colour blocks.** Every surface used to be a flat fill, so hierarchy rested on brightness alone and collapsed against a bright battlefield. Shared toolkit gained three primitives - `Frame` (outline), `HLine` (divider) and `AccentBar` (selection stripe) - and the panel now draws a separate title-bar fill, a divider under the title, an outer frame, inset borders around both lists and a left stripe on favourite rows. Hierarchy is now face + line + bar.
- **Palette pulled back to mid-dark grey.** The 2.5.1 lift went too far (feedback: "too light"): panel #14181D, title bar #1F252C, control #262D35, hover #333B45, selected #46505C - stepped 8-14 brightness levels apart, which is what makes the new outlines read.
- Fixed row heights that were never multiplied by the adaptive scale (title / faction / pager / item-help / crew / preview).
**2.5.1**
- **Adaptive UI.** The panel, placing badge and carry badge now follow the **game's own UI size setting** (`ResourcesManager.ResolutionMult`, with a screen-resolution fallback). Change UI size in the game options and the panel rescales with it — no dragging, no number fields, no config. Clamped to 0.75x – 1.6x.
- The layout is no longer frozen: size and font tokens in the shared toolkit were compile-time constants; they are now scale-driven properties and styles rebuild automatically when the scale changes.
- The carry badge (previously a hard-coded 560 px) now converges into the screen instead of spilling off narrow displays.
- **Grey-black palette lifted.** 2.5.0 read too dark. Panel / title bar / control / row / selected now step +8 to +14 brightness levels apart.
- Last green leftovers removed: the carry badge background and the drag-target ring (was bright green) are on the shared grey/white palette now.

**2.5.0**
- **Grey-black mono UI.** The panel now uses the neutral grey-black palette from the shared toolkit (`Shared/Er2Ui.cs`) instead of the old military green — list rows, tabs, buttons and the preview area read as one flat dark surface, with hierarchy carried by brightness rather than hue. Set `UI/uiMono=false` to restore the previous green preset.

**2.4.2**
- **No user-visible change — a code-quality release preparing the UI for a full redraw.** The panel now builds a **row plan** first (one entry per row: title / faction / tabs / list / pager / preview) and derives its height by summing that plan, while drawing walks the same list — so a row can no longer be drawn outside the panel (the class of bug fixed in 2.4.0). Both mods now share one UI toolkit (`Shared/Er2Ui.cs`): design tokens (spacing, font sizes, colours) and drawing primitives live in a single place, so a visual change lands in both mods at once.
- Tab text fitting now measures properly (`GUIStyle.CalcSize`) with caching, instead of estimating per-character widths.

**2.4.1**
- **The "Mod Vehicles" tab no longer overlaps its neighbours.** Tabs are a fixed-width grid with a fixed font, so long labels ("Mod Vehicles", "Medical/Food") spilled past their button onto the next tab. Every tab now auto-shrinks to the largest font size that fits its cell.
- **"Mod Vehicles" / "Mod Items" tabs are no longer permanently empty.** Discovery used `ModsLoader.mods_installed`, which is empty on the main menu; the probe saw zero mods, flagged itself ready and never retried — so mod content (which only loads in battle) never showed up. Content is now discovered by scanning each mod's `index.xml` on disk (workshop + local mod folders), with the runtime validation deferred until the item database is ready, and periodic re-scans so newly subscribed mods appear without a restart.

**2.4.0**
- **The spawn panel no longer overflows its background on item tabs.** The fixed panel height never accounted for item tabs, favourite sub-tabs, the sub-category row or the letter index, so the lower list rows, pagination and help text drew past the panel. Height is now computed from the rows actually drawn.
- **Item entries use the game's own display-name mapping** (`GetMappedResourcesName`, the same lookup the game UI uses), falling back to the internal name. Vanilla items with raw internal names (e.g. "arisaka t38carbine") now show their registered names. Third-party workshop items keep the name their author registered (a Chinese-named workshop weapon stays Chinese in every language — that is mod data, not UI text).

**2.3.0**
- **The item 3D ghost preview is back** (2.2.0's never showed): a wrong anchor offset teleported it away from the cursor every frame; it is now positioned before the offset is recorded, so the translucent model follows the ground point like unit previews.
- **Mouse wheel / MMB camera controls work while placing or carrying** (requires ER2 Battlefield Commander 1.4.16+): the host now separates gesture-mutex blocking from UI-panel capture, so wheel zoom and camera rotation stay live during placement; clicks are still swallowed.
- English build: the "Wearable" sub-tab label no longer leaks Chinese.
- Item-ghost failures are logged unconditionally for diagnosis.

**2.2.2**
- English build: fixed leftover Chinese text (two EN dictionary keys had drifted out of sync with the code and silently fell back to Chinese). Added a missing-translation self-check that names the offending string in the log. Item/squad/vehicle names come from the game database and from each mod's own manifest, so they follow the game's language, not the mod's UI language.

**2.2.1**
- Fixed a 2.2.0 regression that made the whole item list render blank (off-by-one in the letter-index row aborting the OnGUI frame).

**2.2.0**
- **Item carry preview is now a real 3D ghost model** (replaces the ground ring + cursor icon). Validated mod content is back: a "Mod Vehicles" tab and a "Mod Items" tab filled from each workshop/local mod's own `index.xml`, ids computed with the game's `GenerateModItemId` and **runtime-validated** before listing. Mod squads already appear under "Infantry".
- Fixed overlapping buttons in the item letter-index row.

**2.1.1**
- **Replaced the item keyword box with a click-only initial-letter index.** `GUI.TextField` is stripped from the game's IL2CPP build — calling it throws `Method unstripping failed`, which aborts the entire `OnGUI` frame, so 2.1.0 rendered the sub-category tabs and then an empty list. The index row lists only letters that actually occur in the current bucket/sub-category (plus "All"), with 24 px buttons that wrap automatically.

**2.1.0**
- **Items can now be favourited** (★ on every item row, persisted alongside unit favourites), and **favourites are grouped by category** — the Favs tab gains a row of sub-tabs listing only the categories you actually favourited (unit categories and item categories each keep their own click behaviour: place vs pick up).
- **Item sub-categories** for the huge buckets (~700 weapons): Rifles / Pistols derived from the game's own `Weapon.weaponPose`, Wearables from `Interagible.IsWerable()` — no name guessing.
- **Initial-letter index** on item lists (click-only, letters that actually occur + "All", buttons wrap). This **replaced** the keyword box shipped in 2.1.0 — see 2.1.1.

**2.0.7**
- **Fixed the real root cause of "no item tabs at all"** (2.0.3–2.0.6 only treated symptoms). (1) **Removed the readiness gate entirely** — every prior version waited for "database ready AND the `items` category non-empty", but 2.0.6 diagnostics proved that can never be true: `ItemsDatabase.Loaded` is always true while `GetAllItemsOfType(PropType.items)` always returns an empty array (measured 154 s across three scenes). The gate never opened, so enumeration never ran. It now enumerates unconditionally and retries when empty. (2) **It was querying the wrong type** — switched the primary source from `GetAllItemsOfType<PropData>` to `GetAllItemsOfType<ItemObject>` (`ItemObject` is what `GetItemObject(id)` returns and carries `item_id`/`icon`); `PropData` kept as fallback, de-duplicated by id. Diagnostics now print real counts for both types across all four categories.

**2.0.6**
- Fixed "no item tabs at all". 2.0.5's watchdog logic was **inverted**: it began with "if the probe is running, do nothing", but when the coroutine is killed by a scene change **nothing resets the state** (the thing that would reset it is the dead coroutine itself). The state therefore stayed at "running" forever and the watchdog **assumed all was well and never restarted it** — the probe fell silent right after printing "not ready, polling every frame", so no item tabs were built.
- **Liveness is now determined by counting the coroutine's own frames, not by time.** The coroutine increments a counter on each frame it runs; the watchdog compares that counter between invocations — **if it has not advanced, the coroutine really is not running** and is restarted immediately. This test is time-independent, so scene loads and clock drift cannot fool it. Every earlier round failed for the same reason: treating *elapsed time* as proof of coroutine death (150 s in 2.0.3, a 20 s heartbeat in 2.0.4) — during a scene load the main thread is held and the coroutine stalls **while the clock keeps advancing**, which guarantees a misjudgement. That assumption has now been removed entirely.

**2.0.5**
- Fixed "still no item options". 2.0.4's per-frame polling was right, but the watchdog misread the **normal stall during a scene load** as "coroutine dead" and restarted it; each restart incremented the counter, and **after 6 restarts the probe marked itself permanently abandoned**. The abandon path is removed and readiness diagnostics added.

**2.0.4**
- Fixed item spawning still being unavailable. 2.0.3 corrected the readiness test but polled it only **once every 2 seconds**, so its wait window landed exactly on the scene change and was killed. It now **polls every frame**; the enumeration budget starts only after readiness.

**2.0.3**
- Fixed item spawning being unavailable altogether. 2.0.2 replaced the item catalog with a live enumeration of the game's item database, but its readiness check only required the returned list to be non-null — and before the database loads, that call returns an **empty but non-null** array. The probe passed the gate instantly, found 0 items, flagged itself as permanently failed and never retried, so no item tabs were ever built and the entire item-spawn feature was dead.
  The probe now waits on the game's own `ItemsDatabase.Loaded` flag **and** a genuinely non-empty result, retrying for up to ~2 minutes rather than dying on the first attempt, and the watchdog no longer treats a legitimate wait as a killed coroutine.

**2.0.2**
- Fixed "only items with an icon can spawn, everything else is invalid": the item catalog is now **enumerated directly from the game's live item database**. The old build guessed item ids from the prefab manifest on disk, but those filenames are not the same key space as the runtime database — some entries (ArisakaT38, Carcano, Syringe, Thompson_M1928) really exist on disk yet cannot be resolved, while entries whose names happened to match (bar_1918, bandages, ToolBox) could spawn. Since the icon comes from the item object itself, "has an icon" accidentally became a marker for "actually works". Only items that genuinely spawn are listed now, and categories come from the game's own item types.
- Removed all uniform entries: uniforms are loadout data, not pickable items — all 757 of them were dead weight.
- The item catalog is now built by a background time-sliced enumeration at startup (max 3 ms per frame), so opening the panel stays instant.

**2.0.1**
- Fixed: clicking an item placed it instantly instead of picking it up (the panel click's mouse-release was re-evaluated as a drop in the same frame).
- Fixed: many items reported "invalid item". Firearms whose names aren't English (Gewehr43, Springfield_1903, MAS_36, VZ24…) were mis-sorted into Misc — now under Weapons; and manifest entries that don't exist in the game's runtime item database are now verified at startup and dropped from the list.
- Fixed: dragging onto a soldier often failed (the camera ray usually hit the ground in front of them) — the target search now scales with camera distance, and ground drops no longer refuse when a soldier is in the way.
- Every item failure path now logs a reason instead of failing silently.

## Known limitations
- Singleplayer verified; multiplayer only as host
- Item icons are decoded lazily in the background as you scroll; a freshly opened item tab may briefly show names before icons appear
- Planes spawn on the ground and may crash — handle with care
- Spawned units are not guaranteed to survive phase transitions
- Custom squads created in the mission editor cannot be spawned: the game does not expose their member data at runtime

## Requirements

- Easy Red 2 (BepInEx IL2CPP)
- [ER2 Battlefield Commander](https://www.nexusmods.com/easyred2/mods/…) v1.2.19 or newer (required — the mod only works inside its RTS view)

## Shout outs

- **Corvostudio** — for Easy Red 2 and an engine that welcomes modding
- **BepInEx / Il2CppInterop / Harmony** teams
- The Battlefield Commander mod — this tool lives inside its god view
