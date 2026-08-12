# AC Mod Hub 1.0.0 Preview 1

Download `AC-Mod-Hub-Setup.exe`, verify it against `SHA256SUMS.txt`, and run it on Windows x64.

- Installs/extracts to `%LocalAppData%\Programs\ACModHub`
- Includes an app-local .NET/Windows Desktop runtime
- Does not require a separate .NET installation
- Unsigned preview: Windows SmartScreen may display a warning

Build source commit: `9d211e1`
Build result: 0 warnings, 0 errors.

The original source remains .NET 8. This emergency offline distributable was retargeted to .NET 9 in an isolated build copy because the sandbox could not reach .NET 8/NuGet endpoints. See the GitHub pre-release notes for details.
