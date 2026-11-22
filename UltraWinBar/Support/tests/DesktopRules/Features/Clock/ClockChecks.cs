using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using ManagedShell.AppBar;
using UltraWinBar.Controls;
using UltraWinBar.Utilities;

internal static class ClockChecks
{
    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        var clockType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Controls.Clock");
        var canShowSecondsMethod = clockType.GetMethod("ClockCanShowSeconds", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var nextTickDelayMethod = clockType.GetMethod("GetNextTickDelay", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        bool CanShowSeconds(bool showSeconds, bool overrideFormat, string format) =>
            (bool)canShowSecondsMethod.Invoke(null, new object[] { showSeconds, overrideFormat, format });
        TimeSpan NextTickDelay(DateTime now, bool secondsVisible) =>
            (TimeSpan)nextTickDelayMethod.Invoke(null, new object[] { now, secondsVisible });
        if (!CanShowSeconds(true, false, null) ||
            CanShowSeconds(false, false, null) ||
            !CanShowSeconds(false, true, "h:mm:ss tt") ||
            CanShowSeconds(false, true, "h:mm tt") ||
            CanShowSeconds(false, false, "h:mm:ss tt"))
            throw new Exception("Clock seconds-visibility rule mismatch.");
        Console.WriteLine("PASS: clock format can show seconds only via ShowClockSeconds or an override format containing 's'.");
        DateTime midSecond = new DateTime(2026, 1, 1, 12, 0, 30, 500);
        TimeSpan delayWithSeconds = NextTickDelay(midSecond, true);
        TimeSpan delayWithoutSeconds = NextTickDelay(midSecond, false);
        if (delayWithSeconds <= TimeSpan.FromMilliseconds(500) || delayWithSeconds >= TimeSpan.FromMilliseconds(600))
            throw new Exception($"Seconds-visible tick should re-arm for the next second, got {delayWithSeconds}.");
        if (delayWithoutSeconds <= TimeSpan.FromMilliseconds(29500) || delayWithoutSeconds >= TimeSpan.FromMilliseconds(29600))
            throw new Exception($"Seconds-hidden tick should re-arm for the next minute, got {delayWithoutSeconds}.");
        DateTime nearRollover = new DateTime(2026, 1, 1, 12, 0, 59, 990);
        TimeSpan delayNearRollover = NextTickDelay(nearRollover, true);
        if (delayNearRollover <= TimeSpan.FromMilliseconds(10) || delayNearRollover >= TimeSpan.FromMilliseconds(60))
            throw new Exception($"Tick just before a second boundary should fire ~10ms later plus a small margin, got {delayNearRollover}.");
        DateTime onBoundary = new DateTime(2026, 1, 1, 12, 1, 0, 0);
        TimeSpan delayOnBoundarySeconds = NextTickDelay(onBoundary, true);
        TimeSpan delayOnBoundaryMinutes = NextTickDelay(onBoundary, false);
        if (delayOnBoundarySeconds <= TimeSpan.FromSeconds(1) || delayOnBoundarySeconds >= TimeSpan.FromMilliseconds(1100))
            throw new Exception($"Tick exactly on a second boundary should re-arm a full second later, got {delayOnBoundarySeconds}.");
        if (delayOnBoundaryMinutes <= TimeSpan.FromMinutes(1) || delayOnBoundaryMinutes >= TimeSpan.FromMinutes(1) + TimeSpan.FromMilliseconds(100))
            throw new Exception($"Tick exactly on a minute boundary should re-arm a full minute later, got {delayOnBoundaryMinutes}.");
        Console.WriteLine("PASS: clock re-arms for the next second boundary when seconds are visible and the next minute boundary otherwise, landing just after it with a small margin.");

        var virtualDesktopContextType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.VirtualDesktopContext");
        var buildSessionPathMethod = virtualDesktopContextType.GetMethod("BuildSessionPath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        string builtSessionPath = (string)buildSessionPathMethod.Invoke(null, new object[] { 7 });
        if (builtSessionPath != @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\7\VirtualDesktops")
            throw new Exception("Session desktop registry path formula changed: " + builtSessionPath);
        var treeWatchType = virtualDesktopContextType.GetNestedType("RegistryTreeWatch", System.Reflection.BindingFlags.NonPublic);
        var parentPathMethod = treeWatchType.GetMethod("ParentPath", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        var ancestorChainFromMethod = treeWatchType.GetMethod("AncestorChainFrom", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
        string ParentOf(string path) => (string)parentPathMethod.Invoke(null, new object[] { path });
        string[] AncestorsOf(string path) => ((System.Collections.IEnumerable)ancestorChainFromMethod.Invoke(null, new object[] { path })).Cast<string>().ToArray();
        if (ParentOf(builtSessionPath) != @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\7" ||
            ParentOf("NoBackslashHere") != null)
            throw new Exception("Registry watch parent-path computation is wrong.");
        var expectedAncestorChain = new[]
        {
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo\7",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer\SessionInfo",
            @"Software\Microsoft\Windows\CurrentVersion\Explorer",
            @"Software\Microsoft\Windows\CurrentVersion",
            @"Software\Microsoft\Windows",
            @"Software\Microsoft",
            @"Software",
        };
        if (!AncestorsOf(builtSessionPath).SequenceEqual(expectedAncestorChain))
            throw new Exception("Registry watch ancestor chain is wrong: " + string.Join(" | ", AncestorsOf(builtSessionPath)));
        Console.WriteLine("PASS: virtual-desktop session registry path is computed once from a cached session ID, and its watch climbs to the nearest existing ancestor so a missing-at-start or Explorer-recreated desktop key is picked back up.");

        string taskButtonSource = System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot.FullName,
            "UltraWinBar", "UI", "Tasks", "Buttons", "TaskButton.xaml.cs"));
        int taskButtonLoadedStart = taskButtonSource.IndexOf("private void TaskButton_OnLoaded");
        int taskButtonLoadedEnd = taskButtonSource.IndexOf("private void Window_GetButtonRect", taskButtonLoadedStart);
        if (taskButtonLoadedStart < 0 || taskButtonLoadedEnd < 0)
            throw new Exception("Could not locate TaskButton_OnLoaded to check its idempotency guard.");
        string taskButtonLoadedBody = taskButtonSource.Substring(taskButtonLoadedStart, taskButtonLoadedEnd - taskButtonLoadedStart);
        int isLoadedGuardIndex = taskButtonLoadedBody.IndexOf("if (_isLoaded)");
        int settingsSubscribeIndex = taskButtonLoadedBody.IndexOf("WeakSubscriptions.SubscribeSettings(Settings_PropertyChanged);");
        int dragHandlerCreateIndex = taskButtonLoadedBody.IndexOf("dragHandler = new DelayedActivationHandler");
        int animateCallIndex = taskButtonLoadedBody.IndexOf("Animate();");
        if (isLoadedGuardIndex < 0 || settingsSubscribeIndex < 0 || dragHandlerCreateIndex < 0 || animateCallIndex < 0 ||
            isLoadedGuardIndex > settingsSubscribeIndex || isLoadedGuardIndex > dragHandlerCreateIndex || isLoadedGuardIndex > animateCallIndex)
            throw new Exception("TaskButton_OnLoaded must return early when already loaded, before re-subscribing, replacing dragHandler, or replaying the slide-in animation.");
        if (System.Text.RegularExpressions.Regex.Matches(taskButtonLoadedBody, @"_isLoaded\s*=\s*true").Count != 1)
            throw new Exception("TaskButton_OnLoaded should set _isLoaded exactly once, after the idempotency guard.");
        Console.WriteLine("PASS: a repeated TaskButton Loaded without an intervening Unloaded is a no-op and does not re-subscribe, replace dragHandler, or replay the slide-in animation.");

        // R9-M / К20: typed access instead of a source-text scan between two method names
        // (StartWatch/OnForeground) - a rename breaks this at compile time, and reordering the
        // methods can no longer silently pick up the wrong body.
        if (InputLanguage.EventSystemForeground != 3)
            throw new Exception("Input language foreground hook must watch EVENT_SYSTEM_FOREGROUND (3).");
        if (InputLanguage.LayoutPollIdleMs < 500 || InputLanguage.LayoutPollIdleMs > 1000)
            throw new Exception($"Input language idle poll must stay a cheap 500-1000ms fallback, was {InputLanguage.LayoutPollIdleMs}ms.");

        // Constructing a Control requires an STA thread (FrameworkElement's InputManager asserts
        // it); this harness's main thread is MTA, so run the construction and the check on one.
        Exception inputLanguageFailure = null;
        var inputLanguageThread = new System.Threading.Thread(() =>
        {
            try
            {
                var inputLanguage = new InputLanguage();
                inputLanguage.StartWatch();
                if (inputLanguage._foregroundSubscription == null || !inputLanguage.layoutWatch.IsEnabled)
                    throw new Exception("StartWatch must install the foreground WinEventHook and start the poll timer.");
                inputLanguage.StopWatch();
                if (inputLanguage._foregroundSubscription != null || inputLanguage.layoutWatch.IsEnabled)
                    throw new Exception("StopWatch must dispose the foreground WinEventHook and stop the poll timer.");
            }
            catch (Exception error)
            {
                inputLanguageFailure = error;
            }
        });
        inputLanguageThread.SetApartmentState(System.Threading.ApartmentState.STA);
        inputLanguageThread.Start();
        inputLanguageThread.Join();
        if (inputLanguageFailure != null) throw new Exception("Input language foreground hook lifecycle check failed.", inputLanguageFailure);
        Console.WriteLine("PASS: input language layout watch reacts to EVENT_SYSTEM_FOREGROUND, falls back to a 500-1000ms idle poll, and installs/disposes the hook alongside the poll timer.");

        var japaneseImeType = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Controls.JapaneseIme")
            ?? throw new Exception("Japanese IME indicator is missing.");
        var getRegKanaMd = japaneseImeType.GetMethod("GetRegKanaMd", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var kanaMdCachedField = japaneseImeType.GetField("_kanaMdCached", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var kanaMdValueField = japaneseImeType.GetField("_kanaMdValue", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        var kanaMdWatchField = japaneseImeType.GetField("_kanaMdWatch", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
        if (getRegKanaMd == null || kanaMdCachedField == null || kanaMdValueField == null || kanaMdWatchField == null)
            throw new Exception("Japanese IME kanaMd cache fields are missing.");
        // Bypasses the UserControl constructor (its XAML binds Settings.Instance, forbidden in tests):
        // GetRegKanaMd/the cache fields under test touch nothing InitializeComponent would have set up.
        object imeInstance = System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(japaneseImeType);
        foreach (bool cachedValue in new[] { true, false })
        {
            kanaMdCachedField.SetValue(imeInstance, true);
            kanaMdValueField.SetValue(imeInstance, cachedValue);
            if ((bool)getRegKanaMd.Invoke(imeInstance, null) != cachedValue)
                throw new Exception("A cached kanaMd value must be reused as-is instead of being re-read from the registry.");
        }
        kanaMdCachedField.SetValue(imeInstance, false);
        kanaMdWatchField.SetValue(imeInstance, null);
        bool liveResult = (bool)getRegKanaMd.Invoke(imeInstance, null);
        bool cachedAfterLiveRead = (bool)kanaMdCachedField.GetValue(imeInstance);
        bool watchArmedAfterLiveRead = kanaMdWatchField.GetValue(imeInstance) != null;
        (kanaMdWatchField.GetValue(imeInstance) as IDisposable)?.Dispose();
        if (cachedAfterLiveRead != watchArmedAfterLiveRead)
            throw new Exception("A live kanaMd read must cache the value and arm the registry watch together, or do neither.");
        Console.WriteLine($"PASS: a cached kanaMd is reused without a registry read; an uncached read (found={cachedAfterLiveRead}, value={liveResult}) arms its registry watch exactly when it caches.");

        // Running lights around the Hebrew date while the pointer is over it: evenly spaced and seamless around the
        // frame, one lamp in four lit and moving clockwise, and cheap - nothing runs unless the lights are on, shown
        // and animations are allowed.
        var lamps = MarqueeFrame.LampPositions(120, 18);
        double frameWidth = 120 - 2 * MarqueeFrame.Inset, frameHeight = 18 - 2 * MarqueeFrame.Inset, lampStep = 2 * (frameWidth + frameHeight) / lamps.Length;
        if (lamps.Length < 8 || lamps.Length % MarqueeFrame.GroupSize != 0 || Math.Abs(lampStep - MarqueeFrame.Spacing) > MarqueeFrame.Spacing * 0.15)
            throw new Exception($"The lights must keep their spacing and a lamp count that fits the light pattern, got {lamps.Length} lamps {lampStep:0.00} apart.");
        if (Math.Abs(lamps[0].X - MarqueeFrame.Inset) > 1e-9 || Math.Abs(lamps[0].Y - MarqueeFrame.Inset) > 1e-9 ||
            Math.Abs(lamps[^1].X - MarqueeFrame.Inset) > 1e-9 || Math.Abs(lamps[^1].Y - MarqueeFrame.Inset - lampStep) > 1e-9)
            throw new Exception("The lights must start at the top left corner and the last lamp must sit one step before it, so the frame has no seam.");
        if (lamps.Distinct().Count() != lamps.Length || lamps.Any(lamp =>
                lamp.X < MarqueeFrame.Inset - 1e-9 || lamp.X > 120 - MarqueeFrame.Inset + 1e-9 || lamp.Y < MarqueeFrame.Inset - 1e-9 || lamp.Y > 18 - MarqueeFrame.Inset + 1e-9 ||
                Math.Min(Math.Min(lamp.X - MarqueeFrame.Inset, 120 - MarqueeFrame.Inset - lamp.X), Math.Min(lamp.Y - MarqueeFrame.Inset, 18 - MarqueeFrame.Inset - lamp.Y)) > 1e-9))
            throw new Exception("Every lamp must be a different point on the edge of the frame.");
        if (MarqueeFrame.LampPositions(8, 8).Length != 8 || MarqueeFrame.LampPositions(2, 50).Length != 0 ||
            MarqueeFrame.LampPositions(0, 0).Length != 0 || MarqueeFrame.LampPositions(double.NaN, 10).Length != 0)
            throw new Exception("A tiny frame keeps the minimum of eight lamps; one too small for a lamp, or without a size, has none.");
        for (int phase = 0; phase < MarqueeFrame.GroupSize; phase++)
        {
            int lit = 0, glowing = 0;
            for (int lamp = 0; lamp < 8; lamp++)
            {
                int level = MarqueeFrame.Level(lamp, phase);
                if (level == 2) lit++;
                if (level == 1) glowing++;
                if (lamp > 0 && MarqueeFrame.Level(lamp, phase + 1) != MarqueeFrame.Level(lamp - 1, phase))
                    throw new Exception("A step must move the whole light pattern one lamp on, clockwise.");
            }
            if (lit != 2 || glowing != 2 || MarqueeFrame.Level(phase, phase) != 2 || MarqueeFrame.Level(phase - 1, phase) != 1)
                throw new Exception($"One lamp in four must be lit and the one behind it glowing (phase {phase}: {lit} lit, {glowing} glowing).");
        }
        for (int mask = 0; mask < 8; mask++)
            if (MarqueeFrame.ShouldAnimate((mask & 1) != 0, (mask & 2) != 0, (mask & 4) != 0) != (mask == 7))
                throw new Exception("The lights may animate only while they are on, shown, and Windows animations are allowed.");
        if (MarqueeFrame.StepInterval < TimeSpan.FromMilliseconds(80) || MarqueeFrame.StepInterval > TimeSpan.FromMilliseconds(250))
            throw new Exception("The lights must step about ten times a second: smooth enough, and cheap.");
        string marqueeSource = System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot.FullName, "UltraWinBar", "UI", "Clock", "MarqueeFrame.cs"));
        foreach (string forbidden in new[] { "Storyboard", "DoubleAnimation", "BeginAnimation", "CompositionTarget" })
            if (marqueeSource.Contains(forbidden))
                throw new Exception($"The lights must not use WPF's per-frame animation clock ({forbidden}): they step on one timer.");
        if (System.Text.RegularExpressions.Regex.Matches(marqueeSource, @"new DispatcherTimer\(").Count != 1 || !marqueeSource.Contains("DispatcherPriority.Background"))
            throw new Exception("The lights must have exactly one background-priority timer, created when they first run.");
        string clockXaml = System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot.FullName, "UltraWinBar", "UI", "Clock", "Clock.xaml"));
        int hebrewStart = clockXaml.IndexOf("x:Key=\"HebrewClockTemplate\"", StringComparison.Ordinal);
        int hebrewEnd = clockXaml.IndexOf("</ControlTemplate>", hebrewStart, StringComparison.Ordinal);
        if (hebrewStart < 0 || hebrewEnd < 0) throw new Exception("The Hebrew date clock template is missing.");
        string hebrewTemplate = clockXaml.Substring(hebrewStart, hebrewEnd - hebrewStart);
        if (System.Text.RegularExpressions.Regex.Matches(clockXaml, "<local:MarqueeFrame").Count != 1 ||
            !System.Text.RegularExpressions.Regex.IsMatch(hebrewTemplate, "<local:MarqueeFrame[^>]*IsHitTestVisible=\"False\"") ||
            !System.Text.RegularExpressions.Regex.IsMatch(hebrewTemplate,
                @"<Trigger SourceName=""HebrewDateHost"" Property=""IsMouseOver"" Value=""True"">\s*<Setter TargetName=""HebrewDateLights"" Property=""IsRunning"" Value=""True""\s*/>"))
            throw new Exception("The lights belong to the Hebrew date template only, must not take the pointer, and run while the pointer is over the date.");
        Console.WriteLine("PASS: the Hebrew date lights are evenly spaced and seamless, one lamp in four lit and moving clockwise; they run on one slow timer only while hovered, shown and animations are allowed.");
    }
}
