# ER2 Inventory Pause v1.0.5 — Pause the Battle While You Sort Your Gear

## Description
Tired of getting shot while looting? **ER2 Inventory Pause** freezes the battle whenever you open an inventory — your own backpack **or** a corpse backpack — and resumes it the moment you close it. Sort your gear, drop unwanted weapons, take your time.

## Installation instructions
1. Requires BepInEx 6 (IL2CPP) for Easy Red 2.
2. Drop `ER2_InventoryPause.dll` into `Easy Red 2\BepInEx\plugins\`.
3. Launch the game once, then configure in the in-game Mod Manager page (recommended) or edit `BepInEx\config\er2.inventorypause.cfg`.

## Main features
- **Real pause, not a shield:** the world truly stops while the inventory is open — soldiers freeze, bullets stop mid-air, grenade fuses stop ticking, explosions never happen. Nothing can hurt you, and there is no "invincibility" to exploit (your opponents simply don't act).
- **Covers both inventories:** your own backpack and corpse backpacks (the game's "around" inventory panel) both trigger the pause.
- **Animation-safe:** the pause engages about 0.5s after opening so the inventory's open animation plays normally (the game's UI animations run on scaled time — an immediate freeze would stall them).
- **Dropped items land properly:** the physics is frozen during the pause, so weapons you drop would otherwise hang in the air — this mod auto-settles them to the ground.
- **Singleplayer-first:** defaults to singleplayer only; multiplayer pausing can be enabled in settings (pausing is local-only, so it may be unfair).
- **Zero conflicts:** uses the same timeScale mechanism as the game's own pause menu; no vanilla files touched; remove the DLL to uninstall fully.

## Requirements
- Easy Red 2 (Steam)
- BepInEx 6 for IL2CPP

## Shout outs
- The Easy Red 2 modding community.
- BepInEx / Harmony for IL2CPP.
