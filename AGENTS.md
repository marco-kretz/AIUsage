# AGENTS.md

Windows 11 tray app (WinUI 3, .NET 10, unpackaged, self-contained) showing AI usage limits. Currently only Claude Code. See `README.md` for architecture and design decisions.

## Commands

```powershell
dotnet build                                   # whole solution; must stay at 0 warnings
dotnet test                                    # xUnit v3 on Microsoft.Testing.Platform (opted in via global.json)
dotnet run tools/smoke.cs [en]                 # one real request against the usage endpoint
dotnet publish src/AIUsage.App -c Release -r win-x64 --self-contained -o artifacts/publish
```

- Stop a running `AIUsage.exe` before building the app, because the output files are locked.
- `artifacts/publish` may be the user's live installation (autostart points there). Ask before overwriting it.

## Layout

- `src/AIUsage.Core`: provider-agnostic logic with no UI references. Models (`Usage.cs`), `UsageMonitor` (polling/backoff), `UsageFormat` (shared presentation rules), `AppSettings`, `Claude/` (provider + parser), `Resources/Strings*.resx`.
- `src/AIUsage.App`: WinUI shell. `Tray/` (Shell_NotifyIcon P/Invoke + GDI+ icon renderer), `Views/`, `ViewModels/` (CommunityToolkit.Mvvm), `Interop/Native.cs` (all P/Invoke), `UsageNotifier` (toasts), `Autostart` (HKCU Run).
- `tests/AIUsage.Core.Tests`: parser fixtures in `Fixtures/*.json`, monitor merge/backoff tests.
- `tools/`: file-based C# scripts (`smoke.cs`, `make-icon.cs`).

## Rules

- **Provider-agnostic UI:** tray, tooltip, flyout, settings and toasts consume only `UsageSnapshot`/`UsageWindow`. No provider-specific logic outside `Core/<Provider>/`. A new provider implements `IUsageProvider` and is registered in `App.ConfigureServices`.
- **Token handling:** read it locally, send it only to `api.anthropic.com`, never log it, never refresh it. Do not log request headers or credentials file contents.
- **Endpoint limits:** never poll the usage endpoint more often than every 180 s (`UsageMonitor.MinPollInterval`). Tests must not hit the network. Use fixtures.
- **Parsing:** the response is undocumented. Parse defensively: skip null, missing or wrongly typed fields, never throw for them. Every format change gets a fixture plus a test.
- **Strings:** all UI text goes in `Strings.resx` (German, neutral) and `Strings.en.resx`, and both must stay in sync. The strongly typed `Strings` class is generated at build time. There is no designer file.
- **Code style:** code, comments and identifiers in English. Comment only the non-obvious why. All Win32 interop lives in `Interop/Native.cs`.
- **Level colors:** usage level colors exist twice. Keep `App.xaml` (`Level*Brush`) and `TrayIconRenderer.LevelColor` in sync.

## Gotchas

- Keep `EnableMsixTooling=true`. Without it, unpackaged publish drops `AIUsage.pri` and the `.xbf` files, and the app crashes at startup inside `Microsoft.UI.Xaml.dll`.
- Do not reference the `Microsoft.WindowsAppSDK` meta package. It adds about 60 MB of AI/ML runtimes. Use the component packages (`.WinUI`, `.InteractiveExperiences`, `.Runtime`).
- Trimming uses `TrimMode=partial`. Use source-generated `System.Text.Json` contexts (see `AppSettingsJsonContext`), not reflection-based serialization.
- Do not use `AppNotificationManager`. It needs the WinAppSDK Singleton package, which self-contained apps lack. Toasts go through `Windows.UI.Notifications` with an HKCU AUMID.
- XAML errors like `Cannot resolve DataType` together with warning WMC1509 usually mean the C# pass failed first. Look for the CS error.
- The tray icon GUID is fixed. Do not change it, or users lose their "always show" setting.

## Verifying UI changes

Do not click on the user's desktop. Drive the app through its hidden tray window instead (class `AIUsage.TrayWindow`): post `WM_APP+1` (0x8001) with `lParam = 0x400` (left click / toggle flyout) or `0x7B` (context menu, `wParam = x | y << 16`). Then screenshot the region via `System.Drawing` `CopyFromScreen` in a DPI-aware process. Logs are in `%LOCALAPPDATA%\AIUsage\logs\`.
