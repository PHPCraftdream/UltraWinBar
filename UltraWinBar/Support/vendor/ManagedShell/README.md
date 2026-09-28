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
- `ImmersiveShellHelper`: added `Reset()`, called by the app on `TaskbarCreated`, to drop the
  static `_immersiveShell`/experience-manager caches that otherwise stay dead forever after an
  Explorer restart; calls on a cached manager that fail with a severed-proxy HRESULT now reset and
  retry once. Raw `GetExperienceManager`/`QueryInterface` pointers are released in `finally`, and
  `WindowsDeleteString` now runs in `finally` even if `GetExperienceManager` throws.
- `NativeWindowEx.OnThreadException` now logs via `ShellLogger.Error` instead of the WinForms
  default (silently swallowing the exception). Since R9-H it reports through `CallbackGuard`
  instead (see below), now that `CallbackGuard` lives in `ManagedShell.Common`.
- `ShellLogger._orphanedEvents`: bounded ring buffer (500, oldest dropped) instead of an
  unbounded, unsynchronized `List`, so a never-attached observer no longer leaks memory for the
  whole uptime and concurrent `Debug`/`Info`/... calls can't corrupt it. `OnLog` invokes each
  attached observer independently, so one throwing observer no longer stops the rest or escapes
  into the caller.
- `TasksService.Dispose`/`Initialize`: `cloakEventHook`/`moveEventHook` (and `_HookWin`) are zeroed
  on `Dispose`, so a following `Initialize` (e.g. `ExplorerMonitor` on `TaskbarCreated`) reinstalls
  them instead of finding a stale non-zero handle and skipping the hook. A failed `Initialize` rolls
  back only what that attempt installed, so a retry does not create a second hook window and
  double-process shell hook messages.
- `TasksService`: removed `Debugger.Break()` from `ShellWinProc`'s catch (logs instead);
  `CloakEventCallback`/`MoveEventCallback` (WinEvent callbacks that run our code, including COM
  calls, via `PropertyChanged`) are now wrapped in `try`/`catch`; `getInitialWindows`'s `EnumWindows`
  callback only collects handles, adding to `Windows` (and its `CollectionChanged` handlers/filters)
  after `EnumWindows` returns.
- `TasksService.ShellWinProc` (`HSHELL_GETMINRECT`) and `ApplicationWindow.SetOverlayIconDescription`:
  `lParam`/`SHLockShared` results are pointers from an arbitrary process in the session, not
  marshaled by the system. A new `MemorySafety` helper validates a pointer with `VirtualQuery`
  (committed, read/write, not `PAGE_GUARD`/`PAGE_NOACCESS`, whole struct inside the region) before
  `GETMINRECT` reads/writes `SHELLHOOKINFO`; the overlay description is read with a bounded length
  (`ApplicationWindow.ReadBoundedString`, capped at 260 chars) instead of an unbounded
  `PtrToStringAuto`.
- `TasksService.windowsProperty`: `DependencyProperty.Register` moved from an instance field to a
  static readonly registration (a second `TasksService` used to throw); the default value is no
  longer a shared `ObservableCollection` — each instance creates its own in its constructor.
- `ApplicationWindow`: icon loading resets `_iconLoading` in a `finally`, so a throwing lookup no
  longer sticks the icon at "loading" forever; `PropertyChanged` is raised on the owner thread's
  `Dispatcher` (captured at construction) instead of the icon-loading STA thread, and fires
  synchronously when there is none (e.g. tests).
- `FileLog`: no longer sets `AutoFlush`/flushes per line; added `Flush()` so the caller (now
  `RollingFileLog`'s background writer) controls batching instead of flushing on every line.
- `ShellLogger`: added `Debug`/`Info` overloads taking an `[InterpolatedStringHandler]`, so an
  interpolated string literal argument is only formatted when that severity is enabled (existing
  call sites are unaffected; a plain `string` argument still binds to the old overloads).
- R9-H (one native-callback layer, review K12): `CallbackGuard`, `WinEventHook` and
  `ShellComProxy`/`ShellCom` moved here from the app (`ManagedShell.Common.Native`, `internal` with
  `InternalsVisibleTo` for the app and every ManagedShell project that needs them), so ManagedShell's
  own native callbacks share the same exception barrier instead of the app owning it alone.
  `ShellComProxy` no longer references the app's `ExplorerMonitor` directly; it subscribes to the
  new `ExplorerLifecycle.Restarted` signal here, which `ExplorerMonitor` raises alongside its own
  app-level event.
  - New `NativeCallback.Wrap(name, handler)` factory for delegates user32 calls back into directly
    (`WNDPROC`, `EnumWindows`): roots the wrapper, reports an escaping exception via `CallbackGuard`
    (falling back to `DefWindowProc`/`false` respectively), and counts calls per name.
    `TrayService.WndProc` now goes through it (replacing a local rate-limited counter);
    `TasksService.getInitialWindows`'s `EnumWindows` callback now goes through it too.
  - `TasksService`'s cloak/move `SetWinEventHook`/`UnhookWinEvent` pairs (raw static `IntPtr`
    handles) are now instance-owned `WinEventHook` fields, closing the class of bug behind Н3
    structurally (a stale non-zero static handle skipping re-installation after `Dispose`).
  - New `WinEventHub`: one underlying `WinEventHook` per `(event, flags)` pair on the UI thread,
    reference-counted across managed subscribers, instead of each owner installing its own hook for
    the same OS event (review section 5 counted 5 `EVENT_SYSTEM_FOREGROUND` and 3
    `EVENT_OBJECT_SHOW` registrations delivering the same event to the UI thread once per
    registration). `TrayService` and `ExplorerHelper`'s `EVENT_OBJECT_SHOW`/`EVENT_SYSTEM_FOREGROUND`
    hooks, and the app's `StartMenuMonitor`/`WindowPlacementGuard`/`InputLanguage`/
    `DesktopActivationGuard` foreground/show subscriptions, now go through it. Cloak/move/menu-event
    range hooks, and hooks installed on a dedicated thread (`DesktopActivationHookThread`'s mouse
    hook), are unaffected.
  - `ShellContextMenu` (the only remaining `NativeWindow` subclass without an `OnThreadException`
    override) now reports through `CallbackGuard` too.
- R9-I (explicit service lifecycle, review K14): new `ManagedShell.Common.Native.ServiceLifecycleState`
  enum (`Created`/`Running`/`Stopped`/`Disposed`), `internal` with `InternalsVisibleTo("DesktopRules")`
  added to `ManagedShell.Common` for the new restart test.
  - `TasksService`: added a `LifecycleState` property alongside the existing `IsInitialized` field,
    set on both the success and rollback paths of `Initialize`. `Initialize` called after `Dispose`
    now logs and reinitializes instead of throwing `ObjectDisposedException` — `ExplorerMonitor.cs`
    calls `Dispose()` then `Initialize()` on the same instance on every `TaskbarCreated`, so this
    service's `Dispose()` is a restartable Stop, not a terminal disposal.
  - `TrayService`: same `LifecycleState`/log-and-reinitialize policy as `TasksService`, for symmetry
    and because the review lists it in the same restart test. `Initialize()` now rolls back a
    partially-failed `RegisterTrayWnd`/`RegisterNotifyWnd` attempt (previously left `HwndTray`/
    `HwndNotify` however they landed, with no cleanup). `DestroyWindows()` now calls `UnregisterClass`
    unconditionally instead of only when a window of that class exists, closing a leak where a
    registered-but-never-created window class outlived a failed `Initialize()`.
  - `ExplorerHelper`: added a `LifecycleState` property (`Running` from construction — it has no
    separate `Start`). Unlike the two services above, no shipped code restarts it in place (one
    instance per `ShellManager`, disposed once at shutdown), so `Dispose()` is a true terminal
    disposal: idempotent, and `HideExplorerTaskbar` set afterward is logged and ignored instead of
    reinstalling the taskbar-show hook.
