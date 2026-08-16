# Development phases

Section 16 of the original brief was not supplied. This plan is the binding
phase list. Each stage ships complete, testable code. Later stages must not
break earlier ones.

## Stage 1 — Electron shell, security, i18n, Home + Settings

- Electron main process with the non-negotiable security flags
- Preload `contextBridge` API (frozen, allow-listed)
- Logger, centralized endpoints, IPC validators
- Renderer shell: sidebar, RTL/LTR, dark/light, Vazirmatn local font
- Home (honest empty catalog states) and Settings (language / theme / motion)
- Automated tests + preview server

## Stage 2 — SQLite local database

- `DatabaseAdapter` + `better-sqlite3` implementation
- `PRAGMA user_version` migrations, WAL, foreign keys
- Tables: settings, installed_content, favorites, download_history,
  download_queue, sync_state
- Settings IPC switches from JSON file to the settings table
- `postinstall`: `electron-builder install-app-deps`

## Stage 3 — Catalog

- JSON Schemas + templates + sample `database/` entries
- GitHub Action `build-catalog.yml` (validate, merge, publish)
- `catalogSource` with jsDelivr → raw → local cache
- ETag / 304, backoff + jitter, 403/429/5xx handling
- Two-request sync, 15-minute throttle, manual Refresh
- Home / Cars / Tracks / Mods consume the catalog

## Stage 4 — Assetto Corsa detection

- Saved path → Steam registry → libraryfolders.vdf → common paths → browse
- Path validation (exe + content/cars + content/tracks + writability)
- Settings: game path + diagnostics

## Stage 5 — Archive layer

- Magic-byte detector, ZIP/7z via 7za, RAR via node-unrar-js
- Path-traversal / zip-slip / zip-bomb tests
- Isolated temp extract in a `utilityProcess`
- Multi-volume RAR message + optional WinRAR/7-Zip helper
- Encrypted archive password prompt (never logged)

## Stage 6 — Download manager + checksum

- Streamed download, moving-average speed, ETA
- Pause / resume (HTTP Range) / cancel / retry / queue
- SHA-256 streaming verify
- Queue restore from SQLite

## Stage 7 — Installer pipeline

- Structure detection, preview, conflict, backup / overwrite / cancel
- Rollback, files_manifest, safe uninstall
- Disk-space checks, orphan temp cleanup

## Stage 8 — Packaging and updater

- `electron-builder` NSIS + portable
- GitHub Releases auto-update (consent first)
- Release workflow
