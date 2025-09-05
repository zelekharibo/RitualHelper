# RitualHelper

A Path of Exile 2 plugin for ExileCore2 that automates ritual deferring to help manage valuable items efficiently.

## Features

- **Automatic Item Deferring**: Automatically defer items based on configurable criteria
- **Existing Items Management**: Option to defer items that are already deferred
- **New Items Filtering**: Defer new items based on a customizable filter list
- **Human-like Behavior**: Configurable delays to simulate natural mouse movements
- **One-Click Operation**: Simple button interface for quick activation

## Configuration

The plugin provides several configuration options:

- **Action Delay**: Delay between actions (50-1000ms) to simulate human behavior
- **Random Delay**: Additional random delay (0-100ms) for more natural timing
- **Cancel with Right Click**: Option to cancel operations with right mouse button
- **Defer Settings**: Toggle deferring of existing items and new items separately
- **Item Filter List**: Comma-separated list of item names/types to automatically defer

## Default Filter List

The plugin comes with a default filter for valuable items:
- Perfect (items)
- Divine Orb
- Exalted Orb
- Chaos Orb
- Omen of (items)
- 20 (quality items)
- Petition Splinter

## Usage

1. Open a ritual window in Path of Exile 2
2. Click the pick button that appears next to the defer/cancel buttons
3. The plugin will automatically defer items based on your configuration

## Installation

1. Copy the plugin folder to your ExileCore2 plugins directory
2. Restart ExileCore2
3. Enable the plugin in the ExileCore2 settings

## Requirements

- Path of Exile 2
- ExileCore2 framework
- .NET 8.0

## License

This project is open source and available under the MIT License.
