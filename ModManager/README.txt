ER2 Mod Manager - In-Game Mod Settings Manager
===============================================
Version 1.7.9 | Easy Red 2 (BepInEx 6 / IL2CPP)

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
- Favourite any mod with the star on its title row; favourites appear in a block at the top of the
  page and expand + jump to the mod when clicked.
- Compatible with the game 2.1.x settings rework (async page filling is handled).
- Third-party ConfigurationManager attributes are honoured: entries marked hidden (`Browsable = false`)
  stay out of the list, and a declared `Order` is respected (read by reflection, no dependency).
- Each mod shows its own identity (full name, GUID, version, setting count) on one dim line, and
  "Copy all" includes it - useful when reporting a problem.
- Remembers which setting groups you opened inside each mod, across game restarts (`Ui / rememberExpanded`, on by default). Mods themselves always start folded.

v1.7.9 changelog
-----------------
- **Lazy page build is now on by default** (`Ui / lazyBuild`). It was held back because inserting rows at
  expand time had produced "clicked a mod and nothing opened" three times; the root cause is fixed (the
  insertion point was being clamped away by its own safety net), every deferred build is self-checked and
  falls back to a full page build automatically if anything looks wrong, and the expand-window probe has
  verified the lazy path across four sample points. Opening the MODS page now costs roughly 35-100 ms
  instead of ~0.3-0.6 s for 970 rows. If you prefer the old behaviour, turn the option off - the full
  build path is unchanged.

v1.7.8 changelog
-----------------
- **The hotkey-conflict block got a header with a count and a Hide/Show button.** The header always
  shows ("⚠ Hotkey conflicts: N"), so you can see how many conflicts exist even with the details
  hidden; the button collapses or expands the detail rows in place (no page rebuild) and the choice is
  written to `Ui / hotkeyWarnings` and saved immediately, so it survives restarts. The same entry also
  appears in ER2 Mod Manager's own settings list, so both places stay in sync.
- **Fixed the reason several conflicts could read as "only one".** The detail rows' height was estimated
  assuming a ~500 px line width, but the label actually has about 400 px available (row 425 minus the
  label margins) - longer warnings were allocated too few lines, so their text overflowed the row bottom
  and stacked into the next warning, visually merging several conflicts into one blob. The available
  width is now measured from the content page (with a conservative fallback), so each warning gets the
  lines it needs. 1.7.7's one-line-per-key aggregation removed the duplicated pairings on top of that.

v1.7.7 changelog
-----------------
- **The hotkey-conflict warnings were audited against every config file actually installed**, and two
  defects fell out. First, the conflict scan was more permissive than the renderer: any String entry
  whose name happens to contain "key" was treated as a hotkey and scanned. The concrete case on this
  install is More Physics' `ExcludedNameKeywords` (a comma-separated list of prefab names) - it joined
  the comparison, and two mods with the same list default would have produced a fake "hotkey conflict".
  String entries now also have to look like a key (a single word such as F5 / Space / Mouse0, an A+B
  combo, or empty), so name lists, paths and sentences are out. Second, three bindings on the same key
  used to produce two lines ("A and B", "A and C") that never mention B vs C; conflicts are now
  aggregated to one line per key listing every binding, which also surfaces self-conflicts inside a
  single mod.
- With debug logging on, the scan reports how many conflict groups it found (`hotkey conflict scan: N`).
- For reference, this install currently has three real conflicts, all listed on the page: **F**
  (Advanced Combat Movement "Follow Or Marker" × ER2 Battlefield Commander "Key Scatter"), **F9**
  (ACM "Restart Mission" × Battlefield Commander god key) and **G** (Battlefield Commander "Key Pack"
  × ER2 Universal Generation panel key).

v1.7.6 changelog
-----------------
- **The fade now covers page turns, setting groups and individual entries too**, not just expanding a
  mod. A whole page (entering the MODS page, leaving and coming back, or a rebuild) fades in through a
  single canvas group on the page container; a setting group and an entry's description line fade the
  same way as a mod's settings do. Everything is tied to the same duration (`Ui / revealMs`).
- **Page-turn flash hardening.** The per-frame cleanup that removes native rows the game's asynchronous
  page filler drops into our page used to `Destroy` them - and destruction only takes effect at the end
  of the frame, so those rows were still drawn for one frame, which is exactly what a page-turn flash
  looks like. They are now deactivated first (immediate) and destroyed after. The page path also
  re-checks row hit areas for the opaque-white state that a disabled row can come back with (the same
  1.5.13 white block, which recurs on any "deactivate then reactivate" path - and building a page is
  exactly that, for a hundred rows at once); wrong rows are corrected on the spot and reported at
  warning level.
- **The page fade cannot replay in a loop**: if the page is rebuilt repeatedly by the self-heal paths it
  is faded once, not per rebuild (a rebuild burst would otherwise read as the page repeatedly fading
  out, which is worse than a flash). The fade is skipped entirely for page builds above 300 rows - those
  get a single hidden frame instead, so the animation can never become the expensive option.
- Diagnostics: with `Debug / debugLog` on, **every page build is now probed** the same way expansions
  are (same frame, +0.03 s, +0.13 s, +0.36 s), reporting how many distinct row positions the page has
  settled on, the worst row hit-area alpha, the content height, the number of foreign (non-mod-manager)
  objects found in the page, and the frame number. The tab and page-build log lines carry the frame
  number too, so a build that lands a frame later than the tab press is visible rather than inferred.
- Verified in 1.7.5's log: expanding a mod produces no unsettled rows, no opaque hit areas and a
  constant content height across all four sample points - the expand path is clean, which is what made
  the remaining page-turn flash stand out.

v1.7.5 changelog
-----------------
- **Expanding a mod no longer flashes.** With the lazy page build, expanding a folded mod builds that
  mod's whole setting block on the spot. The rows are laid out within that frame, but a few appearance
  details settle a beat later, so one or two frames could be drawn half-formed - the "every option
  blinks once" you see. Newly built rows now **fade in** instead of appearing instantly (`Ui / revealMs`,
  200 ms by default): the space opens immediately and the content follows it, which turns the expand
  into a transition rather than a blink. A canvas group does not participate in layout, so the rows
  still take their space and the rows below do not jump an extra time.
- **Row hit areas are re-checked whenever a mod is expanded.** A disabled row loses its renderer colour
  (the graphic clears it on disable), and it is re-applied on re-enable by the selectable's state
  transition; if that chain misses a beat, the hit area comes back **opaque white** - the same white
  block that was fixed in 1.5.13. Rows that are wrong are corrected on the spot, and the log reports it
  at warning level (a fault, not a diagnostic, so it is visible with debug logging off).
- `Ui / revealAnim` (on by default) switches the fade off if you prefer it instant. The fade is skipped
  for expansions of more than 300 rows, so a very large mod still appears instantly instead of risking a
  stutter - animation must never be the more expensive option.
- Diagnostics: with `Debug / debugLog` on, every expand logs an `[MM-flash]` probe at four points in time
  (same frame, +0.03 s, +0.13 s, +0.36 s) giving how many distinct row positions the layout has settled
  on, the worst row hit-area alpha, and the content height - the three physical causes of a flash, so a
  report can name the cause instead of guessing at it.

v1.7.4 changelog
-----------------
- **Fixed the lazy page build inserting a mod's settings at the bottom of the list instead of under its
  title** - which is what the "I clicked it and nothing opened" report was. The insertion point was
  computed correctly (right after the mod's title row) but then passed through a safety clamp that read
  "if the target position is before the newly created rows, use that instead". Since the newly created
  rows are always appended at the end of the list, the title row's position is always *before* them, so
  the clamp fired every single time and put the rows back at the end. The clamp is gone; the position is
  now trusted, and the only remaining guard is the structural self-check, which caught this exact problem
  in testing (it reported "row[0] not contiguous" and fell back to the normal page - which is why
  clicking again a little later worked).
- Note that this defect could only appear on the lazy path: when the page is built the normal way, the
  rows are created immediately after their title, so the clamp was a no-op there - which is why the
  option has always worked fine when off.

v1.7.3 changelog
-----------------
- **Opening the settings page is much cheaper with `Ui / lazyBuild` - and the option is now fail-safe.**
  Building the page creates every setting of every mod up front: 970 rows, measured at 0.34-0.59 s on
  this machine, which is the pause you feel when entering the settings. With `lazyBuild` on, a folded mod
  keeps only its title row and its settings are built the first time you expand it. This was tried before
  and withdrawn three times because "clicking a mod showed nothing"; those failures are now understood and
  fixed, and - more importantly - **the page now checks itself**: after a deferred build the mod verifies
  that the rows really sit directly under the mod's title and that the page height kept up with its
  content. If the check fails it retries, and after two failures it abandons the lazy path for the
  session and rebuilds the page normally, logging exactly what went wrong. Worst case is therefore one
  ordinary page build - the cost you already pay today - instead of a broken page.
  Still **off by default** until it has been confirmed on a running game.
- The page-build log now reports where the time goes (`bodies=..ms fav=..ms warn=..ms other=..ms`),
  so a report of "the settings page stutters" can be answered with data instead of guesswork.

v1.7.2 changelog
-----------------
- **A favourited mod now expands on the first click.** Clicking an entry in the favourites block only
  marked the mod as open without actually opening it (the rows stayed hidden), so the list looked
  untouched - and the next click on that mod's title merely undid that invisible state, so real content
  only appeared on the click after that. Both paths now go through a single function that sets the state
  and does the work (show the rows, flip the arrow, relayout), so one click is enough.
- **Hovering a favourite entry now reacts.** The only feedback was a row tint of 3.5% white, which is
  effectively invisible on a dark background; the entry's text now brightens under the pointer, using the
  same mechanism as the star buttons.
- **Small buttons react to the pointer too.** The star, \"Restore\", \"Reset all\" and \"Copy all\" buttons
  had their hover/press transition switched off (a workaround dating back to 1.3.1), so hovering them did
  nothing at all. They now brighten on hover and dim when pressed.
- **A favourited mod's star is gold.** Starred and unstarred used the same dim grey, so the list gave no
  hint of which mods were already saved.

v1.7.1 changelog
-----------------
- **Fixed: the favourites block was invisible.** It occupied its space at the top of the page but showed
  nothing - neither the heading nor the saved mods. The cause was the label rectangles: they were
  positioned with the vertical anchors pinned to the middle and both offsets set to zero, which leaves
  the rectangle zero pixels tall - and a text field with no height draws nothing at all. The rows were
  active and laid out the whole time, which is why the page was simply pushed down by a blank strip
  instead. Those labels now use the same fixed-height sizing as the mod info line. The same defect was
  present on the \"default value ...\" label of a changed setting's actions row, so that is fixed as well.
- **Mods always start folded.** Which mods you had open used to be part of the saved state, so a mod you
  expanded in one session would open expanded on the next launch and push everything below it down.
  That is no longer saved or restored. Which **setting groups** you opened inside a mod is still
  remembered - that part is worth keeping, since otherwise every group has to be re-opened by hand.

v1.7.0 changelog
-----------------
- **New: favourite mods.** Each mod's title row now carries a star on its right. Click it and the mod is
  added to a **Favourite mods** block at the top of the page; clicking an entry there expands that mod
  and scrolls straight to it. Favourites live in `BepInEx/config/er2.modmanager.favorites.txt` and
  survive restarts (up to 24). Click the star again to remove one.
- Removed the search box that briefly existed in 1.6.x. In practice, filtering this list by keyword
  turned out to be the wrong tool for the job - it disturbed the page far more than it helped, since a
  single keystroke could restructure several hundred rows at once. Favourites cover the same need
  without moving anything: the mods you actually reach for sit one click away at the top.

v1.5.25 changelog
----------------v1.5.25 changelog
-----------------
- **Setting groups default to collapsed again (back to how they always were).** 1.5.23 had flipped them
  to expanded while hunting down "clicking a mod shows nothing", but the real cause of that turned out to
  be the expanded-state memory having been switched off in 1.5.20 - and that memory is on again, so the
  groups you open are remembered across sessions. With the memory working, the original default
  (collapsed) no longer leaves a mod looking empty. Expanding a mod lists its groups, and the ones you
  open stay open next time.

v1.5.24 changelog
-----------------
- **The experimental "lazy page build" is now off by default and marked as not recommended.** It made
  opening the MODS page much cheaper (46 rows / ~60 ms instead of 942 rows / ~300 ms) by creating a
  folded mod's settings only at the moment you expand it - but inserting rows at that moment has now
  produced "clicking a mod shows nothing" three separate times (1.5.9, 1.5.19, 1.5.23).
  One concrete defect is fixed in this build: the row that marks where to insert was recorded as
  "whatever the container's last child happens to be", and when that reference went stale the new rows
  were appended to the very end of the list instead of under the mod's title - the content existed, but
  sat far below what you were looking at. It now uses the title's own parent transform, which cannot
  drift. Even so, the switch stays off until it has been confirmed on a running game.
  Practical effect: opening the MODS page costs about 0.3 s again (the long-standing cost), and
  expanding is back to the plain show/hide that has always worked.

v1.5.23 changelog
-----------------
- **Setting groups inside a mod now start expanded, and the expanded-state memory is back on by default.**
  These are two halves of one problem. A mod with several setting groups used to open showing only its
  group headings - 4 to 7 dim lines, with every actual setting folded away one level deeper - so opening a
  mod looked like it had no contents at all. On top of that, 1.5.20 turned the saved-state memory off by
  default, reasoning that it only mattered across game restarts; that was wrong, because it was also the
  only place that state was ever kept, so every launch started from scratch. It is on again.
  Net effect: expanding a mod shows its settings directly. The group headings are still there and still
  collapsible if you prefer the compact view, and a group you collapse on purpose stays collapsed.

v1.5.21 changelog
-----------------
- **Fixed the intermittent flash.** The row inset - the padding that keeps rows clear of the scrollbar -
  is recomputed every frame, and the vanilla viewport alternates between reserving scrollbar space and not,
  so the target value flips (24 <-> 8, measured). Every flip wrote a new size and then called
  ForceRebuildLayoutImmediate, which re-laid the **entire column in the same frame** - that is the flash you
  see, and it explains why it is not constant: it only happens on a flip. The row width barely changes
  (426 <-> 425); what is actually needed is just telling the layout that the width changed, and Unity does
  that on its own during the normal layout pass. The forced rebuild is now kept only for the first apply
  (right after the page is built, before the geometry has settled) and the later flips merely mark the
  layout dirty.
- **Fixed "expanding a mod shows nothing" with the lazy page build.** The rebuild only covered the row
  container, but that container is itself a child of the content page and its position and height are
  governed by that outer layer - the rows were created, active, with a clean parent chain, and the content
  grew (all confirmed by diagnostics), yet the new rows landed outside the visible area. The relayout now
  rebuilds both layers with a canvas refresh and repeats once on the following frame, since a
  ContentSizeFitter inside a nested layout group often needs a frame to settle.

v1.5.20 changelog
-----------------
- **The expanded-state memory is now off by default.** Two concrete reasons. Within a single session that
  state was never lost in the first place - it lives in memory and survives page turns and reopening the
  settings - so the file on disk only ever mattered across game restarts, which is a small win. And it
  actively worked against the lazy page build: a mod remembered as expanded has to have its settings built
  the moment the page opens (Combat Tweaks alone is 70 rows), which is exactly the cost that switch is
  there to remove. The feature and its option (`Ui / rememberExpanded`) both stay - it just no longer
  participates unless you ask it to.

v1.5.19 changelog
-----------------
- **Optional lazy page building (`Ui / lazyBuild`, off by default).** Opening the MODS page builds every
  setting of every mod up front - 942 rows for 28 mods, measured at 0.3-0.4 s on this machine - which is
  the brief pause you feel when entering the settings. With this switch on, a folded mod keeps only its
  title row and its settings are built the first time you expand it, so the page opens with a fraction of
  the work. It stays **off by default**: an earlier attempt at this (1.5.9) produced "clicking a mod does
  nothing" - the handler ran, the rows existed, no exception, and the content stayed invisible - and that
  was never explained, so it is not the default until it is. With the switch on, the mod logs what it is
  doing (`[MM-lazy] ...`): rows created, where they were inserted, whether the container and rows are
  actually visible in the hierarchy, and the container's height against its preferred height.

v1.5.18 changelog
-----------------
- **Fixed the mod info line looking wrong.** The line under each mod title (GUID, version, setting count)
  was drawn with the same helper used for entry descriptions - and that helper estimates its height from
  the character count, always reserving at least two lines and assuming a narrow column. The info line
  therefore came out about twice as tall as it should be and pushed the whole list down. It is now a
  proper single line: fixed height, smaller font, truncation by measured width with an ellipsis, and it
  no longer repeats the mod name that the title above already shows (that repetition was most of its
  length).

v1.5.17 changelog
----------------
- **Labels no longer mangle common acronyms.** AI showed up as "Ai", HUD as "Hud", UI as "Ui", FOV as
  "Fov", and MG42 as "Mg 42". A short whitelist of well-known abbreviations (AI, UI, HUD, FOV, MG, SMG,
  AP, HE, WW, single letters like M/T/S, ...) now keeps them uppercase, and an abbreviation followed by a
  number is joined into one token (MG + 42 -> MG42, M + 1 -> M1). The test is deliberately a whitelist and
  not "keep every word that is fully uppercase": SCREAMING_SNAKE_CASE keys are common in config files and
  that rule would have turned MAX_COUNT into "MAX COUNT". Mods that declare no such keys are unaffected.
- **Third-party ConfigurationManager attributes are honoured.** Entries a mod marks `Browsable = false`
  (typically its internal or debug knobs) are no longer listed, and `Order` is respected when a mod
  declares it. Both are read by reflection, so the manager still works when ConfigurationManager itself is
  not installed. A mod that declares no Order keeps its own binding order - the list is only sorted when
  at least one entry actually asks for a position, since binding order is usually deliberate.
- **Every mod now says who it is.** Expanding a mod shows its full name, plugin GUID, version and setting
  count as one dim line, and "Copy all" starts with that same line - so a screenshot or a pasted config
  block identifies the exact mod and version without digging through the BepInEx log.
- **Expanded state survives a restart.** Which mods (and which sections inside them) you had open is
  written next to the config file and restored on the next launch - no more re-opening the same five mods
  every session. Switch it off with `Ui / rememberExpanded`.

v1.5.16 changelog
----------------
- The page-turn click sound is back, and the cause turned out to be timing rather than a failure. Moving
  the click to after the page work (1.5.15) still left the MODS page silent, while the vanilla pages
  clicked normally in between - and the click probe never reported one failure, so the call succeeded and
  the sound was simply swallowed. The page rebuild recreates a thousand-odd UI objects in a single frame,
  and a click played into that frame never comes out. Click sounds for page turns are now **scheduled
  instead of played**: the click is queued and picked up by the per-frame poll 0.25 s later, once the
  rebuild has settled and the audio side is no longer being disturbed.

v1.5.15 changelog
-----------------
- Second attempt at the silent page turn: the click was played before the page work started, so the
  rebuild that immediately followed destroyed the very thing that had just begun playing. Playback is now
  ordered after the page work - after the MODS page is rebuilt when entering it, and after the page
  switch when leaving it or looping back to the first page.

v1.5.14 changelog
-----------------
- Diagnostics only. The page-turn click had gone silent while every branch still called the click sound,
  so the only place a failure could hide was the empty catch wrapped around that call. It now logs any
  failure ("ModManager: ClickSound failed: ..."). That is also what ruled out an exception as the cause,
  and pointed the next two builds at playback timing instead.

v1.5.13 changelog
----------------
- Found the flash, and this time it is on camera. A frame-by-frame analysis of a screen recording
  (average screen brightness per frame) showed the jump precisely: 125 -> 344 -> 97, lasting two frames
  (~67 ms) at the moment the MODS page appears. The bright frame shows every row covered by a full-width
  white block - the transparent click area each row carries (a white Image whose visibility is controlled
  by a Button colour block with an invisible normal state).
- The cause: setting a Button's ColorBlock only stores the configuration, it does not paint it. At the
  moment the Button component is added, its OnEnable paints the canvas renderer with the DEFAULT colour
  block - normal = opaque white. Our transparent normal only gets applied on the first state change
  (say, the mouse crossing the row), so for the first two frames every click area on the page showed as
  solid white. That is the flash, and it is why it looked identical wherever it appeared: it always
  accompanied the moment rows were created or re-shown.
- Fixed by painting the normal colour onto the canvas renderer immediately after configuring the block,
  so the click areas are transparent from the frame they are created.

v1.5.12 changelog
----------------
- Reverted the deferred building introduced in 1.5.9. The game log proved the expand handler ran and the
  rows existed ("toggle mod 'X' -> open rows=118"), yet the content stayed invisible - and this started
  exactly with 1.5.9. Deferred building traded a guaranteed-correct page for paging speed, and that trade
  is not worth it: paging is fast again in 1.5.10+ for other reasons anyway (the per-frame rewrite loop
  is gone), so the risky half-measure goes. Page building is back to creating everything up front; clicks
  only toggle visibility, as before.
- Also removed the scroll-position restore added in 1.5.11 for the same reason: it was an unverified
  guess layered on top of a problem that was not yet understood, and it could move the viewport itself.
- Replaced both with one decisive diagnostic. After every expand it now logs how many of the rows are
  active, how many of those actually fall inside the viewport, where the rows sit on screen, and the
  scroll position: "ModManager: state mod 'X' rows=118 active=118 onScreen=118 rowY=... viewportY=...
  vnp=.. contentH=..". Whatever is still wrong, the next log pinpoints which of the three it is:
  rows not activated, rows outside the viewport, or rows visible-but-not-drawn.

v1.5.11 changelog
----------------
- The flash now has a concrete mechanism: expanding or collapsing anything changes the height of the
  scrollable content, and the scroll position is stored as a *fraction* of that height - so when the
  height changes, the same fraction points somewhere else and the whole list jumps for one frame. Every
  click was followed by exactly that "content height jump" in the log. Expanding and collapsing now
  remember the pixel offset of the viewport and restore it afterwards, so the list stays where you were
  looking instead of sliding under the cursor.
- Added one log line per expand/collapse (mod, section or setting, with how many rows it had). It is the
  only way to tell apart the two possible causes of "clicking does nothing": the click never reaching the
  handler, versus the handler running but having no rows to show.
- Note on the previous build: the row width no longer oscillates (measured 0-5 writes per 5 seconds
  instead of once per frame), and building the page now creates about 46 rows for 28 mods instead of
  every setting of every mod, so paging is responsive again.

v1.5.10 changelog
----------------
- Fixed a regression from the previous build: clicking one mod could expand another one. Deferred
  building used to remember each mod title's row index when the page was built, but as soon as another
  mod above it was expanded - which inserts rows - every remembered index below it shifted, so the rows
  were inserted under the wrong mod. The insertion point is now looked up from the title row at the
  moment of insertion, so it cannot go stale.
- Second attack on the flickering options. The row width inset was compared against the container's
  current size, but that size is owned by the parent layout, which recalculates it every frame - so the
  comparison kept reporting "changed", and every frame rewrote the width and forced a full relayout of
  the column. That is a per-frame relayout, which reads as flicker. It now compares against the last
  value it intended to write, so an unchanged target means no write and no relayout.
- Diagnostics changed shape: the inset log used to print one line and drop the rest, which hid exactly
  the thing that mattered - how often it was writing. It now reports the number of writes per 5-second
  window; a few is healthy, hundreds means it is still oscillating.

v1.5.9 changelog
----------------
- The flickering options had a measurable cause. Every click pushed a height onto the wrong object: the
  self-healing scroll code was being handed the row container instead of the settings page's own content
  object. The container carries a Content Size Fitter, so the value we wrote was recalculated away on the
  next frame and the height bounced back and forth (measured in the log: 1644 -> 1784 -> 1644), and every
  bounce relaid out the whole column. That is the flicker. The height is now always applied to the real
  content object, so it settles instead of oscillating.
- Paging was unresponsive because opening the page built every setting of every mod up front, including
  the ones inside folded-away mods - with twenty mods that is a thousand-odd objects recreated on every
  single page turn. Folded mods now keep only their title row; their settings are built the first time you
  actually expand them, and from then on they are shown and hidden in place as before. Nothing rebuilds
  the whole page.
- Page build cost is logged (rows, mods, built bodies, elapsed ms) so the improvement is measurable
  rather than assumed. This line is not behind the debug switch - it is the evidence for the paging
  complaint.

v1.5.8 changelog
----------------
- Found the flash, and it was not in the list at all: leaving the MODS page with the left arrow was
  handed to the game's own tab handler. The MODS page is appended after the last vanilla page, so the
  page index we hold (4) is out of range for the game, which only knows pages 0..3. Handing that back
  made the game refill the whole settings panel from an index it does not understand - a full repaint,
  which is the flash. The log shows it plainly: "tab left:pass-native cur=4 myIndex=4" on every exit.
  The MODS page now handles the left arrow itself and goes back to the last vanilla page, the exact
  mirror of "right arrow on the last vanilla page opens MODS". The game's out-of-range repaint is gone.
- Also closed a hole in the previous diagnostic: the rebuild that happens when the settings menu is
  reopened was not being logged, so "no rebuilds happened" only covered one of the two rebuild paths.
  Both are logged now.

v1.5.7 changelog
----------------
- Diagnostics only, no behaviour change. Still chasing the click flash: the 1.5.6 theory (page rebuilds)
  was checked against the game log and came up empty - not a single rebuild happened during the test, so
  the flash comes from somewhere else.
- Added a probe that detects rows being drawn at the same vertical position right after every forced
  relayout. That is what actually causes a white flash: for one frame the rows sit on top of each other
  and their light value boxes stack into a white block. If it happens, the log says
  "ModManager: rows stacked at one y after 'SelfHealScroll' - visible=.. sameY=.. containerH=..".
- The row-inset diagnostic used to print every frame, which would flood the log the moment diagnostics
  are switched on. It is throttled to one line every 5 seconds now, like the other diagnostics.
- Both probes only run with Debug / debugLog enabled (off by default).

v1.5.6 changelog
----------------
- Chasing the remaining click flash. The per-frame self-repair decided whether a page was "dead"
  purely from heights: container <= 101 px and content <= 201 px meant "the layout never ran", so it
  rebuilt the page. But the content height has a floor of 200 px (see FillContent), so a page that is
  genuinely SHORT - few mods installed, or most of them folded shut - always matched that test and got
  rebuilt every frame. A rebuild is precisely what makes the whole page flash white.
  The test now compares what the rows ask for against what actually got rendered: the page is only
  rebuilt when the requested height is clearly larger than the rendered one (the layout really did not
  run). A legitimately short page is left alone.
- Every rebuild is now logged with its reason and evidence, throttled to one line per reason per 5
  seconds: "ModManager: rebuilding MODS page (visible flash) reason=layout-dead(containerH=.. contentH=..
  preferred=..)" or reason=container-missing. This is not hidden behind the debug switch - a rebuild is
  a visible fault and has to be visible in the log.
- The freshly built page now has its layout finalised before the per-frame watchdog sees it, so the
  first frame can no longer trip that same check.
- Diagnostic: content height jumps of 40 px or more are logged behind the Debug switch
  ("ModManager: content height jump X -> Y"), to tell rebuild flashes apart from relayout flashes.

v1.5.5 changelog
----------------
- Changed settings are now marked: any value that differs from its default shows a " •" suffix on
  its label, so you can see at a glance what you have touched. The mark is recomputed from the same
  staging buffer the controls write to, so it appears/disappears as you change values, including
  before the change is written to disk.
- Added per-setting restore. Expanding a changed setting shows its default value and a Restore
  button, which rolls back that one setting only. Previously the only way back to a default was
  "Reset all" for the whole mod, which also wiped every other setting. Restoring updates the
  control in place (the page is not rebuilt, so nothing flashes).
- "Reset all" now needs two clicks. It used to reset every setting of a mod and, 0.8 s later, write
  them to disk with no way to undo it. The first click switches the button to "Confirm?" for three
  seconds and only a second click resets; letting it time out cancels.
- Settings whose own description says they need a restart (restart / reboot / 重启 / ...) now say so
  on their info line, and those are the changes players most often report as "I changed it and
  nothing happened" - many mods read their config once at startup. After such a change is saved, a
  hint says how many changes need a restart, and the log lists which ones
  ("ModManager: saved, N change(s) require a game restart to take effect: ...").
- Fixed long setting labels overflowing onto the value control on the native-template path: the
  truncated label was being overwritten with the untruncated text right after measuring, so the
  ellipsis never survived. Also removed the last page rebuilds (Reset all, and clearing a hotkey with
  "None"), which were the remaining sources of the click flash.

v1.5.4 changelog
----------------
- Restored the click sound while tabbing through a third-party page. The native tab methods play
  their own click sound, so any Prefix that returns false kills the sound along with the original
  call. Advanced Combat Movement's page code never plays a sound of its own, so the whole stretch
  from vanilla page #3 to its two pages was silent. Every branch where we know the native method
  will not run now plays the click sound ourselves; branches that do reach the native method stay
  untouched so the sound is never doubled. Tab navigation also logs the branch it took
  ("ModManager: tab <branch> cur=.. myIndex=.. thirdParty=..") to make this diagnosable.

v1.5.3 changelog
----------------
- Shares the settings tab chain with third-party "fake page" mods, starting with Advanced Combat
  Movement (Responsive Orders). That mod hijacks the right-arrow on the native page #3 and always
  returns false; since its DLL loads before ours (A < E) it used to run first, so the MODS page -
  which is appended after the last vanilla page - could no longer be reached by tabbing right.
  The MODS page is now reached from that mod's last page, and our own navigation no longer steals
  its page in return. Detection is reflection-only: with the other mod absent nothing changes.

v1.5.2 changelog
----------------
- Removed the NativeFull developer switch and the whole native-row page renderer behind it. It
  was the second leftover developer switch, and unlike the POC page it was self-locking: NativeFull
  is a setting of this mod, so it appears as a toggle on the MODS page itself - turning it on broke
  that page, and turning it back off was only possible from that same page. That is a trap, so the
  switch and its renderer (NativePage.cs) are gone. The MODS page is now built by one code path only.

v1.5.1 changelog
----------------
- Removed the leftover POC test page. It was a developer-only switch (NativePoc in the config
  file) that drew four hard-coded rows - poc.header, poc.toggle, poc.slider and poc.button - with
  raw untranslated labels and callbacks that only wrote to the log. With that switch on, the MODS
  page showed nothing but those four dead rows: an empty box, a toggle and a slider that did
  nothing, and a box reading "click" that did nothing. That is exactly the "mod menu is broken"
  report. The switch and the page are gone, so a release build can no longer reach them.
- Added a "Debug" / debugLog switch (default off, keep it off in release). The noisy page-build,
  template and value diagnostics now only print when it is on, so a release build stays quiet.

v1.5.0 changelog
----------------
- Fixed the white flash when clicking: mod, section and setting rows are now all created once and
  only toggled active/inactive in place, with visibility recomputed per level (mod -> section ->
  setting). Nothing rebuilds the page on click any more, so no frame can ever show the whole list
  relaid out (which is what made every value box flash white).
- Fixed expanding a setting wiping its own name (the row label was being replaced by the chevron
  instead of having its prefix swapped), and fixed the chevrons: they are always visible now
  (collapsed / expanded) and they really do update when you fold a section.
- Fixed descriptions leaking between mods: the per-setting state key used ConfigFile.ToString(),
  which BepInEx does not override, so identically named settings in different mods shared one key.
  The key is now the config file path.
- Sections stay collapsed by default and their settings are indented one level deeper, so a
  section can no longer be mistaken for a setting row.
- Row labels are plain text at one uniform size: the rich-text range hint was rendered literally
  (<color=...>) and long names ran underneath the value box. Ranges moved into the row's "info"
  line (shown when you click the row), and over-long names are ellipsized instead of being
  shrunk, so the list keeps a consistent type size.
- Value controls are smaller (200x28 -> 130x24) and toggles now sit at the right edge of the
  value column like every other control.
- Mod names have a hairline rule underneath, which also separates them clearly from the
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
