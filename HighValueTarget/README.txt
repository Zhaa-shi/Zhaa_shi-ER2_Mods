ER2 Veteran HVT v1.2.2
=======================

Real battles are won by a handful of exceptional men - and in this mod,
exceptional men get MARKED. Every kill on the battlefield is tracked; the
more a unit kills, the higher its VETERAN LEVEL rises and the deadlier it
gets. Veterans are marked as HIGH-VALUE TARGETS, the side they have been
slaughtering focuses its fire on them, and putting them down rewards you.

1. VETERAN LEVELS
   Every kill is attributed (bullets, grenades/explosions, melee,
   helmet headshots included; stolen-kill protection: if you hit a unit
   within the last 5 seconds, the kill counts for you).
   - Every 5 kills (default) = +1 veteran level, up to a cap of 5.
   - Level 1 = marked as a High-Value Target.
   - Levels reset when the battle ends (and on your death).

2. LEVEL-BASED BUFFS
   Each level multiplies the veteran's stats (defaults):
   - Damage taken x0.85 per level (Lv5 ~0.44)
   - Damage dealt x1.18 per level (Lv5 ~2.3x)
   - Accuracy x1.30 per level (Lv5 ~3.7x)
   - Fire rate x1.15 per level (shorter shot interval)
   - Raise speed x1.15 per level (faster target-to-first-shot; the game
     has no separate raise API, so it scales the engage delay)
   - Move speed x1.05 per level
   - Level 1+: ignores suppression, never surrenders.
   - Veteran drivers drive faster (tanks/vehicles, +10% per level,
     capped at 1.6x).

3. FOCUS FIRE (hatred)
   The side a veteran has been killing prioritizes it:
   - Enemy AI within 500m (default) treat the marked unit as their best
     visible enemy - they concentrate fire while still moving, using
     cover and reloading normally.
   - If YOU are a veteran, the enemy team converges on you.

4. TRAITOR MECHANIC (optional, on by default)
   - The player CAN damage allies (friendly-fire protection is bypassed
     for this mod only), and after 2 friendly kills (default) you are
     marked a TRAITOR: your own side turns on you and actively chases
     you down. Traitors show no head marker.
   - Turning this OFF fully disables the mechanic: friendly fire is
     never counted, and any existing traitor mark is cleared within a
     second.

5. MARKERS & RENDERING
   - Deep-red (enemy) / deep-blue (friendly) diamond markers with white
     Roman numerals (I-V); both sides shown at once.
   - NEW (v1.2.0): markers are true 3D world-space billboards - they sit
     above the unit in the scene with a constant readable on-screen size
     at any range, and are occluded by the world like any 3D object
     (a marker behind cover is covered by it).
   - NEW (v1.2.0): vehicle crews merge into ONE marker above the vehicle
     (showing the highest crew level) instead of one marker per
     crewman, and vehicle-weapon kills (tank guns, coax MGs) are
     credited to the ENTIRE crew so the whole crew levels up together.
   - No persistent minimap in the game: press M - marked units appear as
     colored diamonds right next to the game's own unit markers (one
     icon per vehicle for crews).
   - Being marked: red flash + toast warning + persistent level bar.
   - Eliminating a marked unit: toast + golden flash + kill sound.
   - Leveling up yourself: single toast (level-up + marked warning
     merged into one message).

6. HIDE ANYTHING COMPATIBILITY
   Works with the "No Interaction Hints" (Hide Anything) mod: ticking
   this mod's checkbox in its settings hides this mod's markers/icons/
   hints along with the rest of the HUD.

INSTALLATION
------------
1. Requires BepInEx 6 for IL2CPP (Easy Red 2).
2. Copy ER2_VeteranHVT.dll into <Game>\BepInEx\plugins\
   (keep the ER2_VeteranHVT folder next to it - it contains the kill
   sound).
3. Launch the game once, then adjust the settings either in the in-game
   Mod Manager page (preferred) or in
   <Game>\BepInEx\config\er2.highvaluetarget.cfg

SETTINGS
--------
[General]
  Enabled            - master switch
  ApplyInMultiplayer - also apply in multiplayer (default: singleplayer)

[Veteran]
  KillsPerLevel     (1-50)  - kills per veteran level (5)
  MaxLevel          (1-10)  - level cap (5)
  PlayerHitWindow (0.5-30)  - stolen-kill protection window (5)
  FriendlyKillsToMark (1-20)- friendly kills to become a traitor (2)
  TraitorFeature           - traitor mechanic on/off (true)
  DmgTakenPerLevel (0.5-1)  - damage-taken multiplier per level (0.85)
  DmgDealtPerLevel (1-2)    - damage-dealt multiplier per level (1.18)
  AccPerLevel      (1-2)    - accuracy multiplier per level (1.30)
  FireRatePerLevel (1-2)    - fire-rate multiplier per level (1.15)
  RaisePerLevel    (1-2)    - raise-speed multiplier per level (1.15)
  SpeedPerLevel   (1-1.5)   - move-speed multiplier per level (1.05)
  SuppressionImmune         - veterans ignore suppression (true)
  NeverSurrender            - veterans never surrender (true)

[Focus]
  FocusEnabled       - marked unit's hated side prioritizes it (true)
  FocusRadius (10-2000)     - focus range in meters (500)
  FocusChance (0.05-1)      - kept for compatibility (unused)

[Visual]
  ShowMarkers       - 3D diamond markers above veterans/vehicles;
                      vehicle crews share one marker (true)
  MarkerRange (10-1000)     - marker display range in meters (500)
  MiniMapIcons      - icons on the M map next to unit markers (true)
  PlayerWarnings    - hints/flash when you get marked (true)
  PlayerIndicator   - persistent level bar while marked (true)
  KillFeedback      - toast/flash/sound when you eliminate a marked unit (true)
  LevelUpFeedback   - toast when you level up (true)

[Debug]
  debugLog          - verbose diagnostic logging; enable temporarily when
                      troubleshooting (false)

COMPATIBILITY
-------------
- Hooks the game's own kill/damage/accuracy/targeting entry points;
  no vanilla files are modified. Remove the DLL to uninstall.
- Friendly-fire protection is only bypassed for the player while the
  TraitorFeature is on.
- NEW (v1.2.1): a public API - ER2VeteranHVT.VeteranApi - lets other mods
  read and write veteran levels/kills (GetLevel, SetLevel, SetKills,
  ApplySquadLevel, GetSquadKills, GetSquadMaxLevel, GetSquadAliveCount).
  This is what makes veteran ranks PERSISTENT: a campaign mod (such as
  ER2 Conquest) can pour a unit's saved rank into freshly spawned
  soldiers at the start of a battle, and read the results back when the
  battle ends. Nothing changes in normal play; the API is inert unless
  another mod calls it.

CHANGELOG
---------
v1.2.2
  - Fixed the counter-intuitive "small up close, huge far away" marker
    scaling. Markers now use a FIXED world size (0.8 m), so their
    on-screen size is pure perspective: bigger as you approach, smaller
    as you move away, just like any real object.
    (The old code aimed for a constant on-screen size, which in practice
    read as too small at close range and over-inflated at long range.)
  - Added the standard debug log switch: [Debug] debugLog (default
    false). Set it to true to emit diagnostic logs for marker
    reconciliation, kill attribution and traitor decisions.

v1.2.1
  - Added the public VeteranApi facade so other mods can read/write
    veteran levels and kills (enables persistent, cross-battle ranks in
    campaign-style mods). No gameplay change on its own.

v1.2.0
  - Head markers are now true 3D world-space billboards with a constant
    on-screen size at any range, occluded by the world like any 3D
    object (no more screen-space overlay).
  - Vehicle crews now share ONE marker above the vehicle, showing the
    highest crew level.
  - Vehicle-weapon kills (tank guns, coax MGs) are now credited to the
    entire crew, so the whole crew levels up together.
  - The M-map shows one icon per vehicle for crews.
  - Turning the traitor mechanic off now fully disables it (friendly
    fire is never counted, existing traitor marks are cleared).

Shout outs: the Easy Red 2 modding community.
