# ER2 Mod Manager v1.1.1

> **v1.1.1 fix:** enum-typed settings are now supported — hotkey-style enums (e.g. Coax MG Hotkey's InputSystem Key config) render as click-to-rebind buttons, other enums as dropdowns. Previously such settings were silently skipped in the MODS page.

An in-game settings manager for Easy Red 2 BepInEx mods: adds a native-looking MODS page to the game's settings menu where every plugin's config can be tweaked live — with **zero hardcoded translation tables**, so it automatically works with any mod you install, now and in the future.

## Description

- Adds a **MODS** page inside the game's native settings (Esc menu) — every loaded BepInEx plugin's options appear as vanilla-style controls: toggles, dropdowns, sliders and numeric inputs.
- **Fully automatic, zero maintenance**: the manager auto-discovers every loaded plugin and auto-renders whatever config entries each one exposes. Config labels are humanized from their key names (`enableAIVaulting` → `Enable AI Vaulting`), so third-party and future mods show readable names without any per-mod lookup table.
- Mods with no config entries still appear as read-only "installed" entries, so you can see everything that's loaded.
- Controls are cloned from the game's own settings UI, so it looks and feels like stock.
- **Staged auto-save**: changes are saved automatically when you leave the settings.
- Handles bool / int / float / string / KeyCode entries; hotkeys are re-bound by clicking the button and pressing the new key.

## Installation instructions

1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy `ER2_ModManager.dll` to `<Easy Red 2 folder>/BepInEx/plugins/`.
3. Launch the game, open Settings (Esc) → the MODS page lists every plugin's configuration.

## Main features

- Native-styled MODS page cloned from the vanilla settings UI.
- Auto-discovers every loaded BepInEx plugin and renders all its config entries — no hardcoded mod list.
- Humanized config labels (camelCase/snake_case → readable text) for any mod, including third-party ones.
- Mods without config appear as read-only "installed" entries.
- Toggle / dropdown / slider / numeric-input controls for all common config types.
- Numeric inputs with range hints (with engine-font fallback so digits always render).
- Hotkey capture rebinding.
- Per-entry copy & reset with flash feedback.
- Staged auto-save; honors each plugin's master switch.

## Requirements

- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs

- Corvostudio for Easy Red 2 and its mod-friendly settings UI.
