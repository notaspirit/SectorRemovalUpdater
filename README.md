# Sector Removal Updater

SRU is a tool for automatically updating ArchiveXL Sector Node Removals and Mutations.

## Usage Instructions
### Installation & Setup
    1. Download and Unzip the Portable Download from the Releases Page
    2. Open a new terminal in the folder you unzipped SRU to
    3. Set the GamePath with `SectorRemovalUpdater.exe config set GamePath <PATH TO DIR>`
    4. Set a Database location with `SectorRemovalUpdater.exe config set DatabasePath <PATH TO DIR>`
#### Getting the Hashes
    You can either build them yourself based on your local game files or download precompiled ones.
##### Building the Hashes
    1. Open a new terminal in the folder you unzipped SRU to
    2. Start the Interactivemode via `SectorRemovalUpdater.exe start`
    3. Build the hashes with `SectorRemovalsUpdater.exe HashNodes`
##### Downloading the Hashes
    1. Download the Hashes from the Releases Page for the Versions you need and unzip them
    2. Open a new terminal in the folder you unzipped SRU to
    3. Load the Hashes with `SectorRemovalsUpdater.exe LoadHashes <PATH TO BIN>`
### Updating Removal files
    1. Open a new terminal in the folder you unzipped SRU to
    2. Run `SectorRemovalsUpdater.exe Update <PATH TO XL> <PATH TO OUTPUT XL> <SourceVersion> <TargetVersion>`