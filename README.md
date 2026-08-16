# StockCorsa Launcher

Independent Windows desktop launcher for **Assetto Corsa** cars, tracks and mods.
Built with Electron, vanilla ESM, and a locked-down renderer. Persian (RTL) is
the default language; English is included.

Assetto Corsa and Steam are trademarks of their respective owners. This project
does not bundle or redistribute game files or third-party mods.

**فارسی:** این برنامه از صفر با Electron بازنویسی شده است. مرحلهٔ ۱ — پوسته،
امنیت، i18n و تنظیمات زبان/تم — آماده است. قابلیت‌های کاتالوگ، دانلود و نصب در
مراحل بعد اضافه می‌شوند. معماری: [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md) ·
فازبندی: [docs/PHASES.md](docs/PHASES.md)

## Current status

| Stage | Scope | State |
| --- | --- | --- |
| 1 | Electron shell, CSP/sandbox, i18n, Home, Settings | **done** |
| 2 | SQLite + migrations | next |
| 3 | Aggregated catalog + GitHub Action | planned |
| 4 | Assetto Corsa detection | planned |
| 5 | ZIP / 7z / RAR extract | planned |
| 6 | Download manager + SHA-256 | planned |
| 7 | Installer pipeline | planned |
| 8 | electron-builder + auto-update | planned |

## Requirements

- Node.js 20.11+ (22 recommended)
- Windows 10/11 to run the packaged desktop app
- Linux/macOS can run unit tests and the web preview of the renderer

## Commands

```bash
npm install
npm test
npm start          # Electron window (Windows / local desktop)
npm run preview    # Renderer only, http://0.0.0.0:4173
```

`npm start` needs a desktop session. In this repository the renderer is also
servable through `npm run preview` so the UI can be reviewed without Electron.

Native module rebuild (`electron-builder install-app-deps`) is **not** used yet.
It will be added in Stage 2 together with `better-sqlite3`.

## Security (Stage 1)

- `contextIsolation`, `sandbox`, `webSecurity` on; `nodeIntegration` off
- Renderer CSP forbids `unsafe-inline`, `unsafe-eval` and all network (`connect-src 'none'`)
- Preload exposes only `window.stockcorsa` — never raw `ipcRenderer`
- Every IPC payload is validated (type / enum / length)
- No tokens, API keys or credentials exist in the source tree

## License

MIT — see [LICENSE](LICENSE).
