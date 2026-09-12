# ER2 AI Food

## Description

Your AI squadmates carry food and never eat it. ER2 AI Food fixes that: when an AI soldier is hurt, it eats from its own inventory and heals — using the same recovery the game already gives you as the player. Wounded men stop being walking dead weight and get back into the fight.

## Installation instructions

1. Install BepInEx 6 (IL2CPP) into your Easy Red 2 folder and run the game once so the interop assemblies are generated.
2. Drop `ER2_AIFood.dll` into `<Game>\BepInEx\plugins\`.
3. Launch the game — `Loading [ER2 AI Food 1.4.0]` in the log means success.
4. Adjust the threshold in **ER2 Mod Manager** (Settings → MODS) or in `BepInEx\config\er2.aifood.cfg`.

## Main features

- **AI self-healing** — an AI soldier below the health threshold (default 40) eats a food item from its own inventory and heals by that item's value.
- **Native behaviour** — uses the game's own eat + heal + consume + animation path, so it looks like the game doing it, not a script.
- **Keeps your food** — the player-controlled soldier is skipped entirely.
- **Sensible pacing** — a 10-second per-soldier cooldown prevents an AI from chain-eating its whole inventory at once.
- **Configurable** — threshold, check interval and master switch are all exposed and hot-reloadable.

## Requirements

- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs

Corvostudio for Easy Red 2 and its open attitude to modding, and the Easy Red 2 modding community.
