# ER2 Combat Tweaks v1.2.2 — Friendly Fire Off, Ammo Modifiers & Combat Tweaks

> **v1.2.2 fix:** enemies killed by you no longer freeze standing (weapon drops but no death ragdoll) right after an enemy grenade/shell explosion — the friendly-blast window is now only opened by FRIENDLY explosions, so enemy explosions no longer suppress enemy death ragdolls.
>
> **v1.2.1 fix:** friendly-fire protection no longer pushes freshly spawned soldiers through walls/floors (wallclip) — spawning units are left untouched for 3 seconds, and the ground-snap only pulls down soldiers actually being launched by a friendly blast, with a floor-piercing guard. Reported on Invasion of France.

## Description
A battlefield quality-of-life and balance pack in one small BepInEx plugin:

- **Friendly fire off in singleplayer.** Same-faction units can no longer damage each other. Your bullets, explosions and collisions won't hurt your allies, and friendly AI can't hurt you. Enemy damage is untouched. By default it only applies in offline singleplayer; multiplayer is left alone.
- **Ammo modifiers for every weapon class.** Independently adjust the ammo of emplacements, artillery, AA guns, self-propelled artillery, tank/vehicle machine guns, tank main-gun shells, aircraft bombs and infantry weapons. Multiplier-based (2.0 makes every round last twice as long), plus optional true infinite ammo per category.
- **Combat tweaks.** A global explosion-radius multiplier and a global infantry damage multiplier, both live-tunable in-game.

## Installation instructions
1. Requires BepInEx 6 (IL2CPP) for Easy Red 2.
2. Drop `ER2_CombatTweaks.dll` into `Easy Red 2\BepInEx\plugins\`.
3. Launch the game once, then configure in the in-game Mod Manager page (recommended) or edit `BepInEx\config\er2.combattweaks.cfg`.

## Main features
- **Friendly Fire protection (singleplayer):** blocks same-faction damage at the game's own damage bus — covers bullets, melee, grenades/explosions, artillery and vehicle collisions; AI allies can no longer hurt you, and you can't hurt them.
- **Emplacement ammo multiplier & infinite:** stationary machine-gun positions (sandbags, bunkers, tripods).
- **Artillery / AA / self-propelled-artillery ammo:** separate multipliers and infinite switches for each class.
- **Tank machine-gun & main-gun shell ammo:** coaxial/hull MGs and main-gun rounds, independently adjustable.
- **Aircraft bomb ammo:** an infinite-bombs option that refills the bomb bay after every drop.
- **Infantry weapon ammo:** multiplier and infinite ammo for rifles, SMGs, MGs and other handheld guns (players and AI alike).
- **Explosion radius multiplier:** scale the blast radius of all explosions (shockwave, fragments and camera shake scale together).
- **Infantry damage multiplier:** a global multiplier on all damage dealt to soldiers (bullets, melee, collisions and explosions).
- **Hot-configurable:** every option is a BepInEx config entry — live-tunable from the in-game Mod Manager without restarting.
- **Zero conflicts:** only intercepts the game's damage checks and ammo-extraction chokepoints; no vanilla files touched; remove the DLL to uninstall fully.

## Requirements
- Easy Red 2 (Steam)
- BepInEx 6 for IL2CPP

## Shout outs
- The Easy Red 2 modding community.
- BepInEx / Harmony for IL2CPP.
