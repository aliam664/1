# StockCorsa Launcher — Architecture

This document is the source of truth for process boundaries, data flow and IPC.
It is written before Stage 1 code and updated only when a later stage introduces
a new boundary.

## 1. Why this shape

The launcher is a Windows desktop app. It must never require Chrome/Edge to be
opened. All UI is rendered inside the Electron window.

Two hard constraints drive the design:

1. **Renderer is untrusted.** It has no Node, no `fs`, no `child_process`, no
   raw network. A compromised renderer cannot read the game folder or the
   catalog cache.
2. **GitHub anonymous rate limits (since May 2025).** The launcher never
   fetches one JSON file per car/track. It performs at most two HTTP requests
   per catalog sync (`manifest.json`, then optionally `catalog.json`).

## 2. Process map

```
┌──────────────────────────────────────────────────────────────────┐
│  Main Process  (Node.js, full privileges, no UI)                 │
│                                                                  │
│  app / security / logger / settings                              │
│  ipc  (validated allow-list only)                                │
│  catalog client   ──HTTPS──►  jsDelivr → raw.githubusercontent   │
│  download manager ──HTTPS──►  package hosts                      │
│  archive service  ──utilityProcess──►  extract worker            │
│  installer / AC detector / updater                               │
│  SQLite  (userData/stockcorsa.db)   catalog cache  (disk JSON)   │
└───────────────┬──────────────────────────────▲───────────────────┘
                │ contextBridge (preload)      │
                │ invoke / handle only         │
                ▼                              │
┌──────────────────────────────────────────────────────────────────┐
│  Preload  (sandbox: true, isolated world)                        │
│  Exposes window.stockcorsa — a frozen, typed API.                │
│  Never forwards ipcRenderer itself.                              │
└───────────────┬──────────────────────────────▲───────────────────┘
                │                              │
                ▼                              │
┌──────────────────────────────────────────────────────────────────┐
│  Renderer  (Chromium, sandbox, CSP, no Node)                     │
│  HTML + CSS + Vanilla ESM                                        │
│  i18n / theme / router / pages                                   │
│  Talks only to window.stockcorsa                                 │
└──────────────────────────────────────────────────────────────────┘
```

A third process appears in Stage 5: an Electron `utilityProcess` that performs
archive extraction so a multi-gigabyte RAR never blocks the main thread.

## 3. Data flow

```
GitHub  database/{cars,tracks,mods}/*.json
   │
   │  GitHub Action: schema validate + merge
   ▼
dist/catalog.json + dist/manifest.json   (branch `catalog`)
   │
   │  1) jsDelivr CDN
   │  2) raw.githubusercontent.com
   │  3) last-known-good local cache
   ▼
Main: catalogSource  (ETag / If-None-Match, backoff, timeout)
   │
   ▼
userData/cache/catalog.json   +  in-memory index
   │
   │  IPC  catalog:get / catalog:search
   ▼
Renderer pages (Home, Cars, Tracks, Mods)

SQLite holds ONLY local user state:
  settings, installed_content, favorites,
  download_history, download_queue, sync_state
```

Catalog payload is never stored in SQLite. The online JSON is the source of
truth for names, descriptions and artwork metadata.

## 4. IPC contract (Stage 1 surface)

Channels are defined once in `src/main/ipc/channels.js`. Preload and Main
import the same constants. Unknown channels are rejected.

| Channel                 | Dir     | Payload                         | Stage |
| ----------------------- | ------- | ------------------------------- | ----- |
| `app:getInfo`           | invoke  | — → `{ name, version, … }`      | 1     |
| `settings:get`          | invoke  | — → `Settings`                  | 1     |
| `settings:setLanguage`  | invoke  | `'fa' \| 'en'`                  | 1     |
| `settings:setTheme`     | invoke  | `'dark' \| 'light'`             | 1     |
| `settings:setReducedMotion` | invoke | `boolean`                   | 1     |
| `settings:changed`      | event   | `Settings`                      | 1     |

Every invoke handler:

1. Validates type, length, enum and range.
2. Never trusts a file path from the renderer without normalize + whitelist
   (no path-taking channels exist in Stage 1).
3. Returns `{ ok: true, data }` or `{ ok: false, error: { code, message } }`.
4. Logs the full error in Main; the renderer never receives a stack trace.

Later stages add channels for catalog, downloads, install, AC detection and
updates. Each new channel is added to `channels.js` plus a validator plus a
test. There is no catch-all `ipcRenderer.send(any)`.

## 5. Security posture

| Flag / control              | Value                                      |
| --------------------------- | ------------------------------------------ |
| `contextIsolation`          | `true`                                     |
| `nodeIntegration`           | `false`                                    |
| `sandbox`                   | `true`                                     |
| `webSecurity`               | `true`                                     |
| `webviewTag`                | `false`                                    |
| `allowRunningInsecureContent` | `false`                                  |
| CSP                         | no `unsafe-inline`, no `unsafe-eval`       |
| `connect-src`               | `'none'` (renderer has no network)         |
| Navigation                  | `will-navigate` blocked except own file    |
| New windows                 | `setWindowOpenHandler` → deny              |
| External links              | `shell.openExternal` after `https:` check  |
| Secrets                     | none in source, none in the build          |

Fonts, icons and CSS are bundled locally. The HTML never references a CDN.

## 6. Module layout (Main)

```
src/main/
  app/          bootstrap, window, security policy, paths
  ipc/          channel constants, validators, handler registration
  config/       endpoints + appConfig  (single place for URLs)
  logger/       rotating file logger under userData/logs
  settings/     Stage 1 JSON store; Stage 2 replaced by SQLite repository
  github/       Stage 3 catalogSource
  catalog/      Stage 3 cache + in-memory index
  database/     Stage 2 adapter + migrations
  downloader/   Stage 6
  archive/      Stage 5  (ZIP / 7z / RAR / external tool)
  installer/    Stage 7
  assetto-corsa/ Stage 4 detector
  filesystem/   Stage 5 SafePath
  updater/      Stage 8
```

Shared logic lives in one module. Callers never duplicate validation, path
sanitization or HTTP retry.

## 7. Renderer layout

Vanilla ESM. No bundler. Pages are functions that return a DOM tree. A tiny
store holds route, language, theme and online state. All visible strings come
from `i18n/fa.json` and `i18n/en.json`. Direction (`dir`) and theme swap
without restart via `document.documentElement` attributes and CSS logical
properties.

A browser fallback (`services/bridge.js`) lets the same renderer run under
`scripts/preview.mjs` for UI review when Electron is not available. The
fallback uses `localStorage` and never pretends to have Node privileges.

## 8. Stage 1 storage vs Stage 2

Stage 1 persists `{ language, theme, reducedMotion }` as JSON in
`userData/settings.json` (or `localStorage` in preview). This is intentional:
SQLite (`better-sqlite3`) is a native module that needs
`electron-builder install-app-deps` and belongs to Stage 2, where the full
schema and `PRAGMA user_version` migrations land together.

The settings API shape will not change. Stage 2 swaps the store
implementation behind the same IPC channels.

## 9. Simplicity vs correctness

Where the two conflict, correctness wins.

Example already taken: the renderer is forbidden from doing HTTP, even though
a `fetch` in the page would be fewer lines. Catalog traffic must share one
retry/ETag/fallback path in Main; otherwise a 429 from GitHub becomes an
unrecoverable IP block for the user.

## 10. What Stage 1 does *not* do

| Capability                         | Lands in |
| ---------------------------------- | -------- |
| SQLite + migrations                | Stage 2  |
| Catalog schema, Action, network    | Stage 3  |
| Assetto Corsa detection            | Stage 4  |
| Archive extract (RAR/ZIP/7z)       | Stage 5  |
| Download manager                   | Stage 6  |
| Installer pipeline + uninstall     | Stage 7  |
| Auto-update + Windows installer    | Stage 8  |

Unimplemented navigation targets render an honest empty state that names the
stage they depend on. There are no `TODO` stubs in the running path.
