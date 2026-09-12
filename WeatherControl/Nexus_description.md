# ER2 Weather Control

## Description

Stop letting the mission decide what the sky looks like. ER2 Weather Control forces the weather and the time-of-day atmosphere of every battle — clear skies, rain, snow, midday, dusk, night, fog — and keeps them that way even when the campaign script tries to reset them.

## Installation instructions

1. Install BepInEx 6 (IL2CPP) into your Easy Red 2 folder and run the game once so the interop assemblies are generated.
2. Drop `ER2_WeatherControl.dll` into `<Game>\BepInEx\plugins\`.
3. Launch the game — `Loading [ER2 Weather Control 1.7.2]` in the log means success.
4. Pick your weather in **ER2 Mod Manager** (Settings → MODS) or in `BepInEx\config\er2.weathercontrol.cfg`.

## Main features

- **Weather override** — force Clear, Rain or Snow, or leave the map's own weather alone with "Default".
- **Atmosphere override** — force Midday, Sunset, Dawn, Night, Cloudy or Foggy, or keep the mission's own mood.
- **Applies live** — change the setting and the battle changes with it: no restart, no reload, no new mission.
- **Survives mission scripting** — the mod intercepts the game re-applying its own weather and puts your choice back, so a campaign reset cannot undo it.

## Requirements

- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs

Corvostudio for Easy Red 2 and its open attitude to modding, and the Easy Red 2 modding community.
