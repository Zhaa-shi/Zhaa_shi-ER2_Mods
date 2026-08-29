ER2 Veteran HVT v1.1.26
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

5. MARKERS & RENDERING
   - Deep-red (enemy) / deep-blue (friendly) diamond markers with white
     Roman numerals (I-V) above marked units; both sides shown at once.
   - Markers become semi-transparent when the unit is behind cover.
   - No persistent minimap in the game: press M - marked units appear as
     colored diamonds right next to the game's own unit markers.
   - Being marked: red flash + toast warning + persistent level bar.
   - Eliminating a marked unit: toast + golden flash + kill sound.
   - Leveling up yourself: single toast (level-up + marked warning
     merged into one message).

6. HIDE ANYTHING COMPATIBILITY
   Works with the "No Interaction Hints" (Hide Anything) mod: pressing
   F5 hides this mod's markers/icons/hints along with the rest of the
   HUD. A per-mod toggle appears in Hide Anything's settings.

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
  ShowMarkers       - diamond markers above veterans (true)
  MarkerRange (10-1000)     - marker display range in meters (500)
  MiniMapIcons      - icons on the M map next to unit markers (true)
  PlayerWarnings    - hints/flash when you get marked (true)
  PlayerIndicator   - persistent level bar while marked (true)
  KillFeedback      - toast/flash/sound when you eliminate a marked unit (true)
  LevelUpFeedback   - toast when you level up (true)

COMPATIBILITY
-------------
- Hooks the game's own kill/damage/accuracy/targeting entry points;
  no vanilla files are modified. Remove the DLL to uninstall.
- Friendly-fire protection is only bypassed for the player while the
  TraitorFeature is on.

Shout outs: the Easy Red 2 modding community.
