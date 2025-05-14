# WoW Addon Updater

A simple, lightweight tool for managing and updating addons from CurseForge for all versions of World of Warcraft (Retail/Classic/TBC/WOTLK/Cata/MoP).

![WoW Addon Updater](screenshot.png)

## Features

- 🔄 **No Login Required**: Works without any login credentials or API keys
- 📋 **Addon Tracking**: Keeps track of addons installed through the application
- ⬆️ **One-Click Updates**: Update all your addons with a single click
- 🔍 **Addon Search**: Search and install new addons directly from CurseForge
- ⚙️ **ElvUI Support**: Install, update, and manage ElvUI alongside your CurseForge addons
- 🗑️ **Easy Removal**: Easily remove addons you no longer use
- 💾 **Smart Version Tracking**: Keeps track of installed versions and notifies when updates are available

## Requirements

- Windows 10/11
- .NET 8.0 Runtime or later

## Installation

1. Download the latest release from the [Releases](https://github.com/vrilya/wow-addon-updater/releases) page
2. Extract it to a location of your choice
3. If you don't already have it, install the [.NET 8.0 Desktop Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/thank-you/runtime-desktop-8.0.15-windows-x64-installer)
4. Run `WowAddonUpdater.exe`

## Getting Started

1. **First Launch**: On first launch, you'll need to set your WoW addons directory in the Settings
2. **Select Game Version**: Use the dropdown menu at the top to select the correct game version
3. **Set Addon Path**: Click the ⚙️ (Settings) button and browse to your WoW addons folder
   - For example `C:\Program Files (x86)\World of Warcraft\_classic_\Interface\AddOns`
4. **Important Note**: The application only manages addons that it installs. For best results, I recommend removing previously installed addons and reinstalling them through this tool
5. **Scan for Updates**: Click the "Scan for Updates" button to check for available updates

## Usage

### Managing Addons

- **Scanning**: Click the "Scan for Updates" button to check for addon updates
- **Update All**: Click "Update All" to update all addons that have new versions available
- **Individual Updates**: Click the "Update" button next to any addon that needs updating
- **Remove Addons**: Click the trash icon to remove an addon you no longer want
- **Note**: The application can only track and update addons that were installed through this tool

### Installing New Addons

1. Click the "Search" button to open the search window
2. Enter an addon name or keywords in the search box
3. Select an addon from the results list
4. Click "Install Selected Addon" to download and install

### ElvUI Management

ElvUI is a popular complete UI replacement that isn't available on CurseForge. This tool can manage ElvUI alongside your other addons.

1. Go to Settings (⚙️)
2. Check the "Install/Uninstall ElvUI" checkbox to install ElvUI
3. ElvUI will now appear in your addon list and can be updated like any other addon

