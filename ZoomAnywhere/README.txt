ER2 Zoom Anywhere v1.0.1
=========================

Allows holding breath / weapon zoom in any stance and movement state.
Vanilla Easy Red 2 only lets you hold breath while standing still and aiming;
this mod removes that restriction and optionally adds an extra zoom multiplier.

Installation
------------
1. Install BepInEx 6 (IL2CPP) for Easy Red 2.
2. Copy ER2_ZoomAnywhere.dll into:
   <Easy Red 2 folder>/BepInEx/plugins/
3. Start the game. Default key is Left Shift.

Configuration
-------------
Open the in-game Mod Manager (MODS page) or edit BepInEx/config/er2.zoomanywhere.cfg:

- enabled: Master switch.
- anyMovement: Allow hold-breath zoom while moving / in any stance.
- withoutAiming: Also apply extra zoom while not aiming (hip fire).
- extraZoom: Extra zoom multiplier (1.0 = vanilla only, 1.5 = 1.5x closer).
- holdKey: Key that activates the zoom (default LeftShift).

Main features
-------------
- Zoom while walking, running, or in any stance.
- Optional hip-fire zoom when not aiming.
- Optional extra zoom multiplier, clamped 1.0-3.0.
- Does not stack with magnified scopes to avoid excessive zoom.

Requirements
------------
- Easy Red 2
- BepInEx 6 (IL2CPP)

Shout outs
----------
Thanks to the Easy Red 2 modding community for API research and testing.
