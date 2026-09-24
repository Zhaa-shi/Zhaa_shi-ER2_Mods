# ER2 More Physics

## Description
Adds physics to Easy Red 2: scene objects and loose items become physical and can be knocked around, destroyed objects fly apart into debris, soldiers physically collide with each other, and corpses get shoved aside instead of blocking paths. Bullet, melee and explosion knockback and damage are configurable, and flying physicalized objects can hurt soldiers. Since v0.1.48 the unit & corpse collision module matches the standalone "Unit Collision" mod: AI units take part in collision through per-frame position resolution, so AI can no longer walk through the player or through each other.

## Installation instructions
1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy `ER2_MorePhysics.dll` to `<Easy Red 2 folder>/BepInEx/plugins/`.
3. Launch the game. Configure settings in the in-game Mod Manager (MODS page) or in `BepInEx/config/er2.morephysics.cfg`.

## Main features
- **Unit & corpse collision**: living soldiers block each other using the vanilla body colliders (zero added volume); the player can't pass through AI (physics matrix) and AI can't pass through the player or other AI (position-level resolution for NavMeshAgent-driven movement). Corpses are shoved aside by the player and AI, with any corpse state (kinematic corpses are translated directly, dynamic ones get velocity written).
- **Scene object physicalization**: furniture, containers, building parts and misc props take damage from bullets, melee and explosions and then fly apart into physical debris, with per-category toggles and exclusion keywords.
- **Item physics**: loose `ItemObject` items get real physics and can be pushed around by soldiers, the player and explosions.
- **Knockback & damage multipliers**: separate bullet / melee / explosion knockback and damage values, plus damage from flying physicalized objects (`PhysicsDamage`, 0 = off).
- **Shooting range override** (optional): range targets stay shootable instead of flipping and can be destroyed and physicalized.
- **Singleplayer only by default** (`SingleplayerOnly`); collision and physics are not synced in multiplayer.
- **Hot-applying toggles**: switching the mod (or unit collision / corpse shoving) off in the Mod Manager instantly restores the vanilla collision matrix — no game restart needed.
- **Diagnostics mode**: set `UnitCollisionLayer` to `-2` to log collision matrices and collider details without changing anything.
- Bilingual config descriptions (follows the game's language in the regular build; the `_CN_` package is fixed Chinese).

## Requirements
- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs
- Thanks to the Easy Red 2 modding community for API research and testing.
