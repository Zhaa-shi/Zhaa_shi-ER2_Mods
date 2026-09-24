# ER2 More Physics - Unit Collision

## Description
A lightweight companion mod to ER2 More Physics that ONLY keeps the unit and corpse collision features. Living soldiers physically block each other (no overlap) by reusing the vanilla body colliders, and corpses no longer stop living units — soldiers and the player shove them aside instead of walking over them. All other More Physics features (scene-object physicalization, item physics, knockback, impact damage) are removed, so this mod is tiny, predictable, and has no effect on props or the environment.

## Installation instructions
1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy `ER2_MorePhysics_UnitCollision.dll` to `<Easy Red 2 folder>/BepInEx/plugins/`.
3. Launch the game. Configure settings in the in-game Mod Manager (MODS page) or in `BepInEx/config/er2.morephysics.unitcollision.cfg`.

## Main features
- **Unit blocking**: living soldiers block each other using the vanilla body colliders — zero added volume. The player can't pass through AI (physics-based); AI can't pass through the player or other AI (position-level resolution, since AI moves via NavMeshAgent outside the physics engine).
- **Corpse shoving**: corpses no longer stop living units — both the player and AI shove them aside instead of walking over them. Works with any corpse state: kinematic (game-frozen) corpses are translated directly, dynamic ones get their velocity written.
- **Reliable corpse detection**: corpses are gathered from the unit tables plus a cached full-scene scan, so AI interactions work even though the game removes ragdollized units from `aliveCreatures`.
- **Independent toggles**: `UnitCollision` (unit blocking) and `PushCorpses` (corpse shoving) can be disabled separately; `CorpsePushForce` controls how far corpses are shoved (0 = corpses block but are not shoved).
- **Hot-applying toggles**: switching the mod (or unit blocking / corpse shoving) off in the Mod Manager instantly restores the vanilla collision matrix — no game restart needed.
- **Singleplayer only by default** (`SingleplayerOnly`), like the full More Physics mod.
- **Diagnostics mode**: set `UnitCollisionLayer` to `-2` to log collision matrices and collider details without changing anything.
- Bilingual config descriptions (follows the game's language in the regular build; the `_CN_` package is fixed Chinese).

## Requirements
- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs
- Thanks to the Easy Red 2 modding community for API research and testing.
