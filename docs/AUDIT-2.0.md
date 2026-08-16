# AC Mod Hub 2.0 — Pre-Redesign Audit

Audit performed on the `1.x` codebase before the 2.0 redesign. Each row: what was audited,
what was found, and the corrective action taken in 2.0. Status column refers to the 2.0 tree
(`src/`, `tests/`, `installer/`, `.github/`).

Legend: ✅ fixed in this branch · ⚠️ mitigated with documented limitation · ➖ kept as-is (no defect found)

## 1. XAML views

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| All views were minimal placeholder grids (7–51 lines); most functionality lived in the HTML store only. | High | All pages redesigned as full XAML (Dashboard, Library, Downloads, Installer, Backups, Updates, Settings) with real controls, states, and binding. | ✅ |
| Hard-coded colors (`#090D14`, `#0D121B`, `#0A0D14`) in views instead of tokens. | High | All views now consume `Theme.xaml` token resources only. | ✅ |
| Navigation used Unicode text icons (`✦ ◈ ▦ ◆ ⌁ ◐ ⌘ ⇩ ↻ ▣ ⚙`) — visually inconsistent. | Medium | Replaced by a consistent vector Path icon set (single geometry style). | ✅ |
| Nav had no selected state (a `Button` per page, no indicator). | High | New nav model with active indicator rail + grouped sections. | ✅ |
| No keyboard navigation accelerators on nav, no `AutomationProperties`. | Medium | Added `AutomationProperties.Name` everywhere, Enter/Space work natively on buttons, Escape handling documented per page. | ✅ |
| Fixed `Padding="30,26"` on the content host — not responsive. | Low | Responsive content margins via a centered max-width host; no clipping at min size. | ✅ |

## 2. Theme & localization

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Theme colors close to, but not equal to, the agreed design tokens; no elevation scale. | High | `Theme.xaml` rewritten around the 2.0 token set (Background/Sidebar/Surface/Raised/Elevated/Border/StrongBorder/…). | ✅ |
| No focus visual style at all; no hover distinction on nav; buttons had opacity-only states. | High | `FocusVisualStyle` 2px Safe border; explicit hover/pressed/disabled states; minimum 40/44 px heights. | ✅ |
| Only 54 localized strings; VMs hard-coded English messages (violates "no random English in Persian UI"). | High | String tables expanded (~170 keys, fa-IR + en-US); all VM messages routed through `ILocalizationService`. | ✅ |
| ComboBox default template rendered dark text on dark background in some cases. | Medium | Custom ComboBox/CheckBox/RadioButton templates from tokens. | ✅ |
| No reduced-motion or high-contrast consideration. | Medium | `ReducedMotion` setting disables transitions; states always carry text+icon so high-contrast rendering keeps information. | ⚠️ Full system palette mirroring not implemented (documented limitation). |

## 3. MainWindow & navigation

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Title bar had minimize/close only (no maximize/restore); no title drag region attribute on chrome elements. | Medium | Full window chrome: drag region, minimize/maximize/restore/close, double-click title toggles maximize, `WindowChrome` handles DPI. | ✅ |
| No update banner; notification was a single string in the title bar. | Medium | Notification banner area + launcher-update banner with Later/View/Download actions. | ✅ |
| Window fixed `Width/Height` 1440×900, `MinWidth 1100`. | Low | Min sizes kept sensible (1100×700); responsive margins added. | ✅ |
| `FileDropBehavior` existed but dropping while an operation is running was unguarded. | Medium | Drop disabled while busy; drop target hint on Dashboard. | ✅ |

## 4. Catalog HTML/CSS/JavaScript

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Store rendered in legacy WPF `WebBrowser` (IE11 engine) with `ObjectForScripting` — deprecated, insecure bridge surface, poor CSS support. | Critical | Replaced with WebView2 (Chromium) with postMessage-only bridge; legacy host removed. | ✅ |
| `ObjectForScripting` exposed a COM object; any page loaded could call it. | Critical | No host objects in 2.0. JSON messages schema-validated; only 4 commands allowed. | ✅ |
| No CSP, no sandboxing, `Navigating` allowlist was coarse (`about`/`res`). | High | Virtual host mapping (`https://acmhub.local`) + CSP meta + `NavigationStarting` cancel for non-local + new-window interception with trusted-link validation. | ✅ |
| Remote mod data was injected with `JsonSerializer` into a `<script>` tag — `</script>` escaped, but `<`/`&` and U+2028/29 not fully hardened; strings rendered with innerHTML in JS. | High | Server-side (C#) escaping of `<` `>` `&` U+2028/29 and `</`; JS builds DOM with `textContent` only (no innerHTML for remote data). | ✅ |
| Images loaded with no size/content-type checks, no cache limits. | High | Covers: HTTPS-only, allowlisted hosts, max bytes, content-type check, bounded cache with cleanup, lazy loading (`loading="lazy"`), decode-failure fallback. | ✅ |
| No skeleton/empty/offline/error/reduced-motion/ARIA states in the store. | High | Full state system implemented in the new `catalog.js`/`catalog.css`. | ✅ |

## 5. WebBrowser host & bridge

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| `WebBrowser` (IE engine) as the store host. | Critical | WebView2 with hardening: DevTools off in Release, context menus off, host objects off, permissions denied, navigation restricted to local virtual host. | ✅ |
| Crash on machines without WebView2 runtime was unhandled. | Critical | Runtime availability checked before creating the control; native XAML store fallback + localized "install WebView2 Runtime" message and official link. | ✅ |
| No schema validation of incoming messages. | High | Bridge validates command whitelist + argument schemas (mod ID regex, HTTPS+allowlisted URL). | ✅ |

## 6. ViewModel lifecycle

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| `AsyncRelayCommand` had no exposed cancel surface; `Cancel()` existed but was unused by pages. | Medium | Long operations now bind a visible Cancel button to `command.Cancel()`; state restored in `finally`. | ✅ |
| Event subscriptions (`PackageReady`, `CancelRequested`) accumulated on repeated navigation (MainViewModel re-subscribed each visit). | High | Handler deduplication (unsubscribe before subscribe) in MainViewModel; VMs unsubscribe on unload. | ✅ |
| `DownloadsViewModel` subscribed to `ProgressChanged` and was never unsubscribed (singleton — ok) but event raised from thread-pool updated collections without dispatch safety in some paths. | Medium | All collection updates marshal to the Dispatcher; `HttpDownloadManager` isolates subscriber exceptions. | ✅ |

## 7. Async commands / cancellation

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| `.GetAwaiter().GetResult()` in DI registration (startup-only blocking read of settings). | Low | Replaced by lazy factory that reads settings asynchronously at first use of the download manager concurrency. | ✅ |
| `OperationCanceledException` surfaced to the user as an error in some paths. | Medium | Cancellation is detected and shown as "cancelled" state, never as an error toast. | ✅ |
| All heavy services already async with CancellationToken (detection, scan, hash, archive, backup, install, verify, repair, uninstall, catalog, download). | — | Kept; audit confirmed no `.Result`/`.Wait()` outside startup. | ➖ |

## 8. Download queue

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| `CancelAsync` after `Completed` would flip state to `Cancelled` (terminal-state corruption) and only deleted `.part`. | High | Terminal states protected: cancel on Completed/Verifying is a no-op. | ✅ |
| `ProgressChanged` subscriber exceptions could kill the download loop. | High | Subscribers invoked inside try/catch; one failing subscriber cannot break others. | ✅ |
| File-name validation was only `Path.GetFileName` equality — Windows reserved names (CON, NUL, …), trailing dots/spaces accepted. | High | Names validated with `SafePath.IsSafeFileName` (reserved device names, trailing dot/space, invalid chars). | ✅ |
| Pause/Resume race: resume during retry could double-start. | Medium | State machine made race-safe (queued/downloading guard in `Start`, token replacement under lock). | ✅ |
| No provider abstraction for the update package download. | Medium | Update download implemented through the same queue contract (separate progress surface for the updater). | ✅ |
| Retry used fixed 3 attempts with exponential backoff — acceptable. | Low | Kept; backoff + jitter. | ➖ |

## 9. Catalog cache

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Single hard-coded URL in settings; no fallback chain; cache write was non-atomic; a bad remote response could overwrite a good cache. | High | Endpoint chain: advanced HTTPS override → Pages primary → raw generated fallback → embedded empty catalog. ETag/If-None-Match with 304 handling; atomic cache write (temp + move); last-known-good cache is never replaced by invalid data. | ✅ |
| No redirect policy: HTTP downgrade redirects accepted. | High | Manual redirect handling (max 5 hops); every hop must remain HTTPS or the request fails. | ✅ |
| Size/read limits existed (2 MiB) but no per-field length bounds, mod-count bound, URL length bound, duplicate rejection was case-insensitive-only for IDs. | Medium | Full bounds validation (ID regex ≤80, name ≤120, description ≤2000, tags ≤32×64, URL ≤2048, mods ≤2000). | ✅ |
| Revoked/draft/hidden/deprecated statuses not modeled. | High | Full status model from the Data catalog schema; revoked metadata kept but download/install blocked with reason; deprecated shows warning; draft/hidden excluded. | ✅ |
| `minimumLauncherVersion` not checked. | Medium | Per-mod and catalog-level minimum launcher version validated with SemanticVersion; incompatible mods blocked with localized reason. | ✅ |

## 10. Installer transaction

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Core pipeline (staging → analyze → preview → conflict → backup → journal → install → verify → commit, with rollback + crash recovery) existed and was sound. | — | Kept; added executable/script extension rejection to archive validation and ADS check. | ➖/✅ |
| Archive validation lacked an explicit ADS (`file.txt:stream`) check. | Medium | Added ADS rejection in `SafePath`. | ✅ |
| No wrapper-folder correction existed for "miscellaneous" archives. | Low | Structure detector already handles root detection; documented; skin standalone targeting present. | ➖ |
| Journal operation append uses `FileOptions.WriteThrough` + flush-to-disk before mutation — correct. | — | Kept. | ➖ |

## 11. Backup / rollback

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Backup + journal + `CrashRecoveryService` present, tested. | — | Kept; retention setting honored; recovery runs at startup before the main window. | ➖ |

## 12. Manifest & ownership

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Per-mod manifest with hashes, ownership stack, baseline backup, newer-owner protection, modified-file detection all present and tested. | — | Kept. Uninstall now surfaces modified/blocking decisions in the Library detail panel with localized explanations. | ➖ |

## 13. Uninstall

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Blind deletion protected by ownership + modified-file decisions (preserve/restore/abort) — present. | — | Kept; UI exposes the three options explicitly with explanations and confirmation. | ➖ |

## 14. Settings persistence

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| Atomic JSON settings existed; no validation of values. | Medium | `AppSettings` validation: channel enum, concurrent downloads clamp, catalog override HTTPS-only, game path must be rooted. | ✅ |
| Endpoints hard-coded in several places (DI, catalog service, docs). | High | Single `AppConfig` (Core) with production defaults; DI and services consume it; advanced override stays in user settings. | ✅ |

## 15. Error handling & logging

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| `UserErrorMessageService` localized user-facing errors; logging existed (Debug + rolling file). | — | Kept; added scrubbing of URLs (no query secrets) in log messages. | ➖ |
| Unhandled dispatcher exceptions showed raw `ex.Message` to the user. | Medium | Routed through the localized error handler; raw details go to logs only. | ✅ |

## 16. Update architecture

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| No launcher updater at all; `UpdatesViewModel` only polled mod content providers (which returned nothing for direct HTTP). | Critical | Full GitHub-Releases updater: background check with ETag, Stable/Beta channel filtering (Stable excludes prereleases), semver comparison, downgrade rejection, consent-first download, SHA-256 verification via `SHA256SUMS.txt`, single-instance apply guard, restart marker, health marker, rollback on failed apply documented + installer-side rollback (Inno). | ✅ |
| Version strings scattered (`1.0.1` in DI user-agents, `.iss`, `app.manifest`, release notes). | High | Single version source `Directory.Build.props` (2.0.0-preview.1) consumed by assembly info, UI, User-Agent (via `AppInfo`), installer (generated `version.iss`), and release pipeline. | ✅ |
| Velopack evaluation: Velopack's build pipeline requires the `vpk` CLI + NuGet access on a Windows host; the repository already ships a working per-user Inno Setup pipeline and the sandbox cannot restore Velopack packages. Decision documented in `docs/AUDIT-2.0.md` and `docs/UPDATER.md`: GitHub Releases + `SHA256SUMS.txt` + Inno Setup updater path with the same guarantees (HTTPS, SHA-256, version monotonicity, release provenance). | — | Documented. | ⚠️ |

## 17. Installer packaging

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| `releases/*/AC-Mod-Hub-Setup.exe` files in git were text placeholders ("!Require Windows") — fake artifacts. | Critical | Removed from the repository; releases are produced only by the CI pipeline (Inno Setup) and attached to GitHub Releases. No placeholder EXEs are committed. | ✅ |
| Inno script: per-user (`PrivilegesRequired=lowest`), upgrade-aware, uninstall entry — correct baseline. | — | Extended: AppMutex wait for in-place update, WebView2 prerequisite message (no silent elevation), update-restart marker honored, version from single source. | ➖/✅ |

## 18. Version consistency

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| `1.0.1` hard-coded in ≥4 files; `app.manifest` identity 1.0.1.0; embedded catalog format differed from the public Data schema. | High | `Directory.Build.props` = 2.0.0-preview.1; `AppInfo` reads it at runtime; `scripts/validate.py` enforces no stray version literals; embedded catalog follows the Data schema exactly. | ✅ |

## 19. Tests

| Finding | Severity | Corrective action | Status |
| --- | --- | --- | --- |
| 17 test files covering installer/archive/backup/recovery/manifest/scanner — good coverage of the transaction core. | — | Kept; added tests for: SemanticVersion, update channel filtering, downgrade rejection, catalog client (ETag/304, redirects, oversize, duplicates, status handling, cache atomicity), SHA256SUMS parsing, update download (hash mismatch, disk space, cancel), download safe names, terminal states, subscriber isolation, settings validation, store message schema. | ✅ |
| No CI workflow existed. | High | Added `validate` (Python static checks that also run here), `ci.yml` (Windows build+test), `release.yml` (publish + Inno + SHA256SUMS + GitHub Release on tag). | ✅ |

## 20. What could not be executed in this environment (honest limitations)

- This sandbox has **no .NET SDK and no access to dotnet.microsoft.com or nuget.org**
  (network restricted to github.com/api.github.com/npmjs.org). Therefore
  `dotnet restore/build/test/publish` and Inno Setup compilation **cannot be executed here**.
  The code is written to compile cleanly under `TreatWarningsAsErrors` and the static
  validation suite (`scripts/validate.py`) runs here, but binary build/test verification is
  delegated to the Windows CI workflow added in this branch. See the final report for the
  exact list of not-executed checks.
