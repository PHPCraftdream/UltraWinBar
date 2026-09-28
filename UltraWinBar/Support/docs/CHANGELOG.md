# Changelog

## Unreleased

### Changed

- ManagedShell is vendored from its 0.0.358 source (`Support/vendor/ManagedShell`)
  and built with the app, so its defects are fixed at the source; the upstream
  version is unchanged.
- Every WinEvent hook goes through one primitive that keeps its callback rooted,
  removes it on the installing thread and never lets a handler exception reach
  user32; every Explorer-hosted COM object goes through one proxy with a single
  disconnect check, 2/5/15/60 s backoff and recreation on Explorer restart.
- UI controls and windows subscribe to Settings and virtual desktop changes
  weakly, so a missed unsubscribe can no longer keep a closed panel alive.
- The log gets a resource snapshot 5 minutes after start and then every 30
  minutes (handles, GDI/USER objects, threads, memory, hooks, subscribers), and
  exceptions on background threads and unobserved tasks are logged.
- Closed taskbar panels (settings/display changes, Explorer restart) are tracked
  with a bounded list of `WeakReference`s; the health snapshot forces a GC pass
  and logs how many are still alive, with their edge and screen, so a missed
  unsubscribe anywhere shows up without a profiler. The health line also reports
  the worst UI-thread delay and how often it crossed 250 ms/1 s over the
  interval, total `CallbackGuard` failures, how many `ShellComProxy` instances
  are currently unavailable, and log lines lost or dropped by the file logger.
  A panel counts as leaked only a minute after it closed.
- The native callback primitives (exception barrier, WinEvent hook, Explorer COM
  proxy) live in ManagedShell and are shared by the app and ManagedShell; the
  tray window procedure and window enumeration go through them too. Foreground
  and window-show events are delivered once per event instead of once per
  interested component (4 system hooks instead of 8 or more). A test rejects
  hooks, window classes and registry watches created outside these primitives.
- All low-level mouse hooks (panel and element drag, toolbar drag, desktop
  activation) share one hook on a dedicated thread, so a busy UI thread no
  longer delays every mouse event in the system or gets the hook removed by
  Windows; moving a panel to another edge no longer runs inside the hook.
- One registry watch implementation for virtual desktops and the Japanese IME
  mode; the thread-agnostic flag is used only on Windows 8 and later.
- The log is written on a background thread in batches; errors are written
  immediately together with everything queued before them. Debug and info
  messages are not formatted when their level is off. Debug logging is off by
  default for new installations.
- Window, tray and Explorer helper services have explicit start/stop/dispose
  states; stopping releases every hook, window and class, a failed start rolls
  back, and repeated stops are harmless.
- Faster window tracking: lookups by window handle use a dictionary; a title
  change redraws only that window (other windows of the same program only when
  it flashes); taskbar progress and overlay messages no longer allocate a
  temporary window object. The Win+number hotkey table is built in one pass
  over explorer.exe instead of copying it; tray messages no longer re-parse
  constant GUIDs or allocate an icon before checking that it is new.
- Windows that vanished without a notification, or whose handle was reused by
  another process, are removed from the task list by a sweep every 30 minutes
  and counted in the health line.
- Cross-process data (tray and AppBar messages, AppBar shared memory, shell
  hook pointers, overlay descriptions) is parsed in one place with tests on
  garbage input; tests use typed access to internals instead of reflection.
- Large files are split by responsibility (Start menu monitor, taskbar window,
  desktop activation, Japanese IME, settings, tests) without behavior changes.
- Fewer background wake-ups of the UI thread: keeping Explorer's taskbar hidden
  and keeping the app's tray window ahead of Explorer's now react to window
  events instead of 100 ms polls (a 2 s check remains as a safety net); the
  keyboard layout indicator reacts to foreground changes and polls every 750 ms
  instead of 200 ms; the Japanese IME kana/romaji mode is cached and re-read
  only when its registry value changes.

### Fixed

- Fix memory-unsafe COM declarations: desktop pin and move queries returned a
  4-byte `BOOL` into a 2-byte `VARIANT_BOOL`, and the immersive launcher
  interfaces lacked `PreserveSig`. A test now rejects any COM method with a hidden
  `[out, retval]`, unmarked `bool`/VARIANT, or a pointer-sized value declared as
  a 32-bit integer, in the app and in ManagedShell (shell item, image factory and
  service provider interfaces now return their real HRESULT).
- ManagedShell: ignore tray `WM_COPYDATA` payloads shorter than their structure
  instead of reading past the sender's buffer; destroy only icons the app owns;
  retry a failed window path lookup at most every 30 s; use pointer-sized
  handles and message parameters throughout.
- Exceptions in the Start menu, window placement, task recovery and registry
  watch callbacks are logged instead of terminating the process.
- Task buttons sort by cached ranks instead of rebuilding and scanning the saved
  order on every comparison; tray icons cache their order position.
- Only the live settings object is saved; a stray `Settings` instance can no
  longer overwrite the user's settings file.
- Fix a crash (access violation) after hours of uptime: ManagedShell's Start
  launcher visibility sink declared its COM methods without `PreserveSig`, so
  each Start open/close notification wrote a phantom return value through a
  garbage pointer. Start monitoring now uses its own correctly declared sink.
- Keep installed low-level mouse hooks rooted until they are removed, and remove
  drag hooks when a panel closes or a quick-launch button unloads. Moving a panel
  to another edge reopens the panels mid-drag; the old panel's hook callback
  could be freed and crash the next mouse event with an access violation.
- Build the notification-area view and icon controls only on the visible panel
  that hosts the tray; other panels no longer keep a live view or show balloons
  a second time after the tray moves.
- Query the foreground IME state with a bounded, hang-aware call so a hung
  application can no longer block the taskbar.
- Treat a foreground CoreWindow as the Start menu only when Start was opened
  from a taskbar or the launcher reports itself visible; Search, Action Center
  and the emoji panel no longer mark Start as open.
- Remove the previous language dictionaries before merging the next language,
  so switching languages no longer stacks resource dictionaries.
- Wait (up to 3 s) for the work-area change broadcast when the watchdog
  restores the work area and on shutdown, so other applications see it.
- Drop cached Start launcher COM objects after an Explorer restart.
- Stop restoring all-desktop pins through COM on every virtual desktop switch;
  restore them when the virtual desktop service reconnects. The initial pin
  import gives up on persistently failing windows after 5 attempts.
- Remove per-event diagnostic logging from the foreground, Start poller and task
  drag paths, and attach the console log only when a console exists.
- The tray window procedure no longer lets an exception reach user32, and AppBar
  requests from a process whose shared memory cannot be mapped (elevated or
  already exited) fail instead of crashing; AppBar forwarding and the Explorer
  tray icon import no longer leak native memory.
- Messages forwarded to Explorer (tray, AppBar, Win+number hotkeys, Show
  desktop) and IME control messages to the foreground application wait at most
  500 ms, so a hung Explorer or application can no longer freeze the taskbar.
- Action Center, the clock calendar and other Explorer flyouts, and the Start
  launcher, work again after an Explorer restart without restarting UltraWinBar;
  their COM references are released instead of leaked.
- Window tracking reinstalls its cloak and move hooks after an Explorer restart,
  rolls back a partial start, validates the minimize-rectangle pointer and
  bounds the overlay-icon description sent by other processes, and a failed
  icon load no longer stops the window's icon from ever updating.
- WinEvent hooks disposed from another thread are removed on their own thread;
  an Explorer COM object can no longer be created twice when activation
  re-enters; exceptions in hidden message windows are logged instead of
  silently dropped.
- The log never throws into its caller when a new log file cannot be created,
  and keeps at most 500 messages while no log is attached.
- Tray icon settings (Always show, Hide, Remove) are keyed by the icon's GUID or
  executable and ID, not its tooltip, so they survive tooltip changes such as
  unread counters.
- Shutdown on sign-out runs once and signals the work-area watchdog; settings
  are saved before an automatic restart after a crash.
- The Start menu opens beside the panels, not over them, also when opened with
  the Windows key or when its visibility notification arrives before it takes
  focus. With Open Shell, about half of the Start button presses left the menu
  where Open Shell put it: the menu takes focus as a hidden placeholder and is
  shown ~50 ms later, and a poll in that gap dropped the placement as "closed".
- The Open Shell menu no longer flashes at Open Shell's own position for
  20–50 ms before moving beside the panels: it stays transparent (layered alpha;
  cloaking another process's window is denied) until it is placed, then fades
  in over 150 ms, or at once with client-area animations off. A menu not placed
  within 2 s is shown where it is; its style is restored on close and on exit.
- Open Shell's avatar no longer flashes beside the menu's first position before
  jumping into place: moving it waits for Explorer's thread, so a hook thread
  keeps it transparent (per-pixel layered alpha, which takes effect at once)
  until it is placed, then it fades in with the menu. It is shown at the latest
  when the menu is fully opaque, and made opaque again if the menu closes first.
  An event for a moved or newly shown avatar is never taken for the echo of our
  own alpha change, which let it show for a frame when Open Shell was slow.
- The first Start press after launch no longer shows the menu and avatar where
  Open Shell puts them: the placement path is warmed up (JIT, P/Invoke stubs,
  hook thread) once when the app is idle after start.

## 2.2.0 (2026-09-28)

First published release of the UltraWinBar fork. The Nerdbank.GitVersioning
prefix moves from `2.1` to `2.2` (2.1 was never released); the version height is
offset so this release is exactly 2.2.0.

### Added

- The clock context menu offers Restart UltraWinBar next to Exit; like Exit,
  it follows the "show Exit menu item" setting.
- Tray-icon activation now records the owning process before its callback is
  sent. A double-click pre-moves its sole foreign window; other clicks wait for
  the exact window activation before attempting a move.

### Fixed

- Prune task-order entries and per-window taskbar assignments whose window no
  longer exists; the saved order previously grew with every window ever opened.
  Entries of windows that still exist but are not yet listed are kept.
- Stop an endless task-list rebuild loop when a pinned application's primary
  window could not be verified through its process; liveness now comes from the
  current window list.
- Stop rebuilding every task list and re-checking all-desktop application pins
  on each window activation; pins are restored only when windows are added or
  the task list is reset.
- Skip windows that can never become task buttons before creating task objects
  during desktop-switch recovery, and refresh panels only when something changed.
- Serialize settings once per burst of changes on the UI thread instead of on
  every property change; shutdown still saves the latest state synchronously.
- Keep logs for 7 days instead of 7 hours and roll to a new file at 20 MB.
- Run the desktop-activation mouse hook on its own thread so a busy UI thread no
  longer delays mouse input system-wide or lets Windows silently remove the hook.
- Install the Start menu placement event hook only while a Start menu is being
  positioned instead of listening to all system object events permanently.
- Update the clock at the next second or minute boundary instead of every 200 ms.
- Re-attach the virtual-desktop registry watch when Explorer creates or recreates
  its desktop key, so desktop switches keep being detected after an Explorer restart.
- Ignore repeated Loaded events on task buttons to avoid duplicate subscriptions.
- Recreate the virtual-desktop manager and Start menu visibility COM objects
  after an Explorer restart or a disconnected-proxy error instead of querying
  dead proxies for the rest of the session.
- Detect Start menu visibility from shell events; the fallback poll runs every
  1.5 s and speeds up to 100 ms only while a Start menu is open or positioned.
- Filter only the added, removed or changed window instead of refreshing every
  task list, skip monitor changes that cannot affect a panel, and share desktop
  lookups between panels. Windows moved between desktops are re-checked from
  their cloak events.
- Drop expired missed tray notifications and keep at most 10 per icon; remove
  tray icons whose owner window no longer exists when the tray is hovered.
- Post desktop-activation cancels from the mouse hook only while something is
  armed, and log from the UI thread instead of the hook thread.
- Recompute the saved original work area and restart its crash watchdog when
  the primary monitor's bounds change, so exit and crash restore the right size.
- Keep retrying the virtual-desktop manager and Start visibility objects with
  backoff when Explorer has not registered them yet after a restart, instead of
  losing desktop filtering or Start detection until UltraWinBar restarts.
- Re-read the current desktop before re-checking windows from cloak events.
- Release closed panels: tray and task views are detached from ManagedShell's
  app-lifetime collections, which kept every panel ever created alive (about
  1 MB each) across theme, language, edge and display changes. The hidden
  Quick Launch toolbar no longer keeps a folder watcher per panel.
- Reopen panels once per settings change instead of up to four times.
- Recreate shell COM objects before reopening panels after an Explorer
  restart, and re-filter panels when the desktop manager becomes available.
- Cache window desktops per dispatcher pass for task-assignment filtering and
  skip all-desktop pin restores for windows of apps that were not remembered.
- Stop leaking inverted tray-icon effects (and the panels holding them) through
  a static shader event, and guard repeated Loaded events on the Start button
  and task thumbnails.
- Tick clocks only in the panel that shows them, read the keyboard layout
  without per-poll allocations or exceptions, reload the System theme once per
  color broadcast, and leave balloon promotion to the panel hosting the tray.
- Coalesce work-area recovery events and pause recovery after repeated resets
  to prevent conflicts from continuously resizing desktop windows. Pauses grow
  from 1 to 5 to 15 minutes while a conflict persists, a single probe runs after
  each pause, and reopening the panels restores full recovery.
- Retry a deferred work-area recovery when its cooldown ends instead of waiting
  for the next window activation.
- Stop correcting window bounds on every location-change event, and suppress
  repeated placement attempts when applications refuse the requested bounds.
  Windows moved programmatically or with Win+Arrow are now constrained on
  their next show or activation rather than immediately.
- Report work-area notification enumeration failures only when enumeration fails.
- Restore the reserved screen work area if Windows or Explorer clears it while
  the panels remain open, so maximized windows continue to avoid the panels.
- Keep maximized window geometry under Windows control instead of resizing it
  on every activation; restored windows still move clear of panel edges.
- Desktop executable shortcuts with one matching window on another desktop can
  bring that window forward before Explorer launches the shortcut, avoiding a
  visible round trip between desktops. Ambiguous shortcuts keep the existing
  activation behavior.
- The experimental desktop guard attempts a bounded return when Windows
  switches after a move or before the target window's activation event. Later
  user input cancels the return.

## 2.0 development (unreleased)

The Nerdbank.GitVersioning prefix is raised from `1.23` to `2.0`. No 2.0 release
was published.

### Added

- Taskbar context-menu shortcuts to Windows Settings and Computer Management,
  plus a submenu for This PC and individual drives in File Explorer.
- An opt-in experimental guard that tries to move an already open window to the
  current virtual desktop after a desktop-icon double-click or pinned-launcher
  activation. Diagnostic logs record when Windows switches desktops first.

### Changed

- Exit is available only from the clock's right-click menu.
- The first Taskbar settings page is split into five localized sub-tabs.
- Vertical-panel clock bottom spacing is set to 22.5 physical pixels and scaled
  for DPI and taskbar scale.

### Fixed

- Settings writes are coalesced and atomic; unreadable settings are preserved
  before defaults replace them.
- Work-area notifications no longer block the UI while waiting on other windows.
- Window-specific taskbar assignments use window-lifetime IDs, and saved order
  validation checks process start time to avoid reused PID/HWNDs.
- Low-level mouse hook callbacks follow the Win32 contract and recover when a
  drag hook cannot be installed.
- Incomplete desktop-pin imports remain retryable instead of saving partial data.
- Task lists now refresh from shell window changes without WPF live shaping,
  preventing collection changes during its deferred filtering pass.

### Previously documented changes

The following sections retain the history recorded for the preceding 1.23.x
development work.

### Committed on 2026-09-22

#### Added

- Up to four taskbars per enabled screen, one per edge, with configurable edge
  priority through **Stretch**. Applications can be assigned to a panel by dragging;
  Ctrl+drag assigns only the selected window, with commands to reset assignments.
  ([f5609b6](https://github.com/PHPCraftdream/UltraWinBar/commit/f5609b6))
- **Make main** selects the default panel for unassigned windows. The clock,
  notification area, Start button, and language indicator have independent panel
  locations; clock, tray, and language blocks can be dragged between panels.
  ([7829ebf](https://github.com/PHPCraftdream/UltraWinBar/commit/7829ebf),
  [0b960b3](https://github.com/PHPCraftdream/UltraWinBar/commit/0b960b3))
- Independent row counts and widths for each edge, retaining the previous global
  size as a fallback for edges without an override.
  ([e33ac5a](https://github.com/PHPCraftdream/UltraWinBar/commit/e33ac5a))
- Saved task-button order per panel and drag-to-reorder within the same panel.
  ([3041e71](https://github.com/PHPCraftdream/UltraWinBar/commit/3041e71),
  [d667152](https://github.com/PHPCraftdream/UltraWinBar/commit/d667152))
- Separate Quick Launch shortcut sets per panel, context-menu reassignment, and
  cross-panel dragging. The initial Ctrl requirement was removed: plain dragging
  both reorders shortcuts and moves them between panels. Reordering one panel
  preserves the other panels' saved entries. Quick Launch is hidden by the later
  pinned-launcher changes documented below.
  ([501ce09](https://github.com/PHPCraftdream/UltraWinBar/commit/501ce09),
  [7a43a19](https://github.com/PHPCraftdream/UltraWinBar/commit/7a43a19),
  [0cce5c8](https://github.com/PHPCraftdream/UltraWinBar/commit/0cce5c8))

#### Changed

- The Start button became icon-only, without the reserved space for its old label.
  ([07bdd46](https://github.com/PHPCraftdream/UltraWinBar/commit/07bdd46))
- Flattened the Zune theme's vertical-panel background and task-button side borders
  and background to remove glare and visible stripes. The later all-theme cleanup
  is listed in the follow-up changes section.
  ([52f333d](https://github.com/PHPCraftdream/UltraWinBar/commit/52f333d),
  [9e13577](https://github.com/PHPCraftdream/UltraWinBar/commit/9e13577),
  [96e20f0](https://github.com/PHPCraftdream/UltraWinBar/commit/96e20f0))
- Improved Start-menu placement around the invoking panel: cached the modern
  launcher per monitor, added foreground-event positioning for modern Start and
  Open-Shell, retried position drift, and added related user-picture positioning.
  Further Open-Shell corrections are listed in the follow-up changes below.
  ([1c3d0d4](https://github.com/PHPCraftdream/UltraWinBar/commit/1c3d0d4),
  [8a3c860](https://github.com/PHPCraftdream/UltraWinBar/commit/8a3c860),
  [a1e868f](https://github.com/PHPCraftdream/UltraWinBar/commit/a1e868f),
  [43cdd40](https://github.com/PHPCraftdream/UltraWinBar/commit/43cdd40),
  [834f0b4](https://github.com/PHPCraftdream/UltraWinBar/commit/834f0b4))

#### Fixed

- Panel freezes caused by the task-order save/refresh feedback loop: unchanged
  orders no longer trigger another write, and reordering refreshes its owning
  panel instead of broadcasting a recursive refresh to all panels.
  ([2cb28ec](https://github.com/PHPCraftdream/UltraWinBar/commit/2cb28ec),
  [2f11dc0](https://github.com/PHPCraftdream/UltraWinBar/commit/2f11dc0))
- Blank task lists and interrupted assignment updates during collection changes
  or transient window-enumeration failures.
  ([0b8c591](https://github.com/PHPCraftdream/UltraWinBar/commit/0b8c591),
  [ea94587](https://github.com/PHPCraftdream/UltraWinBar/commit/ea94587))
- Reordering that did not immediately update the visible list, competing drag
  handlers, and dragging being unavailable with only one panel enabled. The final
  implementation uses mouse capture, explicit completion, cursor feedback, and
  insertion markers based on actual screen bounds, including RTL layouts.
  ([f0733c5](https://github.com/PHPCraftdream/UltraWinBar/commit/f0733c5),
  [1de7776](https://github.com/PHPCraftdream/UltraWinBar/commit/1de7776),
  [3cd02f0](https://github.com/PHPCraftdream/UltraWinBar/commit/3cd02f0))
- Changing window titles no longer directly invalidate order keys. This commit
  introduced executable/ordinal keys; their remaining restart ambiguity is
  addressed by the window-lifetime identities in the follow-up changes below.
  ([5b07df5](https://github.com/PHPCraftdream/UltraWinBar/commit/5b07df5))
- Task-list and tray-toggle orientation now follows the hosting panel rather
  than the primary panel. Language-indicator dragging receives the mouse-down
  event before its inner button consumes it.
  ([5ac6b28](https://github.com/PHPCraftdream/UltraWinBar/commit/5ac6b28),
  [729b4ad](https://github.com/PHPCraftdream/UltraWinBar/commit/729b4ad))
- Notification icons with invalid owner windows are hidden, and icons without a
  bitmap wait for it to arrive instead of briefly displaying an empty image.
  ([7e7063e](https://github.com/PHPCraftdream/UltraWinBar/commit/7e7063e),
  [7dda9e2](https://github.com/PHPCraftdream/UltraWinBar/commit/7dda9e2))
- Clock actions run on a completed click rather than mouse-down, so dragging does
  not open the calendar. Clicking an already-open notification center no longer
  immediately reopens it after dismissal.
  ([9921de2](https://github.com/PHPCraftdream/UltraWinBar/commit/9921de2),
  [74282f7](https://github.com/PHPCraftdream/UltraWinBar/commit/74282f7))
- Computed primary-panel size properties are excluded from JSON serialization;
  per-edge size records remain the persisted source of truth.
  ([eca0c84](https://github.com/PHPCraftdream/UltraWinBar/commit/eca0c84))

### Previously documented follow-up changes

#### Added

- Per-panel pinned applications: closed applications retain a launcher, while
  running applications show a titled task button for each window.
- Virtual-desktop-specific panel assignments, pins, and task ordering.
- Task-menu commands to show an application's windows on all desktops, move one
  window to another desktop, or move all windows of an application.
- Persistent all-desktop pin preferences, restored at startup and when windows
  are activated, added, or desktops change. Existing system pins are imported once.
- Configurable log masks with wildcard inclusion and exclusion rules.
- Drag cursors and destination markers for moving the clock, notification area,
  and language indicator between panels.
- Optional Hebrew date above the ordinary date, with `Day-1` through `Day-6` and
  `Shabbat` weekday labels. Calendar arithmetic uses .NET `HebrewCalendar`;
  month labels use bundled Unicode CLDR data for the interface languages, with
  a Belarusian Cyrillic override.
- Regression checks for desktop rules, settings persistence, task identities,
  calendar formatting, localization coverage, and off-screen theme/layout rendering.

#### Changed

- Unified UltraWinBar naming across source files, namespaces, binaries,
  installer, settings storage, startup registration, localization, and documentation.
- New flat, two-tone application icon with nine sizes from 16 to 256 pixels,
  shared by the executable, installer, and settings window.
- Update checks and download links target the project's GitHub releases.
- All 19 built-in theme resource files use solid fills instead of gradients.
  Shared flat templates replace layered bevels and glossy taskbar controls,
  with distinct hover, active, pressed, and flashing states.
- Modern vector Start icons with contrast appropriate to each theme.
- The Start button and closed pinned launchers are clickable across the full
  width of vertical panels; launcher icons remain centered. Horizontal launchers
  remain compact.
- Task-button separation is 2 DIPs. Vertical-panel clock spacing is 22.5
  physical pixels, adjusted for DPI and taskbar scale, without changing
  horizontal panels.
- Larger task, clock, and language-indicator text; larger task and notification
  icons, with notification-icon padding and a rounded notification-area outline.
- Quick Launch is hidden in favor of per-panel pinned launchers. Inactive taskbar
  drag grips are hidden; the language-indicator grip remains available.
- Settings use a resizable, bounded window, independently scrollable tabs, aligned
  form columns, and system UI fonts independent of the selected taskbar theme.
- Expanded Belarusian Cyrillic translations, including new desktop and calendar
  controls, with checks for missing translations and unintended Latin text.

#### Fixed

- Task order no longer depends on the discovery ordinal of same-application
  windows. Window/process-lifetime identities preserve positions when UltraWinBar
  restarts while those windows remain open. Pinned applications also remember
  which window occupies their launcher position.
- Windows omitted during startup because they were on another virtual desktop
  are recovered when they become available, instead of requiring individual activation.
- Multiple windows of a pinned application retain separate task buttons rather
  than disappearing into a single launcher.
- Centering and icon clipping in pinned task buttons.
- Open-Shell requests now target Explorer's taskbar window rather than the
  application's same-class helper window. Removed the delayed second request.
- Start-menu placement corrections respond to window events, skip the temporary
  placeholder window, and avoid stealing focus. Menu bounds and the separate
  user-picture position are adjusted for the initiating panel.
- Improved single-monitor startup layout and work-area reservation, including
  top/left edge priority, window placement guards, and work-area restoration on exit.
- Settings no longer retain obsolete theme dictionaries after switching themes.
  Removed the manual screen-width measurement that could lay out settings fields
  outside the actual window.
- Enabling the Hebrew date no longer changes the user's time or ordinary-date
  format. Month names follow the selected interface language independently.

### Current limitations

- System-wide desktop pinning and window-move commands currently support Windows
  10 builds 19041–19045. They are disabled on unsupported builds.
- Exact task-order restoration applies to windows that remain open during an
  UltraWinBar restart. It does not recreate application windows after a reboot;
  older ordinal-based order records can only be migrated on a best-effort basis.
- The Hebrew date changes at local midnight, not sunset. No location-based
  astronomical calculation is performed.
- Update checks require a published GitHub release; making the repository public
  alone does not publish an update.
