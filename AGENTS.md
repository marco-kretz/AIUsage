# AGENTS.md

Windows 11 tray app (WinUI 3, .NET 10, unpackaged, Native AOT) showing AI usage limits. Currently only Claude Code. See `docs/development.md` for build, release, architecture and design decisions; `README.md` is for end users.

## Commands

```powershell
dotnet build                                   # whole solution; must stay at 0 warnings
dotnet test                                    # xUnit v3 on Microsoft.Testing.Platform (opted in via global.json)
dotnet run tools/smoke.cs [en]                 # one real request against the usage endpoint
$env:PATH += ";${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer"   # see Gotchas
dotnet publish src/AIUsage.App -c Release -r win-x64 -o artifacts/publish     # Native AOT, ≈ 11 MB
```

- Stop a running `AIUsage.exe` before building the app, because the output files are locked.
- `artifacts/publish` may be the user's live installation (autostart points there). Ask before overwriting it.

## Layout

- `src/AIUsage.Core`: provider-agnostic logic with no UI references. Models (`Usage.cs`), `UsageMonitor` (polling/backoff), `UsageFormat` (shared presentation rules), `AppSettings`, `Claude/` (provider + parser), `Resources/Strings*.resx`.
- `src/AIUsage.App`: WinUI shell. `Tray/` (Shell_NotifyIcon P/Invoke + GDI+ icon renderer), `Views/`, `ViewModels/` (CommunityToolkit.Mvvm), `Interop/` (all P/Invoke: `Native.cs` Win32, `GdiPlus.cs` icon drawing), `UsageNotifier` (toasts), `Autostart` (HKCU Run).
- `tests/AIUsage.Core.Tests`: parser fixtures in `Fixtures/*.json`, monitor merge/backoff tests.
- `tools/`: file-based C# scripts (`smoke.cs`, `make-icon.cs`).

## Rules

- **Provider-agnostic UI:** tray, tooltip, flyout, settings and toasts consume only `UsageSnapshot`/`UsageWindow`. No provider-specific logic outside `Core/<Provider>/`. A new provider implements `IUsageProvider` and is registered in `App.ConfigureServices`.
- **Token handling:** read it locally, send it only to `api.anthropic.com`, never log it, never refresh it. Do not log request headers or credentials file contents.
- **Endpoint limits:** never poll the usage endpoint more often than every 180 s (`UsageMonitor.MinPollInterval`). Tests must not hit the network. Use fixtures.
- **Parsing:** the response is undocumented. Parse defensively: skip null, missing or wrongly typed fields, never throw for them. Every format change gets a fixture plus a test.
- **Strings:** all UI text goes in `Strings.resx` (German, neutral) and `Strings.en.resx`, and both must stay in sync. The strongly typed `Strings` class is generated at build time. There is no designer file.
- **Code style:** code, comments and identifiers in English. Comment only the non-obvious why. All interop lives in `Interop/`.
- **Level colors:** usage level colors exist twice. Keep `App.xaml` (`Level*Brush`) and `TrayIconRenderer.LevelColor` in sync.

## Gotchas

- Keep `EnableMsixTooling=true`. Without it, unpackaged publish drops `AIUsage.pri` and the `.xbf` files, and the app crashes at startup inside `Microsoft.UI.Xaml.dll`.
- Do not reference the `Microsoft.WindowsAppSDK` meta package. It adds about 60 MB of AI/ML runtimes. Use the component packages (`.WinUI`, `.InteractiveExperiences`, `.Runtime`).
- The app is Native AOT (`PublishAot`). Keep `dotnet build` free of AOT/trim warnings. No reflection-based JSON (use `AppSettingsJsonContext`), no `System.Drawing.Common` (it pulls `System.Formats.Nrbf`, which breaks ILC; draw via `Interop/GdiPlus.cs`). The only accepted publish warning is IL2104 from Serilog.
- AOT publish fails at the link step with a "vswhere.exe not found" message baked into the linker path: VS 18's `vcvarsall.bat` calls `vswhere` without a path. Add `%ProgramFiles(x86)%\Microsoft Visual Studio\Installer` to `PATH` before publishing.
- The Windows App SDK is framework-dependent (`WindowsAppSDKSelfContained=false`). Users need the Windows App Runtime 2.x.
- Do not use `AppNotificationManager`; it needs the WinAppSDK Singleton package. Toasts go through `Windows.UI.Notifications` with an HKCU AUMID.
- XAML errors like `Cannot resolve DataType` together with warning WMC1509 usually mean the C# pass failed first. Look for the CS error.
- The tray icon GUID is fixed. Do not change it, or users lose their "always show" setting.

## Verifying UI changes

Do not click on the user's desktop. Drive the app through its hidden tray window instead (class `AIUsage.TrayWindow`): post `WM_APP+1` (0x8001) with `lParam = 0x400` (left click / toggle flyout) or `0x7B` (context menu, `wParam = x | y << 16`). Then screenshot the region via `System.Drawing` `CopyFromScreen` in a DPI-aware PowerShell process. Settings controls can be driven via UI Automation (`UIAutomationClient`, e.g. `TogglePattern`, `InvokePattern`). If the user has a real `settings.json`, back it up before tests and restore it through the UI so the in-memory state matches. Logs are in `%LOCALAPPDATA%\AIUsage\logs\`.
