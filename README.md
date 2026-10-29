<p align="center"><img src="docs/logo.png" width="120" alt="P+ Modding Tool logo"></p>

<h1 align="center">P+ Modding Tool</h1>

<p align="center">Mods, stats and frame data for <b>Project+</b> on <b>Brawl Online</b>, in one app.<br>
No typing commands, no manual file juggling: everything is a button.</p>

<p align="center"><b><a href="../../releases/latest">⬇ Download the latest version</a></b> (Windows 10/11)</p>

---

## What it does

### 🎨 Mods
- Add **costumes (skins), stages, music and texture packs** from a file, or browse and install them from the built-in **Mod Store** (GameBanana) with one click.
- **Your own music:** drop in MP3 or WAV files and they become songs the game plays. Name them, choose where they loop, set the volume, listen first, then add them to any stage's songs or replace a P+ song.
- Turn any mod on or off at any time, keep profiles, and put P+'s originals back with one click.
- Every stage is **checked so you stay in sync online**. Anything that could break online play is refused before it touches your SD card.

![Mod Store](docs/mod-store.png)
![Costume slots](docs/costume-slots.png)

### 🧍 Model Swapper (beta)
Put a 3D model from another game or a 3D program (.glb, .gltf, .obj, .dae) on a P+ character. It keeps P+'s own skeleton and animations, so hits stay the same for both players.

![Model Swapper](docs/model-swapper.png)

### 📊 Stat Tracker & in-game overlay
- **Overview**: your form over your last games, and your **Smash stats** as tiles with trends: openings per kill, damage per opening, neutral wins, average kill and death percent. Plus your favorite kill move, the move you land most, and what kills you most.
- **Characters**: every fighter you play on one chart (comfort picks and traps), each with its own stats, the moves you land and kill with, and its best and worst matchups.
- **Matches**: a simple list of your games. Click one to see how it went: the percents over the game, openings, and every stock taken and lost (and with what).
- Your records by **matchup, stage and opponent**, and your **chance to win** before a match starts, with counterpick and stage suggestions.
- A see-through **overlay** on the game with your opponent's info, and a hotkey for the full view.
- Everything is read from Brawl Online's own game history and replays (read only, never changed).

![Stat Tracker](docs/stat-tracker.png)
![Your characters](docs/stat-tracker-characters.png)
![Your matches](docs/stat-tracker-matches.png)

### 🎯 Frame Data
Like a frame data site, for the **whole P+ cast**, read straight from P+'s own files:
- Every move played **frame by frame** with its **hitboxes and hurtboxes**: startup, active frames, first actionable frame, landing lag (and L-cancelled), damage, angle, knockback, shield damage.
- **Projectiles** and spawned hitboxes (lasers, arrows, Fox's side B...), throws, dodges and ledge options.
- **Launch & combos**: where a hit sends your opponent at any percent and DI, hitstun, frame advantage on hit and on shield, and a chart of the **true combos** each move leads into at every percent.
- **How to DI**: drag your fighter onto the move as it plays. It picks out the hitbox that would hit you there, shows where it sends you, and draws the stick: which way to hold to **survive**, which way to **get out of combos**, and how to **SDI**. With the percent each DI kills you at.
- Save any move as a **GIF**.

![Frame data](docs/frame-data.png)
![Launch and combos](docs/launch-and-combos.png)
![How to DI](docs/how-to-di.png)

<p align="center"><img src="docs/marth-forward-air.gif" width="400" alt="Marth forward air hitboxes"></p>

## Requirements
- Windows 10 or 11
- Brawl Online installed, with Project+ set up

## Install
1. Download **PPlusModdingTool-Setup-x.y.z.exe** from [Releases](../../releases/latest) and open it.
2. If Windows says *"Windows protected your PC"*: click **More info**, then **Run anyway** (the app isn't signed with a paid certificate).
3. Open the app. It finds Brawl Online by itself.

The app updates itself: when a new version is out, a bar appears with an **Update now** button.
Your mods, downloads and backups of P+'s own files stay in the `Data` folder next to the app.

## Is it safe to use online?
Yes. The app only changes what you choose on your own SD card image, keeps a backup of every original file, and
checks every stage before it's added so you stay in sync with other players. Stats only *read* Brawl Online's game
history; the overlay never touches the game.

## Notes
- Frame data and combo numbers come from P+'s own files; the combo chart is an estimate (shown on the page: no DI unless chosen, the opponent right in front, aerials landed and L-cancelled).
- DI advice and kill percents are estimates with the Melee rules P+ follows (DI turns a launch up to 18°), hit in the middle of a Battlefield-sized stage.
- Smash stats are worked out from each replay's percents and stocks: a hit is credited to the move the other player was doing at that moment, so projectiles and items show up as "other".
- A few hitboxes are made by the game's code rather than its move files and can't be shown yet.

## Credits
Made by **Last Zodiac**. Built on the work of the Project+ team, Brawl Online, [BrawlLib / BrawlCrate](https://github.com/soopercool101/BrawlCrate) (LGPL-3.0), [OpenTK](https://opentk.net) (MIT), [NLayer](https://github.com/naudio/NLayer) (MIT) and [GameBanana](https://gamebanana.com), where every mod belongs to its creator.

Building from source: see [build/README.md](build/README.md).

## License
Copyright © 2026 Last Zodiac.

P+ Modding Tool is free software under the **GNU General Public License v3.0** ([LICENSE](LICENSE)): you can use, study, change and share it,
but anything you release that's built on it must keep the credit and be released under the GPL-3.0 too, with its full source.

The name "P+ Modding Tool" and its logo identify this project. If you publish your own changed version, please give it a different name and logo.

Libraries it uses keep their own licenses: BrawlLib (LGPL-3.0), OpenTK (MIT), NLayer (MIT, in `SkinManager.Core/ThirdParty/NLayer`). See `LICENSES` in the app's folder.
