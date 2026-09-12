# ER2 Limb Tweaks

## Description

Realistic limb injuries for Easy Red 2. Limbs can be shot off — on living soldiers and on corpses — and a severed limb bleeds the soldier out unless it is treated. Wounded survivors keep fighting, but with the limitations their injuries actually imply: a severed leg puts a man on the ground, a severed arm stops him reloading, picking weapons up or throwing.

## Installation instructions

1. Install BepInEx 6 (IL2CPP) into your Easy Red 2 folder and run the game once so the interop assemblies are generated.
2. Drop `ER2_LimbTweaks.dll` into `<Game>\BepInEx\plugins\`.
3. Launch the game — `Loading [ER2 Limb Tweaks 2.13.101]` in the log means success.
4. Tune the settings in-game via **ER2 Mod Manager** (Settings → MODS) or by editing `BepInEx\config\er2.limbtweaks.cfg`.

## Main features

- **Corpse shooting** — hitting a dead body severs the limb near the bullet impact.
- **Living dismemberment** — accumulated damage on the same limb can sever it while the soldier is still alive and fighting.
- **Bleeding** — a severed limb bleeds continuously until death, and blocks the game's natural regeneration. The player bleeds at half rate, giving you a window to react.
- **Bandages help, then stop helping** — each bandage halves the bleed rate; the third and later ones do nothing ("Bandage has no more effect"), so a severed limb is a permanent problem, not a solved one.
- **Realistic consequences** — severed leg locks the soldier prone; severed right arm forces surrender and blocks reloading / picking up / equipping / throwing / bandaging; severed left arm blocks reloading and long weapons.
- **On-screen feedback** — native hints tell you what just happened to whom.

## Requirements

- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs

Corvostudio for Easy Red 2 and its open attitude to modding, and the Easy Red 2 modding community.
