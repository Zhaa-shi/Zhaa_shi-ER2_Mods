# ER2 Veteran HVT v1.2.2 — Veteran Levels, Marked Men & Focus Fire

## Description
Real battles are won by a handful of exceptional men — and in this mod, exceptional men get **marked**. Every kill on the battlefield is tracked; the more a unit kills, the higher its **veteran level** rises and the deadlier it gets. Veterans are marked as **High-Value Targets (HVT)**: the side they've been slaughtering focuses its fire on them, and putting them down rewards you with clear feedback.

## Installation instructions
1. Requires BepInEx 6 (IL2CPP) for Easy Red 2.
2. Drop `ER2_VeteranHVT.dll` into `Easy Red 2\BepInEx\plugins\` and keep the `ER2_VeteranHVT` folder next to it (it contains the kill-confirm sound).
3. Launch the game once, then configure in the in-game Mod Manager page (recommended) or edit `BepInEx\config\er2.highvaluetarget.cfg`.

## Main features
- **Kill attribution & veteran levels:** bullets, grenades/explosions, melee and even helmet headshots are attributed per soldier (hit recorded before the damage is applied, so instant kills count too). Every 5 kills (default) = +1 level, up to the level cap. Levels reset at battle end and on your death.
- **Stolen-kill protection:** if you hit a unit within the last 5 seconds, the kill counts for you even if someone else landed the final shot — no more losing credit in chaotic firefights.
- **Level-based buffs:** each level scales damage taken (×0.85), damage dealt (×1.18), accuracy (×1.30), fire rate (×1.15), raise speed (×1.15 — the game has no separate raise API, so it scales the engage delay) and move speed (×1.05); level 1+ veterans ignore suppression, never surrender, and drive vehicles faster. A Lv.5 veteran is a one-man army.
- **Focus fire (hatred):** the side a veteran has been killing prioritizes it through the game's own target-selection — enemy AI within the focus radius treat the marked unit as their best visible enemy, concentrating fire while still moving, using cover and reloading naturally (no forced-target side effects).
- **Traitor mechanic (optional):** the player can damage allies (friendly-fire protection is bypassed for this mod only), and after enough friendly kills your own side turns on you and actively hunts you down. Turning it off fully disables it — friendly fire is never counted and any existing traitor mark is cleared.
- **Markers (v1.2.2 — true 3D, real perspective):** deep-red (enemy) / deep-blue (friendly) diamonds with white Roman numerals (I-V) rendered as world-space billboards anchored above the unit — a fixed world size, so they grow as you close in and shrink as you pull back, occluded by the world like any 3D object (hidden behind cover). Vehicle crews merge into ONE marker above the vehicle (showing the highest crew level), and vehicle-weapon kills (tank guns, coax MGs) are credited to the entire crew, so the whole crew levels up together.
- **M-map integration:** the game has no persistent minimap — press M. Marked units appear as colored diamonds right next to the game's own unit markers (one icon per vehicle for crews), so their map positions are always accurate.
- **Kill feedback:** eliminating a marked unit triggers a toast + golden flash + kill-confirm sound; leveling up and being marked give clear toasts (level-up and marked-warning merge into a single message).
- **Hide Anything compatible:** works with the "No Interaction Hints" mod — its checkbox for this mod hides the markers/icons/hints along with the rest of the HUD.
- **Live-tunable:** every number is a BepInEx config entry, adjustable from the in-game Mod Manager without restarting.
- **Singleplayer-first:** defaults to singleplayer only; multiplayer can be enabled in settings.
- **Zero conflicts:** hooks the game's own kill/damage/accuracy/targeting entry points; no vanilla files touched; remove the DLL to uninstall fully. Nothing is written into your campaign saves.

## Requirements
- Easy Red 2 (Steam)
- BepInEx 6 for IL2CPP

## Changelog
### v1.2.2
- **Marker scaling fix:** markers now use a fixed world size (0.8 m) instead of a constant on-screen size. Their on-screen footprint is pure perspective — bigger as you approach, smaller as you move away, like any real object. The previous "constant screen size" approach read as too small up close and over-inflated at long range.
- Added the standard debug log switch (`[Debug] debugLog`, default off) for marker-reconciliation, kill-attribution and traitor diagnostics.

### v1.2.1
- Added a public API (`ER2VeteranHVT.VeteranApi`) so other mods can read and write veteran levels and kills. This enables **persistent, cross-battle veteran ranks** in campaign-style mods: a campaign can pour a unit's saved rank into freshly spawned soldiers when a battle starts, and read kills/levels back when it ends. No gameplay change on its own — the API is inert unless another mod calls it.

### v1.2.0
- Head markers are now true 3D world-space billboards: constant readable on-screen size at any range, and occluded by the world like any other 3D object.
- Vehicle crews share ONE marker above the vehicle (highest crew level) instead of one marker per crewman.
- Vehicle-weapon kills (tank guns, coax MGs) are credited to the entire crew, so the whole crew levels up together; the M-map shows one icon per vehicle.
- Turning the traitor mechanic off now fully disables it: friendly fire is never counted, and any existing traitor mark is cleared.

## Shout outs
- The Easy Red 2 modding community.
- BepInEx / Harmony for IL2CPP.
