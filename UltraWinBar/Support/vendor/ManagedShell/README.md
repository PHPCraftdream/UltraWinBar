# ManagedShell (vendored)

Source of [ManagedShell](https://github.com/cairoshell/ManagedShell) at upstream commit
`b4521b2cbc94a71f74a17ebdf86cd58ba145a331`, the commit NuGet package 0.0.358 was built from.
Licensed under the Apache License 2.0 (`LICENSE`).

Vendored so interop and resource defects can be fixed at the source (see
`Support/docs/reviews/2026-09-28-round6-review.md`, section K8). The upstream version is
not changed; every local modification is listed below and marked in code with `UltraWinBar:`.

## Local modifications

- Build: `net480` target, NuGet packaging and SourceLink removed; `Directory.Build.props`
  pins version 0.0.358 instead of git-height versioning.
- `IAppVisibility` / `IAppVisibilityEvents`: `[PreserveSig]` HRESULT methods and 4-byte `BOOL`
  marshalling. The sink (`AppVisibilityEvents`) wrote its `long` result through a garbage
  `[out, retval]` pointer on every Start open/close; it now also never lets an exception reach
  native code. `AppVisibilityHelper` keeps its throw-on-failure behavior.
- `TrayService`: `WM_COPYDATA` notify-icon (`dwData = 1`) and icon-identifier (`dwData = 3`)
  payloads shorter than their structure are ignored instead of read past the sender's buffer.
- `IconImageConverter.GetImageFromHIcon`: destroys the icon only when the caller owns it
  (`ownsIcon`), also on conversion failure. Window, class, overlay and notify icons belong to
  their windows and are no longer destroyed; `ApplicationWindow` owns only its file-icon fallback.
- `ApplicationWindow.WinFileName`: a failed path lookup is retried at most every 30 s.
- P/Invoke types: pointer-sized `HANDLE`/`HKL`/`WPARAM`/`LPARAM` (`OpenEvent`, `SetEvent`,
  `EnumWindows`/`EnumChildWindows` callbacks, keyboard hook, `LoadKeyboardLayout`,
  `ActivateKeyboardLayout`, `SendNotifyMessage`); the truncating `int` overloads of
  `CloseHandle`, `PostMessage` and `SendNotifyMessage` are removed. `ITrayNotify` bools marshal as
  `BOOL`; `IOleCommandTarget.Exec` VARIANT parameters are explicit.
- `ExplorerHelper`: the 100 ms `taskbarMonitor` poll that kept Explorer's taskbar hidden is now a
  `SetWinEventHook(EVENT_OBJECT_SHOW)` watch on `Shell_TrayWnd`/`Shell_SecondaryTrayWnd`, with the
  timer kept as a 2 s safety net (hook install/uninstall track timer start/stop; `ExplorerHelper`
  is now `IDisposable` and unhooks on dispose).
- `TrayService`: the 100 ms `trayMonitor` poll that kept our tray window ahead of a rival
  `Shell_TrayWnd` is now event-driven — out-of-context hooks on `EVENT_OBJECT_SHOW` and
  `EVENT_SYSTEM_FOREGROUND` (other processes only), plus our own `WM_WINDOWPOSCHANGED`, schedule
  the same check via `Dispatcher.BeginInvoke`; the timer now only runs every 2 s as a safety net.
- `IParentAndItem`, `IShellItem`, `IShellItemImageFactory`, `IServiceProvider` (UWPInterop):
  `[PreserveSig]` on every method, matching their real native signatures (no phantom trailing
  `[out, retval]`). Callers already compared the return to `S_OK`/`0`, so a real failing HRESULT
  no longer throws past those checks; behavior at each call site is unchanged.
- `TrayService.WndProc`: wrapped in a try/catch exception barrier (rate-limited `ShellLogger.Error`,
  falls back to `DefWindowProc`) so a bad payload from any process can no longer escape into user32.
- `TrayService.ForwardMsg` and `AppBarManager.appBarMessage_QuerySetPos`: forward to Explorer's tray
  via `SendMessageTimeout(SMTO_ABORTIFHUNG)` (~500 ms) instead of `SendMessage`, so a hung Explorer
  can no longer hang our UI thread; added `NativeMethods.TrySendMessageTimeout` helper and the
  `SMTO_NORMAL`/`SMTO_ABORTIFHUNG` constants.
- `TrayService.Dispose`/`Initialize`: `Dispose` zeroes window/class handles and is now a no-op on a
  second call (`App.ExitApp` runs twice on session end) instead of double-destroying windows and
  re-broadcasting `TaskbarCreated`; `Initialize` reuses the rooted `WndProc` delegate across retries
  instead of creating a second one that a still-registered class could be left pointing at.
- `AppBarManager.appBarMessage_GetTaskbarPos`/`appBarMessage_QuerySetPos`: every `SHLockShared`
  result is checked for `IntPtr.Zero` and bails out with a failure result instead of
  `PtrToStructure`-ing a null pointer; `SHUnlockShared`/`SHFreeShared`/the `hAmd` and `hCopyData`
  `AllocHGlobal` blocks (previously leaked on every call) are now released in `finally`.
- `ExplorerTrayService.GetTrayItems`/`GetTrayItem`: `OpenProcess`/`VirtualAllocEx` results are
  checked before use (a failed `OpenProcess` no longer sends `TB_GETBUTTON` with a null remote
  pointer); the per-item `AllocHGlobal` blocks and `VirtualFreeEx`/`CloseHandle` are released in
  `finally` instead of leaking on early return or an exception.
