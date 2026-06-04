# TWWH3 Solo Multiplayer Patcher (.NET)
A C#/.NET patcher for modifying Total War: WARHAMMER III to play multiplayer campaigns solo.

It does **not** include or distribute `Warhammer3.exe`. It patches a user's own executable after verifying exact byte signatures and creating a backup.

## Description
The multiplayer campaigns in TWWH3 contain minor differences from the solo campaigns. These differences can snowball into larger ones as the game goes on. The purpose of this patch is to enable solo play in multiplayer lobbies, allowing a user to experience these differences without requiring a second player.

## License

MIT License. See [`LICENSE`](LICENSE).

## Commands

Running the executable will open a GUI where you may browse for `Warhammer3.exe`, check patch status, apply the patch, and restore the backup. The GUI tries to find Steam installs automatically, including Steam libraries on other drives.

A CLI is also available for those who prefer:

```powershell
twwh3-solo-mp-patcher status  "C:\path\to\Warhammer3.exe"
twwh3-solo-mp-patcher apply   "C:\path\to\Warhammer3.exe"
twwh3-solo-mp-patcher restore "C:\path\to\Warhammer3.exe"
```

If no path is provided, the patcher will look for `Warhammer3.exe` in the current directory.

The release executable is built as a Windows GUI app so double-clicking it does
not open a console window. For scripted PowerShell use, prefer:

```powershell
Start-Process .\twwh3-solo-mp-patcher.exe -ArgumentList @("status", "C:\path\to\Warhammer3.exe") -Wait -NoNewWindow
```

Applying creates:

```text
Warhammer3.vanilla.exe
```

Keep that file if you want one-click restore later. Restoring copies it back over
`Warhammer3.exe`, then deletes the backup so a future apply can create a fresh
vanilla backup. The patcher will not overwrite an existing backup from the GUI;
the CLI `--force` option only overwrites the backup when the selected executable
still matches the original unpatched bytes.

Alternatively, running Steam's "Verify integrity of game files" should restore
`Warhammer3.exe` to the factory state.

## Build

Install the .NET SDK, then from this folder run:

```powershell
.\scripts\Publish-Release.ps1
```

Or double-click:

```text
scripts\Publish-Release.bat
```

The helper script cleans and publishes to:

```text
dist\
```

The equivalent publish command is:

```powershell
dotnet publish .\TWWH3SoloMp.csproj `
  -c Release `
  -r win-x64 `
  --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -p:DebugType=None `
  -p:DebugSymbols=false `
  -o .\dist
```

Published release artifacts are available from the GitHub Releases page.

## Patch Profiles

Patch data lives in [`patches.json`](patches.json), which is embedded into the
release executable at compile time.

Profiles can include:

- `displayVersion`: the game version shown to users, such as `v8.0.2 Build 46904.4121883`.
- `exeSha256`: optional exact executable hash for a strict known-version check.
- `exeSize`: optional exact executable size for another strict profile check.
- `expectedOffset`: the expected file offset for the patch.
- `before`: the byte pattern to find before patching.
- `replaceOffset`: where the replacement starts inside the `before` pattern.
- `replace`: the bytes to write.

Use `??` in `before` for volatile bytes, such as the four-byte relative operand
in an `E8 call rel32` instruction. Replacement bytes cannot contain wildcards.
This lets the patcher match a larger context while only modifying the exact
instruction bytes that need to change.

## Exact Patches (v8.0.2 Build 46904.4121883)

### Allow Timer Start

Before:

```text
48 8B 8F 60 0B 0E 00 48 8B 01 FF 90 B0 01 00 00 83 F8 03 8B CE 48 8B 87 10 0B 0E 00 0F 94 C1 FF C1 8B 90 04 28 00 00 3B D1
```

Replace at offset `31`:

```text
90 90
```

### Suppress Solo Invalid-Session Force Game Over

Before:

```text
48 8D 05 ?? ?? ?? ?? 48 8D 55 F0 48 89 45 F0 C6 45 F8 01 E8 ?? ?? ?? ?? 40 88 B3 F0 0C 0E 00 48 39 B3 48 50 07 00
```

Replace at offset `19`:

```text
90 90 90 90 90
```

## Notes

- The patcher fails closed if signatures are missing or ambiguous.
- The patcher requires both the expected byte pattern and expected file offset to match.
- Do not distribute a patched game executable. Distribute this patcher/source instead.
