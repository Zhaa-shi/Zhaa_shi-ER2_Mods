ER2 Battlefield Commander v1.1.0
=================================

A BepInEx plugin for Easy Red 2 that adds an "RTS god-view squad command" layer — mouse operations inspired by Gates of Hell: Ostfront.

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
* RMB short press = order: ground = move (selected units only), enemy = persistent focus-fire mark (fire priority only, no auto-advance), friendly/neutral vehicle = open interaction ring.
* RMB double-click (same spot within 0.6s) = native "Move & Defend" (HoldArea): one native order per selected squad, no extra orchestration.
* RMB hold + drag with vehicles selected (drag past 14px within 0.35s) = vehicle facing drag (Gates of Hell style):
  an arrow from every selected vehicle follows the cursor; release pivots each hull in place toward the release point.
  Turn rate follows the vehicle class (tanks turn slower than wheeled vehicles). Holding still opens the command ring as before;
  pressed on a unit the facing drag never engages.
* Route lines: after a move or boarding order, a thin grey dashed line is drawn from every marching unit to its target
  (boarding lines follow the target vehicle in real time) and disappears when everyone arrives.
* RMB long press (0.35s) = command ring: on empty ground → Stand / Crouch / Prone / Halt / Cover / Rally / Hold Fire (toggle) / Scatter;
  pressed on a unit the ring never opens — release issues the short-press order (friendly/neutral vehicle = Board / Dismount / Repair). Space pauses.
* Ctrl+1~9 = save current selection as a group; 1~9 = recall group (dead units auto-pruned, replaces selection).
* Interaction ring [Dismount] = all selected vehicle crews dismount (and walk away, so they don't instantly re-board).
* Space = pause/resume the world (camera still moves while paused).
* Bottom-right [Squad List] = number + symbols: one □ per armored unit, one ○ per infantry (□ always before ○).
  Click selects the squad, double-click selects and flies the camera there.
* Ordering with nothing selected shows a hint instead of commanding all friendlies by mistake.
* FPS WASD, mouse look, shooting and native controls are never taken over; after switching back to FPS, issued RTS tasks keep running in the background.

[Config] BepInEx\config\er2.squadcommand.cfg
* enabled: master switch.
* moveRadius: move arrival / defend radius, default 8 m.
* dragFacing: vehicle facing drag toggle, default on (off = RMB long press always opens the command ring).
* debugLog: debug logging, default off.
* godKey: god view key, default F9.
* UI section colorBase/colorHover/colorText: UI theme colors (hex, defaults are translucent dark greens),
  applied live (button fill / border / text / friendly foot rings / selection brackets).

[Install]
1. Install BepInEx (IL2CPP version) into the game root folder.
2. Drop ER2_BattlefieldCommander.dll into <game root>\BepInEx\plugins\.
3. Launch the game — "Loading [ER2 Battlefield Commander 1.1.0]" in the log means success.

[Notes]
* Entering god view detaches every friendly squad from native task pulling; takeover auto-closes leftover native "select squad" panels.
* Cursor locking is patched in god view (native cannot lock/hide the cursor), and native controllers that fight the camera are disabled — no more flicker/recentering.
* Full native squads use the game's own Squad move command; partial or cross-squad selections fall back to per-soldier orders.
* Move orders have priority over auto-engagement: while marching, units hold fire (native has no priority knob), engagement resumes on arrival/timeout/new order.
* Boarding completion detection, vehicle sync retry and persistent focus-fire keep running after leaving RTS; boarding is fully native
  (board order issued at command time, walking/seating handled natively) — the mod only watches completion and re-issues on timeout.
  Taking over a unit only stops that unit's own mod tasks.
