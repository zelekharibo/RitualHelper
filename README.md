# RitualHelper

An ExileCore2 plugin for automating ritual defers in Path of Exile 2.

## Features

- One-click ritual defer automation
- Priority-based defer rules with minimum stack sizes
- Optional handling for already deferred items
- Optional auto confirm, auto pickup, and auto reroll
- NinjaPricer-backed pricing for unlisted ritual items
- Debug overlay for ritual item classification

## Requirements

- Path of Exile 2
- ExileCore2
- NinjaPricer plugin
- .NET 8.0

`NinjaPricer` is required. RitualHelper reads prices through `PluginBridge` and does not have a standalone pricing backend.

## Configuration

- `Action Delay` and `Random Delay` control automation timing.
- `Defer existing items` allows re-processing already deferred ritual entries.
- `Category Filters` define which currency, ritual, and unique categories are eligible for NinjaPricer-based auto defer and what their minimum value in exalted orbs is.
- Manual defer rules are always active. Auto-priced unlisted items are considered whenever at least one category filter is enabled.

Manual defer rules are managed in the `Defer Items` section:

- `Name` matches by substring against the resolved item name.
- `Priority` controls click order, highest first.
- `Min Stack Size` blocks deferring low stack counts.

## Usage

1. Open a ritual window.
2. Click the button drawn next to the ritual reroll control.
3. RitualHelper enters defer mode, applies matching manual rules, evaluates unlisted items against the enabled NinjaPricer category thresholds, and then runs any enabled follow-up actions.

## Notes

- Unidentified uniques are matched using local unique art mapping when possible.
- If NinjaPricer bridge methods are unavailable, price-based auto defer is skipped.

## License

MIT
