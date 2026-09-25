# ER2 Battlefield Commander — RTS God-View Squad Command

## Description
Adds an "RTS god-view squad command" layer to Easy Red 2, with mouse operations inspired by Gates of Hell: Ostfront. Press F9 to enter a free god view overlooking the battlefield, hold LMB to box-select friendly units as a temporary selection — native squads are never split automatically; only the explicit [Split] button creates a real new squad. Vehicle crews stay together as one selectable unit. Right-click issues move, focus-fire mark, boarding and vehicle movement orders; hold-and-drag RMB pulls a formation arrow: infantry spread along the line or walk into nearby cover natively (with a white translucent preview of every cover spot), vehicles take line slots and pivot to the arrow direction on arrival. Thin grey route lines show where commanded units are heading until they arrive. Space pauses the world for unhurried command. Orders already issued — movement, focus-fire, boarding and vehicle sync — keep executing after you switch back to first person; FPS movement, aiming and shooting are untouched. Takeover puts you into a random living selected member to keep fighting.

## Installation instructions
1. Install BepInEx (IL2CPP build) into the game root folder.
2. Put `ER2_BattlefieldCommander.dll` into `Easy Red 2\BepInEx\plugins\`.
3. Launch the game — `Loading [ER2 Battlefield Commander 1.4.24]` in the BepInEx log means success.

## Main features
- **God-view command**: F9 to enter free camera, WASD move, wheel zoom, MMB rotate, Q/E height, Space to pause/resume the world.
- **Temporary box-select & explicit split**: box-selecting only changes the current selection, never splits squads; the top [Split] button is the only way to create a real new squad; Shift+box = append.
- **Gates of Hell style mouse interaction**:
  - LMB on a friendly soldier/vehicle = select (a vehicle crew counts as one unit); LMB on enemy/empty = clear selection
  - Double-click a friendly = select their whole squad
  - RMB short press = order (ground = move; enemy = persistent focus-fire mark; friendly/neutral vehicle or fire position = board/enter directly; friendly soldier in a vehicle = board as reinforcement; building/house = selected infantry enter and take cover inside); RMB double-click = native "Move & Defend"
  - **RMB long press + drag = formation arrow (Gates of Hell style)**: the line is centered on the press point, perpendicular to the drag, as long as the drag; units face along the arrow. Where native cover points exist near the target (sandbags, walls, Combat Cover props...), infantry are assigned cover spots and walk in through the native cover system (they take the cover's suggested stance/facing themselves); the rest spread along the line. While dragging, white translucent ghost models preview every assigned cover spot plus line/vehicle slot markers. Vehicles take line slots, drive there, then pivot to the arrow direction after arriving
  - **Command hotkeys** (rebindable): Z stand / X crouch / C prone / V halt / B hold-fire toggle / N cover nearby / M rally on leaders / F scatter into cover
  - **Route lines**: after a move or boarding order, every marching unit draws a thin grey dashed line to its target (boarding lines follow the vehicle), cleared on arrival
  - Ctrl+1~9 save the current selection as a group; 1~9 recall (dead units auto-pruned)
  - Only units with the ◆ cursor (selected) respond to orders — unselected units stay put
- **Bottom-left info panel**: focused unit name/class, health bar, stance, suppression level; ◀ ▶ cycles through selected units. With vehicles selected it also carries [Dismount] / [Repair] (the right-click interaction ring was removed). Immobile fire positions/artillery only take formation facing — the arrow pivots them in place. Left-click on a corpse (any faction) selects it.
- **Grid backpack windows (v1.3.2)**: [Backpack] / [Cargo] (or G) opens Minecraft-style grid windows — the game's own item icons, count badges, hover tooltips and a weight bar, 6x4 cells per page with page flipping. Same-type items merge into one cell (xN = total) and cells are sorted by category. Open several windows at once and drag whole groups between them: move or swap, all weight-checked; drop outside the windows to throw items on the ground — equipped/held weapons can be picked up and thrown away too (native unequip path). **Right-click a corpse opens its backpack (since v1.4.11 together with the selected unit's backpack); right-click a friendly soldier on foot opens both backpacks — in both cases the nearest selected soldier first walks within the link radius (windows open on arrival; since v1.4.9 they also open as soon as the walker stops, no need to land inside the 3 m ring; since v1.4.10 the soldier sent over is picked from the **on-foot** selection only — when the whole selection is riding a vehicle the errand goes to the nearest friendly soldier on foot instead, and the same fix stops units from popping out of a tank right after boarding, plus the window title no longer overlaps the weight readout); right-click a ground item = the nearest selected soldier picks it up.** The first opened window is the anchor — other packs only open and stay open within a link radius of it (cfg packRange, default 3 m). All changes hit the soldiers' real inventories, so AI reacts normally. **(v1.4.11: closing a window or clicking a menu entry no longer clears your selection.)** **Right-click a cell = the game's own interaction menu (v1.4.0, modal in v1.4.1): wear gear, eat food, use medical items, unequip — executed through the native interaction pipeline (v1.4.12: wearing resolves the item's live instance first and falls back to writing the native equipment fields directly if the official call does nothing — fixes "can only take off, can't put back on"). Right-click an ammo box or other multi-action ground items opens their native menu (refill ammo); simple loot is picked up instantly within the link radius, or fetched by the nearest selected soldier.**
- **Vehicles & emplacements**: click a vehicle/fire position to select the whole crew (no need to click the men inside); right-click a friendly/neutral vehicle for the interaction ring, per-man boarding; dismount/repair through the ring.
- **Random takeover**: with units selected, click the top `[Take Command]` button to possess a random living member back into first person; F9 emergency exit when everyone is wiped.
- **RTS/FPS coexistence**: after switching back, FPS WASD, mouse look, shooting and native controls keep working; movement, focus-fire, boarding and vehicle-sync tasks issued in RTS survive leaving the view. Taking over a unit stops only that unit's own persistent tasks.
- **Native command chain**: move/defend/boarding go through the game's own command entries (Squad.moveTo/HoldArea, boardVehicle); focus-fire marking rides the native target selection (GetBestVisibleEnemy/CurrentVisibleTarget) — the mod builds no movement orchestration of its own, it only issues orders and observes completion.
- **Cursor flicker fixed**: in god view the mod patches `Cursor.set_lockState`, so the game can no longer lock/hide the cursor — no more flicker or cursor snapping to screen center on right-click.
- **Custom cursor** (`customCursor` / `cursorStyle`): a hollow translucent ring tinted by what you point at (white default / green friendly / red enemy / cyan vehicle / yellow building / orange emplacement / light blue interactable). `cursorStyle` = Circle (default) / Arrow / Cross.
- **Native interference suppressed**: native task pulls on your side's squads are released on entry; the native "choose teammate" leftover panel is closed automatically on takeover.

## Requirements
- Easy Red 2
- BepInEx (IL2CPP build)

## Shout outs
Thanks to the Easy Red 2 community and the BepInEx / Harmony ecosystem, and to all authors whose IL2CPP modding work paved the way.

## Recent changes
**1.4.24**
* **Marker lines raised again (~60%)** - infantry ring 1.5 -> 2.4, selection brackets 2.2/2.6 -> 3.5/4.2,
  focus ring 3.0 -> 4.8, route/boarding dashes 1.7/1.8 -> 2.7/2.9, formation 2.4/2.5 -> 3.8/4.0.
  Thickness stays resolution-independent.
* **The panel is now neutral black, not brown.** The warm brown cast is gone entirely (all surfaces
  are R=G=B now), and the leather noise texture lost its warm tint too - over a neutral black base a
  warm overlay just reads as yellow.
* **Panel opacity is now yours to tune:** `UI/uiPanelAlpha` (default 0.85, range 0.55-1.0).
  Translucency and darkness pull against each other - a translucent panel over brown dirt inevitably
  picks up the terrain colour - so instead of guessing at a compromise, the whole axis is exposed as
  a config entry. 1.0 = fully opaque.


**1.4.23**
* **Marker lines were too thin after the distance fix** - base widths raised about 40%
  (infantry ring 1.1 -> 1.5, selection brackets 1.6/1.9 -> 2.2/2.6, focus ring 2.2 -> 3.0, etc.).
  The on-screen thickness stays resolution-independent.
* **Why the UI came out "completely brown": translucency was tinted by the terrain.** At alpha 0.82
  the panel lets the brown dirt through, so no amount of palette work could make it black. Panel
  opacity is now 0.92 and the base colour moved to near-black (#0C0906), keeping only a faint warm
  cast.
* **Leather feel.** A near-black rectangle is still just a colour swatch, so the panel base is now
  rendered as: near-black fill + a 64x64 procedural leather noise texture (two octaves, deterministic)
  tiled at 10% opacity + a 1px lit top edge. Texture and the lighting edge - not colour - are what
  read as leather.
* **Fixed the overlapping buttons at the top of the panel.** The title row was 26px tall but its
  content starts 8px in with 22px-tall buttons, so the Clear / x buttons crossed the divider under
  the title bar. Title row is now 34px (8 + 22 + 4).
* Config defaults darkened to match (#0F0B08 / #2A2017).


**1.4.22**
* **Line width blown up by a wrong camera distance - the real cause of "still too thick".**
  Marker width is derived from camera distance, but that distance was computed as
  `cam.transform.position.magnitude` - the distance to the **world origin**, not to the markers.
  On an ER2 map the origin can sit hundreds of metres away, so the value was always a bogus large
  number. The old build hid this behind a `Clamp(0.6, 2.5)` multiplier; 1.4.21 replaced that with a
  linear pixel formula, so the bogus distance went straight into the width - rings filled in as
  solid discs and selection brackets merged into fat X shapes. The distance is now the real
  line-of-sight ground-intersection distance (0.1 s cache, shared by GodView and formation markers).
* **Rings no longer scale their parent object.** Rings were still sized with `localScale = radius`,
  and `LineRenderer` width is multiplied by the parent's `lossyScale` - so rings were inflated by up
  to 4.2x on vehicles. Radius is now baked into the vertices (the fix the brackets already got), so
  width is a pure, predictable world value.
* **Anti-aliasing for lines and dots.** Line meshes ignore MSAA. Lines now sample a feather texture
  across their width (25% smooth falloff per side) and dots use a radially feathered disc, so edges
  read as clean instead of stair-stepped. Rings also went from 48 to 64 segments.
* **Dark-brown translucent UI.** Palette moved from cold grey to warm dark brown with translucency
  (panel #1E1813 @ 0.82, title bar #2A2119 @ 0.86, control #32271E @ 0.84, selected #584331 @ 0.96).
  Warm hues sit far better against dirt and grass, and the battlefield still shows through.
* **Elements are clearly separated now.** Every tab, faction button and crew button gets an outline
  (warm brown when idle, warm white when selected); list rows get hairline separators; the panel
  border and dividers are warm brown so they actually read on a dark-brown surface.
* **Text no longer collides with its background.** Crew / preview / title / faction / list / pager
  rows still used hard-coded pixel sizes that never multiplied by the adaptive scale, and row
  spacing was baked into only *some* rows - so the crew and preview rows sat flush together. All of
  those sizes now follow the scale, and spacing is applied once in the row loop.
* English config descriptions filled in for 16 strings that previously fell back to Chinese.


**1.4.21**
- **Line width is defined in pixels now.** `LineRenderer.widthMultiplier` is world-space, so a fixed number is fat up close and hair-thin at range, and any distance-multiplier fudge silently got 1.33x fatter at 1440p / 2x at 4K. Call sites now state a 1080p target pixel width (1.1 thin ... 2.2 emphasis) and the shared toolkit converts it from the real camera distance and FOV. Constant on-screen thickness at any distance and resolution.
- **Selection brackets no longer read as arrowheads.** Real cause: the bracket root was scaled to `radius` and `LineRenderer` width is multiplied by the parent `lossyScale`, so on a vehicle a 0.1 m line became ~0.5-1.0 m thick against ~1 m corner arms - two fat arms merged into a solid triangle. The root now stays at scale 1 with the radius baked into the vertices; corner arms lengthened 0.34 -> 0.42 of the radius.
- **World markers moved from transparency to greyscale.** Semi-transparent grey (alpha 0.26-0.48) gets eaten by grass, snow and sand. All world markers are now near-opaque greyscale (alpha >= 0.80) with hierarchy carried by lightness (#9AA1A8 -> #C6CBD0 -> #E2E6EA -> white). Only the ghost preview stays translucent.
- **Panels got structure instead of flat colour blocks.** New `Frame` (outline), `HLine` (divider) and `AccentBar` (selection stripe) primitives, a separate title-bar fill, inset list borders and a left stripe on favourite rows. Hierarchy is now face + line + bar.
- **Palette pulled back to mid-dark grey** (1.4.20 lift went too far): panel #14181D, title bar #1F252C, control #262D35, hover #333B45, selected #46505C.

**1.4.20**
- **Adaptive UI.** Panels, HUD text, the bottom hint bar, backpack grids, tooltips and the context menu now follow the **game's own UI size setting** (`ResourcesManager.ResolutionMult`, with a screen-resolution fallback). Change UI size in the game options and both this mod and Universal Generation rescale together. No dragging, no number fields, no config entry. Clamped to 0.75x – 1.6x.
- Long-standing cause of the UI being "frozen" fixed: size and font tokens in the shared toolkit were compile-time constants, so a resolution change could never move them. They are now scale-driven properties, and styles rebuild automatically when the scale shifts.
- The bottom hint bar (previously a hard-coded 1400 px) now converges into the available screen width instead of spilling off narrow displays.
- **Grey-black palette lifted.** The previous step read too dark — dark surfaces compressed into each other. Panel / title bar / control / row / selected now step +8 to +14 brightness levels apart.

**1.4.19**
- **Grey-black mono UI.** Panels, lists and buttons now use a neutral grey-black palette (`Shared/Er2Ui.cs`) instead of the old military green — hierarchy comes from brightness, not hue. `UI/uiMono=false` restores the old green preset.
- **World-space markers redrawn.** The selected-unit marker changed from four 45-degree arcs (which read like a weather symbol) to **right-angle corner brackets** — the standard RTS language. Friendly rings, focus ring, move target, route/boarding lines and formation markers are now white / semi-transparent grey, separated by **opacity tier + dash rhythm + shape**.
- Route and boarding lines finally look different — they previously used the identical colour literal. Route = dim long dashes, boarding = brighter short dashes.
- Marker pulse phase is offset per key (all markers used to breathe in perfect sync).
- Line width compensates for camera distance (LineRenderer width is in world units and thins out at range).
- Name plates gained a dark backing plate for readability over snow/sky.
- Cursor states are greyscale (only Enemy stays red, Emplacement stays orange).
- **14 new visual config switches** (`[Markers]` + `[UI]`): markersEnabled, showFriendlyRing, showSelectedBracket, showFocusRing, showMoveTarget, showPathLines, showFormationMarkers, showNamePlates, markerPulse, markerScale, markerLineWidth, markerThroughWall, markerColorMode (Mono|Semantic), uiMono.

**1.4.18**
- Internal only, no gameplay or visual change: the HUD, info panel and backpack now build their text styles through the shared UI toolkit (`Shared/Er2Ui.cs`) that Universal Generation also uses, instead of hand-rolling a `GUIStyle` in each file. Same fonts, alignment and colours — groundwork for redrawing both mods into one coherent look.

**1.4.17**
- Picking up **weapons** from the ground no longer teleports them into a soldier's inventory. Ground weapons are multi-interaction items, so right-clicking opens the native interaction menu — and a pickup entry there executed at **any** distance. Menu pickup entries now join the same flow as plain items: instant within the link radius, otherwise the nearest selected soldier walks there and picks the weapon up on arrival. Other interactions (ammo refill etc.) unchanged.

**1.4.16**
- Add-on mods can declare their full-screen gesture mutex as a placement/drag gesture (new `externalCameraPass` hook): the camera keeps responding to wheel/MMB during such gestures while clicks stay swallowed (used by Universal Generation's item carry / placement).
