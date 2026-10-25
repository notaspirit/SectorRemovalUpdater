# Sector Removal Updater

![GitHub all releases](https://img.shields.io/github/downloads/notaspirit/SectorRemovalUpdater/total) ![GitHub release (latest by date)](https://img.shields.io/github/v/release/notaspirit/SectorRemovalUpdater) ![GitHub latest release date](https://img.shields.io/github/release-date/notaspirit/SectorRemovalUpdater)

SRU is a tool for automatically updating ArchiveXL Sector Node Removals and Mutations.

The repository contains two front ends that share the same update, hash and settings services:

| Project | Description |
| --- | --- |
| `SectorRemovalUpdater.Gui` | Cross-platform Avalonia desktop app for Windows, Linux and macOS |
| `SectorRemovalUpdater` | Command-line interface for scripting and automation |

## Desktop GUI

The GUI wraps the normal update and hash workflow in a single window with an activity log.

### Using the GUI

1. Start the GUI build for your platform: `SectorRemovalUpdater.Gui.exe` on Windows, or `./SectorRemovalUpdater.Gui` on Linux/macOS.
2. Open **Hashes & Database** and either load a local precompiled `.bin` hash file, download one from the [PrecompiledBinaries release](https://github.com/notaspirit/SectorRemovalUpdater/releases/tag/PrecompiledBinaries), or build the hashes from your game installation.
3. Open **Settings** and set the **Cyberpunk 2077 game folder** to the folder that contains `bin/x64/Cyberpunk2077.exe`.
4. Open **Update file**, choose the ArchiveXL file you want to update and an output path, then select the source and target game versions. The file picker starts in the configured game's `archive/pc/mod` folder when no input file has been selected yet.
5. Click **Update file**. The original file is left untouched.

Precompiled databases can be downloaded directly in the app. The GUI downloads the selected `.bin` into a temporary folder, verifies it against the SHA-256 digest published by GitHub, imports it into the local LMDB database, and then deletes the temporary file.

The **activity log** at the bottom of the window captures everything the shared services print, and can be copied or saved to a text file. It also reports errors when a database or operation cannot be opened.

### Platform notes

* SRU itself runs on Windows, Linux and macOS. The game is Windows-only, so on Linux and macOS the configured **game folder** must point at a Wine/Proton prefix (or any other location) where the game files are visible to SRU.
* Building hashes reads the game archives and can take several minutes. A precompiled database is the faster option when one is available for your game version.
* The LMDB database folder and `settings.json` are shared with the command-line version:
  * Windows: `%APPDATA%\SectorRemovalUpdater\`
  * Linux: `~/.config/SectorRemovalUpdater/`
  * macOS: `~/Library/Application Support/SectorRemovalUpdater/`
* Windows-only extras bundled by WolvenKit (for example the ClearScript V8 native library) are not used by the GUI.

### Building from source

Requires the [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0).

```bash
dotnet build SectorRemovalUpdater.sln -c Release
dotnet run --project SectorRemovalUpdater.Gui
```

Run the command-line version with:

```bash
dotnet run --project SectorRemovalUpdater -- start
```

### Publishing

Publish a self-contained GUI build for each target platform. Native LMDB, Skia and HarfBuzz libraries are copied automatically for the selected runtime.

```bash
# Windows x64
dotnet publish SectorRemovalUpdater.Gui -c Release -r win-x64 --self-contained true -o publish/win-x64

# Linux x64
dotnet publish SectorRemovalUpdater.Gui -c Release -r linux-x64 --self-contained true -o publish/linux-x64

# macOS Intel
dotnet publish SectorRemovalUpdater.Gui -c Release -r osx-x64 --self-contained true -o publish/osx-x64

# macOS Apple silicon
dotnet publish SectorRemovalUpdater.Gui -c Release -r osx-arm64 --self-contained true -o publish/osx-arm64
```

On Linux and macOS, make the published launcher executable if necessary (`chmod +x publish/<rid>/SectorRemovalUpdater.Gui`). The macOS output is a plain executable, not a signed `.app` bundle; Gatekeeper may require allowing the file in **System Settings → Privacy & Security** the first time it is started.

## Usage Instructions (CLI)

### Installation & Setup

1. Download and Unzip the Portable Download from the Releases Page
2. Open a new terminal in the folder you unzipped SRU to

#### Getting the Hashes

You can either download precompiled hashes (based on basegame + phantom liberty) or build them yourself locally

##### Downloading the Hashes

This is the recommended option for versions which have a precompiled binary.

The CLI can list the available precompiled databases and load one directly. The download is verified against the SHA-256 digest published by GitHub, imported into the local database, and the temporary `.bin` file is deleted afterwards:

```bash
SectorRemovalUpdater.exe ListReleaseDatabases
SectorRemovalUpdater.exe LoadReleaseDatabase <Version>
```

The version shown after loading is the game version stored inside the downloaded database.

Alternatively, download the `.bin` from the Releases Page yourself and load it from disk:

1. Download the Hashes from the Releases Page for the Versions you need
2. Open a new terminal in the folder you unzipped SRU to
3. Load the Hashes with `SectorRemovalUpdater.exe LoadDatabaseFromFile <PATH TO BIN>`

##### Building the Hashes

This is intended for versions which don't yet have a precompiled binary.

1. Open a new terminal in the folder you unzipped SRU to
2. Set the GamePath with `SectorRemovalUpdater.exe config set GamePath <PATH TO DIR>`
3. Start the Interactivemode via `SectorRemovalUpdater.exe start`
4. Build the hashes with `HashNodes`
5. (optional) To Export the hashes for a given version use `SectorRemovalUpdater.exe SaveDatabaseToFile <Version> <PATH TO OUTPUT BIN>`

### Updating Removal files

1. Open a new terminal in the folder you unzipped SRU to
2. Run `SectorRemovalUpdater.exe update <PATH TO XL> <PATH TO OUTPUT XL> <SourceVersion> <TargetVersion>`

### Additional Notes

SRU only matches exact nodes, meaning if a node changed it will not be found even if the change is minor.
If instanced or collision nodes fail lowering the MinimumActorHashMatchRate value may help find more but can introduce false positives.
