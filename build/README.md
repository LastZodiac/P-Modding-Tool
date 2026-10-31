# Building P+ Modding Tool

You never need to build anything: `P+ Modding Tool.exe` in the main folder is ready to double-click.
These notes are for anyone rebuilding it.

- App code: `SkinManager.App` (windows) and `SkinManager.Core` (SD card, skins, checks). Target: .NET Framework 4.7.2, x86.
- BrawlLib comes from BrawlCrate (github.com/soopercool101/BrawlCrate), commit a4c1ee3385010d5c64a88df9c56267c61f213321.
  `patch_brawllib.sh <BrawlCrate folder>` prepares it to build without NuGet or WPF (see the comments in it).
  The output goes in `lib\` next to the exe (BrawlLib.dll and its OpenTK.dll).
- Facts the app relies on were read from a real P+ v3.2 card (sd.raw) and from Brawl Online's source:
  costume slot lists in pf/info/costumeslots/NN.masq, portraits in pf/menu/common/char_bust_tex,
  battle portraits in pf/info/portrite, stock icons in pf/menu/common/StockFaceTex.brres, and the
  1,096,170-byte costume limit online (largest P+ costume, FitSamus27.pac).

## Find skins online (GameBanana)
`SkinManager.Core/GameBanana.cs` reads GameBanana's public API (apiv11 `Mod/Index`, `Mod/<id>/ProfilePage`; apiv13 `File/<id>` for the list of files inside a download). Brawl is game 5678, costumes are category 10131 with one sub-category per character.
`source/tests/gamebanana-fixtures.tgz` holds real API answers captured on 2026-10-26, used to test the parser offline.
Set `SKINMANAGER_GB_BASE` / `SKINMANAGER_GB_IMAGES` to point the app at a local copy of the API for testing.

## Stages, music, profiles, look
- Stages: `StageCheck.cs` fingerprints a stage's gameplay parts (collision, settings blocks, position models, moving-platform bones and their CHR0 animation); `Stages.cs` names P+'s stage files and caches fingerprints in `Data/stage-index.json`.
- Music: `Songs.cs` reads P+'s track lists (pf/sound/tracklist/*.tlst) with BrawlLib's TLST parser; songs are replaced by writing a .brstm over the song's file (or a new file for songs the game plays from the disc). Track lists are never changed.
- Stages and songs are both "file mods" (`InstalledStage`, `Kind` = null or "song").
- Profiles: `Library.Profiles` / `ActiveProfile`; the active profile always remembers what's on.
- Look: `Chrome.cs` (backdrop, see-through panels, sidebar buttons), settings in `Data/look.json`.
- BrawlLib is patched to never show its "model has errors" boxes (see patch_brawllib.sh).

## Round: stage fixing, music by stage, all files at once
- Stages are compared by what they do (collision shapes, blast zones, spawns, camera, moving parts), not by bytes.
- A stage that plays differently (usually made for an older P+) can be fixed: StageCheck.MakeMatch copies P+'s
  gameplay blocks (adding missing ones) into the reskin and keeps its models; the result is checked again before install.
- Songs: pf/stage/stageinfo/*.param (STEX) give each stage file's track list; the music window filters by stage.
  Music in a stage download is offered right after the stage, preset to that stage.
- Store: "Install all" downloads every costume file of a mod and opens them in one list (Archives.OpenAll).
  Default ticks: every variant except a Brawl/untagged copy of a costume that also comes in a P+ version.

## Round: fixing older stages, checked against real downloads
- Bones are compared by scale/rotation/translation (what the game uses); stage files often carry stale stored matrices
  (P+'s own Dream Land camera limits changed in SRT only), so the old matrix compare missed real changes.
- Collision is split into shape ("collision") and surface material ("material") so material-only changes aren't called a reshape.
- Position models must have nothing to draw (a scenery model named "...Position" isn't gameplay).
- MakeMatch only replaces position/platform model files that actually differ, refuses when that would drop the reskin's
  scenery, adds missing item-spawner folders (parsed without a parent: BrawlLib reads an entry header before the data otherwise
  and crashed), and reports which of the reskin's models were swapped.
- StageCheck.HowFarOff: compares the reskin's own fixed collision with P+'s (scenery is built to its collision) -> offsets shown in the window.
- Stage programs: STEX Module per stage; a file may only go on a stage running the same program family (_ht/_noshadowrotate ignored)
  or the plain st_final. Vanilla names map STGFINAL->st_final, STGBATTLEFIELD->st_battle.
- Stage files with spaces in the name ("STGFINAL - Everything.pac") are recognised.
- Stage window: stages a map is named for come first (Stages.NamedFor: title words, short names like GHZ/FD/PS2, Brawl's old
  file names like STGGREENHILL -> STGGREENHILLZONE), with the reason when one can't be used; "Put it on a different stage"
  tries any P+ stage. Closest-stage suggestions only appear when they fix with floors within 10.
- MakeMatch adds a model file P+ needs that the map lacks (GHZ swing platform, Model Data [80]) and removes the map's
  same-named copy elsewhere so the platform has one model to ride on.

## Round: P+ updates
- Before any mod goes (back) on the card - "Put them back" after an update, switching one on, profile switches - it is
  checked against P+'s current file (Installer.RefreshOriginal saves what's on the card as P+'s file unless it's ours):
  skins must still fit the slot's skeleton (SkinStillFits); stages must still play like P+'s current stage, else they are
  re-fixed with MakeMatch (StageForNow), else they stay off. Installer.Notes lists what happened; shown after Apply.
- Not covered: an update that skips a file we replaced (P+ wanted to change it but ours stayed). Needs Brawl Online's update list.
- Program.CleanTemp removes unpack folders in %TEMP%\PPlusModdingTool older than 12 hours.

## Round: SD card space, adding songs (findings from Brawl Online 0.1.108's own files)
- Brawl Online copies User\Wii\sd.raw to netplay\pponline-sd\sd.raw (+ PPOnline.rel, ~240 KB) whenever it changes;
  Dolphin plays that copy, so the game never writes to our card.
- P+ updates (netplay\pplus-versions\releases\*.json lists every file + sha256): the updater copies the card, writes each
  changed file over whatever is there (modded or not; deletes first, so it needs only net growth), checks, renames.
  So an update never skips a modded file; the startup "overwritten" check covers it.
- Rooms: the session card is synced in chunks of the image (joiners fetch differing chunks into their card), so a bigger
  card would make everyone joining download most of it. Card size is read from the card itself (no fixed 2 GB), but don't resize.
- Space.Measure: a mod's cost = its size minus the P+ file it replaces (cluster-rounded). MinFreeBytes 20 -> 6 MB.
- Songs can be added (Kind "song-new"): the .brstm goes to strm/Custom/, and Installer.RebuildTrackLists rebuilds the stage's
  netplaylist + tracklist .tlst from P+'s original plus the added songs that are on (IDs F000+ free, freq 40, vol 80).
  Brawl Online's Dolphin re-seeds the match RNG at stage construction and offers "Client Side Music Off"; music is per player.

## Round: two SD cards, version checks
- CardTools.Grow: bigger FAT32 copy without rewriting files (FAT tables enlarged, data area copied byte for byte after
  them; BPB total sectors / FAT size and FSInfo free count updated). Verified: all 3,869 P+ files identical, fsck.vfat clean.
- Cards: User\Wii\sd.raw is always the card in play; the other waits as sd.raw.parked-<normal|big>; Data\cards.json notes
  which. The big card's mods list lives in Data\Cards\big. Switching = two renames (Brawl Online rebuilds its pponline copy
  because the card's size/mtime changed). Before switching in, Cards.CatchUp brings the parked card to the P+ release the
  card in place is on (netplay\pplus-versions\releases\<v>.json): changed P+ files copied over (a parked card's mod is kept
  unless P+ changed the file under it), P+-removed files under /Project+/pf deleted; done on a copy, checked by the probe file.
  Brawl Online's own updater only updates a card that is exactly one release behind ("other" cards are left as they are).
- Versions: launcher version from resources\app.asar's package.json, Dolphin from netplay\dolphin.json, P+ from
  netplay\pplus\pplus.json, newest P+ from pplus-versions\index.json, newest launcher from brawlonline.net/updates/launcher/latest.yml.
  Data\versions.txt remembers the last seen set; changes are reported at startup.
- Dolphin game ID for P+ on Brawl Online: RSBE01 (shader cache names) -> texture packs would go in User\Load\Textures\RSBE01.
- SKINMANAGER_TEST_SKIP_SPACE=1 skips the free-space check for making the big card (test machines only).

## Round: look and favorites
- Theme.CardFrame / PictureArea / Pill / Star / Cue: shared card drawing (rounded frame, soft shadow, gradient, hover edge,
  spotlight behind pictures, gold strip under the picture when a mod is on / installed).
- Favorites (Core/Favorites.cs, Data/favorites.json, shared by both SD cards): key "gb:<GameBanana mod id>" or
  "local:<installed id>"; entries keep name/author/picture so the store's Favorites view needs no network.
- My Mods: ★ Favorites tab, Sort box (favorites first / recently added / name / biggest on the SD card), search hint.
  The tab row has a fixed size (an auto-sized GlassFlow measured 100 px tall and covered the space meter).
- Mod Store: ★ Favorites view, "Hide ones I have", "Hide outdated" (applied to the page shown), header in rows.

## Round: C: drive, updates, shortcuts, texture packs
- LastSave (Core/LastSave.cs): the card from before each save is kept ONCE, in Data\LastSave (next to the app, on G:),
  with a copy of the mods list (installed.json, Originals, Skins, StageMods) taken before the save. The old
  sd.raw.skinmanager-backup next to the card (2 GB on C:) is deleted at startup and on every save.
  "Undo last save" (Profile menu) is allowed only while the card is exactly what that save left (same card, size and
  date): a P+ update since would otherwise be undone too. Undo copies the card back (or moves it, on the same drive).
- ModUpdates (Core/ModUpdates.cs): for installed mods from GameBanana, a newer version = a file added later whose name is
  the same once version numbers are left out (or, if the installed file was taken down, the one with the same name /
  the only file). Checked at startup, at most every 12 h per file (Data\updates.txt). My Mods shows "Update available"
  and "Update to the new version": it downloads the file and runs the normal install (same checks); the old version's
  slot is picked first, and the old version is removed in the same save (MainForm._updatingFile in Run).
- SelfUpdate (App/SelfUpdate.cs): a newer build in <app>\Update (or source\new-build) is swapped in at startup: running
  files are renamed to *.old (Windows allows renaming, not overwriting), new ones copied, the app restarts; *.old are
  deleted on the next start. Settings: desktop / Start menu shortcuts (WScript.Shell), offered once on first start.
- TexturePacks (Core/TexturePacks.cs): Dolphin texture packs (tex1_WxH_... .png/.dds). On = netplay\User\Load\Textures\
  RSBE01\<pack>, off = Data\TexturePacks\<id> (off packs don't use C:). Adding/turning on also sets [Settings]
  HiresTextures = True in netplay\User\Config\GFX.ini (Brawl Online only rewrites Dolphin.ini, and its own code reads
  HiresTextures from GFX.ini). Client-side pictures only: nothing on the SD card, nothing sent to the opponent.
  My Mods > Textures lists them; "Add from file" recognises a pack when a download has textures and no costume/stage/song.

## Round: model swapper (first stage)
- Core/ModelSwap.cs: Load (.obj parsed directly with its .mtl pictures; .dae read with BrawlLib's Collada importer, then
  turned into a triangle list with positions placed by their bones), Fit (stand up = Z-up to Y-up, quarter turns, sized to
  the P+ costume's height and set where it stands), Simplify (grid clustering, cell size searched to fit the triangle
  budget), Build: writes a clean indexed .dae (pictures resized to powers of two, max 512) -> BrawlLib import -> each point
  gets the blended bone weights of the 4 nearest points of P+'s own model (inverse square distance, up to 3 bones, >= 8%)
  -> objects moved into P+'s MDL0 (old objects, materials and their TEX0/PLT0 removed from the texture BRRES; draw calls'
  visibility bone = the first bone) -> SetEditedAssets after the move (BrawlLib's HardCopy forgets the "changed" flags, and
  without them the draw lists aren't regrouped for the new weights) -> metal materials regenerated if the costume had them
  -> pictures encoded CMPR (RGB5A3 with transparency). Bones are never touched.
- Verified in the VM: a re-opened result has every point weighted to P+ bones, same Y extent, SkinCheck.SameSkeleton passes,
  and it follows Mario's own CHR0s (AttackS4S, Run, AttackHi4 rendered next to the original). Not verified in game yet.
- ModelPreview.RenderPosed(costume, motionFile, animation, frame): previews in a pose (needs Shift-JIS on mono: I18N.CJK).
- App/SwapPanel.cs: "Model Swapper" page; "Make it a costume" saves Data\Swaps\<name> on <character>\Fit<prefix><NN>.pac
  and opens it with the normal add-a-skin steps (slot, pictures, size and skeleton checks).

## Round: fixes from first real use, launch buttons
- Program.Main had lost [STAThread] (it sat on CleanTemp): Windows' file dialogs hung and drag-and-drop / ComboBox
  autocomplete threw. SwapPanel also runs BrawlLib's Collada importer (a Form) on its own STA thread.
- Battle portraits: every InfFaceNNNN.brres holds exactly one picture, named the Brawl way (InfFace.121..130 for Peach) and
  P+'s extra slots reuse copies of the first; Cosmetics.FindTexture uses a file's only picture when the name isn't there.
- PackForm: slot list items were added with null text before being named; Windows' ComboBox rejects a null item text
  (CB_ADDSTRING fails -> exception), so the lists stayed empty on Windows only. Items get their text when added;
  ToString never returns null; errors there now go to Data\errors.log (Program.Log). "Set up this one step by step"
  now returns to the pack list afterwards (MainForm.AddSkin loops; SetUpOne runs the wizard).
- Launch modded / Launch unmodded (header): find Brawl Online.exe (Data\launcher.txt, %LocalAppData%\Programs\Brawl Online,
  Windows' Uninstall entries, else ask once), put the right card in place, start it. Unmodded uses a third card, "clean":
  Cards.MakeClean copies the normal card and writes P+'s own file at every path of the P+ release json (from the card in
  place, the parked cards, or the mods lists' saved originals), deletes files mods added, and keeps it only if all release
  files read back with P+'s sha256 (VM: 3,869/3,869 identical to an untouched P+ card). Switching is a rename, like the
  big card. cards.json also remembers "modded" (the card Launch modded goes back to). Saving mods while the clean card is
  in place is refused (offers to switch back). Installer.WorkInPlace exists for throwaway copies (no safety copy).

## Stage rounds: where stages are, and stage select pictures
- Stages.cs: stage programs are told apart by the program ID in a stage's own .rel (vanilla st_village counts as Smashville's program);
  Smashville's times of day are matched by number (STGVILLAGE_02 -> Dusk); "already in P+" note for stages P+ ships (FoS = Corneria, hold R).
- StageSelect.cs (new): reads P+'s stage select from the card (Switch00.rss pages, stageslot/*.ASL buttons and L/R lists,
  stageinfo/*.param, stagelist names) and says where each stage file shows up ("Smashville (page 1, row 3, 3rd icon), hold R").
  Also finds a download's stage select pictures and paints them into P+'s pictures (big preview / L-R-Z thumbnail / icon / name).
- Installer: sc_selmap.pac is rebuilt from P+'s saved original each save, with the pictures of the stage mods that are on;
  with none on, P+'s file goes back byte for byte. Pictures are kept as select-*.png in the mod's folder.
- Install window: every option says where it is in the game; a picture section previews the change, with
  "Use a different picture..." and "Keep P+'s picture". My Mods stage cards show page/row/icon (full text on hover) and the picture.

## Stage looks swapper and "What's different?"
- StageLooks.cs (StageCheck, partial): Explain() lists exactly what differs from a P+ stage in plain words (floors off by
  N units, markers moved; markers that all moved together = "P+ re-centered the stage"); SwapLooks() keeps P+'s own
  stage file and puts the map's scenery in its biggest pure-scenery part, the way P+'s own alts are built (PS2 Gateway:
  StgStadium00 with no pieces + new models): P+'s scenery loses its pieces but keeps bones/materials/names; the map's
  models, animations (first of each kind = the map's main part) and textures/palettes (renamed zMap_*) are added;
  moving platforms keep P+'s look. Result must fingerprint identical to P+'s or it isn't offered.
- Fingerprints v6 add "outline" (fixed floors' shape, not compared); StageIndex.ByShape finds P+ stages shaped like a map.
- ModelPreview.RenderStage: front view of a stage with floors drawn on top (back faces culled, sky domes skipped).
- Install window: "Use its looks on P+'s X" options with preview; shape matches when nothing plays alike; program
  mismatches offer looks instead of a dead end; "What's different from P+'s stage?" window.

## Online safety: parts the stage program uses (Green Hill desync)
- MK Adventure GHZ (made for P+ 2.15) desynced: P+'s st_greenhill_ht.rel looks up "StgGreenhillMain" by name; the map's
  is Brawl's old 81-bone version with a 601-frame loop (P+'s: 9 bones, 359 frames). Floors/markers/platform were identical.
- StageIndex.ProgramNames(card, stage): every text in the stage's program (.rel). Fingerprints (v8) hold each model in
  detail (slot, bones, the CHR0/VIS0 that move its bones: name, length, loop); Compare/MakeMatch/SwapLooks/Explain take
  the program names and require those models to be exactly P+'s.
- Installed mods are re-checked at startup (Installer.UnsafeStages): red bar with "Turn them off".
- Looks swap: map's own copies of moving platforms left out (they have the bone P+'s platform floor rides on); map bones
  named like P+'s or the program's get "_map"; map animations named "map_*"; map animations go first only in a part the
  program doesn't use; PAT0 renames set directly (BrawlLib's setter re-lists on every change: hung on long animations).
- MakeMatch now brings the pictures of P+'s parts it copies in (P+'s GHZ platform was left unpainted).
- Official P+ files: Brawl Online keeps pplus-versions/releases/<ver>.json (sha256 per file) - use it to tell P+'s file.

## Distribution (public): Setup + auto-updates from GitHub Releases
- source/SkinManager.Setup: Setup.exe with the app inside (payload.zip). Installs to %LocalAppData%\Programs\P+ Modding Tool
  (no admin), shortcuts, Apps & features entry (HKCU), keeps Data on update; Uninstall.exe removes only the files it
  listed in installed-files.txt (+ Update, *.old), keeps Data unless asked.
- App: OnlineUpdate.cs asks api.github.com/repos/<update-source.txt>/releases/latest (every 6 h; Settings > Check), shows
  a bar; downloads PPlusModdingTool-update-<v>.zip, checks it against SHA256SUMS.txt, unpacks to Update\ with
  verified-update.txt, restarts; SelfUpdate swaps it in. Tested: update 1.1.0 -> 1.1.1, and a wrong checksum is refused.
- build/release/release.sh <version>: builds everything into Releases\v<version>: Setup exe, update zip, BrawlLib source
  (LGPL-3.0: with our changes and the upstream commit), SHA256SUMS.txt. Put "owner/repo" in ~/rel/update-source.txt first.

## Launch protection
- MainForm.StagesSafeToLaunch: "Launch modded" re-checks stage mods (Installer.UnsafeStages) and will not open Brawl Online while one fails; one click turns them off, saves and launches. Confirmed by Brawl Online logs: every desync (GHZ x2, Dream Land x3) was on the two flagged stages; 20 games on other custom stages had none.

## Model Swapper: GLB/glTF and reference pictures
- ModelSwapGltf.cs: .glb/.gltf (scene transforms, materials -> parts, base colour pictures saved as PNG to temp, plain colour as a 4x4 picture; Draco/meshopt refused with a plain message). Tested: OBJ, GLB and glTF of the same model load identically.
- ModelPreview.RenderViews + "Save P+ Model Pictures": front/side/back, one shared scale, no perspective, default pose, 1024 px on white, plus a how-to with the height.

## Model Swapper: rigging by the skeleton
- ModelSwapRig.cs: each point follows the nearest body-bone segments (joint to next joint), up to 3 blended (1/d^4 within 1.35x of the closest). Left out: bones with no weights, attachments (names ending in M), soft parts (skirt, hair, cape, tail, ear, coat...), fingers and face bones. The old way (nearest P+ costume point) made jeans follow Peach's dress bones. ModelSwap.UseSkeleton switches back.
- TEX0Node.Remove(true) when clearing the old pictures: BrawlLib asked "delete the associated PLT0?" in a message box for palette textures (Peach/Zelda eyes), stopping the swap.

## Model Swapper: Line up the joints (2026-10-28)
- New "Line up the joints..." button (under the pose list). It opens a window with the model from the front, P+'s skeleton in gold
  and a blue dot on each main joint. Drag the dots onto the model's own joints; the model is reshaped (ModelSwap.LineUp) so
  those spots land on P+'s joints. P+'s skeleton is never changed (hitboxes/hurtboxes hang on its bones).
- Dots are saved per model + character + costume in Data/Swaps/joints/.
- ModelSwap.Build no longer changes the mesh it's given (it re-saved picture paths into it, so later previews lost pictures).
- Zelda's apron bones (Maekake) are no longer body bones.
- "Paint: stays with body" brush in the Line up window: painted points (seen from the front) follow only the middle of the
  body (hips/waist/chest/neck/head) instead of the nearest bone. For long hair beside the arms, coat tails, skirts.
  Saved with the dots (lines starting with ~paint / ~erase). ModelSwap.KeepTest + SkeletonRig.Centre; Build takes it as an option.

## Model Swapper: crash fix + "suit" lineup (2026-10-28)
- CRASH: swapped costumes crashed the game when the fighter was created (nw4r ScnMdlSimple came back null). Cause: every point
  had its own bone mix (2,331 influences vs P+ Peach's 253), more matrices than the game can hold. Build now snaps weights
  (ModelSwap.SnapWeights: up to 2-3 bones, steps of 1/10, coarser if needed) so mixes never exceed the P+ costume's own
  count. Make() refuses if they would. Status line shows the count. Crash traced with Dolphin's log + doldecomp/brawl symbols (RSBE01_02).
- LineUp now turns each body part onto P+'s bone (rotation + stretch along the bone, normals turned too) instead of
  smearing by translation. Middle-line dots only slide up/down. Fit centres on the legs (lower quarter), not the outline.
- Dots are now placed automatically (ModelSwap.AutoMarks): limbs measured slice by slice, body middle from the thighs, and
  the DEPTH of every joint (some downloaded models sat ~1.5 units in front of the skeleton, invisible from the front view; that was
  the main cause of the mangling). "Place the dots for me" re-measures; right-click puts a dot back on its measured spot.
- Arm rule (SkeletonRig.Weights): an arm bone can't take points nearer the body's middle than its own joint, so hair down
  the back and jacket sides aren't lifted with the arm (fixed up smash on the kpop model).
- NEW RIGGING (ModelSwapHeat.cs, SightWeights): each point follows the nearest body bone it can SEE from inside the model
  (ray test against the model's own triangles; points within 0.7 of a bone skip the test), unseen points inherit along the
  surface (Dijkstra), then blending at joints over a real distance (quarter of the shorter bone). End joints (wrists) get a
  bone 0.55x the forearm long. Helper bones ([NJ][a-z]$, e.g. LArmNa) are never used. Bench (20 Peach moves, % surface
  stretched >33%): P+ Peach 22.6, old 38.4, nothing set 32.1, new 23.4. Sheik 20.9 vs her own 18.7.
- Remaining on Peach: moves where her legs fold under the dress / go overhead (crouch, d-tilt, d-smash, bair, uair,
  launched) - animation itself, not rigging. ZSS: thin spikes at hands still to fix.
- Only the standard Brawl body bones move a swapped model (BodyName whitelist: HipN WaistN BustN NeckN HeadN and
  [LR] ShoulderN/ShoulderJ/ArmJ/HandN/LegJ/KneeJ/FootJ/ToeN). Sheik's Pony01-04N and SwordN were grabbing hair and chest.
- Hair hanging behind the back (deeper behind the spine than 0.8x the chest's front depth) can't take hips/waist/chest by
  sight or by spreading from the jacket; hair below/behind the head and below the neck rides on the neck (rigid, short
  blend with the head) so it doesn't stick out when the head bows (Sheik up smash). Note: the stretch score prefers rigid
  hair, so hair was judged by eye. Sheik: all 20 bench moves clean. Peach unchanged (24.5).

## Stats add-on (2026-10-28)
- Data: Brawl Online's own database, %AppData%\Brawl Online\replays.sqlite (tables file/game/player), read with a small
  read-only SQLite reader (SqliteReader.cs; no native library), including the -wal file so the latest games show.
  Character numbers = the game's gmCharacterKind (doldecomp/brawl include/Brawl/Include/...), P+ adds Roy 50, Mewtwo 51,
  Knuckles 53 (checked against replay file names). Stage numbers = stage slots; names from stageslot/Stageslot IDs.txt.
- MatchHistory.cs: records by stage/character/matchup/opponent; projection = overall record moved by each known factor
  (matchup with anyone, head-to-head, stage), each shrunk towards the overall rate (8 games) and combined with
  diminishing weight (1, 1/2, 1/4) because they overlap. Counterpicks need 2+ games vs that character.
- StatsPanel.cs (PLAY > Stats; Settings > Add-ons switches it and the opponent card off).
- LiveMatch.cs follows netplay\User\Logs\dolphin.log (read only, from its end): lock-in (your character while
  searching), Match ID (mode), [Matchmaking] P<n> name (code), session as host/joiner on P<n>, game N setup (stage;
  the host's line also has both characters), ftManager ChrKind/SlotNo, game N winner. LiveCard.cs shows it bottom-right,
  never takes focus, hides 12 s after the fight starts, returns between games with picks/stages for the next one.
- Stats pictures (StatsArt.cs, read once from the SD card): character heads = battle portraits (InfFace, costume 1),
  big portraits = select-screen portraits (MenSelchrFaceB), stage banners = sc_selmap MenSelmapPrevbase.NN (trimmed with
  StageSelect.StagePart; slot -> NN from Switch00.rss, StageSelect.Pictures). Used in every row, the profile header, the
  tiles (last-20 streak strip), the Win chance faceoff and the opponent card. Test hook: SKINMANAGER_TEST_WINCHANCE=mine,theirs,stage.
- Overlay (Overlay.cs): OverlayForm = no-activate + tool + topmost + layered + WS_EX_TRANSPARENT (clicks go to the game),
  Opacity ~0.86. Placed on Brawl Online's Dolphin window (GameWindow.Find: biggest visible window of that Dolphin,
  client area; re-checked every 2 s), top-left (Brawl's timer is top-right). Card while found/loading/between games,
  slim bar 12 s into the fight. FullOverlay (match: opponent/picks/stages/factors; no match: your summary) on a global
  hotkey (RegisterHotKey; Settings > Add-ons: Alt+T default, Alt+S, Ctrl+Alt+S, F9, F10, Off). Test hook:
  SKINMANAGER_TEST_FULL_FLAG=<file> (shown while the file exists).

## Frame Data (2026-10-28)
- PLAY > Frame Data: every fighter on the card (FrameDataBook.Available: Fit<X>.pac + Fit<X>Motion(Etc).pac + one
  Fit<X>NN.pac costume). Moves named like a frame data site (FrameDataBook.Standard: animation names -> jab, tilts,
  smashes (Start + swing), aerials, grabs/throws, dodges/ledge/getup); specials = every Special* subaction with a
  hitbox or intangibility.
- Moveset.cs: own reader of the fighter definition (BrawlLib's moveset reader crashes on P+ files). Data header ints:
  [0] subaction flags+anim names, [1] model visibility, [2] attributes (0x140..0x150 aerial landing lag), [4] misc
  (+12/+16 hurtboxes), [9] fighter-specific action entry scripts, [12] subaction main scripts. Subroutines and gotos
  are inlined.
- FrameData.cs timing (matched to Melee numbers for Fox jab/tilts/aerials/fsmash, Marth's whole kit, Falcon):
  an event after script time t lands on frame t+1; sync timer adds, async timer waits until; Frame Speed Modifier f
  makes an animation frame last 1/f; a smash's Start part hands over on its last frame (len-1). Hitbox args:
  0 bone<<16|id, 1 dmg, 2 angle, 3 WKB<<16|KBG, 4 shield<<16|BKB, 5 size, 6-8 X/Y/Z (as BrawlBox draws them).
  If/Else: the first branch only. Throws from Throw Specifier (id 0). Damage set by a game variable shows "?".
- ModelPreviewMoves.cs: draws each frame from the side (model posed by its CHR0, visibility switches applied:
  Fox's blaster hidden), hitboxes as circles coloured by id (grab boxes purple), hurtboxes as yellow capsules
  (blue intangible, green invincible). Bone 0 = TransN; Kirby/Wario bone 400+ = minus 400 (BrawlBox's fallback).
  Moves that move the fighter (flag 4) keep him centred; the ground's marks scroll.
- GifWriter.cs: animated GIF (one 256-colour palette, LZW), 50 fps, frame number/state/name written on each picture.
- Not shown yet: projectiles (lasers, arrows...) and hitboxes made by the game's code instead of the move script
  (e.g. Fox's side B); Nana.

## Frame Data: projectiles, grouping, Launch & combos (2026-10-28)
- Articles (Moveset.ReadArticles): the fighter-specific offsets after the 124-byte data header (and the entry article,
  header int 26) that look like an article header (13 ints: id, ARC group, bone (-1 ok), action flags, subaction
  flags, actions, subaction main/GFX/SFX, visibility, collision, 2 more). "Generate Article" (10000100 arg0 = index
  among those, in order) in a move links it: Fox 0 blaster, 1 laser (3%), 2 side B image (7% 80°). The article's
  first subaction with hitboxes is run as the projectile (ground/air variant by the move); "+" = still out when its
  script ends (keeps going until it hits/vanishes). Hitboxes made purely in the game's code (.rel) still can't be read.
- List: specials grouped Neutral/Side/Up/Down B; identical ground/air (or angled) versions merged "(ground & air)" /
  "(all angles)"; filter chips All/Ground/Air/Specials/Grabs/Defense.
- Launch & combos (Combos.cs, ComboView.cs): Melee formulas (KB with 1.4 and +18, WKB, hitstun KB*0.4, speed KB*0.03
  slowing 0.051/frame, target gravity/fall speed from attributes 0x64/0x68, weight 0xB0, size 0xB4, jumpsquat 0x30,
  full hop 0x38, short hop 0x44, air speed 0x7C; 361 = 0/44° grounded, 45° airborne; shield stun (d+4.45)/2.235).
  Hitbox positions per frame from the posed model (ModelPreviewGeometry.Places, game units = model x size); target
  body from its hurtboxes. Follow-up = hits before hitstun ends and before a tumbling target touches the ground
  (tech), within reach (aerials: short/full hop + drift). Aerials taken as landed + L-cancelled. Tumble spikes on the
  ground = knockdown (tech chase). Percent chart = every 2% from 0 to 200.

## 1.0.0: renamed to P+ Modding Tool (2026-10-28)
- Exe "P+ Modding Tool.exe" (config renamed too), Setup installs to %LocalAppData%\Programs\P+ Modding Tool, release
  files PPlusModdingTool-Setup/-update-<v>, temp folder %TEMP%\PPlusModdingTool. Old "P+ Skin Manager.lnk"
  shortcuts are swapped for new ones on start (SelfUpdate.RenameOldShortcuts). Namespaces stay SkinManager.*.
- Logo/icon: build/brand (make_logo.py draws it; logo-512/1024.png), SkinManager.App/Assets/app.ico (exe + every
  window via Form.defaultIcon) and logo96.png (header, Credits). Setup has the same icon.
- Smoother: the backdrop cache is 32bppPArgb (fastest to draw behind every see-through panel); pictures in lists,
  stats art and frame data playback are shrunk once and cached per size (Theme.Scaled + Theme.Blit, 1:1 copies);
  big fonts used while painting are made once (Theme.Bold); page switches draw once (WM_SETREDRAW around the
  swap); frame data frames are drawn on all cores (Parallel.For).
- Repo front page: source/README.md with screenshots in source/docs (stats screenshot has the player name blurred).

## Your own music (2026-10-28)
- MusicImport.cs: MP3 (NLayer 2.0.1 source, MIT, in SkinManager.Core/ThirdParty/NLayer; no Span so it builds for
  .NET 4.7.2) or WAV (8/16/24/32-bit, float) -> stereo -> quiet ends trimmed (-60 dB) -> windowed-sinc resample to
  32 kHz (or 44.1) -> RMS evened to -18 dBFS (peak <= 0.97) + the user's dB -> BRSTM (DSP-ADPCM) with BrawlLib's
  RSTMConverter.Encode through a PCM IAudioStream; loop = whole song, from a time, or none. Checked: read back by
  BrawlLib (32 kHz, 2 ch, loop on, loop start on a 14336-sample block), decoded back = correlation 1.000.
  A 3:16 MP3: ~2.5 s to read, ~20 s to encode (under Mono; one CPU core for the ADPCM part).
- MusicImportForm.cs: per song name (ID3 title or cleaned file name), loop, volume, Listen (start) and Hear the loop
  (end then loop start) through System.Media.SoundPlayer; makes "<name>.brstm" files in a temp "My music" folder and
  hands it to the usual SongForm (add to a stage's songs, or replace a P+ song). Entry: My Mods > Music
  "+ Add your own music", Add from file (MP3/WAV, several at once), or dropping MP3/WAV files on the window.
- OGG isn't supported: NVorbis' current versions need System.Memory (Span) and the old ones are Ms-PL (not GPL-compatible).
