# Development phases

All eight stages are implemented. Later schema changes add a new
`PRAGMA user_version` migration; they do not rewrite published ones.

| Stage | Scope | State |
| --- | --- | --- |
| 1 | Electron shell, CSP/sandbox, i18n, Home, Settings | done |
| 2 | SQLite local state (`sql.js` portable + `better-sqlite3` preferred) | done |
| 3 | Aggregated catalog, schemas, `scripts/build-catalog.mjs` | done |
| 4 | Assetto Corsa detection (registry → VDF → common → browse) | done |
| 5 | ZIP / RAR / 7z extract + zip-slip / zip-bomb guards | done |
| 6 | Download manager + streaming SHA-256 | done |
| 7 | Structure detection, preview, install, uninstall | done |
| 8 | electron-builder packaging + GitHub Releases update check | done |

Workflow YAML cannot be pushed by this environment's GitHub App
(`workflows` permission). Copies live in `docs/github-workflows/`.
The catalog builder is `npm run catalog:build` and is what the Action runs.
