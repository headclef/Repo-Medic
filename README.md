# Medic

A [BepInEx](https://github.com/BepInEx/BepInEx) mod for **R.E.P.O.** that lets you revive dead players by pressing a key when near them.

## What This Mod Does

Walk up to a dead teammate and press **R** to revive them with full health. Works for both **host and non-host** players — anyone with the mod can be a medic.

### How It Works

1. A teammate dies
2. Walk within range (default: 3 meters)
3. Press **R**
4. They're back with 100 HP

> There's a cooldown between revives to prevent spam (default: 5 seconds).

## Configuration

Settings are in `BepInEx/config/headclef.Medic.cfg` or in the **in-game mod config menu**:

| Key | Default | Range | Description |
|-----|---------|-------|-------------|
| Revive Key | `R` | — | Key to press to revive |
| Revive Range | `3` | 1–15 | Max distance to dead player (meters) |
| Revive Cooldown | `5` | 0–60 | Seconds between revive attempts |
| Revive Health | `100` | 1–200 | HP the revived player starts with |

## Requirements

- [BepInEx 5.x](https://github.com/BepInEx/BepInEx) installed for R.E.P.O.

## Installation

1. Install via **Thunderstore** (recommended).
2. Or manually: place `Medic.dll` into your `BepInEx/plugins` folder.
3. Launch the game — config file is generated on first run.

## Multiplayer

- **Any player** with the mod can revive dead teammates — host or client.
- The revive uses the game's built-in revive system for proper network sync.

## Development

### Project Structure
```
├── Medic.cs                        # Plugin entry point & config
├── Patches/
│   └── RevivePatch.cs              # Harmony postfix — revive logic
└── README.md
```

### Building
```bash
dotnet build
```

## License

This project is licensed under the MIT License — see the [LICENSE](LICENSE) file for details.
