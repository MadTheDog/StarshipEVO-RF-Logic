# Starship EVO RF Logic

RF Logic adds a Wireless Relay Transmitter and Receiver. Each pair sends an independent sustained signal or pulse between two player-owned grids in a solo game. Several pairs can share an antenna.

## Install through Starship Uplink

1. Select a prepared profile in [Starship Uplink](https://github.com/MadTheDog/Starship-Uplink/releases).
2. Choose **Add from GitHub** and enter `MadTheDog/StarshipEVO-RF-Logic`.
3. Select the compiled RF Logic ZIP, then **Install selected** and **Validate**.

RF 0.2.3 targets Steam build **26451593 / 26w45a**, Unity **6000.6.6f1**, Windows x64 Mono, and BepInEx **5.4.23.5**. The package is a BepInEx plugin that runs alongside Starship Keel. Uplink 0.1.5 bundles Keel 0.1.5, and the current RF verification profile was prepared with the official Keel 0.1.6 pair. RF has no dependency on the experimental Keel block API.

The rendered solo fixture passed 23 native checks with Keel 0.1.6. The release also passed 25 compatibility checks and 82 RF policy checks.

## Pair relays

1. Place one transmitter and one receiver on separate grids. Use the relay menu to pair them by name.
2. Link the transmitter to its local antenna. On the other grid, link the antenna to its receiver.
3. Power **both antennas** with an ON signal. Wire your input to the transmitter and your local output circuit to the receiver.

Each transmitter takes one logic input. Use a logic gate to combine inputs. In **Signal** mode, the receiver mirrors the sustained input, goes OFF when disconnected, and restores the current input on reconnection. In **Pulse** mode, it emits a timed output for a rising edge while connected. Disconnected pulses are discarded.

| Antenna size level | Block size | RF range |
| --- | ---: | ---: |
| 1 | 1/8 m | 25 m |
| 2 | 1/4 m | 50 m |
| 3 | 1/2 m | 125 m |
| 4 | 1 m | 500 m |
| 5 | 2 m | 2,000 m |

Both antennas must be within range of each other. The smaller range governs the link. The package claims fixed brick IDs **1004** and **1005**. This is a solo prototype. Back up worlds containing mod blocks before changing the installed mod set.

## Build

Use the .NET 8 SDK and point the project at the managed assemblies from your own game and BepInEx installation.

```powershell
dotnet build src/RadioLogic.csproj -c Release "-p:ManagedDir=<game managed directory>" "-p:LoaderDir=<BepInEx core directory>"
./tools/Build-Package.ps1 -PluginPath ./src/bin/Release/netstandard2.1/RadioLogic.dll
dotnet run --project tests/PolicyTests.csproj
```

The offline compatibility harness checks method signatures, resource and link-tool hooks, native job ordering and scoped failure behavior. Build it with `-p:CecilPath=<path to Mono.Cecil.dll>` from BepInEx, then run the built DLL with `self-test <Assembly-CSharp.dll> <RadioLogic.dll>`.
