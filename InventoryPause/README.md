# ER2 Inventory Pause

> Part of the [ER2 Mods](../README.md) collection.

Tired of being shot while sorting your backpack? This mod pauses the
battle whenever you open an inventory - your own backpack OR a corpse
backpack - and resumes it when you close it.

## How It Works

- While the inventory is open, the world is truly paused: soldiers
  freeze, bullets stop mid-air, grenade fuses stop ticking, explosions
  never happen. Nothing can hurt you while you sort your gear.
- The pause kicks in about 0.5s after opening, so the inventory's own
  open animation plays normally first (freezing immediately would stall
  the animation - a quirk of the game's scaled-time UI animations).
- Items you drop while paused are auto-settled to the ground (the game
  physics is frozen during the pause, so dropped weapons would otherwise
  hang in the air - this mod lands them for you).
- Your own inventory UI works normally the whole time.

## Settings

**[General]**

- `Enabled` — - master switch
  ApplyInMultiplayer - also pause in multiplayer (default: singleplayer
                       only; pausing is local-only and may be unfair)
- `PauseMode` — - 4 = delayed timeScale freeze (recommended) 5/0-3 = deprecated experimental methods
  PauseDelay (0.1-2) - seconds to wait for the inventory open animation
                       before freezing (0.5 default; smaller = faster
                       pause but may stall the animation)

## Installation

1. Requires BepInEx 6 for IL2CPP (Easy Red 2).
2. Copy ER2_InventoryPause.dll into <Game>\BepInEx\plugins\
3. Launch the game once, then adjust the settings either in the in-game
   Mod Manager page (preferred) or in
   `<Game>\BepInEx\config\er2.inventorypause.cfg`

## Compatibility

- Pauses via Time.timeScale = 0 (the same mechanism the game's own
  pause menu uses); no vanilla files are modified. Remove the DLL to
  uninstall.
- The pause is skipped while the native pause menu is open.

Shout outs: the Easy Red 2 modding community.
