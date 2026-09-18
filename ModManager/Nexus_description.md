# ER2 Mod Manager v1.5.1

> **v1.5.1:** removed a leftover developer-only test page that could leave the MODS page showing four dead rows (an empty box, a toggle and a slider that did nothing, and a box reading "click"). Added a "Debug" switch, off by default, so release builds stay quiet.

> **v1.5.0:** no page rebuilds on click (so no more white flash), always-visible fold chevrons on mods/sections/settings, sections collapsed by default with their settings indented one level deeper, per-setting state no longer leaks between mods, uniform label size with ellipsis instead of font shrinking, smaller value controls, and a hairline rule under mod names. The temporary diagnostics are gone from this build.

> **v1.4.0:** minimalism pass — decoration removed (accent bars, count badges, per-row arrows and per-row Reset/Copy), palette reduced to two text tones plus one light value box, and tighter row spacing. Only elements that carry information remain.

> **v1.3.1:** polish pass — no more permanent row tinting ("light pollution"), no flash when clicking a setting (expansion is in-place instead of a page rebuild), native click sounds on every control, and vanilla-style light value boxes with dark numbers.

> **v1.3.0:** layout refresh — single-line settings rows with one unified right-hand control column, section headers with hairline rules and item counts, dark value boxes with a top highlight, hover feedback, card-style mod rows with colour accents, and per-setting descriptions/Reset/Copy tucked behind a click so the list stays clean.

> **v1.2.5:** the scrollbar clearance is recomputed every frame, so a list that opens before vanilla settles its viewport width no longer shows a wide dark gap for a second.

> **v1.2.4:** scrollbar clearance is pinned to an absolute on-screen target, so the mod rows stay clear of the scrollbar in both of the vanilla layout states (no overlap, no flicker, no oversized gap).

> **v1.2.3:** row-width flicker fix (the scrollbar clearance is now measured against a fixed reference and latched, instead of being re-measured against the already-inset list).

> **v1.2.2:** fixed the scrollbar overlapping the mod rows after leaving and re-entering the settings (the row list now keeps clear of the scrollbar by its real on-screen position).

> **v1.2.1:** compatibility fix for the 2026-09-12 game update — right-arrow paging works again (the game changed a native settings method signature, which had silently broken the paging hook), plus hardened MODS page layout so rows/scrollbar no longer overlap after re-entering the settings.

> **v1.2.0:** alphabetical mod list with letter section headers, "Easy Red 2"/"ER2" prefixes stripped for sorting, name truncation by measured width, two-way page looping with native sounds, and a batch of stability fixes (empty page after fast flipping, scrollbar overlap after re-entering settings, free-form string settings now editable).

An in-game settings manager for Easy Red 2 BepInEx mods: adds a native-looking MODS page to the game's settings menu where every plugin's config can be tweaked live — with **zero hardcoded translation tables**, so it automatically works with any mod you install, now and in the future.

## Description

- Adds a **MODS** page inside the game's native settings (Esc menu) — every loaded BepInEx plugin's options appear as vanilla-style controls: toggles, dropdowns, sliders and numeric inputs.
- **Fully automatic, zero maintenance**: the manager auto-discovers every loaded plugin and auto-renders whatever config entries each one exposes. Config labels are humanized from their key names (`enableAIVaulting` → `Enable AI Vaulting`), so third-party and future mods show readable names without any per-mod lookup table.
- **Alphabetical mod list** with letter section headers; "Easy Red 2"/"ER2" prefixes are stripped for sorting and grouping (the full name is still shown when a mod is expanded).
- Mods with no config entries still appear as read-only "installed" entries, so you can see everything that's loaded.
- Controls are cloned from the game's own settings UI, so it looks and feels like stock.
- **Staged auto-save**: changes are saved automatically when you leave the settings.
- Handles bool / int / float / string / KeyCode entries — free-form strings get a text input, hotkeys are re-bound by clicking the button and pressing the new key, and every setting is guaranteed to render.

## Installation instructions

1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy `ER2_ModManager.dll` to `<Easy Red 2 folder>/BepInEx/plugins/`.
3. Launch the game, open Settings (Esc) → the MODS page lists every plugin's configuration.

## Main features

- Native-styled MODS page cloned from the vanilla settings UI.
- Auto-discovers every loaded BepInEx plugin and renders all its config entries — no hardcoded mod list.
- Alphabetical list with letter headers; ER2/Easy Red 2 prefixes stripped for quick lookup.
- Humanized config labels (camelCase/snake_case → readable text) for any mod, including third-party ones.
- Toggle / dropdown / slider / numeric-input / free-text controls for all common config types.
- Numeric inputs with range hints (with engine-font fallback so digits always render).
- Hotkey capture rebinding (including enum-typed hotkeys).
- Per-entry copy & reset with flash feedback.
- Two-way page looping (first page ⇄ MODS page) with native click sounds.
- Staged auto-save; honors each plugin's master switch.
- Compatible with the game 2.1.x settings rework.

## Requirements

- Easy Red 2
- BepInEx 6 (IL2CPP)

## Shout outs

- Corvostudio for Easy Red 2 and its mod-friendly settings UI.
