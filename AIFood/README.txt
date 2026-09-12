ER2 AI Food v1.4.0
==================

Makes AI soldiers use the food they carry. When an AI soldier is hurt, it eats
from its own inventory to heal, instead of standing there wounded until it
dies - the same recovery the game already gives the player.

WHAT IT DOES
------------
- Every 2 seconds the mod scans the living AI soldiers on the field.
- An AI soldier whose health is below the threshold (default 40) eats one
  food item from its own inventory, healing by that item's recovery value.
- Uses the game's own recovery path (the same eat + heal + consume + animation
  used natively), so it looks and behaves like the game doing it.
- The player-controlled soldier is skipped - your own food stays yours.
- Per-soldier cooldown of 10 seconds, so an AI does not chain-eat its whole
  inventory in one burst. Only soldiers carrying food are affected; if the
  inventory has no food, nothing happens.

CONFIGURATION
-------------
Edit BepInEx/config/er2.aifood.cfg, or use the in-game Mod Manager
(Settings -> MODS). Changes apply immediately.

[General]
  enabled        Master switch for AI auto-eating (default: true).
  eatBelowHp     AI eats food when its health is below this value
                 (default: 40).
  checkInterval  Seconds between AI food checks (default: 2).

INSTALLATION
------------
1. Install BepInEx 6 (IL2CPP) for Easy Red 2 and run the game once so the
   interop assemblies are generated.
2. Copy ER2_AIFood.dll into <Game>\BepInEx\plugins\.
3. Launch the game - "Loading [ER2 AI Food 1.4.0]" in the log means success.

REQUIREMENTS
------------
- Easy Red 2 with BepInEx 6 (IL2CPP).

NOTES
-----
- The scan iterates the game's living-creature list (no per-frame whole-scene
  search).
- Each activation logs one low-frequency line ("AI ate food: hp=...").
- Multiplayer: the mod acts on every AI soldier it can see, so it also affects
  the opposing side's wounded.

Shout outs: the Easy Red 2 modding community.
