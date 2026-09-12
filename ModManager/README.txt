ER2 Mod Manager - In-Game Mod Settings Manager
===============================================
Version 1.5.0 | Easy Red 2 (BepInEx 6 / IL2CPP)

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

v1.5.0 changelog
----------------
- Fixed the white flash when clicking: mod, section and setting rows are now all created once and
  only toggled active/inactive in place. Nothing rebuilds the page on click any more, so no frame
  can ever show the whole list relaid out (which is what made every value box flash white).
- Fixed expanding a setting wiping its own name (the row label was being replaced by the chevron
  instead of having its prefix swapped).
- Sections stay collapsed by default, and their titles now carry a fold chevron that really does
  update when you open/close them (it previously never changed), so the open/closed state is
  always visible; their settings are also indented one level deeper, so a section can no longer be
  mistaken for a setting row.
- Mod names now have a hairline rule underneath, which also separates them clearly from the
  single-letter group headings (those got dimmer and smaller).
- Removed the temporary layout/scroll diagnostics used while chasing the scrollbar overlap; the
  released build only logs meaningful events.

v1.4.0 changelog
----------------
- Minimalism pass, driven by one rule: every element must do something.
  * Removed pure decoration: the coloured accent bar on mod rows, the item-count badges, the
    arrows on every setting row (a single chevron is kept only on expanded rows), the fading
    rule on letter separators, and the per-setting Reset/Copy buttons (the per-mod
    "Copy all"/"Reset all" already covers that use).
  * The two per-mod actions became plain text buttons instead of grey filled blocks, and the
    rebind control now uses the same light value-box style as numeric fields.
  * Palette reduced to two text tones (primary 0.86 / secondary 0.52) plus one light value box
    (0.74 fill, 0.10 text) - no accent colours, no permanent row backgrounds.
  * Density: row height 38 -> 32, spacing 4 -> 2, indent 14 -> 10, control column 220x30 ->
    200x28, section title 48 -> 30, letter row 28 -> 24.
  * Kept because they carry information: the hairline under section titles (grouping), the
    hover highlight (shows a row is clickable), the click sound, and click-to-read descriptions.

v1.3.1 changelog
----------------
- Fixed the "grey stripe / light pollution" look: rows no longer carry a permanent background tint
  (the hit area is fully transparent now and only lights up while hovered or pressed).
- Fixed the flash when clicking: expanding a setting no longer rebuilds the whole page (its
  description and Reset/Copy row are built once and just activated), and page rebuilds detach the
  old rows before destroying them so old and new rows can no longer draw on top of each other.
- Native click sound (SoundManager.ClickSound) is now played by every control the mod draws.
- Value boxes now use the vanilla look: light fill with dark centred numbers (plus a subtle top
  highlight and bottom shade) instead of the dark box with white text.

v1.3.0 changelog
----------------
- Layout refresh (the whole MODS page was rebuilt around a fixed control column):
  * section titles now render as an uppercase-ish header with a full-width hairline rule
    underneath and a right-aligned item-count badge; letter separators became thin grey
    labels with a fading rule, so the hierarchy reads at a glance;
  * every setting is a single 38 px row: label on the left, value control right-aligned in
    one unified 220 px column (toggles, dropdowns, hotkey buttons and numeric boxes all line
    up, instead of the old ragged mix of 200/280 px controls);
  * numeric rows no longer waste two lines: the range hint moved into the label as dim small
    text, and the value box got the reference look (dark fill, 1 px top highlight, centered);
  * noise reduction: per-setting description and Reset/Copy buttons are hidden until you
    click that row (arrow marker shows state) - the list body is one clean line per setting;
  * mod rows became card heads: 4 px hash-coloured accent bar, chevron, and expanded content
    indented 14 px; every row highlights on hover (previously there was no mouse feedback).

v1.2.5 changelog
----------------
- The scrollbar clearance is now recomputed every frame instead of on the 0.5 s self-heal
  tick, so a page that opens while vanilla has not settled its viewport width (which used to
  show a wide dark gap for about a second) is corrected in the same frame.

v1.2.4 changelog
----------------
- Scrollbar clearance is now an absolute target instead of an accumulated inset: the row's
  right edge is pinned 6 px (world) left of the scrollbar, expressed relative to the vanilla
  content page. Vanilla reserves the 17 px scrollbar strip in some code paths but not others,
  and it switches between those two states at runtime; the new formula yields a different
  local inset per state (8 vs 24 px) while the rows stay put on screen - so the list neither
  runs under the scrollbar (1.2.2/1.2.3 symptom) nor leaves the wide dark gap that the
  monotonic latch of 1.2.3 produced.

v1.2.3 changelog
----------------
- Fixed the row-width flicker introduced by 1.2.2: the overlap was measured against the
  already-inset row container, so it read "no overlap" the moment it inset and reset on the
  next frame (24/8 flip-flop). The measurement now uses a fixed reference (the vanilla
  content page edge), and the resulting inset is latched monotonically per page build, so
  the list width is stable no matter which scrollbar state vanilla is in.

v1.2.2 changelog
----------------
- Fixed the scrollbar covering the right edge of the mod rows after re-entering the
  settings. Measured with an in-game layout dump: some vanilla code paths reserve a
  17px strip for the scrollbar (viewport 433 -> row right edge 7px clear of it) while the
  "leave and re-enter settings" path does not (viewport 450 -> rows run 18px underneath
  the visible scrollbar). The mod list now insets itself from the scrollbar's actual
  on-screen rect, so rows never end up under it regardless of what vanilla does.

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
