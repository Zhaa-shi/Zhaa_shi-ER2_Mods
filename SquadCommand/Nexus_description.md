# ER2 Battlefield Commander — RTS God-View Squad Command

## Description
Adds an "RTS god-view squad command" layer to Easy Red 2, with mouse operations inspired by Gates of Hell: Ostfront. Press F9 to enter a free god view overlooking the battlefield, hold LMB to box-select friendly units as a temporary selection — native squads are never split automatically; only the explicit [Split] button creates a real new squad. Vehicle crews stay together as one selectable unit. Right-click issues move, focus-fire mark, boarding and vehicle movement orders; hold-and-drag RMB pulls a formation arrow: infantry spread along the line or walk into nearby cover natively (with a white translucent preview of every cover spot), vehicles take line slots and pivot to the arrow direction on arrival. Thin grey route lines show where commanded units are heading until they arrive. Space pauses the world for unhurried command. Orders already issued — movement, focus-fire, boarding and vehicle sync — keep executing after you switch back to first person; FPS movement, aiming and shooting are untouched. Takeover puts you into a random living selected member to keep fighting.

## Installation instructions
1. Install BepInEx (IL2CPP build) into the game root folder.
2. Put `ER2_BattlefieldCommander.dll` into `Easy Red 2\BepInEx\plugins\`.
3. Launch the game — `Loading [ER2 Battlefield Commander 1.4.56]` in the BepInEx log means success.

## Main features
- **God-view command**: F9 to enter free camera, WASD move, wheel zoom, MMB rotate, Q/E height, Space to pause/resume the world.
- **Temporary box-select & explicit split**: box-selecting only changes the current selection, never splits squads; the top [Split] button is the only way to create a real new squad; Shift+box = append.
- **Gates of Hell style mouse interaction**:
  - LMB on a friendly soldier/vehicle = select (a vehicle crew counts as one unit); LMB on enemy/empty = clear selection
  - Double-click a friendly = select their whole squad
  - RMB short press = order (ground = move; enemy = persistent focus-fire mark; friendly/neutral vehicle or fire position = board/enter directly; friendly soldier in a vehicle = board as reinforcement; building/house = selected infantry enter and take cover inside); RMB double-click = native "Move & Defend"
  - **RMB long press + drag = formation arrow (Gates of Hell style)**: the line is centered on the press point, perpendicular to the drag, as long as the drag; units face along the arrow. **Every infantry slot is previewed live by a white translucent ghost model** (since v1.4.50 — previously only cover spots were). Where native cover points exist near a soldier's own slot (sandbags, walls, Combat Cover props...), he takes that spot and walks in through the native cover system, taking the cover's suggested stance himself; everyone else stays on the line. How far a slot may reach for cover is `formCoverCorridor` (default 10 m, 0 = pure line). Vehicles take line slots, drive there, then pivot to the arrow direction after arriving
  - **Command hotkeys** (rebindable): Z stand / X crouch / C prone / V halt / B hold-fire toggle / N cover nearby / M rally on leaders / F scatter into cover
  - **Route lines**: after a move or boarding order (not formation orders), every marching unit draws a thin grey dashed line to its target (boarding lines follow the vehicle), cleared on arrival — since v1.4.52 arrival is judged per soldier against his own slot, so lines and the "Moving → x/N" readout clear as soon as the last man is in place
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
**1.4.38**
* **Info panel background restored** - I misread your last message: "make it look like the top-left
  readout, no background" was about the **bottom hint bar**, not the info panel. The panel's plate is back.
* **The bottom hint bar lost its background instead**: white text with a shadow drawn straight over the
  terrain, same look as the top-left selection readout. The visibility rule (panel and hint bar take
  turns) is unchanged.


**1.4.37**
* **The hint bar text was the last grey holdout.** `HudStyleSmall()` and the backpack `tipStyle` still
  used a hard-coded military-green-era colour `(0.85, 0.9, 0.85)` - they never went through `uiText`,
  so the 1.4.36 text unification missed them (trap 113's "side door" again). Both now use
  `Er2Ui.Text`. A project-wide scan of every `MakeLabel`/`MakeButton` call confirms no non-white text
  colour remains.
* **The unit info panel lost its background**, matching the game's own top-left selection readout:
  white text with a shadow drawn straight over the terrain, no plate.


**1.4.36**
* **Why the text was still grey: the HUD text colour comes from the config**, and config files never
  update themselves (trap 109). `colorText` has joined the migration chain (old values #E8E8E8 /
  #F1EBE2 / #F0F0F2 -> #FFFFFF), and — more importantly — **the HUD text source is now
  `Er2Ui.Text` directly**, so the commander mod and Universal Generation share the exact same white
  text. The three colour configs are kept but no longer read.
* **The unit-info panel and the bottom hint bar now take turns.** Previously both existed at once on
  the same vertical strip. With nothing selected the info panel is not drawn (and the hint bar is);
  with a selection the panel takes that space and the hint bar steps aside.
* `colorBase` default is now `#00000080` (black at 50%, matching the new opacity), and its migration
  chain includes the previous default.


**1.4.35**
* **The bottom-left panel and the squad list no longer overlap the hint bar.** My previous "symmetry"
  pass set both bottom gaps to 14 px - but the hint bar's top edge sits 30 px above the screen bottom,
  so both blocks were drawn straight through it (your screenshot). Both gaps are now **36 px**
  (hint bar 30 + 6 gap), still symmetric left/right.
* **Panel opacity is 50% now** (you asked for it): `uiPanelAlpha` default 0.72 -> 0.50 (config range
  lowered to 0.40-1.0), and the hint bar's own `Scrim` matches at 50% too. Old defaults (0.85 / 0.72)
  are added to the migration chain.
* **All text is white now** - secondary text too (`#E4E4E8` -> `#FFFFFF`). The hover feedback keeps
  working through a new dedicated hover colour (`TextHover #B4B4BA`) - if hover simply reused the
  now-white normal colour, the "text dims on hover" feedback would have silently vanished again.


**1.4.34**
* **Removed the tooltip box** (you asked for that) and restored the original hover feedback instead:
  hovering a tab or a list row now **dims its text** - that was the old behaviour, lost when the
  outlined-text refactor stopped the labels from seeing hover state. Hover is detected per row now.
* **The bottom-left unit info panel is dynamic now.** It had a fixed 190 px height, so with only a few
  lines of content there was a large empty block underneath (your screenshot). The panel now measures
  its content every frame and resizes to fit, and its bottom edge sits 14 px above the screen edge -
  the same as the squad list on the right, so the two blocks are symmetric.


**1.4.33**
* **Why the UI looked half-converted: your config file.** BepInEx writes a `.cfg` on first run and
  never rewrites it when the code default changes - so `colorBase` / `colorHover` in your file were
  still the values from a much older release (the military-green ones). Panels follow the shared
  palette and looked new, while the HUD buttons kept the old colour. There is now an automatic
  **migration**: if a value still matches one of the historical defaults (meaning you never edited it),
  it is updated to the current default. Values you set yourself are left alone.
* **Ghost preview now replaces every material slot.** The code assigned `renderer.sharedMaterial`,
  which only touches slot 0. Soldiers and vehicles are multi-slot models (body / gear / helmet /
  tracks), so everything after slot 0 kept its original material - which is why gear stayed coloured.
  All slots are filled now.
* **Stronger text outline.** The outline is drawn in four directions instead of two, and list row text
  is one point larger, so labels separate from the panel behind them.


**1.4.32**
* **Hover tooltips are back.** When the 1.4.30 pass switched controls from a text label to
  `GUIContent.none` (so the label could be outlined), it also removed the tooltip channel - so tabs and
  list entries showed nothing on hover. Controls now carry `new GUIContent("", tooltip)` (empty text,
  tooltip intact) and the panel draws its own tooltip box at the end of the frame.
* **Ghost preview fixed - it was rendering solid white.** The material used `Sprites/Default`, whose
  fragment is `texture x vertex colour`: it reads the **vertex colour**, not `_Color`. A mesh has no
  vertex colour, so `ghostMat.color` had no effect at all and every ghost came out opaque white. The
  shader chain now prefers `Particles/Standard Unlit` (unlit, uses `_Color`, supports alpha) with
  explicit Fade blending, and the ghost tint is darker and more transparent.
* **Unit info panel got a background and now lines up with the squad list.** The bottom-left readout
  had no plate at all (text straight over the terrain). Its left edge is now inset by the same amount
  the squad list is inset from the right, so both HUD blocks are symmetric.
* **One HUD plate for everything.** The bottom hint bar background you liked (solid black at 72%) is
  now a shared helper (`DrawHudPlate`) used by the bottom bar, the squad list and the unit info panel -
  same fill, same leather texture, same border. The HUD button colour default is the same black at 72%.


**1.4.31**
* **"Units still grab guns from across the map" - fixed properly this time, and the cause was language.**
  The 1.4.17 fix detected pickup entries by checking whether the interaction label starts with the
  Chinese word "拾起". In an English install the label is "Pick up ...", so the test always failed and
  the entry fell back to the native `Call()` - which has no distance check at all. The detection now
  uses **`Interaction.classType`** (the component that owns the interaction - for a ground item that is
  the item itself), which is language-independent, with a bilingual prefix check as a fallback.
* **Cursor is sharp now.** The cursor texture was 32x32 - the hardware-cursor size limit, so any system
  DPI scaling or fullscreen scaling stretched it into a blur. It is 64x64 now, which makes Unity use a
  software cursor drawn at screen resolution.
* **Cursor colours are back.** The 1.4.19 pass flattened every cursor state to greyscale (only enemy
  red and emplacement orange survived), which made it look like the cursor "stopped changing colour".
  States are colour-coded again: ally green, enemy red, driveable vehicle cyan, building grey-white,
  emplacement orange, interactable item yellow, default white.
* **The commander mod and Universal Generation now share one panel style**: same panel colour tokens,
  same warm-leather texture, same top highlight edge, and the HUD button outline now uses the shared
  border token instead of the text colour.


**1.4.30**
* **The text is outlined now instead of relying on bold.** `FontStyle.Bold` only works if the font
  ships a bold face - the game's font very likely has a single weight, in which case IMGUI silently
  ignores it (no error, no effect). That is why the previous "bold + bigger" pass looked identical.
  Row text and tab labels are now drawn as: dark offset copies first, then the white text on top -
  an outline does not depend on font variants and always takes effect. Row text also went up to
  `FontBody + 3`.
* **Panel colour now matches the bottom HUD bar, which is what you pointed at.** The surfaces lost
  their blue cast (all channels equal now) and panel opacity defaults to **0.72** - the same as the
  HUD bar - so the panel picks up the terrain the same way that bar does instead of looking cold.


**1.4.29**
* **Rows are bolder and one point larger.** Contrast was never the problem (white on #1A1A22 is
  about 15:1) - at 12 px Normal the strokes were simply too thin to *feel* bright on a dark surface.
  Bolding and enlarging the row text is what actually fixes that.
* **Every row now draws its separator.** Previously a line was drawn only *between* rows, so a list
  with a single entry showed none at all - which is why favourites looked like it had lost them.
  The favourites folder list had no separators at all; it has them now.
* **Favourite stars: 14 -> 17 px, bold, and a brighter gold (#FFE81A).** A small ★ glyph has very thin
  strokes, so it read as dim even though the colour was correct.
* **Surfaces lifted another step** (panel #1A1A22, title bar #26262F, control #32323C, row #24242C /
  #2E2E38) and the list backing is less opaque.


**1.4.28**
* **Panel lifted one step.** Surfaces are brighter across the board (panel #121218, title bar #1C1C24,
  control #26262E, row #1A1A20) while staying neutral black, and text stays pure white.
* **Alternating row backgrounds (zebra striping).** With small text and a 1 px divider alone, rows were
  hard to follow; adjacent rows now alternate between two shades, which is the single most effective
  readability fix for a list.


**1.4.27**
* **Translucency now applies to the background only.** Previously the border and divider alpha was
  driven by the panel-opacity setting too - so raising transparency also faded the outlines, which is
  where the contrast went. Faces (panel / title bar / rows / selected) follow `uiPanelAlpha`; text and
  lines are fully opaque.
* **Text and lines are white now** (#FFFFFF text, white borders and hairline dividers) instead of
  near-white grey, so they read clearly against the dark panel over any terrain.
* **Favourite stars were too dark** - cause found: `GUI.contentColor` and `GUIStyle.normal.textColor`
  **multiply**, and the star style used a grey text colour, turning the gold into a muddy #CAA84D.
  The style is pure white now, so the gold is exact (and brightened to #FFD800).
* **The help line at the bottom no longer overflows.** The real cause was horizontal, not vertical:
  the English string is ~99 characters, about 570 px at 12 pt, against ~450 px of panel width. It now
  uses a shorter string plus a dedicated style that auto-shrinks to fit the available width.


**1.4.26**
* **Root cause of the "close = small, far = big disc" look found.** Marker radii are **fixed world
  sizes** (measured from each unit's collider, clamped to 0.35-1.1 m for infantry and 1.6-4.2 m for
  vehicles), so a ring naturally shrinks with distance. Line width, however, was constant in *screen
  pixels* - which means its **world** width grew with distance: 1.5 px at 60 m is already about 0.10 m,
  and around 200 m it reaches ~0.34 m against a 0.6 m ring radius. The ring fills in and reads as a
  solid disc. Mixing a world-space size rule with a screen-space one is the actual bug.
* **Line width now uses fixed world units too** (infantry ring 0.032 m, brackets 0.042/0.060 m,
  focus ring 0.065 m, dashes 0.030-0.038 m, formation 0.048/0.060 m). Lines now scale with the markers
  they belong to - near is thicker, far is thinner, and the ratio stays constant at any camera
  distance. The trade-off is intentional: at extreme range a line thins below a pixel, which is what
  real perspective looks like. `[Markers] markerLineWidth` still scales all of them.


**1.4.25**
* **Marker line width reverted to the 1.4.23 values** (infantry ring 1.5, brackets 2.2/2.6, focus ring 3.0,
  dashes 1.7/1.8, formation 2.4/2.5). The "too thin" impression came from having zoomed the camera in
  very close: marker radii are fixed world sizes, so up close the ring fills a large part of the screen
  while the line stays a constant pixel width - and the line/hole ratio drops. Pixel-constant width is
  still correct across resolutions, but how thick it *feels* depends on the marker's own size on screen.
  If it still reads thin at close range, `[Markers] markerLineWidth` scales all of them.
* **Fixed the text at the very bottom running past the panel edge.** Mathematical padding was there,
  but only ~8px - which reads as "outside". Bottom padding is now 18px (top stays 8) and the help row
  grew from 34 to 38px.
* **Contrast pass.** The panel border is brighter (#555560) and thicker (~2px) so the panel has a clear
  edge even over bright stone/concrete; the selected fill is brighter (#52525E), row fills sit darker,
  row separators went from 10% to 15% and the list background is more opaque - so text, rows and the
  selected state each stand apart instead of blending into one grey field.


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

**1.4.56**
- Cover reach widened: `formCoverCorridor` default 6 → 10 m (0 = pure formation line, never use cover).

**1.4.55**
- Fixed: cover that existed was filtered out twice — the "no facing" fallback query re-ran the same facing filter that had already emptied the first query, so some walls never offered any cover.

**1.4.54**
- **The drag follows the cursor.** The pixel→metre ratio used the camera's height instead of the slant distance, making the arrow about 30 % short on tilted views; the line never reached where you dragged. Fixed, and `formDragSens` now defaults to 1 (1:1 with the cursor).
- Cover queries now sample along the whole formation line, so long lines find cover past the press point.
- Snapped cover spots keep a minimum gap — a dense sandbag wall can no longer compress your line.

**1.4.52**
- Formation orders no longer draw route dashes (the ghosts already showed the plan).
- Fixed: arrival was judged against the shared anchor instead of each soldier's own slot, so "everyone arrived" was never true and the route lines, the "Moving → x/N" readout and the target ring hung around for 45 seconds.

**1.4.50**
- **The formation preview now shows every infantry slot** — cover spots *and* line slots — as a ghost, refreshed every frame while you drag. Previously only cover spots had ghosts, which is why open ground looked empty and cover-rich ground looked crowded.
- Cover is now **slot-anchored**: a soldier only takes a free cover near his own slot, instead of the whole squad grabbing the nearest cover around the press point.

**1.4.49**
- New config `formDragSens`: formation drag sensitivity (1 = default, 1:1 with the cursor; lower = shorter and steadier).

**1.4.48**
- Panel opacity can finally go down to 0.40 — the shared UI toolkit clamped it to 0.55, so the whole 0.40–0.55 range was dead.

**1.4.39 – 1.4.47**
- Maintenance: weapon handling returned to the native pipeline (no more items teleporting into backpacks); the three root causes of grey-on-grey text fixed (colour space / sub-pixel glyph rendering / IMGUI state bleed); pixel-perfect text alignment completed; panel opacity defaults unified with Universal Generation.

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
