# AC Mod Hub 2.0

A Windows launcher, mod manager, secure mod installer, backup manager, download manager and
self-updating desktop client for **Assetto Corsa**. Built with C# 12, .NET 8, WPF/XAML, MVVM,
dependency injection, and fully asynchronous services. Persian (RTL) is the default language;
English is included.

> AC Mod Hub is an independent community project. Assetto Corsa and Steam are trademarks of
> their respective owners. This project does not bundle or redistribute any mod content.

**فارسی:** [راهنمای نصب](INSTALL-FA.md) · [راهنمای کاتالوگ مود](CATALOG-FA.md) ·
[تحقیق طراحی UI](docs/UI-RESEARCH.md) · [ممیزی 2.0](docs/AUDIT-2.0.md)

**Releases:** binaries are published only as GitHub Releases — no binaries are committed to
this repository: <https://github.com/aliam664/1/releases>

## What is new in 2.0

- **Fluent 2 dark + premium motorsport redesign** — central design tokens, 8-px grid, focus
  visible everywhere, 40/44-px touch targets, reduced-motion support, full RTL/LTR.
  See [docs/UI-RESEARCH.md](docs/UI-RESEARCH.md).
- **WebView2 store** (Chromium) replacing the legacy WebBrowser: message-only bridge with
  schema-validated commands, no host objects, CSP, restricted navigation, permissions denied,
  DevTools off in Release — plus a native XAML fallback when the WebView2 runtime is absent.
- **Public catalog** from `aliam664/Data` with an endpoint chain: custom HTTPS override →
  GitHub Pages primary → raw generated fallback → embedded empty catalog. ETag/304 caching,
  atomic last-known-good cache, full payload validation (IDs, semver, SHA-256, sizes, URLs,
  statuses, limits).
- **Launcher updater** on GitHub Releases with Stable/Beta channels, downgrade rejection,
  consent-first download, SHA256SUMS verification and safe in-place apply.
  See [docs/UPDATER.md](docs/UPDATER.md).
- Version is defined exactly once in `Directory.Build.props` and flows to assemblies, UI,
  User-Agent, installer and releases.

## Highlights

- Steam discovery (registry, `libraryfolders.vdf`, App ID `244210`) with multiple selectable installations
- Manual game-path selection + diagnostics (Steam, game path, `content`, write access, disk space)
- ZIP/7Z/RAR inspection and extraction (SharpCompress) — archives are fully untrusted:
  zip-slip/absolute/UNC/ADS/reserved-name/trailing-dot rejection, symlink/hardlink/encrypted-entry
  rejection, duplicate case-insensitive path rejection, entry/size/compression-ratio limits,
  executable/script rejection, staging extraction (never into the game), transaction journal,
  pre-overwrite backups, atomic copies, SHA-256 verification, rollback and startup crash recovery
- Automatic car/track/skin/app/weather/CSP/mixed structure detection, wrapper-folder correction,
  standalone skin targeting, preview and conflict/locked-file checks
- Per-mod manifests, ordered shared-file ownership, baseline backups, modified-file-aware
  uninstall (preserve/restore/abort) with newer-owner blocking
- Resumable download queue (HTTP Range), speed/ETA, expected-size/SHA-256 validation, pause/
  resume/cancel/retry with race-safe states, subscriber isolation and terminal-state protection
- Bilingual UI (fa-IR default, RTL) with localized empty/loading/error/offline states
- Per-user Inno Setup installer (no admin, no silent elevation) + portable self-contained win-x64
- Rotating logs, crash recovery for interrupted installations, background (non-blocking) update check

## Architecture

```text
src/
├── ACModHub.App             # WPF entry point, composition root, startup flow
├── ACModHub.Core            # Domain models, contracts, SemVer, AppConfig, SafePath
├── ACModHub.Infrastructure  # Archive/filesystem/Steam/JSON services, catalog client,
│                            # download manager, install engine, updater services
└── ACModHub.UI              # Views, view models, theme/tokens, localization,
                             # WebView2 store host + bridge, native store fallback
tests/
├── ACModHub.Tests           # Core + Infrastructure unit/integration tests
└── ACModHub.UI.Tests        # Bridge validation, page builder escaping, localization parity
installer/                   # Inno Setup (per-user), version injected from Directory.Build.props
scripts/validate.py          # Static validation: XAML/XML, JSON, JS, YAML, resource parity,
                             # version single-source, StaticResource resolution, hygiene
.github/workflows/           # ci.yml (build+test+validate) / release.yml (installer + GitHub Release)
docs/                        # UI-RESEARCH.md, AUDIT-2.0.md, UPDATER.md
```

`IModRepository`, `IArchiveService`, `IDownloadManager`, `IUpdateChecker`, `IModCatalogService`
and friends isolate persistence and I/O so backends can be swapped without touching the UI.

## Endpoints

| Purpose | Endpoint |
| --- | --- |
| Catalog (primary, GitHub Pages) | `https://aliam664.github.io/Data/catalog.v1.json` |
| Catalog (raw generated fallback) | `https://raw.githubusercontent.com/aliam664/Data/generated/catalog.v1.json` |
| Mod packages | `https://github.com/aliam664/Data/releases` |
| Launcher updates | `https://github.com/aliam664/1/releases` |

Endpoints are defined once in `src/ACModHub.Core/AppConfig.cs`; an advanced HTTPS override
exists in Settings. No credentials or tokens are ever used by the client.

## Building

Requirements: Windows 10 1809+ (or Windows 11), .NET 8 SDK, Inno Setup 6 (for the installer).

```powershell
./build.ps1                 # restore → build → test → publish win-x64 → installer → SHA256SUMS
./build.ps1 -SkipTests      # skip the test run
./build.ps1 -SkipInstaller  # skip Inno Setup
python scripts/validate.py  # static validation (no .NET SDK needed)
```

`EnableWindowsTargeting` allows restore/build/test on non-Windows CI machines; running the
WPF app and compiling the installer require Windows. CI performs the full pipeline on
`windows-latest` (see `.github/workflows/ci.yml`), and `release.yml` publishes tagged
releases with the installer, checksums and build info.

## Security model (short version)

- Mod files are never executed. Executable/script content inside archives is rejected.
- No silent elevation anywhere; installs are per-user.
- Catalog payloads are validated before they can replace the cached copy.
- The WebView2 store gets only validated JSON messages and can never reach the host process.
- Updates are never downloaded or installed without user consent; integrity is verified
  against the published SHA-256 before anything is applied.
- All destructive operations keep backups/journals and can roll back.

## License

MIT — see [LICENSE](LICENSE).
