# Sector Removal Updater

![GitHub all releases](https://img.shields.io/github/downloads/notaspirit/SectorRemovalUpdater/total) ![GitHub release (latest by date)](https://img.shields.io/github/v/release/notaspirit/SectorRemovalUpdater) ![GitHub latest release date](https://img.shields.io/github/release-date/notaspirit/SectorRemovalUpdater)

Sector Removal Updater (SRU) is a tool for automatically updating ArchiveXL Sector Node Removals and Mutations.

---

## Table of Contents

1. [Requirements](#requirements)
2. [Installation](#installation)
3. [Setup](#setup)
4. [Updating Removal Files](#updating-removal-files)
6. [Known Issues & Limitations](#known-issues--limitations)

---

## Requirements

### Mandatory

* **.NET Runtime 8.0.0+** ([Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/8.0))

---

# Installation

Download the portable version and open a terminal in the same folder as the executable.

---

# Setup

SRU requires hashes of all nodes for the source and target version to function, when available you can download precompiled hashes (recommended) or build them locally. 

## 1. Downloading 

1. Download the hashes for your versions from the [releases](https://github.com/notaspirit/SectorRemovalUpdater/releases/tag/PrecompiledBinaries).
2. Load the hashes using `SectorRemovalUpdater.exe LoadDatabaseFromFile "<PATH TO BIN>"` from a commandline in the same folder as the executable.

> Do not forget the quotes (`" "`) around the filepath when it has spaces. 

## 2. Building

1. Set the gamepath using `SectorRemovalUpdater.exe config set GamePath <PATH TO DIR>`.
2. Start the interactive mode using `SectorRemovalUpdater.exe start`
3. Build the hashes using `HashNodes`

> Do not forget the quotes (`" "`) around the filepath when it has spaces. 

---

## Updating Removal Files

1. Update removal files using `SectorRemovalUpdater.exe update "<PATH TO XL>" "<PATH TO OUTPUT XL>" <SourceVersion> <TargetVersion>`

> Do not forget the quotes (`" "`) around the filepath when it has spaces.

---

## Known Issues & Limitations

* SRU only matches exact nodes, meaning if a node changed it will not be found even if the change is minor.
* SRU does *not* validate input, meaning that given a file with incorrect nodes that ArchiveXL may skip, SRU will find matches for even when it isn't the node that was originally intended to be removed.
* The game contains some duplicate nodes, SRU only picks the first it encounters.
* If instanced or collision nodes fail lowering the MinimumActorHashMatchRate value may help find more but can introduce false positives.

