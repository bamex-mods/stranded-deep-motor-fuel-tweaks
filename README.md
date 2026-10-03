# Stranded Deep Motor Fuel Tweaks

A lightweight BepInEx mod for Stranded Deep that changes Boat Motor fuel consumption.

## Features

- Adjustable Boat Motor fuel consumption from 0% to 100%.
- 5% adjustment steps.
- Default value: 50% of vanilla fuel consumption.
- 100% = vanilla fuel consumption.
- 50% = half vanilla consumption.
- 25% = quarter vanilla consumption.
- 0% = no Boat Motor fuel consumption.
- Optional integration with Stranded Deep Mod Settings.
- Falls back to the normal BepInEx configuration file when Mod Settings is not installed.
- Does not modify the Stranded Deep save file.

## Local split-screen

The fuel multiplier is global to the game session and applies to tracked Boat Motors, not to an individual player. In local split-screen both players therefore use the same Boat Motor fuel-consumption setting.

## Mod Settings

With Stranded Deep Mod Settings installed:

`Settings -> Mods -> Motor -> Fuel Consumption`

The slider can be changed while the game is running.

Mod Settings is a soft dependency. Motor Fuel Tweaks continues to work without it.

## BepInEx config

Config key:

`BoatMotor / FuelConsumptionMultiplier`

Config file:

`BepInEx/config/com.bamex.strandeddeep.motorfueltweaks.cfg`

Accepted range:

`0.0 .. 1.0`

## Installation

Requires BepInEx.

For manual installation, extract the release ZIP into the Stranded Deep game directory so that the included `BepInEx` folder merges with the existing one.

Restart the game after installing, updating or removing the mod.

## Version

Current release candidate source: `0.2.1`.
