# ER2 Battlefield Commander — RTS God-View Squad Command

## Description
Adds an "RTS god-view squad command" layer to Easy Red 2, with mouse operations inspired by Gates of Hell: Ostfront. Press F9 to enter a free god view overlooking the battlefield, hold LMB to box-select friendly units as a temporary selection — native squads are never split automatically; only the explicit [Split] button creates a real new squad. Vehicle crews stay together as one selectable unit. Right-click issues move, focus-fire mark, boarding and vehicle movement orders; with vehicles selected, hold-and-drag RMB pivots their hulls toward the release point. Thin grey route lines show where commanded units are heading until they arrive. Space pauses the world for unhurried command. Orders already issued — movement, focus-fire, boarding and vehicle sync — keep executing after you switch back to first person; FPS movement, aiming and shooting are untouched. Takeover puts you into a random living selected member to keep fighting.

## Installation instructions
1. Install BepInEx (IL2CPP build) into the game root folder.
2. Put `ER2_BattlefieldCommander.dll` into `Easy Red 2\BepInEx\plugins\`.
3. Launch the game — `Loading [ER2 Battlefield Commander 1.1.0]` in the BepInEx log means success.

## Main features
- **God-view command**: F9 to enter free camera, WASD move, wheel zoom, MMB rotate, Q/E height, Space to pause/resume the world.
- **Temporary box-select & explicit split**: box-selecting only changes the current selection, never splits squads; the top [Split] button is the only way to create a real new squad; Shift+box = append.
- **Gates of Hell style mouse interaction**:
  - LMB on a friendly soldier/vehicle = select (a vehicle crew counts as one unit); LMB on enemy/empty = clear selection
  - Double-click a friendly = select their whole squad
  - RMB short press = order (ground = move; enemy = persistent focus-fire mark (fire priority only, no auto-advance — use move or double-right-click to advance); friendly/neutral vehicle = interaction ring); RMB double-click = native "Move & Defend"; long press on empty ground = command ring (Stand/Crouch/Prone/Halt/Cover/Rally/Hold Fire toggle/Scatter); pressed on a unit, the ring never opens
  - RMB hold + drag with vehicles selected = **vehicle facing drag**: an arrow follows the cursor, release pivots each hull in place toward it — tanks turn slower than wheeled vehicles; holding still still opens the command ring
  - **Route lines**: after a move or boarding order, every marching unit draws a thin grey dashed line to its target (boarding lines follow the vehicle), cleared on arrival
  - Ctrl+1~9 save the current selection as a group; 1~9 recall (dead units auto-pruned)
  - Only units with the ◆ cursor (selected) respond to orders — unselected units stay put
- **Vehicles & emplacements**: click a vehicle/fire position to select the whole crew (no need to click the men inside); right-click a friendly/neutral vehicle for the interaction ring, per-man boarding; dismount/repair through the ring.
- **Random takeover**: with units selected, click the top `[Take Command]` button to possess a random living member back into first person; F9 emergency exit when everyone is wiped.
- **RTS/FPS coexistence**: after switching back, FPS WASD, mouse look, shooting and native controls keep working; movement, focus-fire, boarding and vehicle-sync tasks issued in RTS survive leaving the view. Taking over a unit stops only that unit's own persistent tasks.
- **Native command chain**: move/defend/boarding go through the game's own command entries (Squad.moveTo/HoldArea, boardVehicle); focus-fire marking rides the native target selection (GetBestVisibleEnemy/CurrentVisibleTarget) — the mod builds no movement orchestration of its own, it only issues orders and observes completion.
- **Cursor flicker fixed**: in god view the mod patches `Cursor.set_lockState`, so the game can no longer lock/hide the cursor — no more flicker or cursor snapping to screen center on right-click.
- **Native interference suppressed**: native task pulls on your side's squads are released on entry; the native "choose teammate" leftover panel is closed automatically on takeover.

## Requirements
- Easy Red 2
- BepInEx (IL2CPP build)

## Shout outs
Thanks to the Easy Red 2 community and the BepInEx / Harmony ecosystem, and to all authors whose IL2CPP modding work paved the way.
