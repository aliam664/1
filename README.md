# StockCorsa Launcher

Independent Windows desktop launcher for **Assetto Corsa** cars, tracks and mods.
Electron main process, sandboxed vanilla ESM renderer, Persian RTL by default.

Assetto Corsa and Steam are trademarks of their respective owners. This project
does not bundle or redistribute game files or third-party mods.

**فارسی:** لانچر دسکتاپ برای مرور، دانلود و نصب محتوای Assetto Corsa.
معماری: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) ·
فازبندی: [docs/PHASES.md](docs/PHASES.md)

## What it does

- Aggregated HTTPS catalog (jsDelivr → raw GitHub → last-known-good cache)
- Local SQLite state: settings, favorites, install manifests, download queue
- Streamed downloads with pause/resume when the host supports HTTP Range
- SHA-256 verification, RAR/ZIP/7z extract, zip-slip / zip-bomb rejection
- Assetto Corsa path detection (Steam registry + `libraryfolders.vdf`)
- Safe install into `content/cars` / `content/tracks` with uninstall from manifest
- Optional WinRAR / 7-Zip helper for multi-volume RAR
- Launcher update check against GitHub Releases (consent-first; no silent apply)

## Commands

```bash
npm install
npm test
npm run catalog:build
npm run preview          # renderer demo at http://0.0.0.0:4173
npm start                # Electron (needs a desktop session)
npm run dist             # Windows NSIS + portable via electron-builder (run on Windows)
```

The web preview uses a bundled demo catalog and simulated downloads. It never
receives Node privileges. Real extract/install/Steam detection run only in Electron.

## Windows EXE

On a Windows machine (this Linux sandbox cannot download the official Electron
Windows runtime from GitHub release assets):

```powershell
./scripts/build-windows.ps1
```

Output:

- `dist-electron/StockCorsa Launcher 1.0.0.exe` — portable, no install
- `dist-electron/StockCorsa Launcher Setup 1.0.0.exe` — NSIS installer

## Database

The adapter prefers **better-sqlite3** when the native module is present and
falls back to **sql.js**. Both read and write a real SQLite file at
`app.getPath('userData')/stockcorsa.db`, so swapping engines later does not
change callers or on-disk data.

`postinstall` runs `electron-builder install-app-deps` only if both Electron
and better-sqlite3 are installed.

## Adding catalog items

Copy `templates/car.json` (or track/mod), fill the fields, drop it in
`database/cars/` and run `npm run catalog:build`. Invalid IDs, empty links or
unknown `archiveType` fail the build. The launcher does not fetch one JSON
file per car.

## Security

- `contextIsolation`, `sandbox`, `webSecurity` on; `nodeIntegration` off
- Renderer CSP: no `unsafe-inline`, no `unsafe-eval`, `connect-src 'none'`
- Preload exposes only `window.stockcorsa`
- Every IPC payload is validated
- No tokens or credentials in the tree

## License

MIT — see [LICENSE](LICENSE).
