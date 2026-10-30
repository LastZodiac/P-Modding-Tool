# Building and testing on Linux

The Linux version is the same `P+ Modding Tool.exe` as the Windows one, run by Mono. These scripts
build it on Linux, run it, and test it without a screen. Everything they
make goes into `build/linux/out/` (ignored by git).

## What you need

Arch:

    sudo pacman -S --needed git mono libgdiplus dotnet-sdk xorg-server-xvfb dosfstools mtools imagemagick fuse2

Debian / Ubuntu:

    sudo apt install git mono-devel mono-utils libgdiplus dotnet-sdk-8.0 xvfb dosfstools mtools imagemagick libfuse2

- `mono` / `mono-devel`: the runtime, `resgen`, `monodis`, and the .NET Framework 4.7.2 reference
  assemblies in `/usr/lib/mono/4.7.2-api` that the .NET SDK compiles against.
- `xvfb`, `dosfstools`, `mtools`, `imagemagick`: only for `smoke-test.sh`.

## Build and run

    build/linux/build-brawllib.sh   # BrawlCrate at the pinned commit, patched, built (about 25 s)
    build/linux/build-app.sh        # the app, laid out like the Windows release (about 10 s)
    build/linux/run-dev.sh          # runs it with the system's Mono (TRACE=1 for trace lines)

`build-brawllib.sh` keeps its BrawlCrate clone in `build/linux/out/BrawlCrate` and resets it to the
pinned commit on every run, then applies `build/patch_brawllib.sh`. It builds with:

- `-p:FrameworkPathOverride=/usr/lib/mono/4.7.2-api`: Windows' .NET Framework targeting pack doesn't
  exist on Linux; Mono's reference assemblies have the same API.
- `-p:DefineConstants=NOWPF`: the patch puts BrawlLib's WPF-only code behind `#if !NOWPF`.
- `-p:PreBuildEvent=`: BrawlLib's pre-build step is a Windows `cmd` command.

## Test

    build/linux/smoke-test.sh

Starts the app under Xvfb for 45 s with a temporary `HOME`, a fake `~/.config/Brawl Online` folder
and a 64 MB FAT32 `sd.raw`, and fails if the app exits or notes unexpected errors. `KEEP=1` keeps the
temporary folder (screenshot, logs).
