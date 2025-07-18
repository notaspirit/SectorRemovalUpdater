# Sector Removal Updater

SRU is a tool for automatically updating ArchiveXL Sector Node Removals and Mutations.

## Usage Instructions
### Installation & Setup
    1. Download and Unzip the Portable Download from the Releases Page
    2. Open a new terminal in the folder you unzipped SRU to
    3. Set the GamePath with `SectorRemovalUpdater.exe config set GamePath <PATH TO DIR>`
#### Getting the Hashes
    You can either download precompiled hashes (based on basegame + phantom liberty) or build them yourself locally
##### Downloading the Hashes
    This is the recommended way for versions which have a precompiled binary.
    1. Download the Hashes from the Releases Page for the Versions you need
    2. Open a new terminal in the folder you unzipped SRU to
    3. Load the Hashes with `SectorRemovalsUpdater.exe LoadDatabaseFromFile <PATH TO BIN>`
##### Building the Hashes
    This is intended for versions which don't yet have a precompiled binary.
    1. Open a new terminal in the folder you unzipped SRU to
    2. Start the Interactivemode via `SectorRemovalUpdater.exe start`
    3. Build the hashes with `HashNodes`
### Updating Removal files
    1. Open a new terminal in the folder you unzipped SRU to
    2. Run `SectorRemovalsUpdater.exe update <PATH TO XL> <PATH TO OUTPUT XL> <SourceVersion> <TargetVersion>`

### Additional Notes
    SRU only matches exact node matches, meaing if a node changed it will not be found even if the change is minor.
    If instanced or collision nodes fail lowering the MinimumActorHashMatchRate value may help find more but can introduce false positives.
