# AC Mod Hub — Launcher Updater Architecture (2.0)

## Why not Velopack?

The 2.0 specification prefers Velopack. After a real evaluation it was not adopted for
this release, for these documented reasons:

1. **Packaging pipeline dependency.** Velopack's `vpk` CLI and NuGet packages are required
   on the build host. The release pipeline for this repository is Inno Setup 6 (already
   shipped and battle-tested in `installer/`), and mixing two packaging systems for one
   per-user app adds failure modes without user-visible benefit.
2. **No signing infrastructure.** Velopack's strongest guarantees come from Authenticode
   signing, which this project cannot do yet (no certificate). Without signing, both
   approaches reduce to the same primitives: HTTPS + SHA-256 + version monotonicity +
   release provenance.
3. **Simplicity of in-place update.** A per-user Inno Setup with `AppMutex` cleanly waits
   for the running app to exit, replaces files, and relaunches — the exact semantics we
   need, with an installer-side rollback if the setup itself fails.

The updater therefore uses **GitHub Releases + SHA256SUMS.txt + Inno Setup**, with the same
guarantees the specification requires. Migrating to Velopack later only touches
`IUpdateChecker`/`IUpdateDownloader`/`IUpdateApplier` and the CI step — the UI contract
(Updates page) stays identical.

## Behavior

1. **Startup**: the window is shown first. A background task (never blocking) checks the
   GitHub Releases feed `https://api.github.com/repos/aliam664/1/releases` with an ETag
   cache; a 304 response reuses the cached feed instantly.
2. **Channel filtering**: Stable excludes prereleases and drafts; Beta includes prereleases,
   still excluding drafts. Versions are compared as Semantic Version 2.0.0; downgrades and
   equal versions are never offered.
3. **Consent**: nothing is downloaded or installed without the user choosing
   "Download & install". "Later" persists the skipped version (not offered again until a
   newer one exists). "View release" opens the GitHub release in the default browser.
4. **Download & integrity**: the setup is downloaded with Range resume support, disk-space
   check, and cancellation; then `SHA256SUMS.txt` is fetched and the installer hash is
   verified. A mismatch discards the package. The file is only marked ready after both
   the hash and the byte size match the release metadata.
5. **Apply**: a single-instance lock prevents concurrent applies; the installer path is
   validated to live inside the dedicated updates cache. The app writes a
   `pending-update.json` marker and spawns the installer (`/VERYSILENT /SUPPRESSMSGBOXES
   /NORESTART`) before exiting. The installer's `AppMutex` waits for the app to exit and
   then replaces the files (Inno rolls the install back if it fails midway).
6. **Restart & health**: after a silent update the installer relaunches the app (marker
   check `UpdateRequested()` in `ACModHub.iss`). On startup the app resolves the marker
   into a health record and shows the "updated" notification. Failed applies keep the old
   version intact and surface an error.

## Integrity without a signing certificate

Until an Authenticode certificate is available we do **not** claim signed binaries. The
chain of trust is: HTTPS to github.com + GitHub release provenance (tag → commit) +
published SHA-256 (verified client-side) + monotonic SemVer (downgrade refused) +
installer-side rollback. To add Authenticode later: sign `AC-Mod-Hub-Setup.exe` in the
release workflow and verify the signature in `UpdateDownloader` before applying.

## Test coverage

- `tests/ACModHub.Tests/Core/SemanticVersionTests.cs` — parsing, prerelease ordering, downgrade detection.
- `tests/ACModHub.Tests/Infrastructure/UpdateCheckerTests.cs` — stable/beta filtering, draft exclusion, downgrade/same-version rejection, ETag/304 cache reuse, redirect policy, asset presence, malformed feeds.
- `tests/ACModHub.Tests/Infrastructure/UpdateDownloaderTests.cs` — hash mismatch, missing sums entry, size mismatch, disk-space rejection, redirect downgrade rejection.
- `tests/ACModHub.Tests/Infrastructure/UpdateApplierTests.cs` — untrusted-path refusal, marker lifecycle, concurrent-apply guard, restart resolution.
