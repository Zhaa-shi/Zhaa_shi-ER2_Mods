# ER2 Throwable Wheel

> Part of the [ER2 Mods](../README.md) collection.

Choose exactly which throwables appear in the game's grenade wheel, and keep
them stocked - throw a grenade and it is refilled from your configured list.

## What It Does

- Replaces the contents of the native grenade wheel (hold G) with a list of
  throwables you choose, instead of only what you happened to pick up.
- Auto-refill: every 2 seconds the player's inventory is checked and any
  missing configured throwable is added back, so throwing does not run you
  dry.
- Optional clean slate: with "ReplaceWheelContent" enabled, the vanilla
  throwables are removed from your inventory first, so the wheel shows ONLY
  your configured list.
- Ready-made loadout presets, or a fully custom comma-separated ID list.
- Everything else still runs through the game's own wheel, selection and
  throwing code - the mod only supplies the item list.
- Off in online matches by default, to avoid balance/integrity problems.

## Presets

- `Classic` — (14 items) - the six national grenades, molotov, two AT charges, four smoke grenades and dynamite.
- `Expanded` — (34 items) - Classic plus the common extra grenades, satchels, mines, RPG rounds and more smoke.
- `All` — (48 items) - every throwable the mod knows about.
  Anti-Tank   (8 items)  - shaped charges, AT grenades, RPG rounds and mines.
- `Incendiary` — (2 items)  - molotov and the Japanese incendiary.
- `Smoke` — (9 items)  - all smoke grenades.
- `Custom` — - use the ItemIds list below.

Note: with too many items (roughly 16+) the game's native wheel may fail to
display properly - use the large presets with care.

## Configuration

Edit BepInEx/config/er2.throwablewheel.cfg, or use the in-game Mod Manager
(Settings -> MODS).

**[General]**

- `Enabled` — Master switch. When false the mod is fully idle (default: true).
- `ReplaceWheelContent` — If true, vanilla throwables are removed from the player inventory and the wheel shows ONLY the configured items (default: false - your picked-up throwables stay).
- `RefillBeforeWheelOpens` — Re-checks and refills the player inventory every 2 seconds (default: true).
- `AllowInMultiplayer` — Off by default; set true to also apply the mod in online sessions (default: false).

**[Wheel]**

- `LoadoutPreset` — Which preset to use (default: Classic).
- `ItemIds` — Comma-separated throwable ID list, used only when LoadoutPreset is Custom.

## Installation

1. Install BepInEx 6 (IL2CPP) for Easy Red 2 and run the game once so the
   interop assemblies are generated.
2. Copy ER2_ThrowableWheel.dll into <Game>\BepInEx\plugins\.
3. Launch the game - "Loading [ER2 Throwable Wheel 1.3.6]" in the log means
   success. Hold G in battle to open the wheel.

## Requirements

- Easy Red 2 with BepInEx 6 (IL2CPP).

## Notes

- Throwables are added as their proper item subclass, and the native wheel,
  selection and throw pipeline is left untouched - the mod does not draw its
  own menu.
- Multiplayer defaults to off; the host/other players see the throwables you
  actually throw.

Shout outs: the Easy Red 2 modding community.
