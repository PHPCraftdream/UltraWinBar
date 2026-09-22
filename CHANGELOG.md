# Changelog

## Unreleased

Reviewed against the 33 preceding commits dated 2026-09-22 in the local Git
history (Europe/Berlin, UTC+02:00), from `f5609b6` through `3cd02f0`.
Their changes and this follow-up change set are listed separately.
No release has been published.

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
  is listed in the working-tree section.
  ([52f333d](https://github.com/PHPCraftdream/UltraWinBar/commit/52f333d),
  [9e13577](https://github.com/PHPCraftdream/UltraWinBar/commit/9e13577),
  [96e20f0](https://github.com/PHPCraftdream/UltraWinBar/commit/96e20f0))
- Improved Start-menu placement around the invoking panel: cached the modern
  launcher per monitor, added foreground-event positioning for modern Start and
  Open-Shell, retried position drift, and added related user-picture positioning.
  Further Open-Shell corrections remain in the working-tree section below.
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
  addressed by the window-lifetime identities in the working tree below.
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

### Follow-up changes

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
- Task-button separation is 2 DIPs. The clock has 15 DIPs of bottom spacing on
  vertical panels, without increasing horizontal-panel height.
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
