ER2 Battlefield Commander v1.4.21
=================================

A BepInEx plugin for Easy Red 2 that adds an "RTS god-view squad command" layer — mouse operations inspired by Gates of Hell: Ostfront.

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
3. Launch the game — "Loading [ER2 Battlefield Commander 1.4.18]" in the log means success.

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
