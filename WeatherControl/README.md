# ER2 Weather Control

> Part of the [ER2 Mods](../README.md) collection.

Force the weather and the time-of-day atmosphere of every battle in Easy Red 2,
instead of letting the mission decide.

## What It Does

- Weather: force Clear, Rain or Snow for the battle (or leave the map's own
  weather alone with "Default").
- Atmosphere: force the time of day / mood - Midday, Sunset, Dawn, Night,
  Cloudy or Foggy (or "Default" for the mission's own).
- Both settings apply immediately when changed - no restart and no need to
  reload the battle.
- Survives the campaign's own weather reset: the mod intercepts the game
  setting the weather/atmosphere and re-applies your choice, so a mission
  script cannot override it mid-battle.

## Configuration

Edit BepInEx/config/er2.weathercontrol.cfg, or use the in-game Mod Manager
(Settings -> MODS). Changes apply immediately.

**[General]**

- `enabled` — Master switch (default: true).

**[Weather]**

- `weatherMode` — Weather applied in battle (default: Default). Acceptable values: Default, Clear, Rain, Snow.

**[Atmosphere]**

- `atmospherePreset` — Time of day / atmosphere applied in battle (default: Default). Acceptable values: Default, Midday, Sunset, Dawn, Night, Cloudy, Foggy.

There is no hotkey in this version - pick the values in the config / Mod
Manager and they apply live.

## Hud Hiding

This mod's on-screen notices obey the "Hide Anything" HUD-hide contract: the
mod appears as its own checkbox under "Hide Other Mods UI" in the in-game Mod
Manager.

## Installation

1. Install BepInEx 6 (IL2CPP) for Easy Red 2 and run the game once so the
   interop assemblies are generated.
2. Copy ER2_WeatherControl.dll into <Game>\BepInEx\plugins\.
3. Launch the game - "Loading [ER2 Weather Control 1.7.2]" in the log means
   success.

## Requirements

- Easy Red 2 with BepInEx 6 (IL2CPP).

## Notes

- v1.7.2: font handling updated for game build 2.1.x, which removed the
  PhaseBarGUI class the mod used for its on-screen text font. It now falls
  back to GUI.skin -> LegacyRuntime.ttf and keeps working.
- Low-frequency functional logs only; debug logging is off by default.

Shout outs: the Easy Red 2 modding community.
