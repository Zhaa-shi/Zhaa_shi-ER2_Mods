# ER2 Hide Anything v4.5.2 — Check Boxes, Hide Everything

> **v4.5.2:** new compatible mods (Veteran HVT, Unit Inspector) are pre-registered — their toggles appear in Mod Manager immediately after install instead of after the first battle. Also fixed chat hiding (wrong field name, silently did nothing).

## Description
Hide anything on your HUD by checking boxes — no hotkeys to remember. Every UI element has its own checkbox in the in-game Mod Manager; checked items are hidden, always, strictly. It also hides the UI of compatible mods, per-mod and independently.

## Installation instructions
1. Requires BepInEx 6 (IL2CPP) for Easy Red 2.
2. Drop `ER2_NoInteractionHints_DoneProMaxEnd.dll` into `Easy Red 2\BepInEx\plugins\`.
3. Launch the game, open Settings → Mod Manager → ER2 Hide Anything and check what you want to hide.

## Main features
- **Checkbox-based, zero hotkeys:** interaction hints, notifications, objective banners, player HUD, phase bar, objectives, map/minimap, vehicle HUD, misc UI, hitmarkers & crosshair, blood splash, chat, scope overlay and world markers — each with its own toggle.
- **Cross-mod UI hiding:** compatible mods (Limb Tweaks, Weather Control, Veteran HVT, Unit Inspector, ...) each get their own checkbox under "Hide Other Mods UI". Detected immediately on install (pre-registered) — no need to enter a battle first. Uncheck a mod and its UI stays visible while everything else hides.
- **Strict enforcement:** hidden elements are re-hidden every frame if the game tries to show them again.
- **Master switch:** one toggle disables all hiding at once.
- **No vanilla files touched:** hides via the game's own UI paths; remove the DLL to uninstall.

## Requirements
- Easy Red 2 (Steam)
- BepInEx 6 for IL2CPP

## Shout outs
- The Easy Red 2 modding community.
- BepInEx / Harmony for IL2CPP.
