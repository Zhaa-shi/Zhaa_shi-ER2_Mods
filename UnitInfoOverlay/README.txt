ER2 Unit Inspector v1.0.4
=========================

Developer-oriented unit status overlay for Easy Red 2: compact, color-coded
info panels float above units (or follow your crosshair) showing live unit
state at a glance.

Features
--------
- Info lines: name ([YOU]/[AI]/[DEAD]), HP (current / observed max /
  incapacitation threshold), state tags (BLEED/DOWN/SURR/SPRINT/RUN/MOVE/
  CRAWL/AIM/RELOAD/THROW/FIRE/VEH/CARRY/CARRIED/TALK/STAM!/WATER), pose
  (Idle/Crouch/Prone), faction, role (MEDIC/GUNNER/AT/SAPPER/MARKSMAN/
  RADIO/LEADER), position + distance, smoothed velocity, instance ID + pointer.
- Two display modes: Targeted (default - only the nearest unit to the
  crosshair, highlighted with a double border) or All (every unit in range,
  capped by maxUnits).
- Color coding: allies green, enemies red, your unit light blue, corpses
  gray; HP color scales green -> yellow -> red; bad states (BLEED/DOWN/
  SURR/FIRE) turn the state line red.
- F10 toggles the overlay (rebindable); top-left summary line shows the
  toggle state, unit counts and range.
- Every info line can be enabled/disabled individually.
- Hidden automatically while the settings/pause menu is open, while dead
  or waiting to respawn, and for the first seconds after a scene loads
  (hideAfterLoad, covers the loading-screen tail).
- Respects the "Hide Anything" F5 HUD-hide contract (per-mod toggle).
- Performance: 10 Hz refresh throttle (Time.unscaledTime, pause-safe),
  distance culling, in-front-of-camera and off-screen culling, unit cap;
  iterates Creature.allCreatures (no FindObjectsOfType).
- Available in English and Chinese builds (overlay content follows the
  build language).

Installation
------------
1. Install BepInEx 6 (IL2CPP) for Easy Red 2 and run the game once so the
   interop assemblies are generated.
2. Drop ER2_UnitInfoOverlay.dll into <Game>\BepInEx\plugins\.
3. (Optional) Use ER2 Mod Manager to tweak settings in-game.

Configuration
-------------
Edit BepInEx/config/er2.unitinfooverlay.cfg or use the in-game Mod Manager:

[General]
  enabled        Master switch.
  toggleKey      Overlay toggle key (default F10).
  displayMode    Targeted (default) / All.
  showAlive      Show alive units.
  showCorpses    Show corpses (default on).
  showPlayer     Also show the unit you control (right at the camera).
  maxDistance    Max display distance in meters (default 60).
  maxUnits       Max units drawn per frame (default 1; raise it for All mode).
  fontSize       Overlay font size (default 12).
  bgOpacity      Label background opacity, 0 = text only (default 0.55).
  aimRadius      Crosshair snap radius in pixels for Targeted mode (default 160).
  showSummary    Top-left summary line (default on).
  hideAfterLoad  Hide for the first N seconds after a scene loads (default 6).
[Info Lines]
  showHp / showState / showPose / showFaction / showRole / showPos /
  showVelocity / showId - each info line can be toggled
  (role and velocity are on by default).

State tags (all read live from the game; only DOWN is inferred):
  BLEED bleeding | DOWN alive with HP below the incapacitation threshold
  (inferred from HP) | SURR surrendered | SPRINT sprinting | RUN running |
  MOVE moving | CRAWL crawling | AIM aiming | RELOAD reloading | THROW
  throwing | FIRE on fire | VEH on a vehicle | CARRY carrying a body |
  CARRIED carried | TALK talking | STAM! out of stamina | WATER in water.

Notes
-----
- The HP "max" is an observed maximum (the game does not expose a max-HP
  field through interop): it starts at the first HP value seen for a unit
  and tracks the highest value since.
- Developer tool: low-frequency functional logs only.
