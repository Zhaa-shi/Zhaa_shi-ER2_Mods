ER2 Limb Tweaks v2.13.101
=========================

Realistic limb injuries for Easy Red 2: limbs can be shot off, and a severed
limb bleeds the soldier out unless it is treated.

WHAT IT DOES
------------
- Corpse shooting: hitting a dead body severs the limb near the bullet impact
  (scored by damage + muzzle velocity x 0.08; default threshold 80).
- Living soldiers: accumulated damage on the same limb can sever it while the
  soldier is still alive and fighting (default 60 damage on one limb).
- Bleeding: a severed limb causes continuous blood loss until death. The game's
  own natural regeneration is blocked while the limb is gone.
- Behaviour limits on a surviving, wounded soldier:
    * severed leg(s)   - cannot stand or crouch (locked to prone)
    * severed right arm - surrenders, and cannot reload / pick up / equip /
                          throw weapons or use bandages
    * severed left arm  - cannot reload, pick up or equip long weapons
    * both arms severed - cannot pick up weapons at all
- Severed limbs are hidden visually (the soldier model loses the limb) without
  calling the game's own DetachLimb, so no instant bleed-out death is triggered.
- On-screen hints tell the player what is happening (native hint channel).

BLEEDING DETAIL (as implemented)
--------------------------------
- On severing, bleed starts at the highest rate.
- Bleed damage per second scales with the bandage counter, not with the number
  of severed limbs: full rate = 7/s, after one bandage = 4/s, after two = 1/s.
- The player-controlled soldier bleeds at half those rates (3.5 / 2 / 0.5 per
  second), which widens the window to react.
- Each bandage lowers the rate by half; bandages are rate-limited to one
  application per 2 seconds. The third and later bandages do nothing
  ("Bandage has no more effect") - the wound keeps bleeding at 1/s.
- Bleeding never fully stops: isBleeding is kept true so the game does not
  regenerate the soldier back to full health.
- Surrendered soldiers have their health taken over by the game (external
  damage does not apply), so a severed-limb surrender bleeds out on a 10 s
  timer instead.

CONFIGURATION
-------------
Edit BepInEx/config/er2.limbtweaks.cfg, or use the in-game Mod Manager
(Settings -> MODS). Changes apply immediately.

[General]
  enabled                Master switch (default: true).
[Corpse Shooting]
  corpseEnabled          Shooting a dead body detaches the limb near the bullet
                         hit (default: true).
[Living Soldiers]
  aliveEnabled           High damage on a limb can sever it while the soldier
                         is still alive (default: true).
[Bleeding]
  bleedEnabled           Severed limbs cause continuous blood loss until death.
                         Disable to keep severing without the bleed-out
                         (default: true).
  affectsPlayer          Apply severing and bleeding to the player-controlled
                         soldier. Disable to make the player immune while AI is
                         still affected (default: true).
[Limbs]
  damageNeeded           Accumulated damage on the same limb required to sever
                         it, alive (default: 60).
  corpseDamageThreshold  Accumulated sever score (damage + muzzle velocity
                         x 0.08) on the same corpse limb (default: 80).

HUD HIDING
----------
This mod's on-screen hints obey the "Hide Anything" HUD-hide contract: the
mod appears as its own checkbox under "Hide Other Mods UI" in the in-game Mod
Manager.

INSTALLATION
------------
1. Install BepInEx 6 (IL2CPP) for Easy Red 2 and run the game once so the
   interop assemblies are generated.
2. Copy ER2_LimbTweaks.dll into <Game>\BepInEx\plugins\.
3. Launch the game - "Loading [ER2 Limb Tweaks 2.13.101]" in the log means
   success.

REQUIREMENTS
------------
- Easy Red 2 with BepInEx 6 (IL2CPP).

NOTES
-----
- Severing a living soldier's limb on purpose is not done through the game's
  DetachLimb (which carries its own 20-30/s bleed-out and is always fatal);
  the mod hides the limb and drives the health itself in small per-frame steps.
- Low-frequency functional logs only; debug logging is off by default.

Shout outs: the Easy Red 2 modding community.
