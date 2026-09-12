ER2 Mod Manager - In-Game Mod Settings Manager
===============================================
Version 1.2.1 | Easy Red 2 (BepInEx 6 / IL2CPP)

WHAT IT DOES
------------
Adds a "MODS" page to the game's native settings menu (press Esc in-game, select the
MODS tab). Every loaded BepInEx plugin's configuration is shown as native-styled
controls: toggles, dropdowns, sliders and numeric input fields.

Fully automatic, zero maintenance: the manager auto-discovers every loaded plugin and
auto-renders whatever config entries each one exposes. Config labels are humanized
from their key names (enableAIVaulting -> "Enable AI Vaulting"), so third-party and
future mods show readable names without any per-mod lookup table.

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
- Alphabetical mod list with letter section headers ("A"..."Z" / "0-9" / "#") for
  quick lookup.
- "Easy Red 2" / "ER2" name prefixes are stripped in the list (sorting and grouping
  use the short name); the full name is still shown when a mod is expanded.
- Humanized config labels (camelCase/snake_case -> readable text) for any mod.
- Mods without config appear as read-only "installed" entries.
- Handles bool / integer / float / string / KeyCode config entries. Free-form string
  settings (colors, addresses, ...) get a text input; enum hotkeys get click-to-rebind
  buttons. Every setting always renders (read-only fallback line at worst).
- Numeric inputs with range hints; slider fallback when no template is available.
- Hotkeys can be rebound by clicking the button then pressing the key (capture).
- Copy / reset actions per config entry, with on-screen flash feedback.
- Page looping: left arrow on the first page jumps straight to the MODS page, right
  arrow on the MODS page returns to the first page. Native click sounds are kept.
- Auto-save staged changes; per-mod master switches honored.
- Compatible with the game 2.1.x settings rework (async page filling is handled).

v1.2.1 changelog
----------------
- Fixed right-arrow paging dying after the 2026-09-12 game update: the game changed
  SettingsGUI_V2.UpdateOpenedMenu(bool) to UpdateOpenedMenu(bool, bool), so the compiled
  call could no longer be resolved at runtime (MissingMethodException) and aborted the
  whole paging hook. The call is now made by reflection with parameter-count detection,
  so future signature changes cannot break paging again.
- Hardened the MODS page layout: template rows cloned from vanilla settings rows now get
  a normalized height (vertically stretched / zero-height rows were laid out on top of
  each other, which is what made rows and the scrollbar overlap after re-entering).
- The vanilla page-fill coroutine is now gated on the MODS page as well, so re-opening
  the settings cannot refill/restore positions behind our back.
- Content anchors/height are restored before a MODS page rebuild, so closing the settings
  while on the MODS page can no longer leak a bad scroll height into the next open.

v1.2.0 changelog
----------------
- Mod list is now sorted alphabetically with letter section headers; "Easy Red 2"/"ER2"
  prefixes are stripped for sorting/grouping (full name shown when expanded).
- Long mod names are truncated by measured text width, so names are only shortened when
  they truly do not fit (the old fixed character count cut names too eagerly).
- Fixed the empty MODS page after fast page flipping (the game disables the shared
  scroll content chain when paging is interrupted; the manager now re-activates it).
- Fixed the scrollbar overlapping mod rows after re-entering the settings, and a bad
  scroll height leaking into vanilla settings pages (anchor/height restore completed).
- Paging now loops in both directions with native click sounds; both arrows always
  visible.
- Free-form string settings (e.g. color values) now render as text inputs instead of
  being silently skipped; every config entry is guaranteed to render.
- Reduced log noise (diagnostics removed; only meaningful events are logged).

v1.1.0 changelog
----------------
- Removed the hardcoded Chinese translation dictionaries (mod names, config key labels,
  descriptions and section names). Config labels are now humanized from key names
  automatically, so the manager works with any current or future mod with zero per-mod
  maintenance.
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
