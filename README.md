# AC Mod Hub

A Windows launcher, mod manager, secure mod installer, backup manager and download manager for **Assetto Corsa**. The desktop client is built with C# 12, .NET 8, WPF/XAML, MVVM, dependency injection and asynchronous services.

> AC Mod Hub is an independent community project. Assetto Corsa and Steam are trademarks of their respective owners.

## Highlights

- Steam discovery through registry locations, `libraryfolders.vdf` and App ID `244210`
- Manual game-path selection and diagnostics for executable, `content`, write access and disk space
- ZIP, 7Z and RAR inspection/extraction through SharpCompress
- Automatic car, track, skin, app, weather, CSP and miscellaneous structure detection
- Wrapper-folder correction, preview, conflict detection and locked-file checks
- Transactional install journal, pre-overwrite backups, atomic copies, SHA-256 verification and rollback
- Persistent per-mod manifests and shared file ownership
- Enable, disable, reinstall, update, repair, verify and ownership-aware uninstall
- Existing installation scanning/import, including separate car skin manifests
- Resumable HTTP download queue with Range support, progress, pause, resume, cancellation, retry, concurrency and optional SHA-256
- Provider-independent `IContentProvider` update architecture (no third-party catalog dependency in v1)
- Persian/English UI (Persian/RTL by default), dark gaming-oriented shell and drag-and-drop import
- Per-user Inno Setup installer and portable self-contained `win-x64` package
- Rotating application logs and startup recovery for interrupted installation journals

## Repository layout

```text
ACModHub/
├── src/
│   ├── ACModHub.App             # WPF entry point and composition root
│   ├── ACModHub.Core            # Domain models, contracts and pure detection logic
│   ├── ACModHub.Infrastructure  # Archive, filesystem, Steam, JSON, downloads, install engine
│   └── ACModHub.UI              # XAML views, themes, localization and view models
├── tests/
│   └── ACModHub.Tests
├── installer/
│   └── ACModHub.iss
├── assets/
├── .github/workflows/windows-release.yml
└── README.md
```

`IModRepository` isolates persistence. Version 1 uses atomic JSON documents plus an individual JSON manifest for every mod. A SQLite repository can be added later without changing Core, the installer, or UI view models.

## Requirements

### Development

- .NET 8 SDK
- Windows 10 1809+ or Windows 11 to run the WPF application
- Visual Studio 2022 17.8+ is optional
- Inno Setup 6 to build the installer

`EnableWindowsTargeting` is enabled, so restore/build/test can also be run by non-Windows CI. Running WPF and compiling the Inno installer still require Windows.

## Restore, build, test and publish

From the repository root:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet publish -c Release -r win-x64 --self-contained true
```

For a predictable publish folder:

```powershell
dotnet publish .\src\ACModHub.App\ACModHub.App.csproj `
  -c Release -r win-x64 --self-contained true `
  -o .\artifacts\publish\win-x64
```

The application project is the only publishable project in the solution. Core, Infrastructure, UI and Tests set `IsPublishable=false`.

## Build both release packages

On Windows, install Inno Setup 6 and run:

```powershell
.\build.ps1
```

This performs restore, Release build, tests and self-contained publish, then creates:

```text
artifacts/AC-Mod-Hub-Portable-win-x64.zip
artifacts/installer/AC-Mod-Hub-Setup.exe
```

To invoke Inno Setup manually:

```powershell
& "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe" `
  "/DPublishDir=$PWD\artifacts\publish\win-x64" `
  ".\installer\ACModHub.iss"
```

The GitHub Actions workflow performs the same build on `windows-latest` and uploads both files as the `AC-Mod-Hub-1.0.0-win-x64` artifact.

## First run

1. Start AC Mod Hub normally, without administrator privileges.
2. The dashboard searches known Steam libraries for Assetto Corsa.
3. If detection fails, open **Settings**, select the folder containing `acs.exe`, and run Diagnostics.
4. Drag a `.zip`, `.7z` or `.rar` archive onto the window, or select **Import mod**.
5. Review detected category, destination files, warnings and conflicts.
6. Explicitly allow resolvable conflicts if appropriate, then select **Install & verify**.

Application state is stored below:

```text
%AppData%\ACModHub\
├── backups\
├── cache\
├── disabled\
├── downloads\
├── journals\
├── logs\
├── manifests\
├── library.json
├── ownership.json
└── settings.json
```

No game or application path is hard-coded.

## Installation transaction

The installer follows this pipeline:

```text
Analyze → Preview → Conflict Check → Backup → Install → Verify → Done
```

For each target, it durably appends a recovery operation to a write-through JSONL journal **before** changing the file; transaction metadata and the previous manifest are stored atomically in JSON. Existing files are copied to a backup tree. New content is copied to a temporary sibling and atomically moved into place. Any extraction, I/O, cancellation or hash-verification failure reverses the journal. Incomplete journals are recovered at the next startup.

Ownership is keyed by normalized game-relative path. Uninstall removes a physical file only after the selected mod is removed from its owner set and no owners remain. Shared files are retained. Disabling a solely owned file moves it into `%AppData%\ACModHub\disabled`; a shared file is not removed from the game.

## Archive security model

Before extraction, every archive entry is validated. AC Mod Hub rejects:

- rooted, absolute, UNC, drive-qualified and traversal paths;
- Windows alternate data streams, reserved device names and ambiguous trailing-dot/space paths;
- symbolic links and hard links;
- encrypted entries;
- duplicate case-insensitive destination paths;
- suspicious compression ratios, oversized entries and excessive aggregate/entry counts;
- executable/command payloads such as EXE, MSI, COM, SCR, BAT, CMD, PowerShell, script-host and shortcut files.

Extraction first targets a private staging directory. Every final destination is independently resolved with `SafePath.CombineUnderRoot`. The app never runs EXE content from a mod, never starts PowerShell or a hidden command, and never silently elevates itself.

Some legitimate packages include a separate executable installer. Under the strict v1 policy these archives are intentionally rejected; install their non-executable content from a trusted repack instead.

## Downloads and updates

Version 1 deliberately has no dependency on a mod catalog. The real HTTP queue accepts a URL, filename, optional SHA-256 and metadata. Partial downloads use `.part` files and resume when the server supports HTTP Range.

Future sources implement:

```csharp
public interface IContentProvider
{
    string Name { get; }
    Task<IReadOnlyList<ContentRelease>> CheckUpdatesAsync(
        IEnumerable<ModManifest> installedMods,
        CancellationToken cancellationToken = default);
}
```

A GitHub Releases or dedicated catalog provider can therefore be added without changing Core or the UI.

## Tests

The test project covers:

- Steam/game detection and manual validation
- mod structure/category detection and wrapper repair
- archive traversal and executable-policy validation
- conflict classification
- backup/restore
- transactional installation and SHA-256 manifests
- forced-failure rollback
- ownership-aware uninstall
- enable/disable
- JSON manifest and ownership persistence

Run with coverage collection if desired:

```powershell
dotnet test --collect:"XPlat Code Coverage"
```

## Troubleshooting

### Game is not detected

Select the folder that directly contains `acs.exe` (normally `...\SteamLibrary\steamapps\common\assettocorsa`) and run Diagnostics. Confirm `content` exists.

### Write access fails

AC Mod Hub does not request UAC elevation. Check Steam library permissions, remove read-only restrictions, or move the Steam library to a user-writable location. Do not grant broad permissions to unrelated system folders.

### An archive is rejected

Read the exact validation error in the installer/log. Encrypted archives, links, unsafe paths, archive bombs and command/executable payloads are blocked by design. Extracting manually does not make an untrusted package safe.

### Files are locked

Close Assetto Corsa, Content Manager and tools that may hold the destination. Re-run analysis; locked targets are blocking conflicts.

### A previous install was interrupted

Restart AC Mod Hub. Startup recovery reverses incomplete journals. If recovery cannot acquire a file, close the process holding it and restart. The journal remains in `recovery required` state rather than being discarded.

### Inno Setup is not found

Install Inno Setup 6 and verify `ISCC.exe` exists below `%ProgramFiles(x86)%\Inno Setup 6`. The portable publish does not require Inno Setup.

## License

MIT. See [LICENSE](LICENSE).
