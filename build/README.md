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

## Stage versions (2026-10-30)
- A stage's normal version = what its icon gives with no button (stageslot/<slot>.asl entry with button 0). Some load
  several files the game picks between (Smashville: 00_MORNING..04_MIDNIGHT by the clock; Spear Pillar, Shadow Moses:
  3 each). StageForm offers "P+'s normal <stage> (all N versions)" as one choice (StagePlan.Group / InstalledStage.Group:
  one card in My Mods, switched and deleted together) and ranks the normal version above button versions.
- StageSelect.Read(card, original): reads P+'s own .asl when ours is swapped (Installer.OriginalSelect), so "normal" means
  P+'s normal. StageSpot has Asl, Entry, Param, File.
- Stage swaps (StageSwaps.cs, Kind "swap", CardPath = the .asl): the name offsets of two entries are exchanged, so the
  button gives the normal version and no button gives the other. Only when every file of both versions passes
  StageCheck.Compare (with program parts) and both use the same stage program (Installer.PlayAlike). On P+'s card only
  Smashville's time-of-day list entries pass (Sakura, Beach... change parts st_smashville drives). Other versions: their
  scenery on each file of the normal version (PlanLooks with P+'s own file of that version as the map). Both kinds have
  PackageKey "version|<slot>|<param>#<entry>"; choosing again removes the old one. Redone on P+ updates (SwapForNow).
- Looks-only installs are never ticked by default and say "try it offline first": MK Adventure GHZ (looks on GHZ) showed
  the map's ground one grass block above the floor in game while every file-level check (collision, models at frame 0,
  all CHR0 animations) lines up; the stage program places it, which the files don't show. ModelPreview.CheckStanding
  (camera-view check of scenery vs floors) exists but isn't used: it flags several of P+'s own stages.
- Mod Store / My Mods "which stage" filter: GameBanana has one flat "1:1 Stages" category, so stage mods are matched by
  title (Stages.NamedInTitle: names, short names like SV/GHZ/DS, "...ville" = Smashville, "Siege" = Castle Siege).
- Pictures named like portraits but far from portrait shape (> 30% off: a sheet of several) aren't used as portraits.

## Pikachu's jolt and thunder (2026-10-31)
- Neutral B is listed as a ground move (SpecialN only), so the old air-only jolt rule never ran. Now one rule: flies at
  slot 5 +4/+8 until the floor (Flight.PathForHeight/HitsForHeight, from where it really starts), then the crawling
  jolt (slot 6, its own Regular hitboxes, slot 6 +0 = 100 frames). Crawl speed isn't in any code or data the app reads
  (sora_melee jolt2 class sets no speed; its animation doesn't move): drawn at the flying speed, marked approximate.
- Down B: flag 0x22000010 (frame 14) makes the cloud 150 up (fighter block 3 +0); its status (ft_pikachu 0x17B44) sends
  bolt pieces (slot 7, fn_98_13B80) down at slot 7 +4 = 5 a frame: reaches Pikachu after 30 frames.
- Not drawn on purpose: Charizard's rock smash stones (random angles), Ike's thrown sword (end point from a work value
  set elsewhere).

## Whole-cast pass (2026-10-31)
- ModelPreview.AuditFrames: RenderMove reports per frame the move's hitboxes, how many got placed and how many projectile
  hitboxes were drawn (no pictures). Every move: none lose hitboxes in drawing; gaps were all in the data.
- FrameData.Run: hitboxes made on a piece's last tick (never on one of its frames) carry into the next piece (Sheik's
  chain, Charizard's air neutral B start). Ones already shown still end with their piece.
- Held things without their own animation (Ness's yo-yo, article bone 66 = ThrowN, which the smash animations sweep
  along the yo-yo's path): hitboxes on that fighter bone (Hitbox.OnArticle), from the frame their part starts.
- Sheik's chain: special hitboxes 0-3 (no bone) put on WpnSheikIronWire links 3/5/7/9 (bind pose: straight, 4 units
  apart to 28.5) hanging off LHaveN; noted as approximate (the game swings it).
- Left with notes: Peach's toad spores (made by code on counter). Sheik's down B 2 sets and removes its hitboxes at once.
- Olimar's Pikmin attacks: per-colour scripts at article header +0x38 + colour*4 (group 0 main), by Pikmin subaction
  (AttackS4SJump, AttackHi4, AttackLw4, AttackAirF/B/Hi/Lw = Pikmin motions 0x1E-0x21, ft_pikmin attack_air execFixPos).
  Power var 0x10000008 set to 0; the note lists each colour's range. Smashes: around his feet + settings 0xFA4/5 (fsmash)
  and 0xFA6/7 (usmash); down smash one each way. Aerials: at node 0x29 (RHaveN) position only (updateHoldPikmin passes a
  Vec2f), facing his way (ArticleNoTurn). Hits follow his hand's swing (fair front-low sweeping under, bair behind then up).
- R.O.B.'s gyro: an item (itKind 0x57) made at flag 0x22000011, node 0xA2 + settings 0xFD2/0xFD3, thrown at 0xFD0 +
  charge*0xFD1 (charge up to 0xFCF = 90). Its scripts: /item/ItmCommonParam.pac ItmRobotGyroParam, "animParam" (+0 flags,
  +4 count, +28 main): Throw = 4% 45° BKB 55, WaitGround = 5% 60° (rehit 40), FuraFura = 2%. Motion from ItmParam.pac
  "R.O.B.'s Gyro" (gravity 0.051, fall 1.8, floor friction 0.03). Drawn uncharged and full charge; the item code's
  speed-to-damage scaling isn't in the files (noted).
- Whole-cast signature dump before/after (start, active, FAF, hits, projectiles): only those 5 rows changed.

## Match history without Brawl Online's list (2026-11-01)
- When Brawl Online can't write in Documents\Brawl Online, its log says "replay: cannot create the file ... saving it in
  ...\netplay\User\Replays\ instead". Its replays.sqlite only lists Documents replays, so it stops adding games.
- MatchHistory.FromNewReplays now reads every replay no record knows (by file name), in the known replay folders,
  Documents\Brawl Online and the backup folder (MatchHistory.BackupReplayFolder); no date window. Replays that can't be
  read yet (still being written) are skipped until their size or time changes. Tested on a real install: 267 → 309 games.
- LiveMatch.TakeFinished: each game the log shows finishing (winner line) becomes a GameRecord (you = LoggedGames.MeMark,
  start = when its fighters came in), kept in Stats\logged.json. LoggedGames.Missing adds one only when nothing else has
  a game against the same opponent code within 30 s of it (replay startTime = first ftManager entry; room games are
  3+ minutes apart). Opponent id from other games with that code, else "code:<code>". Tested: with the replays the
  logged copies add nothing (309); without them they fill in exactly (309, same record).

## Stage memory (2026-11-01)
- The game unpacks a stage file into its stage memory (StageResource, 0x666700 = 6.40 MB, from the game's memory report
  in dolphin.log). P+'s biggest battle stage unpacks to 6,432,864 bytes (Fourside New Pork City); a "looks only"
  Xenoville on Smashville unpacked to 8.6 MB and crashed the game right after loading ("gfPoolFree Error").
- StageCheck.MemoryCheck on every stage plan (fails above the heap; "only just fits" above P+'s biggest).
- SwapLooks: P+'s pictures nothing visible uses any more are made 4x4 (same names, so materials and PAT0 still find
  them): Xenoville on Smashville 8.6 → 5.8 MB unpacked, gameplay identical, looks identical in the preview.
- Installer.TooBigStages + the banner ("Make them fit"): stages put on by older versions are re-made (StageForNow slims
  looks-only ones, turns the rest off with the reason).
- Archives: folder names for single files capped at 40 characters (Windows 260-character path limit: a long mp3 name
  failed to open).

## Stage install screen (2026-11-01)
- Each option: one verdict line (✓ lines up / ⚠ slightly off / ✗ doesn't line up / ? can't measure) from the mod's own
  floors against P+'s, one short line on why it's safe, and where it is in the game. The long caution is only shown when
  the mod was made for another stage program.
- Previews side by side: P+'s stage now | with this mod. On the mod's picture the gold line is where you'll stand and
  the mod's own floors are drawn red wherever they're more than a unit from it (ModelPreview.OffFloors): no red = lines up.
- HowFarOff/SurfacesOf: a stage with no fixed floors (Delfino Secret, Corneria) is measured where its moving parts start
  (as the preview draws it), instead of "can't be measured". Cocktail of Dreams on Delfino Secret: 52 units off (✗).
- A looks-only option is ticked by itself when it lines up and the mod was made for that stage's program.

## Look and feel pass (2026-11-01)
- App/Polish.cs (new): Polish.Install() (called first thing in Main) styles every window the app opens, on Application.Idle,
  and anything added to it later: dark title bars (DWMWA_USE_IMMERSIVE_DARK_MODE 20, 19 before Windows 10 20H1), dark
  scrollbars (SetWindowTheme "DarkMode_Explorer"; dropdown lists "DarkMode_CFD"), and every ComboBox drawn in the app's
  colors (ComboPainter: owner-drawn items + the closed box repainted after WM_PAINT, rounded, with a chevron).
- SmoothButton: Theme.Primary/Secondary now make rounded, beveled buttons (gradient, top highlight, shadow, hover lift,
  press sink). Code that sets BackColor keeps working: the color is kept as the fill and the control stays transparent
  (OnBackColorChanged), and the parent is painted behind it so the corners blend.
- Dialog.Show replaces MessageBox.Show everywhere (same overloads): a dark box with the app's buttons. Program.Crash's
  last-resort fallback stays a plain MessageBox.
- Toast: a note at the bottom of the window ("Saved: 3 mods turned on · Undo") replaces the "Done" box after a save
  when there's nothing to warn about. Saves with notes (stayed off, P+ changed) still get the box.
- Main screen: header line is just the free space (+ "Close Brawl Online to change mods" only while it's open), cut
  with an ellipsis before the buttons, card name in a tooltip. Banners are dark with a colored stripe (red = crashes or
  desyncs, gold = notices, green = app update), text cut to fit with the full list in a tooltip, short sentences;
  MainForm.Short drops "(STGFILE_NAME)" from mod names in sentences. "Made smaller than needed" is now "can use its
  full-quality pictures" with a "Restore quality" button. Sidebar: MODS, TOOLS, then Settings/Credits pinned at the bottom.
- Gold is kept for the main action: Add from file, Search, Add your own music, Choose stage versions and Choose a
  picture are secondary buttons now.
- My Mods: space bar says "2.1 GB free" (breakdown in its tooltip), Space.MB shows GB from 1000 MB, per-mod size only
  when under 200 MB is left, the count line is hidden (the tabs count), "Add more from this download" only on hover.
- Checked by rendering under Mono + Xvfb (buttons, banners, cards, dialog, toast). ComboPainter's repaint is Windows
  behaviour (Mono draws its own combo first).

## Smoothness pass (2026-11-01)
- Why scrolling was choppy: every card, button and label is see-through, so for each one Windows had every panel
  behind it draw itself again (the backdrop, each panel's tint, all the way up to the window), on every scroll step.
- BackPicture (Chrome.cs): GlassPanel and GlassFlow keep what's behind them as a picture, made again only when the
  panel moves in the window, changes size, or the backdrop is redrawn (BackCache.Generation). A card now costs one
  copy of that picture. Backdrop.Paint only copies the part asked for.
- SmoothScroll (Polish.cs, on every scrollable panel via Polish.Style): the wheel glides (each notch moves the goal,
  ease 0.3 a 10 ms tick); dragging the scrollbar stops it. ScrollView (Stat Tracker, Frame Data lists) glides the same.
- Theme.Freeze (WM_SETREDRAW): My Mods and the Mod Store rebuild their cards with drawing off, so the half-built grid
  and the jump back to where you were never show.
- My Mods filter buttons are made once and updated (made again on every refresh, old ones were left drawn over).
- SmoothButton: no dark drop shadow, and a faint light outline instead of the dark one (read as a black frame).

## Buttons and scrolling, second round (2026-11-01, 1.2.3)
- 1.2.2 still showed a dark box behind every rounded button on Windows: the button asked its parent to draw itself
  behind it, and through Windows' see-through painting that came out dark. Now SmoothButton (and the dropdowns) have
  their window cut to the rounded shape (Region), so the corners are simply not theirs, and inside they draw what's
  behind them directly from the nearest panel's kept picture (BackPicture.Behind), never through Windows.
- Fast scrolling smeared ("stretched"): Windows' scroll copies the old pixels along and patches the rest. Theme.Repaint:
  the glide and scrollbar drags now scroll with drawing off and then draw the whole panel once (RedrawWindow).
- BackPicture reuses its picture when the size is the same (Settings' cards are made again on every scroll step).

## Items, "Punish it", "Look out for" (2026-11-01)
- Items a special pulls out (FrameDataBook.ItemMoves / ReadItems / ItemHits): Link's and Toon Link's bombs
  (fighter/<name>/ITEM/Itm<Name>00Param.pac), Snake's grenade, Peach's turnip, Diddy's peanut (item/ItmCommonParam.pac).
  Their script parts with hitboxes are listed on the move as item projectiles (Projectile.Item): "Throw" = when thrown,
  "Born" = the blast. Link bomb blast 6% 70°; Snake grenade blast 12% 80°, thrown 2%; Diddy peanut 4%; turnip damage ? (code).
  Dedede's Waddle Dees aren't on the SD card (they come from the Brawl disc), so they're still not drawn.
- Punish.cs + PunishView ("Punish it" tab): their move on your shield. Shield stun floor((d+4.45)/2.235), advantage =
  hit frame + stun - (FAF-1) (aerials as Combos.Ready: landed + L-cancel); a move that hits more than once is worked out
  from its last hit. Your options: grab, up smash, up B straight out of shield; aerials after jumpsquat. Spare = window -
  (startup + jump). Reach compares the furthest hitbox edge (Places) with theirs at that hit; pushback not counted.
  Checked: Marth f-smash vs Fox (-27: everything punishes, but only up close: Marth's sword reaches 19.5, Fox 12-15).
- WatchOut (DeathCoach.cs): after a game, the opponent's move that took the most stocks / did the most damage, its
  Punish report and a picture of it as it hits (rendered at its hit frame), shown as "LOOK OUT FOR" in the post-game card.
- bapp.sh's error filter hid compile errors (greedy sed); fixed (test tooling only).

## Compare, code-built specials, watch, recaps, sharing, head pictures, collections (2026-11-01, 1.2.3)
- Frame Data "Compare" tab (CompareView): this fighter and one you pick, move by move: startup, active, FAF, the faster
  one green. Rows matched by name, then by the game's animation name (Falco's "Forward tilt (all angles)"), then by the
  start of the name. Several hits show first-last ("6…21"). Column widths fit the window (44-74 px each).
- Code-built specials (from Brawl's and P+'s modules):
  Sonic/Knuckles up B spring (wnSonicGimmickJump "Fall": article params +0x10 vy, +0x14 gravity, +0x18 fall limit;
  hits 4% only when it's used in the air). Zelda side B Din's Fire blast (fireball params +0 time 85, +0x14 speed 3.2,
  +0x18 -0.035/frame, made at HeadN 74 + fighter param 1 +0xC/+0x10; blast damage +0xC + +0x10 a frame flown = 28% at
  its farthest), drawn flying straight with no stick. Kirby's stone: hits in DummySpecialLwToAir/ToGround, now rows
  "Down B stone, falling/landing" drawn on the stone pose. Ike's up B sword removed (only the Final Smash throws it;
  Aether's hits are Ike's own). Moves that really don't hit (Olimar pluck/whistle, Mewtwo teleport) say so and count
  as exact (FrameDataBook.Harmless). Olimar side B explains the Pikmin cling (colour tables are code-only).
  Whole cast: not drawn 16 -> 8 (the rest are set-later things: Snake's mine/C4/Cypher, Dedede's Waddle Dees, Olimar's
  thrown Pikmin, Charizard rock pieces/down B, Zelda down B).
- ProjectileFlight.Apply logs a failing rule when SKINMANAGER_DEBUG_FLIGHT is set (it used to swallow it silently).
- Stat Tracker: "Watch this game" opens Brawl Online's launcher with the replay file (its main process plays a .brep
  given on the command line or to a second instance: playback Dolphin, mode "normal"). Overview starts with "Your last
  session" (games with less than 45 minutes between them: record, vs your usual, characters, each opponent's set).
  Opponents: "The rivalry" (all time, last 5, current streak).
- Shared setups (SharedSetup.cs, SharedSetupForm.cs; Profiles: "Share..." and "Open a shared one..."): a .ppsetup file
  (JSON) listing a profile's mods with their GameBanana mod and file ids; the friend's app shows which they have and gets
  the rest (download + the usual add steps, one at a time). Mods added from a file are listed but can't be fetched.
  Only gamebanana.com page links are kept from an opened file.
- Head pictures from the model (ModelPreview.RenderHead): found from HeadN (HeadJ/Head; no head bone: the whole model,
  Kirby/Jigglypuff), everything above it within reach. Stock icon made automatically when a skin has none (Add and
  Pack), and "Make one from the 3D model" on the stock icon and in-match portrait rows too.
- Mod Store collections: right-click a mod -> add to a collection / new collection (Favorite.Lists in favorites.json);
  Favorites has a dropdown to show one collection.
- Frame Data: on a narrow window "Save GIF..." becomes "GIF..." so it doesn't cover the "Pause on quick hits" box.
- Punish it / Compare: their text stayed put while the boxes behind it scrolled (on Windows, TextRenderer ignores the
  view's scroll offset unless given PreserveGraphicsTranslateTransform: ScrollView.DrawT adds it). Controls on a
  ScrollView ("You play", "Compare with") now scroll with it instead of covering the rows.
- Frame Data's move filter buttons share the list's width when they don't fit ("Defense" was cut off).
- "How accurate" left Frame Data's tab row: Settings → Frame data → "How accurate is the frame data?" opens it.
  Settings' cards restack when one changes height (the SD card card could end up out of place).
- Model Swapper lists your own costumes too: "costume 11: <skin name>" for ones added with the app (P+'s is still listed
  above it), "costume N: yours (put on by hand)" for hand-installed ones (HandMods.Last; the list refreshes when that
  look at the card finishes). Picking one previews it, "Save it as 3D" / "Save its pictures" use it, and it's the
  costume the new model goes on (Installer.CostumeNow: the card's file).
- (A "keep the costume's own head" option was tried in the Model Swapper and taken back out the same day: it's being
  made as a separate Blender tool instead, BlenderKit/.)
- Found while looking at Striker Peach (a hand-installed FitPeach19): P+'s Peach already has full legs under the dress
  (polygon9 has points on LLegJ/LKneeJ/LFootJ), so a dress-less costume is the dress object (polygon19, skirt bones)
  left out plus new parts rigged to P+'s bones. Smash 4 ZSS keeps Brawl's ZSS head objects (samus03_kao, matsuge).
- Model Swapper buttons renamed "Save its pictures..." / "Save it as 3D..." (they now save whichever costume is picked).

## Blender Kit (separate test tool, 2026-11-01) — BlenderKit/, built to "G:\P+ ModManager\Blender Kit"
- P+ Blender Bridge.exe (BlenderKit/Bridge, compiles the Core sources, not part of the app):
  - Send: costume → rigged GLB (RiggedExport): every bone as a node (local = inv(parent bind) × bind), skin with
    inverse bind matrices, each default-shown object as its own mesh with JOINTS_0/WEIGHTS_0 (top 4), its picture
    (image named after the TEX0). Objects ≥80% head-weighted are named "PPlusFace_". Folder: Documents\P+ Blender
    Kit\<label>\ with costume.pac + pplus-costume.json (fighter, file number, hidden bones).
  - Bring back (BlenderImport + ModelSwapKeeping.BuildKeeping, a copy of ModelSwap.Build): weights read from the GLB
    skin by bone name, matched by position; keeps the costume's own objects that are hidden by default (items, other
    faces) and, if ticked, its PPlusFace ones; a material's shader is only removed when no kept material shares it
    (removing a shared one left a dangling Shaders entry: BrawlLib then crashed reading the file); pictures that come
    back unchanged reuse the costume's own TEX0; cut-out alpha → CMPR. Then CheckModel (every MDL0 group entry inside
    the model) and the app's memory budget (CostumeMemory.Shrink if over). Output "<label> (from Blender)\Fit*.pac".
  - Checked: Striker Peach round trip (Blender 4.2 and 5.2): every point's bones identical, 1,002 KB / 1,029 KB,
    passes P+ Modding Tool's checks; kpop body + Peach's head: 1,004 KB / 1,010 KB.
- pplus_blender_kit.py v0.2 (Blender 4.0+, tested headless on 4.2.23 and 5.2.3): a 5-step panel (Open the costume, Your
  model, Line up the joints, Finish, Save) that shows the current step's instructions. Save pictures for a 3D model maker
  (Workbench, flat light, orthographic front/side/back, 1024 px, see-through background, same scale). Joint dots
  (empties "Dot: <bone>" in "Joint dots") on HipN, NeckN, L/R ShoulderJ, ArmJ, HandN, LegJ, KneeJ, FootJ, ToeN; Fit =
  segment warp (each bone segment rotated/stretched onto P+'s; inverse-distance^4 blend of the 3 nearest segments;
  hands/head rigid), Undo fit, Compare. Finish: "Use P+'s head" / "Keep my model's head" (pplus_own_head). Rigging by
  Data Transfer from a hidden joined copy of the costume (nearest face interpolated, limit 4, normalized).
  Fit check (Striker with arms +25%, legs -15%): mean error 2.73% -> 0.31% of height.
- pplus_start.py: the Bridge's "Open it in Blender" runs blender --python pplus_start.py -- "<glb>" "<bridge exe>":
  loads the kit from its folder (nothing installed), opens the costume, opens the sidebar on the "P+ Kit" tab. The
  panel's Save runs the Bridge with --bring "<glb>" [--own-head], which makes the costume file by itself.
  Blender is found from blender-path.txt (Documents\P+ Blender Kit), the Uninstall registry keys, Program Files\Blender
  Foundation, Steam, or the .blend association (newest version); otherwise asked once.
- RiggedExport flattens layered eye materials (lid / shine / iris / white on TexCoord0..3) into one picture on the lid
  UVs, so eyes aren't black in Blender and in the pictures.
- If a costume is still over the memory budget after CostumeMemory.Shrink, the pictures that came from Blender (not the
  costume's own TEX0 names) are halved, biggest first, down to 128 px, and the notes say so (P+ head + its blink
  textures leave little room: Striker + kpop body with P+ head came to 1,013 KB / 1,029 KB with the body picture at 128).
- "Install the P+ Kit into Blender anyway" runs Blender in the background: addon_install + addon_enable + save_userpref.
- Kit v0.3 (after the first real try with kpop peach 2 on Striker Peach): 6 steps (open, your model, clean up, joints,
  shape and finish, check and save).
  - Model import welds UV-seam splits (glTF merge_vertices), so a Meshy model is a few real pieces (body, hair, shoes...)
    instead of ~1,200. Sized by the hands' height when its arms are out (hair/crown don't count); Bigger/Smaller.
  - Clean up: "Take off the hair" removes non-body pieces reaching above the neck; Click pieces / Box a part + Take off
    (whole piece or just the selection); Put it all back (hidden copy "Your model (as it came in)").
  - Fit rewritten: parts = segments (limbs, spine), ends (hands, head) and regions (hips = pelvis+hips, chest =
    neck+shoulders, one affine map each: the old pelvis->hip segments turned the crotch in opposite directions and
    pinched it). Smooth inverse-distance^4 weights with no nearest-3 cut-off (the cut-off made creases), never across
    the body's middle.
  - Shape handles: an armature on the fitted model (Hips, Body, Head, upper arm/forearm/hand, thigh/shin/foot, parented,
    inherit_scale NONE) with the same weights; buttons nudge / longer / shorter / thicker / thinner / turn the selected
    handle (and its mirror with "Both sides"), reset, keep (applies the modifier). Arrow buttons move selected joint dots too.
  - Rigging: after the Data Transfer, weights are smoothed over the model's surface (10 passes) and a point never keeps
    an other-side bone (pieces off to one side, like shoes, count as that side; all-wrong points get the mirrored bone).
    Fixed the shards in poses (feet touching took the other leg's bones; Striker's own boots have some).
  - Test move is a keyframed loop (world-axis rotations, play/stop toggle); every step has Undo; Save explains when the
    model isn't finished; "Keep my model's head" no longer leaves the costume's body visible (it was saved twice).
- Bridge: a GLB not in a Bridge folder is made for the costume picked in the Bridge (asks first); a GLB with shapes not
  rigged to P+ (Blender's own Export) is refused with what to do. Checked: kpop peach 2 on Peach costume 11 (P+ head),
  Blender 4.2.23 and 5.2.3, Bridge in game poses, 1,028 KB / 1,029 KB.
- Kit Finish redoes the model's picture: new Smart UV layout ("P+ picture"), Cycles bake of the diffuse colour from the
  old picture to a 1024 one, one plain material. A Meshy atlas (4096, hundreds of scraps, mostly hair) turned to noise
  when the Bridge shrank it to fit; the baked one stays clean. Bridge: pictures no material points at (the old body's
  speclevel, Striker's leftover p_u_* dress pictures) are taken out first (frames like PeachTexEyeLid.0N stay);
  new pictures are halved down to 128 at most. kpop peach 2 + P+ head on costume 11: body picture 512, 1,006 / 1,029 KB.
- The Bridge shows where it saved the costume (full path), opens Explorer with it selected, and has a
  "Show the costume file" button over the results.
- CRASH FIX (2026-11-01): taking out "unused" pictures removed p_u_canopy/frill/shank/top_frill from the costume's
  "Texture Data [0] [Group 1]": Peach's parasol (WpnPeachKassar, in FitPeachEtcNN.pac) takes its pictures from there,
  so the game crashed loading her. Now: a picture is only taken out if it's in the costume model's own BRRES and no
  MDL0 in Fit<P>.pac / every Fit<P>Etc*.pac on the card points at it (no card: nothing is taken out); and any picture
  those files use that the costume had and lost is put back (TEX0 exported from the original, re-added to the BRRES of
  the same name). Shrinking now halves one step at a time with the lighter formats after each, so pictures only get
  as small as needed (kpop peach 2 + P+ head: 256, 1,022 / 1,029 KB, parasol pictures kept).
- CRASH FIX 2: that costume still crashed. Peach's card: FitPeach19 (Striker) unpacks to 1,035,456 + Etc19 224,800 and
  works; ours was 1,044,672 (+9 KB), under the computed room (FighterBlock - Headroom - Motion - Etc = 1,053,568) but
  over every costume known to work. The Bridge's budget is now the original costume's own unpacked size (the computed
  room isn't reliable). The kit's Finish also lightens models over 4,000 triangles (Decimate collapse) before rigging:
  kpop peach 2 6,765 -> 5,450 triangles in the costume, body picture 512, 1,033,152 <= 1,035,456. NOTE: P+ Modding
  Tool's own check (max(room, original)) let the 1,044,672 file through: the app's rule needs the same change.
- CRASH FIX 3 (the real one, from Brawl Online's dolphin.log): every crash was at "fighter instance create PEACH" with
  "Invalid write to 0x0000011c, PC = 0x807135b4" while Fighter1Resource was only 96% used (5.09 MB). The costumes had
  477 / 472 bone mixes (BrawlLib _influences) where Striker has 300: BuildKeeping budgeted the NEW shapes' mixes at the
  costume's whole count and forgot the kept shapes (P+ head, faces, items) have their own (180). Now: new mixes get
  original - kept (300 - 180 = 120); the Bridge counts the result and rebuilds with a lower budget if it's still over.
  kpop peach 2 on costume 11: 280 mixes, 995 KB / 1,011 KB.


## Fast save (2026-11-01: tried as "P+ Modding Tool (fast save test).exe", now the normal way to save)
- Why saves were slow: each save copied the whole SD card (4 GB) to sd.raw.skinmanager-new, changed it, swapped it in,
  then moved the old card to Data\LastSave for "Undo last save" (another 4 GB copy when Data is on another drive).
- Fast save (Installer.FastSave; Program.FastSave is true since the test build worked on Senpai's card):
  Fat32Image(path, writable, deferred: true) keeps every write in memory (4 KB blocks; reads see them). Nothing reaches
  the card until Commit: the blocks that really changed are compared with the card, their current bytes written to a
  journal (Data\LastSave.new\card-before.journal, flushed, state 1), then the new blocks written and flushed (state 2),
  then the usual read-back check; the mods list saved (state 3); LastSave.FinishJournal makes it the undo point.
  A failed save before Commit leaves the card untouched; after it, CardJournal.RollBack puts it back.
  Undo last save = RollBack of that journal. On start, RecoverInterruptedSave rolls back a journal left at state 1 or 2
  (the save stopped halfway; the mods list is still the old one).
- Checked: 5 random sequences of 60 writes/deletes on a FAT32 image (mkfs.fat): old way and fast way give the same
  files, folders and free space (only folder-entry clock stamps differ); undo puts the image back byte for byte; a
  journal with half the blocks written and the save stopped puts it back byte for byte; recovery acts on states 1-2
  and not 3. Installer on the 2 GB test card (install, undo, off, on, a failing save, delete both): same results as
  before, card untouched by the failing save, undo exact; about 1 s a save against 10-12 s copying the card.

## Stage problems we know, and what catches each (regression list)
Every stage mod goes through these before it's put on the card. When a new kind of failure turns up, it's added here
with its evidence, and a check for it.
1. Too big a file for online play: SkinCheck.Size against StageCheck.MaxStageBytes (P+'s biggest stage file).
2. Unpacks past the game's stage memory: StageCheck.MemoryCheck (StageResource 0x666700 bytes; a stage that unpacked to
   8.2 MB gave "gfPoolFree Error" right after loading, then froze). Pictures nothing visible uses are made tiny in
   "looks" installs so a map plus P+'s pictures fits.
3. Plays differently (online desync, not a crash): StageCheck.Compare of floors, platforms, blast zones, spawns, camera,
   moving parts and the models the stage program names. Fixed by MakeMatch (P+'s gameplay parts copied in) or by
   "its looks on P+'s stage" (SwapLooks).
4. The stage program works with parts the map built differently: StageCheck.ProgramParts (names from the slot's module,
   like StgGreenhillMain) -> refused for 1:1, goes in as looks.
5. Unpacks past the room its stage program leaves (freezes as the stage loads): StageCheck.RoomFor + MemoryCheck(file,
   bytes, program), fixed by StageCheck.FitMemory. The game loads a stage file at the end of stage memory and unpacks it
   in place from the front; some stage programs take memory first. Pokemon Stadium 2 (st_stadium_static.rel) takes
   0x209800 bytes, leaving 0x45CF00 (4,468 KB; the app allows 4,403 KB, 64 KB kept free). "KOF Stadium [PS2 ALT]"
   unpacked to 4,807 KB (whole file) and 5,666 KB (looks on P+'s file): the unpacked parts ran into packed data not yet
   read and the game froze (dolphin.log: invalid reads at PC 0x80014de4 = gfArchive's entry walk, then "gfPoolFree
   Error"). Evidence: main.dol disassembly (0x80014da8 walks ARC entry headers) and a simulation of the in-place unpack
   that reproduces the game's exact bad address (0xa1ae2dea) from the looks file; the same model puts P+'s Stadium 2
   file and its Gateway and Abandoned alts inside the room and both KOF installs past it. FitMemory first leaves out
   effects banks P+'s file doesn't have, then halves the biggest pictures until the file fits; StageForNow re-fits
   stages already on the card at the next save. KOF now: 4,364 KB "made to match" (effects bank left out, one shadow
   picture at half size), simulated unpack ends 103 KB clear. Other stage programs: only the whole stage memory is
   known (item 2); add a program to StageCheck.TakenFirst when one is measured.
   (Earlier guess, withdrawn: that KOF's effects bank ef_StgStadium froze the game. A looks install without it froze
   the same way, and FD [PM] carries an effects bank P+'s file doesn't and works. The structure check that sent such
   maps to looks installs is removed.)

6. Scenery sits higher, lower, bigger or smaller than P+'s floors (the map was made for another version: SSBU FD's deck
   7% narrower than P+'s FD, so fighters stood past its edges): StageCheck.LineUp + MoveLooks move the map's scenery
   onto P+'s floors before it's made to match or put on P+'s stage (see "Lining a stage's scenery up" below).
7. Scenery that doesn't line up piece by piece (a platform elsewhere, a wider main floor, a platform P+ doesn't have,
   a P+ floor the map draws nothing for, a stage laid out differently): StageCheck.FitPieces (StageFit.cs) fits each
   floor's scenery onto P+'s floor, hides what has no floor under it and keeps P+'s own look where the map draws
   nothing (see "Fitting a stage's scenery floor by floor" below). Evidence: Galaxy Colosseum on Battlefield (54 units
   off: its moving platforms floated where P+ has fixed ones, P+'s platforms were invisible), Mario Circuit Ville on
   Rainbow Road (76 off: Smashville's ground floating in the middle of Rainbow Road's roads, the roads invisible).
8. Scenery bent by several bones moved to the wrong place when the whole map is moved (found in the 2026-11-04 review:
   20-60 units off on Orpheon's Queen, Green Hill Zone Brawl's Amy/Silver/Tails, Sauria's King and Arwing). Caught by
   MoveLooks itself now: after the move, every drawn object (bent ones placed by their bones, as the game does) must
   sit exactly where the move puts it, or the map is used as it is.
9. A map's art drawn on top of P+'s own part (a platform P+ keeps in P+'s look) when the floor-by-floor fit wasn't
   used because the fit number didn't change (Sunset Shore on Pokemon Stadium 2 Abandoned: both platforms twice).
   Installer.LinedUp now also takes the fit when it hides art where P+ draws its own or where the main ground goes.
10. A map's model that collision rides on (but that doesn't move) taken over by SwapLooks even when the floor-by-floor
   fit, which is what hides it where P+ draws its own, wasn't used. (Changed again in the second review, item 14:
   SwapLooks always takes such models over now; leaving them out left Yoshi's Story 64's whole ground missing.)
11. Sloped and curved floors (Corneria's Great Fox hull) weren't recognised as drawn: a piece's top was compared with
   the floor's average height. Compared with the floor's height at each point now (Run.YAt).
12. Points written back in a different number format than the objects read them with (Green Greens' apples stored with
   6 fraction bits, read with 12: 64x too small in game). StageFit.SetPoints rewrites the points in exactly the format
   they had; a point that doesn't fit it (or would need depth in a flat array) refuses the fit, nothing is half done.
13. In looks installs MoveLooks refused maps whose moving parts it didn't have to keep ("would have changed a part the
   match uses"): on P+'s file only the map's moving parts and position markers stay put (MoveKeep(looks: true)).
14. A map's part collision rides on that doesn't move was left out of a looks install unless the fit ran, leaving
   Yoshi's Story 64's ground missing: SwapLooks always takes it over now (only moving parts and markers are left out).
15. Every P+ part collision rides on measured 0 wide (the model's box was read before its pose was set), so all of them
   kept P+'s look. ModelWidth sets the pose first. Then (second review) a wide part that moves lost its look:
   a part that moves always keeps P+'s look now (OwnLooks, SwapLooks).
16. Animated scenery measured at its resting pose (frame 0) while the game starts it at frame 1 (Yoshi's Story settles
   5 lower): pieces, floors and riders are read at frame 1 (StartPose).
17. Two objects sharing one array of points, one moved and one not (or moved differently): Edits finds every point
   wanted in two places, including objects that weren't fitted at all (bent by several bones, lines only), and refuses.
18. Fit measured differently before and after (the "before" counted floors no scenery draws): Base and After now both
   count the map's scenery against P+'s floors, P+'s own parts as drawn; a fit that leaves a P+ floor bare (PBare) is
   only taken when it's clearly better.
19. Two stages being made at once could delete each other's picture folder (MakeMatch's cleanup ran outside the lock).
20. Floors measured on the moved map counted twice as far when their collision rides on a bone whose animation
   MoveLooks moved along (Yoshi's Story alts, Orpheon, Green Hill Zone Brawl): taken from the map before the move now
   (MovedRuns), and a floor counts as staying put only when the part it rides on also draws it (StaysPut).
21. A move by the main floor that ends up worse than leaving the map where it is (fitted piece by piece both ways) is
   dropped (Installer.LinedUp compares the two); a move nothing could be fitted after has to be clearly better alone.
   An alt whose main floor is one of P+'s floors already (P+'s Pokemon Stadium 2 has two main floors 3 apart) isn't
   moved by it.
22. A part whose animations each hold it still but at different places or sizes (Corneria 64's ship: "Small" and
   "RealBig") counted as moving, so its look was left out: each animation is checked on its own (Steady).
23. A loose piece not given to any one floor (Delfino Plaza's sloped platform reaching over its neighbour's line) stayed
   floating when every floor it stood on was hidden: it's hidden with them now.

24. Old builds of a stage mod saved as "P+'s file" (2026-11-03): when "Make them fit" made a mod smaller (or a P+ update
   had it fixed again), the copy still on the card was taken for P+'s, so turning the mod off put that old, too-big copy
   back (all 5 Smashville times of day under Xenoville, FD Brawl under FD Ultimate). StageForNow reads P+'s file before
   the copy changes and PutStage is told the copy's old fingerprint; OriginalsRepair puts P+'s real file back for
   originals already saved wrong (taken from the other cards, checked against P+'s release list).
25. P+'s palette pictures made plain (2026-11-04, Snowman's Land on Dream Land froze the game while loading: Dream Land's
   program couldn't put its picture on its Scarfy model): ShrinkHidden re-encoded hidden pictures as 4x4 I4, palette ones
   too, while the model still asks for the palette. Palette pictures (and ones sharing data) are left alone now;
   StageCheck.BrokenPictures finds installs made before (Phendrana Husk, Entrance, Final Boxing had them too) and the
   "Fix them" bar makes them again.
26. A map's looks put in a part of P+'s file that's only far-off background (2026-11-04, Emerald Coast on Green Hill
   Zone: its ground missing in game and its boardwalk huge in front of the camera, while the file drew it exactly on P+'s
   floor; Phendrana Husk on Frigate the same). The game doesn't draw such a part in place. StageLooks.LooksPart puts the
   looks in the part with P+'s own ground when the biggest part has nothing within the fighting area and nothing the
   stage program names; LooksMisplaced finds installs made before.
27. A download with stage files for several stages (2026-11-04, Radical Highway: two Green Hill Zone files and a Luigi's
   Mansion one) went entirely on the first file's stage, and picking another file didn't change the stage shown.
   StageVersionsForm keeps each file's own stage and ticks and installs them all; files named like P+'s (or one letter
   off, or a known old name: STGLUIGIMANSION for P+'s STGLUIGISMANSION) are ticked for that version.
28. Fixed (2026-11-05): a map platform also touching a smaller floor next to it was bent instead of hidden, so its art
   stayed under P+'s own platform (Entrance (Persona 4) on Pokemon Stadium 2 Abandoned, left platform). A flat piece that
   is still bent after the fit, lies only over floors that are hidden, mostly covers a floor paired with P+'s own
   platform, is no wider than it, and sits inside it, is now hidden too (StageFit, after the loose-piece hiding). Taller
   art like the TV pile next to it is left alone. Checked over the whole catalogue (210 pairs): only this one changed.
   Watch for: a platform's art left under P+'s platform, or art next to a platform going missing.
29. Checked and ruled out (2026-11-04): bone flags that don't match the bone's values (Emerald Coast's "main" bone says
   "no transform" but moves and scales). P+'s own Rainbow Road has the same and draws right, so the game uses the
   values; not a cause.
30. OPEN (2026-11-05): Snowman's Land (P+ DL 1:1) on Dream Land still crashes after the palette fix (item 25). The game
   stops 0.25 s after the stage loads, in its shadow system (stShadow, sora_melee 0x80933c84): it reads the width and
   height of a shadow texture slot whose texture pointer is empty. Shadow surfaces are materials with a "shadow"
   user-data entry; the slot keeps the texture bound to the material's first texture. Checked and ruled out, all
   against P+'s own Dream Land: palettes; texture binding (copied the game's rules: own part first, then every Texture
   Data, and a slot that names a palette needs a palette picture), every slot binds in both files; name lookups in
   every group; stored name lengths; the user-data values; model file versions (all 9); model count in one part (P+
   has up to 142); the shadow system's limits (16 and 24, the same for every stage). What differs from P+'s file and
   the mod's own file: the map's 4 shadow surfaces use the renamed texture zMap_TShadow1 inside the part (2 shadow
   textures instead of 1), and part [1] gained AnmTexPat and AnmVis groups, so the game builds all 51 models there
   with extra buffers. Also ruled out since: texture-swap/visibility animations in a part (Entrance and Final Boxing
   have them and work); the shadow texture inside a part (Battlefield's Palutena look and Entrance have that and
   work); a second ground binding part [1] again (P+'s Dream Land program gives each ground its own part: 0 Scarfy,
   1 main art, 2 Whispy, 3 block positions, 4 blocks, 101/102 trainers). The mod's own file can't be used whole: it
   fails the online check (floors, spawns and stage events differ from P+'s). What's unique to this build: 51 models
   in one part, all built with extra buffers (next biggest working part: 21). Every input the crashing code reads is
   valid in the file, so what's left is at runtime (most likely memory); it can't be proven from the file alone.
   Not fixed.
31. OPEN (2026-11-05): Emerald Coast (P+ GHZ 1:1) still looks wrong in game after the far-background fix (item 26),
   though the misplaced check now passes on the installed file. Item 26 doesn't cover it.
32. Not a problem (2026-11-05): the Radical Highway on Green Hill Zone's Z alt is P+'s own stage (the card's file is
   P+'s release file, unchanged). The mod's Green Hill Zone file went on the Marble Zone alt (R).

## Stage pictures (previews): what used to come out blank, and why
Checked on all 286 stage files on the card: 281 draw now (before: 256). Still blank: Castle Siege's three files (no
floors in the file at all) and two files that aren't stages (character roll, dual dummy).
- Floors that all ride on a part, with only walls fixed (Pokemon Stadium 2 and its alts, KOF Stadium, Port Town's PS2
  alt...): StageCheck.StandOn now takes the floors of the first set of collisions that has any.
- A rotating stage's sides are floor, wall and ceiling at once (Brinstar Depths): any surface with the floor bit counts.
- Broken points (not a number) in a model made the drawing stop (Golden Temple's Tempest, Pictochat alts, Luigi's
  Mansion alts): those triangles are skipped.
- When there's still no picture, the stage window says "No picture of this one" instead of taking the box away (which
  made the window jump).

## Code cleanup (2026-11-02, 1.3.1)
- Unused code found by compiling everything (the app, the Blender Kit bridge and Setup) and listing every type, method,
  property and field nothing refers to, repeated until nothing new turned up. Removed: the old stage install window
  (StageForm, replaced by the stage versions window), the stage standing check (ModelPreview.CheckStanding/Standing),
  costume stretch measuring, the old "nearest stage" lookups (StageIndex.Closest/ByShape/Matches), stage outline
  markers, and about 40 smaller leftovers (unused overloads, constants, fields that were only ever written).
- Unused parameters, usings and assignments taken out (the build's code-style analysers report none now); members that
  hide a WinForms one (Left, Top, Text, Events) say so with `new`.
- Every class, and every method or property longer than a couple of lines, has a comment saying what it's for; doc
  comments that had drifted away from their member were put back above it or removed.
- Checked: the code-only comparison (comments ignored) of before/after the comment pass is identical; frame data for the
  whole cast is the same row for row, and move pictures are byte-identical.

## Lining a stage's scenery up with P+'s floors (2026-11-03)
- StageAlign.cs (StageCheck, partial): a map's scenery is built around its own collision, so the map's floors against
  P+'s say where its ground will sit. LineUp() finds the move that puts them on P+'s: a shift, or a shift and a size
  (uniform, around 0,0; a size change has to beat the shift alone by 0.25 units). Fitting: start from the widest floors,
  then 40 rounds pairing every floor point with the nearest point on the other side (both ways) and solving for the
  move by least squares; pairs over max(2, 3x median) apart stop counting after round 8; repeated floors count once.
- Used only when it's worth it (Alignment.Worth): it moves something, the result is under 6 units off ("nearly lines
  up") and at least 0.5 better. A move that only halves a big difference would leave the ground between two floors.
- MoveLooks(): the first bone of every model that draws something and isn't a gameplay part (position markers, models
  collision rides on, models the stage program names) gets scale *= s, translation = s*t + shift; every CHR0 key that
  moves those bones gets the same (scale keys and tangents *= s; translation keys s*v + shift, tangents *= s), unless
  the entry uses the model's own value. (Scenery bent by several bones goes along since 2026-11-04, see the review.) Refused when a
  scenery root bone shares a name with a gameplay part's bone. Checked before it's returned: every gameplay fingerprint
  identical to the map's own, apart from the moved models.
- Installer.LinedUp runs it on the map before MakeMatch (PlanFixedStage), SwapLooks (PlanLooks) and the looks redo
  after a P+ update (StageForNow). StagePlan.Aligned says what was done; plan.Fit is the fit after the move. The stage
  window's verdict says "lines up once its scenery is made 7% bigger, ...".
- Measured on every stage download in Data\Downloads against P+ v3.2.1's files (Linux build, Mono):
  SSBU Final Destination Reworked (all 9 versions) on FD: 5.6 -> 0.1 (7% bigger; its deck now spans -86.5..85.4,
  P+'s floor -85.6..85.6). MK Adventure GHZ's STGFINAL on FD: 32.5 -> 0.0 (28.5 up, 42.6% bigger; its CHR0 root keys
  moved with it). Sunset Shore on FD: 8.2 -> 1.7. All of them still fingerprint identical to P+'s (Compare passes).
  Not changed (a different layout, no single move fixes it): Galaxy Colosseum on Battlefield (54 off, P+'s platforms
  have no scenery there), Cocktail of Dreams on Delfino Secret (52), the old Rainbow Road on P+'s Rainbow Road (65).
  Downloads that already line up (19 files) are untouched.
- Not covered: a map whose collision already matches P+'s but whose scenery doesn't (the MK Adventure GHZ grass block
  case: the stage program places it). Per-part moves: see the next section.

## Fitting a stage's scenery floor by floor (2026-11-03)
The four ways a map's looks missed P+'s floors, and what StageCheck.FitPieces (StageFit.cs) does about each. It runs
after LineUp/MoveLooks (Installer.LinedUp), on the map's scenery only; collision, blast zones, spawns and the program's
parts are never touched, and P+'s file is checked to fingerprint the same before it's used.
1. Whole map moved or resized: LineUp/MoveLooks as before. New: when no single move fits everything (platforms
   elsewhere), MainLineUp moves the whole map by its main floor alone (shift, and uniform size when the two main floors
   are within 0.6-1.67x of each other's width), and the rest is fitted floor by floor. MoveLooks now also moves scenery
   bent by several bones: only the first bones' places change, their resting places (bind matrices) stay as they were,
   so bent scenery goes with them (see the review below). A floor whose collision rides on a part MoveLooks leaves
   where it is counts as moved when other scenery draws it (StaysPut, second review).
2. Pieces in other places: the map's floors are paired with P+'s (main with main when they're alike, then each platform
   with the one on the same side at about the same place; a layout that isn't alike pairs floors where they are). Each
   model object's triangles are split into the separate pieces they make (so a platform built into the stage's mesh is
   still its own piece). A piece belongs to a floor when its top lies on it (main floor: within 4 units over a quarter
   of it; platform: within 3 units over half of it) and it reaches the line fighters stand on (z within 10 of 0), or
   when it hangs right under a platform whose top is in the same model, or is the stage's body under the main floor.
   Those pieces move with their floor (and are stretched sideways to P+'s width). Scenery built as one piece with
   everything else is bent near each floor (full move next to it, fading over 6-12 units; the stage's body all the way
   down), only at the depth of the stage itself. Animated pieces only ever move as a whole (never bent), placed where their
   animation has them at frame 1.
3. Main floor another width: its pieces are stretched (or narrowed) sideways to P+'s main floor.
4. Floors missing or extra:
   - The map draws a floor P+ doesn't have (in the fighting area): its pieces are shrunk to nothing. A moving platform
     P+ has nothing like (no P+ moving platform where it starts): the art riding along with it (animated objects that
     stay on it at 4 points of its animation) is shrunk too. A map floor where P+ draws its own moving part (Corneria's
     Great Fox, an Arwing, FoD's top platform): the map's art there goes, P+'s stays.
   - P+ has a floor the map draws nothing for (looks installs only): P+'s own pieces for that floor stay in sight
     (SwapLooks' keepPPlus, "model/object"); the other pieces of those objects are shrunk. Objects that can't be split
     (bent by several bones: Rainbow Road's roads) stay whole when they draw no floor the map draws.
   - A stage laid out differently (main floors not alike): the map's ground is hidden and P+'s floors keep P+'s look,
     with the map's background around them.
- Floors: a floor riding a part the file doesn't have (Delfino's Secret and Rainbow Road ride "Colission") stays where
  it is; one riding a part whose animation moves it under 6 units (Yoshi's Story settles 5 lower after frame 0) counts
  as fixed where it mostly is; the same floor in two collision sets a unit or two apart counts once (the wider). The
  main floor is the one covering most of x -40..40 (Brawl's Green Hill Zone has far bigger floors far out).
- SwapLooks now takes over a map's models that collision rides on when they don't move (Yoshi's Story 64 draws its whole
  stage in one), whether or not it was fitted floor by floor; position markers and moving parts are still left out
  (StageCheck.MovingGameplay).
- The stage window: "lines up once its scenery is made 8% bigger, 1 of its platforms is moved onto P+'s, 2 platforms
  P+ doesn't have are hidden and P+'s own look is kept for 2 floors it doesn't draw, its scenery on P+'s stage". When
  a map made for the same program could go either way, the looks install is picked when it fits clearly better (P+'s
  own look can only stay there).
- Measured (Linux build, P+ v3.2.1's files; "off" = furthest a P+ floor is from scenery standing on it):
  Galaxy Colosseum on Battlefield 54.4 -> 0.0; Cocktail of Dreams on Delfino Secret 51.7 -> 0.0 (whole map 48.8 up,
  27% bigger); Mario Circuit Ville on Rainbow Road 76.8 -> 0.0 and the old Rainbow Road on P+'s 64.7 -> 0.0 (their
  ground hidden, Rainbow Road's 9 floors in P+'s look); Sunset Shore on FD 8.2 -> 0.0 (both platforms hidden).
  P+'s own alts on their normal stage (35 files): every one 0.0 except Yoshi's Story 64 (24.6: its stage art can't be fitted,
  so P+'s is kept) and Corneria Z (436, laid out quite differently; left as before). The installed looks (FD SSBU, Cocktail
  on FoD, Xenoville, Emerald Coast, Phendrana, Lunar BF, KOF, Floats, Sator) are unchanged (already 0.0 / 0.1).
- Not covered yet: a map platform moving with a different animation than P+'s moving platform in the same place keeps
  its own art (both animate on their own clock); map art for a floor that rides a moving map part can't be fitted
  to a fixed P+ floor (it's hidden instead).

## Review of the floor-by-floor fit (2026-11-04)
Every line of StageFit.cs, the StageAlign/StageLooks/Installer/StageVersionsForm changes and the BrawlLib calls they
rely on read through again, then the whole catalogue run again (18 download/stage pairs both ways, 42 of P+'s own alts
on their normal stage; Linux build in the cloud workspace this time, BrawlLib a4c1ee33 with the 32-bit-address patch;
the PC's build folder had been cleared). Found and fixed:
- MoveLooks put scenery bent by several bones 20-60 units from where it belonged (introduced 2026-11-03, shipped in the
  20:05 build; only maps moved as a whole that have such scenery: of the ones tested, P+'s Orpheon, Green Hill Zone
  Brawl and Sauria alts. None of the installed looks: the only one moved as a whole, SSBU FD, has no bent scenery).
  Cause: RecalcBindState rewrote the bones' resting places, and bent points are kept relative to those. Measured by
  moving each map by 1.2x plus (10, 5) and comparing every object's points: with the fix, Orpheon 65 solid + 3 bent,
  Green Hill Zone Brawl 7 + 12, Sauria 62 + 2, SSBU FD 30 + 0 objects all exactly where they should be, 0 wrong
  (before: 2, 12, 2 wrong). MoveLooks now runs that check itself on every move (regression list 8).
- Installer.LinedUp dropped a fit that only hid art where P+ draws its own (regression list 9), and SwapLooks took over
  ridden models without the fit (10). Sloped floors (11).
- MainLineUp decided "P+ draws its own main floor" by the model the floor rides on, not by what that model draws, so
  Yoshi's Story's alts (riding on Randall the cloud) were never moved as a whole. Uses the same drawing test as
  FitPieces now (MarkOwn): Yoshi's Story SSZ 64 and Cookie now move as a whole and fit 0.0.
- The stage window could say "lines up once left as it is": Alignment.Did, and the window only says what was done.
- A fit that changed nothing but found that P+ draws its own floors (Mario Circuit) wasn't reported, so the window
  fell back to the plain floor comparison (336 off, "doesn't line up well") for a stage that lines up: reported now.
- Tried and taken back: letting the map's art win over a P+ part that keeps P+'s look when the part doesn't move by
  its animation. The stage program can still move it (Corneria's Arwings and Great Fox aren't animated in the file),
  and there it would leave a moving platform invisible. P+'s part always keeps its look; the map's art there goes.
- Checked and fine: BrawlLib's object removal (SwapLooks) only removes points/normals/colours/UVs no other object uses,
  so P+ objects kept for floors the map doesn't draw keep everything they need; the floor pairing, hiding, bending and
  P+-art keeping on all 60 pairs; P+'s file still fingerprints identical to P+'s on every looks install (Compare).
- Result (after): every installed look as before (0.0 / 0.1), except Sunset Shore on Stadium 2 Abandoned, which now
  hides its own copies of the two platforms P+ draws (they were drawn twice). Downloads: Galaxy Colosseum on
  Battlefield 54.4 -> 0.0, Cocktail on Delfino Secret 51.7 -> 0.0, Mario Circuit Ville on Rainbow Road 76.8 -> 0.0,
  old Rainbow Road 64.7 -> 0.0, Sunset Shore on FD 8.2 -> 0.0, MK Adventure's FD 32.5 -> 0.0. P+'s alts: 0.0 except
  Corneria 64 (8.4), Corneria Sauria (10.7), Mario Circuit's Rainbow Road (43.3) and Yoshi's Story 64 (24.6), all
  laid out differently from the stage they were put on.
- Known limits: a moving map platform where P+ has its own keeps its art on its own animation clock; art that rides a
  moving map part can't be fitted to a fixed P+ floor (hidden instead); scenery bent by several bones is never moved
  floor by floor (only with the whole map), and art bent that way where P+ draws its own isn't hidden.

## Second review of the floor-by-floor fit (2026-11-04)
A second full read of StageFit.cs, StageAlign.cs, StageLooks.cs and Installer.LinedUp, plus a separate reviewer reading
the same code and BrawlLib's source without seeing the first review. Everything below was checked on the catalogue
(18 download/stage pairs both ways, 42 of P+'s alts on their normal stage) and a 530-pair sweep of maps onto other
P+ stages (no crashes or errors), on the Linux build. Fixed (regression list 12-23):
- Point formats (12), looks-mode refusals (13), ridden-but-still parts (14), part widths (15), frame 1 (16), shared
  points (17), the before/after measure (18), the cleanup lock (19).
- Floors on a moved map were measured twice as far out when their collision rides on a bone whose animation moved
  along (20). This made the first review's numbers for Yoshi's Story Toybox, Orpheon, Green Hill Zone Brawl and
  Cocktail of Dreams on Delfino Secret wrong (some "0.0" were by accident). Measured from the map before the move now.
- A move by the main floor alone is compared with leaving the map where it is, both fitted piece by piece (21).
- Animations that are versions of the stage (Corneria 64's ship) no longer count as movement (22); loose sloped pieces
  are hidden with the floors they stand on (23).
- Checked and fine (reviewer): SetPoints' header offsets, number types and export path (BrawlLib copies the replaced
  block as it is and fixes the offsets after); MoveLooks skipping the gameplay checks in looks mode (P+'s file is still
  compared in full before use); no new crash paths.
- Result: every installed look as before (0.0 / 0.1). P+'s alts on their normal stage: all 0.0 except Green Hill Zone
  Brawl (30.1; its breakable cliffs are bent with the rest of its ground, so they can't be hidden alone) and Mario
  Circuit's Rainbow Road (43.3), both laid out differently; Battlefield Melee 0.3. Downloads: Galaxy Colosseum on
  Battlefield, Cocktail of Dreams on Delfino Secret, Sunset Shore on FD all 0.0. MoveLooks self-test (1.2x + (10, 5)):
  Orpheon, Green Hill Zone Brawl, Sauria, Cookie 0 wrong. Every output's points read back exactly in their own format
  (only Smashville's balloon, which was already that way in the map, differs).
- Known limits (logged, not changed): pieces are placed where their animation has them at frame 1; on P+'s file the
  map's animations for its second and later model groups go after P+'s and may not play, so those pieces show at
  rest (usually the same place). Animations are matched to bones by name, as the game does. A point that doesn't fit
  its stored format refuses the whole fit (the map is then moved as a whole or left as it is; seen once: Pokemon Stadium
  2 Abandoned, which needs no move). On "made to match" installs, a P+ floor P+ draws itself is counted as drawn even
  when the match didn't bring P+'s part in (only matters for maps that aren't installed as looks). A platform floating
  more than 40 above P+'s highest floor is left in sight (Cocktail of Dreams' fruit on Delfino Secret).

## Third review of the stage fitting, and freezes during play (2026-11-04)
Stage fitting (StageFit.cs, StageAlign.cs, StageLooks.cs, Installer.LinedUp, StageVersionsForm) read through again by a
reviewer that hadn't seen the earlier reviews. Confirmed: every result still goes through StageCheck.Compare before it can
be installed (PlanFixedStage, PlanLooks, StageForNow), and every BrawlLib file the new code opens is opened under
BrawlGate.Lock and disposed. Fixed:
- Steady let an exception out when BrawlLib couldn't play one of a stage's animations; since SwapLooks asks it about P+'s
  own file, one such animation made every looks install on that stage fail. It now counts as "moving".
- Installer.LinedUp tried "moved by its main floor" and "left where it is" inside one try: if the second failed, the first
  was kept without the comparison. Each try now fails on its own ("not measured").
- FitPieces wrote the files with some models still posed at frame 1 (StartPose): every model is put back at rest first.
- MakeMatch's picture folders were only cleaned up on success: now any left over are cleaned up at the start of the next.
- Comments: MarkOwn's summary had ended up above StaysPut, a broken doc comment on Edits, a local function hiding
  StageFit.Moves (now FloorMoves), notes on Run.Done, Touch, TriField and the point numbering of bent objects.
- Cover (run for every piece against every floor) skips triangles outside the floor's height range or beside it before
  the per-point height test; the answer is the same (YAt never leaves Ymin..Ymax).
- Checked old against new on 6 pairs (Emerald Coast on GHZ, Phendrana on Frigate, Xenoville on Smashville, Cocktail on
  FoD, SSBU FD on FD Brawl, Sunset Shore on FD, Mario Circuit Ville on Rainbow Road): the fitted map and P+'s file come
  out byte for byte the same, and every result passes Compare. (SwapLooks' own output differs run to run, old and new
  alike: BrawlLib writes it slightly differently each time.)
- Logged, not changed (looks only, not proven to happen): stretching the main floor up to 2x can fold the fade at its
  ends; objects with the same name in one P+ model share a keep key; a whole object kept for a bare P+ floor isn't
  re-checked for shared points; the "fixed" and "looks" plans measure a ridden ground differently (StageVersionsForm
  picks between them by that number). Repeated ApplyCHR in Runs/MovingGameplay/Steady and repeated file reads make
  planning a stage take a few seconds; a cache per file would cut that.
Freezes (the opponent card left over the desktop after the game and the app were closed; stutters):
- The opponent card only went away when the game's log said the match ended, so closing the game in the middle (or
  after the last game) left it up for good. Now everything over the game goes when the game's window has been gone for
  two checks in a row (about 4 seconds), and the overlays are closed with the app (MainForm FormClosed).
- While Brawl Online's Dolphin runs, the app runs at below-normal priority (GamePriority), so reading replays, the
  after-game notes and pictures never take the computer from the game; replays are read one at a time then, not four.
- GameWindow.Find (every 2 seconds) disposes the Process objects it gets (each held a handle until garbage collected).
- After a game: the second look 12 seconds later only happens when the first didn't find the game; the opponent card
  takes the stage names the Stat Tracker already read instead of opening the SD card again.
- Watchdog.cs: notes in Data\errors.log whenever the window doesn't answer for 1.5 s or more, with what it was doing
  (Program.Doing, set around the game-time work: following the log, the report, the Stat Tracker, drawing an overlay),
  the memory in use and whether the game was running. Tested with SKINMANAGER_TEST_FREEZE (a 3 s freeze is noted).
- The dolphin.log of the last session (2026-11-02, 20:42-22:14) shows the long stalls were the network (the other
  player going silent for 7 s, the rooms server unreachable); the modded stages cost Dolphin about the same per frame
  as P+'s own (game code 2.1-2.9 ms per frame, rollbacks 5-7.4 ms on average on every stage).

## Downloads with stage files for several stages (2026-11-04)
- Radical Highway r4 brings STGGREENHILLZONE, STGGREENHILLZONE_Z_RADICALHIGHWAY and STGLUIGIMANSION. Picking a file in step 2
  used to keep showing the first file's stage and only installed one file. Now each file keeps its own ticks and checks
  (StageVersionsForm.NewLook), picking a file opens its stage, and Install puts in every file that has versions ticked.
- Opening the window ticks, for each file, the version it's named exactly like (or the normal one of the stage it's named
  for under another name: STGLUIGIMANSION -> P+'s STGLUIGISMANSION, Stages.OldNames and a one-letter-off match), except
  a version one of your mods is on already. Ticking a version for one file unticks it for the others.
- Each installed mod is named after the download and its stage (and version when it isn't the normal one).
- Radical Highway's Luigi's Mansion file is 7 units off P+'s current Luigi's Mansion floors (made for an older layout),
  so its verdict says it doesn't line up well; its GHZ file is 0.0 off.

## Snowman's Land crash, and maps drawn off the floor on Green Hill Zone and Frigate (2026-11-04)
- Snowman's Land on Dream Land froze the game while loading. Dolphin's log: Dream Land's program (grDxGreensStage) couldn't
  put its picture on its Scarfy model ("model Scarfy, material Scarfy, texture Scarfy"), then a null pointer. Cause:
  ShrinkHidden made P+'s pictures nothing shows any more tiny by re-encoding them as plain 4x4 I4 pictures, including
  palette pictures (CI8 with a PLT0): the game still loads the model and asks for the palette. ShrinkHidden now leaves
  palette pictures (and pictures sharing their data) as they are. Installed looks with such a picture: Snowman's Land
  (6: LeafA-C, Scarfy, WarpStar, Water), Phendrana Husk (5), Entrance (4), Final Boxing (1).
- Emerald Coast on Green Hill Zone: in game its ground was missing and its boardwalk huge in front of the camera, while
  the file draws it exactly on P+'s floor. Its looks had gone in P+'s biggest scenery part, Model Data [1], which on
  Green Hill Zone holds only far-off scenery (none within the fighting area; the stage program names nothing in it) and
  isn't drawn in place. LooksPart: when the biggest part is such far-off background, the looks go in the part with P+'s
  own ground. Changes Green Hill Zone and Frigate Husk only of the installed looks (FD Brawl's part 1 holds bones the
  program names, Smashville's part 0 a model it names: unchanged).
- Installs made before: Installer.NeedsRemaking (BrokenPictures, LooksMisplaced; kept per copy and P+ file) puts a
  "Fix them" bar up (red when one can crash the game); StageForNow makes them again from the map they came as.
  Tested on the UI test card with the broken Snowman's Land: the bar, the fix, the new copy reads clean.
- Not fixed yet: Entrance (Persona 4) on Stadium 2 Abandoned draws its own left platform under P+'s (the piece also
  touches a TV pile's top, so it's bent instead of hidden). Changing how such pieces are assigned also hid the TV pile;
  left for a proper pass over the catalogue.
