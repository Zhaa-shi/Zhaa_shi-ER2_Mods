ER2 Hide Anything v4.5.3
========================

Hide anything on your HUD - check boxes, done. No hotkeys to remember:
each UI element has its own checkbox in the in-game Mod Manager, checked
items are hidden, always, strictly.

WHAT YOU CAN HIDE
-----------------
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

OTHER MODS' UI (cross-mod support)
----------------------------------
Works with compatible mods (Limb Tweaks, Weather Control, Veteran HVT,
Unit Inspector, ...): each one gets its own checkbox under
"Hide Other Mods UI" - check a mod to hide its UI along with yours.
Mods are detected the moment they are installed (pre-registered); no
waiting for the first battle. Uncheck = that mod's UI stays visible
while everything else hides. Per-mod, independent control.

v4.5.3
------
- New mods (Veteran HVT, Unit Inspector) are pre-registered: their
  toggles appear in Mod Manager immediately after install, no need to
  enter a battle first.
- Fixed: chat hiding used a wrong field name (chatRoot -> chatContent)
  and silently did nothing.

INSTALLATION
------------
1. Requires BepInEx 6 for IL2CPP (Easy Red 2).
2. Copy ER2_NoInteractionHints_DoneProMaxEnd.dll into <Game>\BepInEx\plugins\
3. Launch the game, open Settings -> Mod Manager -> ER2 Hide Anything,
   check what you want to hide.

SETTINGS
--------
[General]
  enabled - master switch for all hiding (default: true)

All checkboxes live in the in-game Mod Manager (no manual cfg editing
needed); they are also visible in
<Game>\BepInEx\config\com.ryan.er2.nointeractionhints.cfg

COMPATIBILITY
-------------
- Hides elements via the game's own UI paths; no vanilla files are
  modified. Remove the DLL to uninstall.
- Other mods integrate via the documented reflection contract
  (HudCompat.IsHudHidden / HudEnabled-HudVisible-ShowHud fields).

Shout outs: the Easy Red 2 modding community.
