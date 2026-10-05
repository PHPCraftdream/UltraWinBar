using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using UltraWinBar.Utilities;

// Task #per-desktop-task-lists: DesktopTaskLists keeps one live payload per virtual desktop so a
// desktop switch reuses existing lists instead of recreating them (the 100-250ms switch delay).
// These checks exercise the cache contract itself — get-or-create identity, switch round-trips,
// RemoveFromAll/PruneTo semantics, and the eviction cap (including the Guid.Empty regression, where
// a valid cached fallback-desktop entry was wrongly evicted because Guid.Empty doubled as a
// "none found" sentinel). The TaskButton visibility gating of Window_GetButtonRect is checked at
// the end: a collapsed (IsVisible == false) button must not overwrite the caller's rect.
internal static class DesktopTaskListsChecks
{
    private sealed class Payload { public int Tag; }

    private static int factories;

    private static DesktopTaskLists<Payload> NewCache(int maxCached = 16) =>
        new DesktopTaskLists<Payload> { MaxCached = maxCached };

    private static Payload Make() => new Payload { Tag = ++factories };

    internal static void Run(string[] args, System.IO.DirectoryInfo repositoryRoot)
    {
        RunGetOrCreateChecks();
        RunSwitchRoundTripChecks();
        RunRemoveFromAllChecks();
        RunPruneToChecks();
        RunCapChecks();
        RunOrderChecks();
        RunTaskButtonRectVisibilityChecks();
        RunDesktopIdCacheSurvivesSwitchChecks();
    }

    // Regression: the per-window desktop-id cache used to be cleared on every desktop switch, so the
    // first panel's filter re-asked Explorer (cross-process, stalled by the switch animation) for every
    // window: 27-143ms per switch. A switch must keep it; only a move/close forgets a window.
    private static void RunDesktopIdCacheSurvivesSwitchChecks()
    {
        var context = (VirtualDesktopContext)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(VirtualDesktopContext));
        var cache = new Dictionary<IntPtr, Guid>();
        typeof(VirtualDesktopContext).GetField("desktopForWindowCache", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(context, cache);
        var hwnd = (IntPtr)0x4242;
        var desktop = Guid.NewGuid();
        cache[hwnd] = desktop;
        typeof(VirtualDesktopContext).GetProperty("CurrentId").SetValue(context, Guid.NewGuid());

        context.RefreshCurrent(); // reads the real current desktop, differs from the random id: a switch
        if (!context.SwitchedWithin(1500)) throw new Exception("A desktop change must be visible to SwitchedWithin.");
        if (context.DesktopForWindowCached(hwnd) != desktop)
            throw new Exception("A desktop switch must not drop cached window desktop ids.");
        context.ForgetWindowDesktop(hwnd);
        if (cache.ContainsKey(hwnd)) throw new Exception("ForgetWindowDesktop must drop the window's cached desktop id.");
        Console.WriteLine("PASS: window desktop ids stay cached across desktop switches; a move/close forgets them.");
    }

    private static void RunGetOrCreateChecks()
    {
        factories = 0;
        var cache = NewCache();
        Payload first = cache.GetOrCreate(Guid.NewGuid(), Make);
        if (first == null) throw new Exception("GetOrCreate on an empty cache must invoke the factory.");
        if (factories != 1) throw new Exception("The first GetOrCreate must invoke the factory exactly once.");
        Payload second = cache.GetOrCreate(cache.Ids.Single(), Make);
        if (!ReferenceEquals(first, second))
            throw new Exception("A second GetOrCreate for the same desktop must return the SAME payload reference, not recreate it (the 100-250ms switch-delay regression).");
        if (factories != 1) throw new Exception("A second GetOrCreate for a cached desktop must not invoke the factory again.");
        Console.WriteLine("PASS: DesktopTaskLists.GetOrCreate invokes the factory once and returns the same reference on every later call for the same desktop.");
    }

    private static void RunSwitchRoundTripChecks()
    {
        factories = 0;
        var cache = NewCache();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        cache.SwitchTo(a, Make);
        cache.SwitchTo(b, Make);
        Payload bPayload = cache.Current;
        cache.SwitchTo(a, Make);
        Payload aAgain = cache.Current;
        cache.SwitchTo(b, Make);
        if (factories != 2)
            throw new Exception($"Switching between two desktops must create each payload exactly once (created {factories}).");
        if (!ReferenceEquals(cache.Current, bPayload))
            throw new Exception("SwitchTo away and back must return the originally created instance, not a new one.");
        if (!ReferenceEquals(cache.GetOrCreate(a, Make), aAgain))
            throw new Exception("GetOrCreate after a switch round-trip must still return the same instance.");
        Console.WriteLine("PASS: DesktopTaskLists.SwitchTo away and back reuses the same payload instance without invoking the factory again.");
    }

    private static void RunRemoveFromAllChecks()
    {
        factories = 0;
        var cache = NewCache();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid();
        cache.SwitchTo(a, Make);
        cache.SwitchTo(b, Make);
        var visited = new List<Payload>();

        // Returning false keeps every payload (this is what TaskList.RemoveWindowEverywhere does).
        cache.RemoveFromAll(p => { visited.Add(p); return false; });
        if (visited.Count != 2)
            throw new Exception($"RemoveFromAll must run on non-current payloads too (visited {visited.Count} of 2).");
        if (cache.Count != 2) throw new Exception("RemoveFromAll returning false must keep all cached payloads.");

        // Returning true drops the payload — including the current one (ResetDesktopLists behavior).
        cache.RemoveFromAll(p => true);
        if (cache.Count != 0)
            throw new Exception("RemoveFromAll returning true must drop every payload, including the current one.");
        Console.WriteLine("PASS: DesktopTaskLists.RemoveFromAll visits non-current payloads; returning true drops the payload (Count drops) and returning false keeps it.");
    }

    private static void RunPruneToChecks()
    {
        factories = 0;
        var cache = NewCache();
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), gone = Guid.NewGuid();
        cache.SwitchTo(a, Make);
        cache.SwitchTo(b, Make);
        cache.SwitchTo(gone, Make);

        // Current is 'gone' but not in the existing set: it must survive anyway.
        cache.PruneTo(new[] { a });
        if (cache.Count != 2)
            throw new Exception($"PruneTo must keep existing ids and the current id even when the current is not in the set (kept {cache.Count} of 3).");
        if (!cache.Ids.Contains(gone))
            throw new Exception("PruneTo must never drop the current desktop's payload.");
        if (cache.Ids.Contains(b))
            throw new Exception("PruneTo must drop a cached non-existing, non-current desktop.");

        // Once 'gone' is no longer current it goes on the next prune.
        cache.SwitchTo(a, Make);
        cache.PruneTo(new[] { a });
        if (cache.Count != 1 || !cache.Ids.Contains(a))
            throw new Exception("PruneTo must drop a former current desktop once it is neither existing nor current.");
        Console.WriteLine("PASS: DesktopTaskLists.PruneTo keeps existing ids plus the current id (even when the current is not in the set) and drops the rest.");
    }

    private static void RunCapChecks()
    {
        factories = 0;

        // Basic eviction: with a cap of 2, adding a third desktop evicts the oldest non-current one.
        var cache = NewCache(maxCached: 2);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        cache.SwitchTo(a, Make);
        cache.SwitchTo(b, Make);
        cache.SwitchTo(c, Make);
        if (cache.Count != 2) throw new Exception("Exceeding MaxCached must evict down to the cap.");
        if (cache.Ids.Contains(a)) throw new Exception("The oldest non-current desktop must be evicted first.");
        if (!cache.Ids.Contains(c)) throw new Exception("The current desktop must never be evicted.");
        if (!cache.Ids.Contains(b)) throw new Exception("A newer non-current desktop must survive eviction of an older one.");

        // Regression for the Guid.Empty sentinel bug: a legitimately cached fallback-desktop
        // entry (VirtualDesktopContext unavailable) must be evictable only by age, never dropped
        // merely because it is Guid.Empty — and other entries must still be evictable around it.
        var emptyCache = NewCache(maxCached: 3);
        Guid x = Guid.NewGuid(), y = Guid.NewGuid();
        emptyCache.SwitchTo(x, Make);
        emptyCache.SwitchTo(y, Make);
        Payload emptyPayload = emptyCache.SwitchTo(Guid.Empty, Make); // count 3 == cap, nothing evicted yet
        emptyCache.GetOrCreate(Guid.NewGuid(), Make); // count 4 -> evict x (oldest)
        emptyCache.GetOrCreate(Guid.NewGuid(), Make); // count 4 -> evict y
        if (emptyCache.Count != 3)
            throw new Exception("The cap must still be enforced when a Guid.Empty entry is cached.");
        if (!emptyCache.Ids.Contains(Guid.Empty))
            throw new Exception("A cached Guid.Empty desktop must not be evicted merely for being Guid.Empty; eviction must follow insertion order.");
        if (!emptyCache.Payloads.Contains(emptyPayload))
            throw new Exception("The Guid.Empty payload itself must still be cached after evicting older entries.");
        if (emptyCache.Ids.First() != Guid.Empty)
            throw new Exception("The eviction victims must be the entries older than the Guid.Empty fallback entry, in insertion order.");
        Console.WriteLine("PASS: DesktopTaskLists evicts oldest non-current entries beyond MaxCached, never the current one, and a cached Guid.Empty (fallback desktop) payload is only ever evicted by age, not for being Guid.Empty.");
    }

    private static void RunOrderChecks()
    {
        factories = 0;
        var cache = NewCache(maxCached: 10);
        Guid a = Guid.NewGuid(), b = Guid.NewGuid(), c = Guid.NewGuid();
        Payload pa = cache.SwitchTo(a, Make);
        Payload pb = cache.SwitchTo(b, Make);
        Payload pc = cache.SwitchTo(c, Make);
        if (!cache.Ids.SequenceEqual(new[] { a, b, c }))
            throw new Exception("Ids must reflect insertion order.");
        if (!cache.Payloads.SequenceEqual(new[] { pa, pb, pc }))
            throw new Exception("Payloads must follow the same order as Ids.");
        cache.PruneTo(new[] { a, c });
        if (!cache.Ids.SequenceEqual(new[] { a, c }))
            throw new Exception("Ids must reflect pruning without reordering survivors.");
        if (!cache.Payloads.SequenceEqual(new[] { pa, pc }))
            throw new Exception("Payloads must match Ids after pruning.");
        Console.WriteLine("PASS: DesktopTaskLists Ids/Payloads report insertion order and reflect eviction/pruning.");
    }

    // TaskButton.Window_GetButtonRect must return early when !IsVisible (the button lives on a
    // non-current desktop's hidden list), leaving the caller's rect untouched.
    private static void RunTaskButtonRectVisibilityChecks()
    {
        Exception failure = null;
        var thread = new Thread(() =>
        {
            try { RunTaskButtonRectVisibilityChecksCore(); }
            catch (Exception ex) { failure = ex; }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure != null) throw failure;
        Console.WriteLine("PASS: TaskButton.Window_GetButtonRect leaves the caller's rect untouched when the button is collapsed (IsVisible == false).");
    }

    // TaskButton is a WPF control, so it is constructed and poked on a dedicated STA thread (its
    // XAML/WinForms interop base classes need one); no running Dispatcher is required because we
    // never raise Loaded or measure it.
    private static void RunTaskButtonRectVisibilityChecksCore()
    {
        // TaskButton.xaml references application-level resources (theme + language dictionaries
        // and the menu text-rendering converter); merge the same ones TaskButton needs so it can
        // be constructed without a running shell, exactly like Shell/App.xaml does.
        if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
        var resources = System.Windows.Application.Current.Resources;
        if (!resources.Contains("menuTextRenderingModeConverter"))
        {
            resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UltraWinBar;component/Assets/Languages/Group01/English.xaml")
            });
            resources.MergedDictionaries.Add(new System.Windows.ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/UltraWinBar;component/Assets/Themes/Core/System.xaml")
            });
            resources["menuTextRenderingModeConverter"] = new UltraWinBar.Converters.BoolToTextRenderingModeConverter();
        }

        var button = new UltraWinBar.Controls.TaskButton();
        button.Visibility = System.Windows.Visibility.Collapsed;
        if (button.IsVisible) throw new Exception("Test setup: a collapsed TaskButton must report IsVisible == false.");

        MethodInfo method = typeof(UltraWinBar.Controls.TaskButton).GetMethod("Window_GetButtonRect", BindingFlags.NonPublic | BindingFlags.Instance);
        if (method == null) throw new Exception("Reflection: TaskButton.Window_GetButtonRect not found.");

        var rect = new ManagedShell.Interop.NativeMethods.ShortRect { Top = -111, Left = -222, Bottom = -333, Right = -444 };
        object[] argsBox = { rect };
        method.Invoke(button, argsBox);
        var after = (ManagedShell.Interop.NativeMethods.ShortRect)argsBox[0];
        if (after.Top != -111 || after.Left != -222 || after.Bottom != -333 || after.Right != -444)
            throw new Exception($"A collapsed (IsVisible == false) TaskButton must not overwrite the rect in Window_GetButtonRect (got T={after.Top} L={after.Left} B={after.Bottom} R={after.Right}).");
    }
}
