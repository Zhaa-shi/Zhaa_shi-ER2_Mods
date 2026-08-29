ER2 Mod Manager - In-Game Mod Settings Manager
===============================================
Version 1.1.1 | Easy Red 2 (BepInEx 6 / IL2CPP)

WHAT IT DOES
------------
Adds a "MODS" page to the game's native settings menu (press Esc in-game, select the
MODS tab). Every loaded BepInEx plugin's configuration is shown as native-styled
controls: toggles, dropdowns, sliders and numeric input fields.

Fully automatic, zero maintenance: the manager auto-discovers every loaded plugin and
auto-renders whatever config entries each one exposes. Config labels are humanized from
their key names (enableAIVaulting -> "Enable AI Vaulting"), so third-party and future
mods show readable names without any per-mod lookup table.

Mods with no config entries still appear as read-only "installed" entries, so you can
see everything that's loaded.

Changes are staged and auto-saved when you leave the settings (no save button to lose).

INSTALLATION
------------
1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy ER2_ModManager.dll to <Easy Red 2 folder>/BepInEx/plugins/.
3. Launch the game, open Settings (Esc) -> the MODS page lists every mod's config.

FEATURES
--------
- Native-styled controls cloned from the game's own settings UI (looks like vanilla).
- Auto-discovers every loaded BepInEx plugin and renders all its config entries - no
  hardcoded mod list or translation table.
- Humanized config labels (camelCase/snake_case -> readable text) for any mod.
- Mods without config appear as read-only "installed" entries.
- Handles bool / integer / float / string / KeyCode config entries.
- Numeric inputs with range hints; slider fallback when no template is available.
- Hotkeys can be rebound by clicking the button then pressing the key (capture).
- Copy / reset actions per config entry, with on-screen flash feedback.
- Auto-save staged changes; per-mod master switches honored.

v1.1.0 changelog
----------------
- Final version: removed the hardcoded Chinese translation dictionaries (mod names,
  config key labels, descriptions and section names). Config labels are now humanized
  from key names automatically, so the manager works with any current or future mod
  with zero per-mod maintenance.
- Mods with no config entries now appear as read-only "installed" entries (previously
  only a hardcoded list of known mods was shown).
- Removed the hardcoded "More Destructibles master switch" special-case support.

v1.0.75 changelog
-----------------
- Fixed numeric input digits not showing after the game 2.1.0 font change (digit font
  resolved at runtime by actually rendering "0123456789").

v1.0.74 changelog
-----------------
- Digit display fix iteration (see v1.0.75 for the final form).
