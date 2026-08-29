# ER2 Veteran HVT v1.1.26 — Veteran Levels, Marked Men & Focus Fire

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
- **Traitor mechanic (optional):** the player can damage allies (friendly-fire protection is bypassed for this mod only), and after enough friendly kills your own side turns on you and actively hunts you down.
- **Markers:** deep-red (enemy) / deep-blue (friendly) diamonds with white Roman numerals (I-V) above marked units, both sides at once; markers turn semi-transparent when the unit is behind cover.
- **M-map integration:** the game has no persistent minimap — press M. Marked units appear as colored diamonds right next to the game's own unit markers, so their map positions are always accurate.
- **Kill feedback:** eliminating a marked unit triggers a toast + golden flash + kill-confirm sound; leveling up and being marked give clear toasts (level-up and marked-warning merge into a single message).
- **Hide Anything compatible:** works with the "No Interaction Hints" mod — F5 hides this mod's UI along with the rest of the HUD, with a per-mod toggle in its settings.
- **Live-tunable:** every number is a BepInEx config entry, adjustable from the in-game Mod Manager without restarting.
- **Singleplayer-first:** defaults to singleplayer only; multiplayer can be enabled in settings.
- **Zero conflicts:** hooks the game's own kill/damage/accuracy/targeting entry points; no vanilla files touched; remove the DLL to uninstall fully. Nothing is written into your campaign saves.

## Requirements
- Easy Red 2 (Steam)
- BepInEx 6 for IL2CPP

## Shout outs
- The Easy Red 2 modding community.
- BepInEx / Harmony for IL2CPP.
