# ER2 Hide Anything

> Part of the [ER2 Mods](../README.md) collection.

Hide anything on your HUD - check boxes, done. No hotkeys to remember:
each UI element has its own checkbox in the in-game Mod Manager, checked
items are hidden, always, strictly.

## What You Can Hide

- Interaction hints (the original F5 feature)
- Notification popups
- Objective banners & short messages
- Player HUD (ammo, weapon, squad)
- Phase bar
- Objective indicator & mission status
- Map & minimap
- Vehicle HUD
- Misc UI (version, tutorial, prompts)
- Hitmarkers & crosshair
- Blood splash effect
- Chat messages
- Scope overlay
- World markers (following markers)

## OTHER MODS' UI (cross-mod support)

Works with compatible mods (Limb Tweaks, Weather Control, Veteran HVT,
Unit Inspector, ...): each one gets its own checkbox under
"Hide Other Mods UI" - check a mod to hide its UI along with yours.
Mods are detected the moment they are installed (pre-registered); no
waiting for the first battle. Uncheck = that mod's UI stays visible
while everything else hides. Per-mod, independent control.

## v4.5.4

- Compatibility with game build 2.1.x, which removed the PhaseBarGUI class:
  the type is now looked up directly in Assembly-CSharp (instead of letting
  AccessTools scan the new game modules and log exceptions), and every
  PhaseBar-related patch is skipped cleanly when it is absent. No more
  TypeLoadException / failed plugin load on the updated game.

## v4.5.3

- New mods (Veteran HVT, Unit Inspector) are pre-registered: their
  toggles appear in Mod Manager immediately after install, no need to
  enter a battle first.
- Fixed: chat hiding used a wrong field name (chatRoot -> chatContent)
  and silently did nothing.

## Installation

1. Requires BepInEx 6 for IL2CPP (Easy Red 2).
2. Copy ER2_NoInteractionHints_DoneProMaxEnd.dll into <Game>\BepInEx\plugins\
3. Launch the game, open Settings -> Mod Manager -> ER2 Hide Anything,
   check what you want to hide.

## Settings

**[General]**

  enabled - master switch for all hiding (default: true)

All checkboxes live in the in-game Mod Manager (no manual cfg editing
needed); they are also visible in
`<Game>\BepInEx\config\com.ryan.er2.nointeractionhints.cfg`

## Compatibility

- Hides elements via the game's own UI paths; no vanilla files are
  modified. Remove the DLL to uninstall.
- Other mods integrate via the documented reflection contract
  (HudCompat.IsHudHidden / HudEnabled-HudVisible-ShowHud fields).

Shout outs: the Easy Red 2 modding community.
