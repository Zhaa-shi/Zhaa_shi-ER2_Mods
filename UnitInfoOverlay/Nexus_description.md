# ER2 Unit Inspector (Floating Unit Status Overlay)

A developer-oriented debugging tool for Easy Red 2: a floating status overlay drawn above every unit (or just your target), showing live unit state at a glance.

## Description
While developing/experimenting with unit behaviour, it helps to *see* what the game thinks a soldier is doing. This mod draws a compact, color-coded info panel above each unit in view:
- Name (with `[YOU]` / `[AI]` / `[DEAD]` tags)
- HP: current / observed max / incapacitation threshold (`thr`)
- State tags: `BLEED SURR SPRINT RUN MOVE CRAWL AIM RELOAD THROW FIRE VEH CARRY CARRIED TALK STAM! WATER` and `DOWN` (alive with HP below the incapacitation threshold)
- Pose (Idle / Crouch / Prone), faction string, role tags (MEDIC/GUNNER/AT/SAPPER/MARKSMAN/RADIO/LEADER)
- World position + distance, smoothed velocity (m/s), instance ID + native pointer
- Top-left summary line: toggle state, unit counts, draw range

Colors: allies green, enemies red, your unit light blue, corpses gray; HP bar color scales green → yellow → red.

## Installation instructions
1. Install BepInEx 6 (IL2CPP) for Easy Red 2 and run the game once so the interop assemblies are generated.
2. Drop `ER2_UnitInfoOverlay.dll` into `<Game>\BepInEx\plugins\`.
3. (Optional but recommended) Install ER2 Mod Manager to tweak settings in-game.

## Main features
- Two display modes: **All** (every unit within range, capped by `maxUnits`) or **Targeted** (default — only the nearest unit to the crosshair, highlighted with a double border).
- Toggle hotkey (default **F10**, rebindable, incl. via ModManager).
- Every info line can be toggled individually (`showHp/showState/showPose/showFaction/showRole/showPos/showVelocity/showId`).
- Distance culling, in-front-of-camera check, off-screen culling, per-frame unit cap — safe for large battles.
- 10 Hz refresh throttle using `Time.unscaledTime` (pause-safe), drawing from a cached layout — no per-frame `FindObjectsOfType` (uses `Creature.allCreatures`).
- Respects the "Hide Anything" F5 HUD-hide contract (separate per-mod toggle in ModManager).

## Requirements
- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs
- Easy Red 2 community and BepInEx team.
