# ER2 Combat Tweaks

> Part of the [ER2 Mods](../README.md) collection.

## v1.2.2 FIX

- Fixed: enemies killed by you no longer freeze standing (weapon drops
  but no death ragdoll) for a few seconds after an enemy grenade/shell
  explosion. The friendly-blast window is now only opened by FRIENDLY
  explosions - enemy explosions no longer suppress enemy death ragdolls.

## v1.2.1 FIX

- Fixed: with friendly-fire protection enabled, the anti-launch clamp
  could push freshly spawned soldiers through walls/floors (wallclip)
  on missions with early friendly shelling (e.g. Invasion of France).
  Spawning soldiers are now left untouched for 3 seconds, and the
  ground-snap only pulls down soldiers that are actually being
  launched by a friendly blast (with a floor-piercing guard).

Battlefield quality-of-life and balance tweaks in one small mod:

1. FRIENDLY FIRE OFF (singleplayer)
   Units of the same faction can no longer damage each other:
   - your bullets, grenades/explosions and vehicle collisions will not hurt allies;
   - friendly AI can not hurt you either;
   - enemy damage is completely untouched.
   By default this only applies in singleplayer (offline) sessions.
   Multiplayer is left alone to avoid integrity/balance issues.

2. AMMO MODIFIERS FOR EVERY WEAPON CLASS
   Adjust how much ammo each weapon class carries, independently:
   - Emplacements (stationary machine-gun positions: sandbags, bunkers...)
   - Artillery (fixed / towed guns)
   - AA guns
   - Self-propelled artillery
   - Tank / vehicle machine guns (coaxial & hull MGs)
   - Tank main-gun shells
   - Aircraft bombs (infinite only: bomb bay refills after every drop)
   - Infantry weapons (rifles, SMGs, MGs, etc. - players and AI alike)
   Multiplier 1.0 = vanilla. Set 2.0 and every round lasts twice as long
   (each shot only consumes half a round). You can also enable true
   infinite ammo per category (the belt never runs out, no reload needed).

3. COMBAT TWEAKS
   - Explosion radius multiplier: scales the blast radius of all
     explosions (shockwave, fragments and camera shake scale together).
   - Infantry damage multiplier: a global multiplier on all damage dealt
     to soldiers (bullets, melee, collisions and explosions).

## Installation

1. Requires BepInEx 6 for IL2CPP (Easy Red 2).
2. Copy ER2_CombatTweaks.dll into <Game>\BepInEx\plugins\
3. Launch the game once, then adjust the settings either in
   the in-game Mod Manager page (preferred) or in
   `<Game>\BepInEx\config\er2.combattweaks.cfg`

## Settings

**[General]**

- `Enabled` — - master switch

[Friendly Fire]
- `FfEnabled` — - same-faction damage is blocked
- `FfSingleplayerOnly` — - only apply in offline singleplayer

**[Ammo]**

- `AmmoEnabled` — - master switch for ammo tweaks
- `EmplacementMult` — (0.2-10000)   - emplacement MG ammo multiplier
- `EmplacementInfinite` — - emplacements never run out of ammo
- `ArtilleryMult` — (0.2-10000)   - artillery ammo multiplier
- `ArtilleryInfinite` — - artillery never runs out of ammo
- `AAMult` — (0.2-10000)   - AA gun ammo multiplier
- `AAInfinite` — - AA guns never run out of ammo
- `SPAMult` — (0.2-10000)   - self-propelled artillery ammo multiplier
- `SPAInfinite` — - self-propelled artillery never runs out
- `TankMgMult` — (0.2-10000)   - tank/vehicle MG ammo multiplier
- `TankMgInfinite` — - tank MGs never run out of ammo
- `TankShellsMult` — (0.2-10000)   - tank main-gun shell multiplier
- `TankShellsInfinite` — - tank main gun never runs out of ammo
- `PlaneBombInfinite` — - aircraft bomb bay refills after each drop
- `InfantryMult` — (0.2-10000)   - infantry weapon ammo multiplier
- `InfantryInfinite` — - infantry weapons never run out of ammo

**[Explosion]**

  ExplosionRadiusMult (0.2-10)   - explosion blast radius multiplier

**[Weapon]**

- `WeaponDamageMult` — (0.1-100)   - global infantry damage multiplier

Example: tank shells x2 + tank MG unchanged -> set TankShellsMult = 2.0,
leave TankMgMult = 1.0. Want everything unlimited -> tick the Infinite
boxes.

## Compatibility

- Friendly fire protection hooks the game's own damage checks
  (BodyPart.AllowDamage / HitPart / TryPenetrateArmor,
  VehicleDamagablePart.HitPart / TryPenetrateArmor), so it covers
  bullets, melee, explosions and collisions.
- Ammo tweaks hook TurretGun.ExtractOneBullet (turrets & emplacements),
  GenericGun.ExtractOneBullet (infantry weapons) and
  VehiclePlane.DropBombs (aircraft bombs) - the single chokepoints both
  players and AI use.
- No vanilla files are modified. Remove the DLL to uninstall.

Shout outs: the Easy Red 2 modding community.
