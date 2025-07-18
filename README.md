# Sector Removal Updater

SRU is a tool for automatically updating ArchiveXL Sector Node Removals and Mutations.

## Usage Instructions
### Installation & Setup
1. Download and Unzip the Portable Download from the Releases Page
2. Open a new terminal in the folder you unzipped SRU to
#### Getting the Hashes
You can either download precompiled hashes (based on basegame + phantom liberty) or build them yourself locally
##### Downloading the Hashes
This is the recommended option for versions which have a precompiled binary.
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
