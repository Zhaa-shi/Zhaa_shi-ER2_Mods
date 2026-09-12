# ER2 Throwable Wheel

## Description

Pick exactly which grenades and throwables sit in your grenade wheel — and keep them there. ER2 Throwable Wheel fills the game's native wheel with a loadout you choose and refills what you throw, so you are never out of smoke because you used your last one two minutes ago.

## Installation instructions

1. Install BepInEx 6 (IL2CPP) into your Easy Red 2 folder and run the game once so the interop assemblies are generated.
2. Drop `ER2_ThrowableWheel.dll` into `<Game>\BepInEx\plugins\`.
3. Launch the game — `Loading [ER2 Throwable Wheel 1.3.6]` in the log means success. Hold **G** in battle to open the wheel.
4. Choose your loadout in **ER2 Mod Manager** (Settings → MODS) or in `BepInEx\config\er2.throwablewheel.cfg`.

## Main features

- **Custom wheel contents** — the wheel shows the throwables you configured instead of only what you picked up.
- **Auto-refill** — every 2 seconds your inventory is topped back up, so throwing does not run you dry.
- **Presets or full custom** — Classic (14), Expanded (34), All (48), Anti-Tank (8), Incendiary (2), Smoke (9), or your own comma-separated ID list.
- **Optional clean slate** — remove the vanilla throwables first so the wheel shows *only* your list.
- **Fully native pipeline** — the game's own wheel, selection and throwing code still does the work; the mod only supplies the item list.
- **Multiplayer-safe by default** — disabled in online matches unless you turn it on.

## Requirements

- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs

Corvostudio for Easy Red 2 and its open attitude to modding, and the Easy Red 2 modding community.
