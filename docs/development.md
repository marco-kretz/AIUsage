# Development

## Build & run

Requirements: .NET 10 SDK, Windows 11 (x64), Visual Studio C++ build tools (for Native AOT publish).

```powershell
dotnet test                                   # parser/monitor unit tests (xUnit v3, Microsoft.Testing.Platform)
dotnet run tools/smoke.cs                     # console smoke test against the real endpoint (add "en" for English)
dotnet build src/AIUsage.App                  # debug build: src/AIUsage.App/bin/Debug/.../win-x64/AIUsage.exe
# VS 18 vcvarsall.bat calls vswhere without a path, so it must be on PATH for the AOT linker step
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"
dotnet publish src/AIUsage.App -c Release -r win-x64 -o artifacts/publish
```

The publish output is a Native AOT build of about 11 MB (see "Size" below). Start `AIUsage.exe`. A second start brings the flyout to the front.

## Release

Push a tag `v<version>` (e.g. `git tag v0.2.0 && git push origin v0.2.0`) or create a release with a new tag on GitHub. `.github/workflows/release.yml` then runs the tests, publishes with the version taken from the tag, and attaches `AIUsage-<version>-win-x64.zip` (without `*.pdb`) to the release, creating it with generated notes if it does not exist yet.

`<Version>` in `src/AIUsage.App/AIUsage.App.csproj` only applies to local builds; bump it alongside the tag.

## Architecture

```
src/AIUsage.Core     provider-agnostic core (no UI dependencies)
  Usage.cs             UsageSnapshot / UsageWindow / UsageStatus, IUsageProvider
  UsageMonitor.cs      one polling loop per provider: interval (≥ 180 s), backoff, Retry-After, keeps last values on failures (Stale)
  UsageFormat.cs       thresholds, countdown and status texts shared by tray, tooltip, flyout
  AppSettings.cs       JSON settings (source-generated serializer)
  Claude/              ClaudeCodeProvider (credentials + HTTP), ClaudeUsageParser (defensive JSON parsing)
  Resources/           Strings.resx (German, neutral) + Strings.en.resx
src/AIUsage.App      WinUI 3 shell
  Tray/                Shell_NotifyIcon wrapper (GUID identity, TaskbarCreated, native menu) + GDI+ icon renderer
  Views/               FlyoutWindow (placement, slide/fade), SettingsWindow
  ViewModels/          CommunityToolkit.Mvvm view models rendered generically from snapshots
  UsageNotifier.cs     80 % / 95 % toasts, once per window and reset period
tests/AIUsage.Core.Tests   parser fixtures (real response, nulls, missing and extra fields), monitor merge/backoff
tools/                 smoke.cs, make-icon.cs (file-based C# apps)
```

To add a provider (Codex, OpenRouter, …), implement `IUsageProvider` and register it in `App.ConfigureServices`. The monitor, tray icon, tooltip, flyout, settings and toasts only deal with `UsageSnapshot` and need no changes.

### Claude Code data source

The app reads `claudeAiOauth.accessToken` from `%USERPROFILE%\.claude\.credentials.json` (the path can be overridden in Settings) and calls `GET https://api.anthropic.com/api/oauth/usage` with `anthropic-beta: oauth-2025-04-20`. The token is never refreshed or logged and is only sent to that host. On 401/403 or when the token has expired locally, the flyout says to start Claude Code, which refreshes the file. The response format is undocumented. Differences from the original assumptions, observed in October 2026:

- `extra_usage` has its own shape (`is_enabled`, `monthly_limit`, …). It only shows up when its `utilization` is a number.
- A `limits[]` array lists model-scoped weekly limits that have no `seven_day_*` field (e.g. "Fable"). These become extra windows.
- `resets_at` has sub-second jitter, so toast deduplication uses 10-minute buckets.
- The plan comes from `.credentials.json` (`subscriptionType` + `rateLimitTier` → "Max 5x"), not from the API.

## Design decisions

- **WinUI 3 rather than WPF.** WinUI gives the native Windows 11 look (acrylic backdrop, rounded corners, InfoBar, theme brushes) with no extra effort. The parts that WinUI 3 lacks are small Win32 interop: a tool window with no taskbar button, and window-wide alpha through `WS_EX_LAYERED` for the fade. That interop works cleanly, so WPF was not needed.
- **Shell_NotifyIcon via P/Invoke rather than H.NotifyIcon.** The flyout is a custom animated window anyway. Calling the API directly gives full control over the GUID identity, `Shell_NotifyIconGetRect` anchoring, TaskbarCreated and per-DPI icon sizes for about 200 lines of code, with no extra dependency. The context menu is a native Win32 menu. Two undocumented uxtheme ordinals, also used by Explorer, make it follow dark mode.
- **Animation.** The window position and the layered alpha are driven from `CompositionTarget.Rendering`. Showing takes 230 ms with cubic ease-out and slides 40 DIP away from the taskbar edge. Hiding takes 140 ms with ease-in. With `UISettings.AnimationsEnabled == false`, the flyout shows instantly. The edge is the monitor edge nearest to the icon, so taskbars on any side, multiple monitors and per-monitor DPI are handled. The flyout is always clamped into the work area.
- **Toasts** use `Windows.UI.Notifications` with a per-user AUMID in `HKCU\Software\Classes\AppUserModelId\AIUsage.App`. The Windows App SDK `AppNotificationManager` needs the Singleton package, which is not guaranteed to be present; the OS API works regardless of deployment.
- **Localization** uses `.resx` with strongly typed `Strings`, bound via `x:Bind`. German is the neutral language and English the satellite. The default follows the Windows display language (German, otherwise English); it can be changed in Settings and applies after a restart.
- **Missing Windows App Runtime.** The Windows App SDK bootstrapper's default auto-initialize options (`OnNoMatch_ShowUI`) show a dialog with a download link instead of crashing silently.

## Size

The app is published with **Native AOT** and a framework-dependent Windows App SDK: about 11 MB (`AIUsage.exe` ≈ 9.7 MB plus a few small DLLs).

| Variant | Size | User needs |
|---|---|---|
| Native AOT, Windows App Runtime installed (current) | ≈ 11 MB | Windows App Runtime 2.x |
| Framework-dependent .NET + Windows App SDK | ≈ 41 MB | .NET 10 + Windows App Runtime 2.x |
| Everything bundled, partially trimmed | ≈ 84 MB | nothing |

- Only the WinUI parts of the Windows App SDK are referenced (`Microsoft.WindowsAppSDK.WinUI`, `.InteractiveExperiences`, `.Runtime`). The meta package would add about 60 MB of AI/ML/Search runtimes.
- The tray icon is drawn through the GDI+ flat API (`Interop/GdiPlus.cs`) instead of `System.Drawing.Common`, which is not AOT-friendly.
- Settings use a source-generated `System.Text.Json` context; no reflection-based serialization.
- `EnableMsixTooling` is required. Without it, an unpackaged publish omits `AIUsage.pri` and the `.xbf` files, and the app crashes at startup.

## Not implemented (by scope)

Other providers, own OAuth login/token refresh, telemetry, reading credentials from WSL, code signing, auto-update. Clicking a toast opens the flyout only while the app is running: there is no COM activator.
