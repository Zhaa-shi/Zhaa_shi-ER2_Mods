ER2 Battlefield Commander v1.4.56
=================================

A BepInEx plugin for Easy Red 2 that adds an "RTS god-view squad command" layer — mouse operations inspired by Gates of Hell: Ostfront.

[1.4.56 Wider cover reach]
* `Control/formCoverCorridor` default 6 → **10 m** — how far a soldier's line slot may reach for a nearby
  free cover spot. Larger = more cover use; 0 = pure formation line; above 12 pulls the whole line onto
  the cover edge in cover-rich terrain.

[1.4.55 Cover that was there but never found]
* **Covers existed, the query killed them.** The "no facing" fallback re-ran the very facing filter that
  had already emptied the first query — so when a wall's cover points did not accept the current facing,
  available cover was always 0 no matter how many times it queried. Both passes now filter for themselves
  (destroyed / occupied / vehicle only), so the fallback actually falls back.

[1.4.54 The drag follows the cursor]
* **The arrow tip was systematically short.** The pixel→metre ratio used the camera's **height**, but a
  tilted god view needs the **slant distance** — at ~45° the arrow came out about 30 % short, so the line
  never reached where you dragged. Fixed; `Control/formDragSens` default is now **1** (1:1 with the
  cursor). Below 1 = slower/shorter, above 1 = faster.
* **Long lines find cover too**: the cover query now samples several points along the formation line
  instead of one circle at the press point (a single circle could never reach both ends of a long line).
* **Cover snapping no longer bunches soldiers up**: taken cover spots keep a minimum gap (~0.6× the line
  spacing, floor 1.5 m), so a dense sandbag wall cannot compress your line.

[1.4.53 Cover preview is back — and localised]
* Dragging a formation shows ghosts at the cover spots again, but snapping is now **anchored to each
  soldier's own slot** (default 3 m): only soldiers whose slot is already next to cover take it, everyone
  else stays on the line. (1.4.51 had switched snapping off entirely, which also killed the preview.)

[1.4.52 No more lingering dashed lines after a formation]
* **Formation orders no longer draw route dashes** — the ghosts already showed where everyone goes.
* **Fixed a 45-second hang**: arrival was measured against the shared anchor, but soldiers deploy to their
  own slots (up to half the line length away), so "everyone arrived" was never true and the route lines,
  the `Moving → x/N` readout and the target ring stayed up for the whole observation window. Arrival is
  now judged per soldier against his own slot — everything clears as soon as the last man is in place.

[1.4.50 The preview now shows every soldier]
* **Ghosts cover every infantry slot** (cover spots *and* line slots), refreshed every frame while you
  drag. Previously only cover spots had ghosts — which is why open ground looked "empty" and walls looked
  "crowded". The line you drag is the line you see.
* Cover switched from "grab the nearest cover around the press point" to **slot-anchored snapping**: a
  soldier only takes a free cover near his own slot; everyone else stays on the line.

[1.4.49 Formation drag sensitivity is a config knob]
* New `Control/formDragSens` — length multiplier for the drag arrow (1 = the old ratio, lower = shorter
  and steadier).

[1.4.48 Panel opacity finally reaches 50 %]
* The shared UI toolkit clamped `uiPanelAlpha` to 0.55–1.0 while the config allowed 0.40–1.0, so anything
  below 0.55 was silently raised — **the whole 0.40–0.55 range was dead**. Clamp fixed to 0.40–1.0.

[1.4.47 Pixel-perfect text alignment completed]
* The last hand-drawn `GUI.Label` / `GUI.Button` call sites (titles, faction row, item help, crew row,
  pager) now go through the shared toolkit, so no text sits half a pixel off.

[1.4.46 / 1.4.45 Grey-on-grey text: root causes fixed]
* Three separate causes of "the text still looks grey": colour-space conversion, sub-pixel glyph
  rendering, and IMGUI state bleeding between draws. All handled in the shared toolkit, so both this mod
  and Universal Generation benefit.

[1.4.44 / 1.4.43 Weapon handling returns to the native pipeline]
* Undid the 1.4.40–1.4.42 "weapons can only go into the backpack" interception — it also broke the native
  "pick up and place in the right hand". Backpack weapon entries now **walk there first, then run the
  native interaction** (no more teleporting items), and taking a weapon in hand uses the native
  `Soldier.PickUpItemFromInventory` with a held-weapon check and a fallback.

[1.4.39 Panel opacity defaults unified]
* Panel opacity default is 0.50 in both this mod and Universal Generation, with matching migration rules —
  the two mods can no longer render two different looks from the same setting.

[1.4.38 Correction: the background-free style applies to the hint bar, not the info panel]
* **Info panel background restored** - I misread your last message: "make it look like the top-left
  readout, no background" was about the **bottom hint bar**, not the info panel. The panel's plate is back.
* **The bottom hint bar lost its background instead**: white text with a shadow drawn straight over the
  terrain, same look as the top-left selection readout. The visibility rule (panel and hint bar take
  turns) is unchanged.


[1.4.37 Last grey text found and fixed; unit info goes background-free]
* **The hint bar text was the last grey holdout.** `HudStyleSmall()` and the backpack `tipStyle` still
  used a hard-coded military-green-era colour `(0.85, 0.9, 0.85)` - they never went through `uiText`,
  so the 1.4.36 text unification missed them (trap 113's "side door" again). Both now use
  `Er2Ui.Text`. A project-wide scan of every `MakeLabel`/`MakeButton` call confirms no non-white text
  colour remains.
* **The unit info panel lost its background**, matching the game's own top-left selection readout:
  white text with a shadow drawn straight over the terrain, no plate.


[1.4.36 Text colour unified to white; info panel and hint bar take turns]
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


[1.4.35 Overlap fix, 50% panel opacity, all-white text]
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


[1.4.34 Symmetric bottom HUD + hover feedback restored]
* **Removed the tooltip box** (you asked for that) and restored the original hover feedback instead:
  hovering a tab or a list row now **dims its text** - that was the old behaviour, lost when the
  outlined-text refactor stopped the labels from seeing hover state. Hover is detected per row now.
* **The bottom-left unit info panel is dynamic now.** It had a fixed 190 px height, so with only a few
  lines of content there was a large empty block underneath (your screenshot). The panel now measures
  its content every frame and resizes to fit, and its bottom edge sits 14 px above the screen edge -
  the same as the squad list on the right, so the two blocks are symmetric.


[1.4.33 Config migration, all ghost material slots, stronger outline]
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


[1.4.32 Hover tooltips back, ghost preview fixed, unified HUD plate]
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


[1.4.31 Ground pickup fixed for non-Chinese games, sharper cursor, unified UI]
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


[1.4.30 Outlined text + panel colour matched to the HUD bar]
* **The text is outlined now instead of relying on bold.** `FontStyle.Bold` only works if the font
  ships a bold face - the game's font very likely has a single weight, in which case IMGUI silently
  ignores it (no error, no effect). That is why the previous "bold + bigger" pass looked identical.
  Row text and tab labels are now drawn as: dark offset copies first, then the white text on top -
  an outline does not depend on font variants and always takes effect. Row text also went up to
  `FontBody + 3`.
* **Panel colour now matches the bottom HUD bar, which is what you pointed at.** The surfaces lost
  their blue cast (all channels equal now) and panel opacity defaults to **0.72** - the same as the
  HUD bar - so the panel picks up the terrain the same way that bar does instead of looking cold.


[1.4.29 Text weight + row separators + brighter still]
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


[1.4.28 Brighter panel + zebra rows]
* **Panel lifted one step.** Surfaces are brighter across the board (panel #121218, title bar #1C1C24,
  control #26262E, row #1A1A20) while staying neutral black, and text stays pure white.
* **Alternating row backgrounds (zebra striping).** With small text and a 1 px divider alone, rows were
  hard to follow; adjacent rows now alternate between two shades, which is the single most effective
  readability fix for a list.


[1.4.27 White foreground + fixed bottom text overflow]
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


[1.4.26 Marker line width back to world space (natural near-big / far-small)]
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


[1.4.25 Contrast pass + bottom padding]
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


[1.4.24 Neutral translucent black UI + thicker markers]
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


[1.4.23 Near-black leather panels + thicker markers]
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


[1.4.22 Fixes + dark-brown UI]
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


[1.4.21 Visual polish]
* **Line width is now defined in pixels, not world units.** `LineRenderer.widthMultiplier` is a
  world-space value, so a fixed number looked fat up close and hair-thin at range - and any
  "distance multiplier" fudge factor silently got 1.33x fatter at 1440p and 2x at 4K. Call sites
  now state a target width in 1080p pixels (1.1 thin ... 2.2 emphasis) and `Er2Ui.LineWidth`
  converts it to world units from the actual camera distance and FOV. Result: constant on-screen
  thickness at any distance **and** any resolution.
* **Selection brackets no longer read as arrowheads.** Root cause found in code, not guessed from a
  screenshot: the bracket root object was scaled to `radius`, and `LineRenderer` width is
  multiplied by the parent's `lossyScale` - so on a vehicle (radius up to 4.2) a 0.1 m line became
  ~0.5-1.0 m thick while the corner arms are only ~1 m long. Two fat arms merged into a solid
  triangle. The bracket root now stays at scale 1 and the radius is baked into the vertices.
  Corner arms also lengthened (0.34 -> 0.42 of the radius) so the L shape stays legible.
* **World markers moved from transparency to greyscale.** Semi-transparent grey (alpha 0.26-0.48)
  gets eaten by grass, snow and sand - the colour drifts with whatever is behind it and edges go
  soft. All world markers are now near-opaque greyscale (alpha >= 0.80); hierarchy comes from
  lightness (#9AA1A8 -> #C6CBD0 -> #E2E6EA -> white) instead of alpha. Only the ghost preview
  keeps its translucency, which is what makes it read as a preview.
* **Panels got actual structure, not just colour blocks.** Every surface used to be a flat fill, so
  "hierarchy" was carried by brightness alone and collapsed on a bright battlefield. Added three
  primitives - `Frame` (outline), `HLine` (divider) and `AccentBar` (selection stripe) - plus a
  separate title-bar fill, inset list borders and a left stripe on favourite rows. Hierarchy is now
  face + line + bar instead of face only.
* **Palette pulled back to mid-dark grey.** The 1.4.20 lift went too far (feedback: "too light").
  Panel #14181D, title bar #1F252C, control #262D35, hover #333B45, selected #46505C - stepped
  8-14 levels apart, which is what makes the new outlines read.
* **Dead code removed:** the old `WidthScale` heuristic, an unused per-frame `float[4]` allocation
  in the bracket path, and two orphaned resolution-cache fields.

[1.4.20 UI]
* **Adaptive UI (no manual scaling).** Every panel, HUD line, hint bar, backpack grid, tooltip
  and context menu now follows the **game's own UI size setting** (`ResourcesManager.ResolutionMult`),
  with a screen-resolution fallback when that cannot be read. Change the UI size in the game
  options and both this mod and Universal Generation rescale together — nothing to drag, no extra
  number fields, no config entry to fiddle with. Range is clamped to 0.75x – 1.6x.
* **The panels are no longer "dead":** size and font tokens in `Shared/Er2Ui.cs` were compile-time
  constants, so a resolution change could never move them. They are now scale-driven properties.
  Styles rebuild automatically when the scale shifts (each panel checks its own last scale, so
  panels can no longer cancel each other's rebuild).
* **Screen-fit for wide fixed-width elements.** The bottom hint bar (previously a hard-coded
  1400 px) now converges into the available screen width, so it no longer spills off a 1366-wide
  display.
* **Grey-black palette lifted.** The previous step was too dark — dark surfaces compressed into one
  another against a bright battlefield. Panel / title bar / control / row / selected now step
  +8 to +14 brightness levels apart, so the hierarchy reads without going back to military green.
* **Universal Generation's leftovers de-greened:** carry badge background, drag target ring
  (was bright green, now white) and the flash text colour are on the shared palette too.

[1.4.19 UI]
* **Grey-black mono UI.** Every panel, list and button now uses a neutral grey-black palette
  (`Shared/Er2Ui.cs`) instead of the old military green — hierarchy comes from brightness, not hue.
  Set `UI/uiMono=false` to fall back to the old green preset.
* **World-space markers redrawn.** Selected-unit marker changed from four 45-degree arcs
  (which read like a weather symbol) to **right-angle corner brackets**, the standard RTS language.
  All ground markers (friendly rings, focus ring, move target, route/boarding lines, formation
  markers) are now white / semi-transparent grey, separated by **opacity tier + dash rhythm + shape**.
* **Route vs boarding lines finally distinguishable** — they used the identical colour literal before.
  Route = dim long dashes, boarding = brighter short dashes.
* Marker pulse phase is now offset per key (previously every marker breathed in perfect sync).
* Line width compensates for camera distance (LineRenderer width is in world units and thinned out at range).
* Name plates got a dark backing plate for readability over snow/sky.
* Cursor states are greyscale (only Enemy stays red, Emplacement stays orange).
* **14 new visual config switches** under `[Markers]` + `[UI]` so everything above can be toggled:
  markersEnabled / showFriendlyRing / showSelectedBracket / showFocusRing / showMoveTarget /
  showPathLines / showFormationMarkers / showNamePlates / markerPulse / markerScale / markerLineWidth /
  markerThroughWall / markerColorMode (Mono|Semantic) / uiMono.

[1.4.18 internal]
* No gameplay or visual change. The HUD, info panel and backpack now build their text styles through
  the shared UI toolkit (`Shared/Er2Ui.cs`) that Universal Generation also uses, instead of each file
  hand-rolling a `GUIStyle`. Same fonts, alignment and colours as before — this is groundwork so both
  mods can be redrawn into one coherent look.

[1.4.17 fix]
* Picking up **weapons** from the ground no longer teleports them into a soldier's inventory.
  Ground weapons are multi-interaction items (their native menu has entries like "Pick up (right
  hand)"), so right-clicking them opened the interaction menu — and choosing a pickup entry there
  executed the native interaction **at any distance** ("pickup across the map"), while plain items
  sent the nearest soldier walking. Menu pickup entries now join the exact same flow as plain
  items: instant within the link radius, otherwise the nearest selected soldier walks there and
  picks the weapon up on arrival. Other native interactions (ammo refill etc.) are unaffected.

[1.4.16 fix]
* Add-on mods (e.g. Universal Generation) can now declare that their full-screen gesture mutex is
  a placement/drag gesture rather than a UI panel (`externalCameraPass` hook). While such a gesture
  is active the camera keeps responding to wheel/MMM input; clicks are still swallowed, so the
  placement click can no longer leak into native selection/orders.

[1.4.15 fix + compatibility]
* God view no longer responds to camera input while the native settings/pause menu (Esc) is open.
  WASD pan, wheel zoom, MMB rotate and Q/E height used to keep working behind the menu — most
  visible as "the camera still zooms when I scroll while the settings are open".
  Rule: the menu freezes all camera input; when the pointer merely sits on one of our own panels
  (squad list, info panel, backpack window, another mod's panel) only mouse-driven camera input
  (wheel / MMB) is frozen, keyboard WASD/Q-E keeps working.
* Compatibility with Advanced Combat Movement (Responsive Orders): that mod installs a prefix on
  Squad.SetHoldFireOrder(false, ...) and swallows the call when the squad leader is the soldier
  you are controlling — squads could stay on hold-fire forever after a move order.
  Restoring fire is now "call -> read back -> if still holding, write the native holdFire field
  directly" (field writes are not affected by Harmony prefixes). No behaviour change without that mod.

[1.4.14 performance]
* Friendly-squad enumeration is cached for 0.3s. The previous implementation walked every
  creature in the scene and type-checked each one; it was called repeatedly in a burst when
  entering RTS, taking command, or saving groups — the main source of the hundreds-of-ms
  hitching felt on large battlefields.
* Resolution multiplier, main camera, the player's controlled soldier, and the own-side faction
  string are now short-lived caches. They were previously re-read via interop on every frame —
  and in the per-soldier-per-frame patches, tens of thousands of times per second on a busy map.
* Per-frame selection markers no longer allocate temp lists or do per-unit component lookups.
* Behaviour is unchanged: the caches only accelerate; every decision still resolves against live state.

[Enter / Exit]
* F9: enter god view (mainly to enter RTS; press F9 again for emergency exit when all friendly squads are wiped).
* Normal exit: box-select/choose units to take over → click the top-center [Take Command] button to possess a random living member, back to first person.
* Move, focus-fire, boarding and vehicle-move orders issued in RTS keep executing after you leave RTS.

[God View Controls (Gates of Hell style)]
* WASD move, wheel zoom, hold MMB rotate, Q/E height.
* LMB click friendly soldier/vehicle = select (infantry = single soldier, vehicle = whole crew); LMB on enemy/empty = clear selection.
* Double-click a friendly soldier = select their entire squad.
* Hold LMB to box-select = temporary selection of friendlies inside the box; native squads are never split automatically.
  Only the top [Split] button creates a real new squad; vehicle crews stay together as one unit; Shift+box = append.
* RMB short press = order: ground = move (selected units only), enemy = persistent focus-fire mark (fire priority only, no auto-advance),
  friendly/neutral vehicle or fire position = board/enter directly (no menu), friendly soldier inside a vehicle = board as reinforcement,
  **friendly soldier on foot = nearest selected soldier walks over and both backpacks open on arrival (v1.3.4)**,
  **ground item = its native interaction menu when it has several (ammo box: refill ammo; weapons: "pick up" entries
  now walk there like plain items instead of teleporting, v1.4.17), otherwise it is picked up —
  instantly within the link radius, or the nearest selected soldier walks there and picks it up on arrival (v1.4.1)**,
  building/house = selected infantry enter and take cover inside, empty ground = move.
* RMB double-click (same spot within 0.6s) = native "Move & Defend" (HoldArea): one native order per selected squad, no extra orchestration.
* RMB long press (0.35s) + drag = formation arrow (Gates of Hell style): the formation line is centered on the press point,
  perpendicular to the drag, as long as the drag; units face along the arrow.
  - Where native cover points exist near the target (sandbags, walls, Combat Cover props...), infantry are assigned to cover
    one by one and walk in natively (they take the cover's suggested stance and facing by themselves); units without cover
    spread along the line. While dragging, white translucent ghost models preview every assigned cover spot, plus line/vehicle
    slot markers. If ghost cloning fails the preview auto-degrades to markers only.
  - Vehicles always take line slots, drive to them, then pivot to the arrow direction after arriving — this supersedes the
    old vehicle facing drag.
  - A very short drag (< 1 m) behaves like a plain move order.
* Command hotkeys (all rebindable in the cfg [Hotkeys] section): Z stand / X crouch / C prone / V halt / B hold-fire toggle /
  N cover nearby (around selection center) / M rally on squad leaders / F scatter into cover / G backpack window.
* Route lines: after a move or boarding order, a thin grey dashed line is drawn from every marching unit to its target
  (boarding lines follow the target vehicle in real time) and disappears when everyone arrives.
* Ctrl+1~9 = save current selection as a group; 1~9 = recall group (dead units auto-pruned, replaces selection).
* Dismount = all selected vehicle crews dismount (and walk away, so they don't instantly re-board); now a [Dismount] button in the bottom-left info panel.
* Space = pause/resume the world (camera still moves while paused).
* Bottom-right [Squad List] = number + symbols: one □ per armored unit, one ○ per infantry (□ always before ○).
  Click selects the squad, double-click selects and flies the camera there.
* Bottom-left info panel: focused unit name/class, health bar, stance, suppression level;
  ◀ ▶ cycles through selected units. [Backpack] / [Cargo] opens a grid backpack window (see below).
  Right-click a soldier — any part of him counts, helmet and gear included (v1.4.4), and **near misses count too**:
  from a zoomed-out view the click often lands on the ground beside him (v1.4.5 tolerance 2.5 m) — a friendly on foot
  = both backpacks after walking over, a corpse = its backpack after walking over (since v1.4.11 the selected
  unit's backpack opens at the same time as well); right-click a ground item = the
  nearest selected soldier picks it up (instantly within the link radius, otherwise he walks there first).
  When vehicles are selected the panel also carries
  [Dismount] / [Repair] buttons (the right-click interaction ring was removed; boarding is direct right-click).
  Immobile fire positions/artillery (no driver chain) take formation facing only — long-press RMB anywhere with them selected
  pulls an arrow straight out of the emplacement and pivots it in place.
* Custom cursor (cfg customCursor / cursorStyle): a hollow translucent ring whose colour tells you what you are pointing at —
  white = default, green = friendly, red = enemy, cyan = vehicle, yellow = building, orange = emplacement, light blue = interactable (items and corpses).
  cursorStyle = Circle (default) / Arrow / Cross.
* Left-click also selects non-unit interactables (empty vehicles, droppable items).
* Ordering with nothing selected shows a hint instead of commanding all friendlies by mistake.
* FPS WASD, mouse look, shooting and native controls are never taken over; after switching back to FPS, issued RTS tasks keep running in the background.

[Backpack Windows (v1.3.4, Minecraft-style grid)]
* Open: [Backpack] button in the info panel (focused soldier), [Cargo] button (focused vehicle),
  the G hotkey, **right-click a corpse**, or **right-click a friendly soldier on foot** (both packs open).
  Anything beyond the link radius: the nearest selected soldier walks there first, the window(s) open on arrival.
  **v1.4.9: the window also opens as soon as the walker stops** — it no longer has to land inside the 3 m ring
  (stopping within 5 m is enough, actual distance is logged).
  **v1.4.10: the window title no longer collides with the weight readout** (auto-shrink font, drop the
  "Backpack · " prefix, then ellipsize). **v1.4.10: the soldier sent over is always picked from the
  on-foot selection** — when the whole selection is riding a vehicle the errand is handed to the nearest
  friendly soldier on foot instead, and passengers are never mistaken for walkers. Changing the selection no longer aborts a meeting
  in progress; only entering RTS / taking control of a unit / leaving RTS cancels it.
  Several windows can be open at once; each window has a draggable title bar, a weight bar,
  6x4 item cells per page with page flipping, and a close (x) button.
* **Right-click a cell = the game's own interaction menu** (v1.4.0): eat food, use medical items, discard, unequip...
  plus a **Wear** entry (v1.4.2) for weapons (load & wield) and uniform/vest/helmet (official Lua wear calls) —
  the game's own interaction pipeline doesn't offer equip actions to non-player interactors, so those are issued
  through the soldiers' official scripting channel.
  **v1.4.12: the item's live instance is resolved before the official wear call, and if the call leaves the
  soldier unchanged the mod writes the native fields directly** (`headgear_ref/headgear_Obj/uniform_ref/uniform_Obj/vest_ref/vest_Obj`
  + `SetHelmetObject` / `TriggerClothingObjRefresh`); native wear entries that silently no-op for non-players are
  pruned from the menu, and every wear logs a `before[...] after[...]` snapshot. The menu is modal (v1.4.1): nothing underneath reacts while it
  is open; click elsewhere or ESC closes it.
* Windows tile into free screen slots (v1.4.3) instead of overlapping; meeting-arrival windows bypass the anchor
  check (the walk already proved proximity) and are exempt from the link-radius auto-close for 30 s.
* Backpack windows close when the selection changes or is cleared (v1.4.1); while a window is open, left-clicking
  empty ground does NOT clear the selection (protects the drag/interact workflow).
  Clearing the selection does **not** abort an in-flight "send a soldier over" meeting (v1.4.9) — the walk
  continues and the window still opens on arrival / on stop.
  **v1.4.11: closing a window (x), clicking a menu entry or finishing a drag never deselects your units** — those
  clicks are swallowed whole, otherwise the mouse release lands one frame after the window vanished and counts
  as a click on empty ground (= clear selection).
* Cells merge same-type items into one slot (xN = total, e.g. all mags of one kind share a cell) and are sorted by
  category (weapons / ammo / explosives / medical / gear / other). Items show the game's own icon, a count badge and a
  hover tooltip (name, count, weight). Equipped/held items get their own grey-bordered cell.
* Mouse drag moves the whole group: drop onto another cell to move / swap it with the group there (weight-checked),
  drop outside all windows to throw it on the ground — equipped/held weapons can also be picked up and thrown away
  this way (they are unequipped through the native path), but can't be placed back onto cells. Right-click or ESC cancels a drag.
* Anchor rule: the first opened window is the anchor. Other packs can only be opened and stay open within the
  link radius (cfg packRange, default 3 m) of the anchor — windows beyond it are closed automatically.
* Everything goes through the soldiers' real inventories: AI reacts normally (they can run out of ammo you took away).

[Config] BepInEx\config\er2.squadcommand.cfg
* enabled: master switch.
* moveRadius: move arrival / defend radius, default 8 m.
* packRange: backpack link radius (m), default 3 — the first opened pack is the anchor; if a walked unit stops short
  of it the mod re-issues the move (up to 8 times) until he gets inside.
* ghostPreview: white translucent unit preview while dragging formations, default on (auto-degrades to markers if cloning fails).
* formDragSens: formation drag sensitivity, default **1** = the arrow tip lands on the ground point under
  the cursor; below 1 = shorter and steadier, above 1 = faster.
* formCoverCorridor: how far a soldier's formation slot may reach for a nearby free cover spot, default
  **10 m** (0 = pure formation line, never use cover; above 12 pulls the whole line onto the cover edge in
  cover-rich terrain).
* customCursor: custom cursor in RTS, default on. cursorStyle: Circle (default) / Arrow / Cross.
* debugLog: debug logging, **default off (keep it off in releases)**. Turning it on emits command / boarding /
  mark / formation / **backpack & wear** diagnostics (e.g. `穿戴 id=… 生效级=… 前[…] 后[…]`, `原生菜单项 …`,
  `会合进行中 dist=…`) — flip it on first when reporting a problem.
* godKey: god view key, default F9.
* Hotkeys section keyStand/keyCrouch/keyProne/keyStop/keyHoldFire/keyCover/keyRally/keyScatter/keyPack: command hotkeys, all rebindable.
* UI section colorBase/colorHover/colorText: UI theme colors (hex, defaults are translucent dark greens),
  applied live (button fill / border / text / friendly foot rings / selection brackets).

[Install]
1. Install BepInEx (IL2CPP version) into the game root folder.
2. Drop ER2_BattlefieldCommander.dll into <game root>\BepInEx\plugins\.
3. Launch the game — "Loading [ER2 Battlefield Commander 1.4.56]" in the log means success.

[Coexisting with Advanced Combat Movement (Responsive Orders)]
* Built-in compatibility: restoring fire is now "call -> read back -> write the native holdFire field
  directly if still holding", so its prefix can no longer swallow the call (see 1.4.15).
* No key collision: its F (marker/follow), H (hold fire) and F1 (restart mission) run inside the native
  PlayerController.Update, which this mod skips while god view is active — so in RTS F = scatter (ours),
  in first person F = its marker command. Nothing overlaps.
* It does not respond inside god view (same reason: RTS owns input) and resumes when you leave RTS.
* Its own AI behaviours (Squad Defensive Hold / Danger Memory / Artillery Evasion) issue **their own**
  move orders and can override yours — that is a design-level conflict, so turn those options off in its
  settings page if your commands keep getting re-issued.

[Notes]
* Entering god view detaches every friendly squad from native task pulling; takeover auto-closes leftover native "select squad" panels.
* Cursor locking is patched in god view (native cannot lock/hide the cursor), and native controllers that fight the camera are disabled — no more flicker/recentering.
* Full native squads use the game's own Squad move command; partial or cross-squad selections fall back to per-soldier orders.
* Move orders have priority over auto-engagement: while marching, units hold fire (native has no priority knob), engagement resumes on arrival/timeout/new order.
* Boarding completion detection, vehicle sync retry and persistent focus-fire keep running after leaving RTS; boarding is fully native
  (board order issued at command time, walking/seating handled natively) — the mod only watches completion and re-issues on timeout.
  Taking over a unit only stops that unit's own mod tasks.
* Formation cover orders use the game's native cover system (CoverManager / per-soldier findCover): vanilla cover slots and
  content-mod cover props (e.g. Combat Cover) both work, no compatibility shim needed.
