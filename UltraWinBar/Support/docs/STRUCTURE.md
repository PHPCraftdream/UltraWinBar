# Repository layout

The source tree keeps at most seven entries in each directory and at most
1000 physical lines in each source file. Git metadata and generated build
directories are outside this limit.

- The repository root holds Git configuration, the license, README, version
  configuration, the shared build props, and the `UltraWinBar` project directory.
- `Shell` contains the application entry point, taskbar window, and dialogs.
- `UI` groups controls and converters by feature.
- `Core` groups settings, desktop integration, input, appearance, and panel logic.
- `Assets/Languages` uses seven numbered groups of at most seven translations.
  The group number is a source-tree partition, not a locale setting.
- `Assets/Themes` groups built-in themes; `Assets/Resources` groups images,
  calendar data, and compiled effects.
- `Support` contains developer scripts, release packaging, tests, and docs.
- `Support/vendor/ManagedShell` is the vendored ManagedShell source (upstream commit
  `b4521b2`, release 0.0.358) built as project references; its `README.md` lists every
  local fix. Vendored code keeps the upstream layout and is exempt from the
  seven-entry and 1000-line limits so it stays diffable against upstream.
- `Core/Infrastructure/Native` holds the only ways to touch native callbacks:
  `WinEventHook` (rooted, exception-safe WinEvent hooks), `CallbackGuard`, and
  `ShellComProxy` (Explorer-hosted COM objects recreated after Explorer restarts).
- `Core/Panels/Tasks` holds task order/assignment/diff helpers plus `TaskModel`:
  a pure, WPF-free function that computes every registered panel's ordered task
  list (and TaskOrder/TaskbarAssignments pruning, pin reconciliation) from one
  snapshot in a single pass, and `TaskModelHost`, the coordinator every
  `TaskList` panel registers with on load/unregisters on unload (weakly, so a
  missed unregister still lets a closed panel be collected); it runs one
  `TaskModel.Compute` pass per dispatcher tick for every live panel — including
  several panels sharing an edge under multi-monitor mode, chained in
  registration order — and applies the resulting Settings writes once, outside
  any panel's view update.
- Tests in `Support/tests/DesktopRules` are split into `Features` (task order,
  pins and assignments under `Features/Tasks`; `TaskModel` under `Features/TaskModel`),
  `Platform`, `Native` and `Logging` suites; `Program.cs` only orders them and runs
  Settings on a scratch file (`ULTRAWINBAR_SETTINGS_PATH`) so a test run can never
  touch the user's settings.

Build outputs live under `Support/BuildTools/artifacts`; test outputs use their
own ignored `bin` and `obj` directories. NuGet package versions and the base
project version remain unchanged.

The installed application still receives flat `Themes`, `Languages`, and
`Resources` directories. The project file maps grouped source assets to these
runtime paths. Built-in base dictionaries are compiled into the application.

Build and verify from the repository root:

```powershell
dotnet restore UltraWinBar/Support/BuildTools/UltraWinBar.sln -p:TargetFrameworks=net6.0-windows
dotnet build UltraWinBar/Support/BuildTools/UltraWinBar.sln -p:TargetFrameworks=net6.0-windows --no-restore
dotnet run --project UltraWinBar/Support/tests/DesktopRules/DesktopRules.csproj -- --themes
```

The test program checks the layout limits and loads the grouped resources.
