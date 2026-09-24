ER2 More Physics v0.1.49
========================

Adds physics to Easy Red 2: scene objects and items can be knocked around,
soldiers and corpses collide, and bullets/melee/explosions carry knockback.

- Unit & corpse collision: living soldiers block each other using the vanilla
  body colliders, and corpses no longer stop living units — they get shoved
  aside instead. AI units participate fully via position-level collision
  (AI moves through the nav system, not the physics engine).
- Scene object physicalization: furniture, containers and props take damage
  from bullets, melee and explosions, then fly apart into physical debris.
- Item physics: loose items in the scene get real physics and can be pushed
  around by soldiers, the player and explosions.
- Knockback & damage: configurable multipliers for bullet / melee / explosion
  knockback and damage, including damage from flying physicalized objects.
- Shooting range override (optional): targets stay shootable instead of
  flipping, and can be destroyed and physicalized.

v0.1.49 changes
---------------
- Fix (important): the collision matrix is now two-way - previously it was
  only ever opened, so disabling the toggles in the Mod Manager left the
  already-open matrix in place for the rest of the session ("turned it off
  but units still collide"). Any switch (master / UnitCollision /
  PushCorpses) going off now immediately restores the vanilla collision
  matrix - no game restart needed.
- Fix: the position-level living-unit separation (from the v0.1.48 unit
  collision backport) is now strictly tied to UnitCollision; AI-corpse
  interaction and corpse physics are strictly tied to PushCorpses.
- Item physics / scene physics remain gated by the master switch as before.

v0.1.48 changes
---------------
- Unit & corpse collision upgraded to the standalone "Unit Collision" v1.0.7
  behavior: AI units now take part in collision through per-frame position
  resolution (previously AI moved via NavMeshAgent and could walk through the
  player and through each other), corpses can be shoved by AI as well, and
  corpse detection no longer depends on `aliveCreatures` (cached scene scan
  fallback).
- Diagnostics mode (`UnitCollisionLayer = -2`) now also dumps living units'
  hit-collider state (layer / trigger / enabled) to troubleshoot reports.
- CharacterController layer probing re-probes after a fallback lock instead of
  locking in before units spawn.
- Verified against game update 2.1: all patch targets and APIs intact.

Installation
------------
1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy ER2_MorePhysics.dll into: <Easy Red 2 folder>/BepInEx/plugins/
3. Start the game. Configure settings in the in-game Mod Manager (MODS page)
   or edit BepInEx/config/er2.morephysics.cfg.

Configuration
-------------
General:
- Enabled: master switch.
- SingleplayerOnly: apply only in singleplayer (default on).

Physics:
- DefaultHealth: base health of scene objects before they can be knocked flying.
- MeleeDamage / BulletDamageMultiplier / ExplosionDamageMultiplier: damage to
  scene objects.
- BulletKnockback / MeleeKnockback / ExplosionKnockback: knockback impulses
  when a scene object is destroyed.
- PhysicsDamage: damage dealt by flying physicalized objects to soldiers
  (0 = off).
- UnitCollision: soldiers physically block each other (default on).
- UnitCollisionLayer: debug only; -1 = normal, -2 = diagnostics (logs collision
  matrices and collider details, changes nothing).
- PushCorpses: units push corpses aside (default on).
- CorpsePushForce: shove impulse strength (0 = corpses block but are not
  shoved).
- PushPhysItems: soldiers/player shove physicalized items out of the way.
- EnableItemPhysics: periodically enable physics on ItemObject items.
- EnableScenePhysics: allow non-item scene objects to become physical when
  destroyed.
- ExplosionOnlyHardObjects: hard objects (walls, concrete) only take explosion
  damage.
- DespawnTime: seconds before a physicalized scene object is removed
  (0 = never).
- EnableFurniture / EnableContainers / EnableDebris / EnableBuildingParts /
  EnableMiscProps: category toggles for physicalization.
- ExcludedNameKeywords: comma-separated name keywords that are never
  physicalized.
- TargetPracticeOverride: keep shooting-range targets shootable so they can be
  destroyed and physicalized (off = vanilla target behavior).

Note
----
This mod and the standalone "ER2 More Physics - Unit Collision" mod overlap in
the unit/corpse collision area - run only one of them.

Language
--------
Config descriptions follow the game's language (English/Chinese) at runtime;
the _CN_ package is fixed Chinese.
