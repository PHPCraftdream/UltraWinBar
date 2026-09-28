using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Data;
using ManagedShell.Interop;
using ManagedShell.UWPInterop;
using UltraWinBar.Utilities;

internal static class UptimeChecks
{
    private const BindingFlags Private = BindingFlags.Static | BindingFlags.NonPublic;

    internal static void Run()
    {
        var assembly = typeof(TaskAssignmentManager).Assembly;
        Type Get(string name) => assembly.GetType(name) ?? throw new Exception($"{name} is missing.");

        int[] disconnected = { unchecked((int)0x80010108), unchecked((int)0x800706BA), unchecked((int)0x800401FD) };
        int[] other = { 0, 1, unchecked((int)0x80070057), unchecked((int)0x80004005) };
        var shellCom = Get("UltraWinBar.Utilities.ShellCom");
        var desktopHr = shellCom.GetMethod("IsDisconnected", Private, null, new[] { typeof(int) }, null)
            ?? throw new Exception("Explorer COM HRESULT disconnect check is missing.");
        var startError = shellCom.GetMethod("IsDisconnected", Private, null, new[] { typeof(Exception) }, null)
            ?? throw new Exception("Explorer COM exception disconnect check is missing.");
        foreach (int hr in disconnected)
            if (!(bool)desktopHr.Invoke(null, new object[] { hr }) || !(bool)startError.Invoke(null, new object[] { new COMException("x", hr) }))
                throw new Exception($"0x{hr:X8} must be treated as a severed Explorer proxy.");
        foreach (int hr in other)
            if ((bool)desktopHr.Invoke(null, new object[] { hr }) || (bool)startError.Invoke(null, new object[] { new COMException("x", hr) }))
                throw new Exception($"0x{hr:X8} must not recreate COM objects.");
        if (!(bool)startError.Invoke(null, new object[] { new InvalidComObjectException() }))
            throw new Exception("A released RCW must recreate the Start visibility helper.");
        if (Get("UltraWinBar.Utilities.ExplorerMonitor").GetEvent("ExplorerRestarted", BindingFlags.Static | BindingFlags.Public) == null)
            throw new Exception("Explorer restart signal for COM recreation is missing.");
        var monitor = Get("UltraWinBar.Utilities.StartMenuMonitor");
        var fast = (TimeSpan)monitor.GetField("FastPollInterval", Private).GetValue(null);
        var slow = (TimeSpan)monitor.GetField("SlowPollInterval", Private).GetValue(null);
        if (fast != TimeSpan.FromMilliseconds(100) || slow < TimeSpan.FromSeconds(1))
            throw new Exception("Start poller must be fast only while a menu is involved and slow otherwise.");
        Console.WriteLine("PASS: severed Explorer COM proxies are recognised by HRESULT or exception, recreated on Explorer restart, and the Start poller idles slowly.");

        var recovery = Get("UltraWinBar.Utilities.TaskWindowRecovery");
        if ((uint)recovery.GetField("EVENT_OBJECT_CLOAKED", Private).GetValue(null) != 0x8017 ||
            (uint)recovery.GetField("EVENT_OBJECT_UNCLOAKED", Private).GetValue(null) != 0x8018)
            throw new Exception("Task recovery must watch both cloak and uncloak to follow windows between desktops.");
        Exception viewError = null;
        var sta = new Thread(() =>
        {
            try
            {
                var shown = new ObservableCollection<Item> { new Item("a", true), new Item("b", false) };
                var view = new ListCollectionView(shown) { Filter = item => ((Item)item).Visible };
                int resets = 0;
                ((System.Collections.Specialized.INotifyCollectionChanged)view).CollectionChanged += (_, e) => { if (e.Action == System.Collections.Specialized.NotifyCollectionChangedAction.Reset) resets++; };
                shown.Add(new Item("c", true));
                shown.Add(new Item("d", false));
                if (view.Count != 2) throw new Exception("Live view must filter added windows without Refresh.");
                shown[1].Visible = true;
                IEditableCollectionView editable = view;
                editable.EditItem(shown[1]);
                editable.CommitEdit();
                if (!view.Contains(shown[1]) || view.Count != 3) throw new Exception("CommitEdit must add a window that now passes the filter.");
                shown[0].Visible = false;
                editable.EditItem(shown[0]);
                editable.CommitEdit();
                if (view.Contains(shown[0]) || view.Count != 2) throw new Exception("CommitEdit must remove a window that no longer passes the filter.");
                if (resets != 0) throw new Exception("Per-window re-evaluation must not reset the whole view.");
                if (!OwnerStaysReachable(shown, detach: false) || OwnerStaysReachable(shown, detach: true))
                    throw new Exception("A view over an app-lifetime collection must be detached to release its panel.");
            }
            catch (Exception error) { viewError = error; }
        });
        sta.SetApartmentState(ApartmentState.STA);
        sta.Start();
        sta.Join();
        if (viewError != null) throw viewError;
        Console.WriteLine("PASS: task views re-filter a single window without a full refresh, and detached views release their panel.");

        var translate = Get("UltraWinBar.Utilities.WindowManager").GetMethod("TranslateWorkArea", Private)
            ?? throw new Exception("Work-area translation is missing.");
        NativeMethods.Rect R(int l, int t, int r, int b) => new NativeMethods.Rect { Left = l, Top = t, Right = r, Bottom = b };
        NativeMethods.Rect Translate(NativeMethods.Rect area, NativeMethods.Rect from, NativeMethods.Rect to) =>
            (NativeMethods.Rect)translate.Invoke(null, new object[] { area, from, to });
        var moved = Translate(R(0, 0, 1920, 1040), R(0, 0, 1920, 1080), R(0, 0, 2560, 1440));
        if (!moved.Equals(R(0, 0, 2560, 1400))) throw new Exception("Other appbars' reservations must carry over to the new monitor size.");
        var shifted = Translate(R(100, 0, 1920, 1080), R(0, 0, 1920, 1080), R(-1280, 0, 0, 1024));
        if (!shifted.Equals(R(-1180, 0, 0, 1024))) throw new Exception("Work area must follow a moved primary monitor.");
        var degenerate = Translate(R(0, 0, 200, 1080), R(0, 0, 1920, 1080), R(0, 0, 1024, 768));
        if (!degenerate.Equals(R(0, 0, 1024, 768))) throw new Exception("Insets that no longer fit must fall back to the full monitor.");
        Console.WriteLine("PASS: the saved original work area follows resolution and primary-monitor changes and never becomes degenerate.");

        var next = shellCom.GetMethod("NextRetryDelay", Private);
        var delay = TimeSpan.FromSeconds(2);
        var sequence = new List<double>();
        for (int i = 0; i < 5; i++) sequence.Add((delay = (TimeSpan)next.Invoke(null, new object[] { delay })).TotalSeconds);
        if (!sequence.SequenceEqual(new double[] { 5, 15, 60, 60, 60 }))
            throw new Exception($"Explorer COM recreation must back off 2/5/15/60 s: {string.Join(",", sequence)}");
        foreach (var owner in new[] { Get("UltraWinBar.Utilities.VirtualDesktopContext"), monitor })
            if (!owner.GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Any(f => f.FieldType.Name.StartsWith("ShellComProxy")))
                throw new Exception($"{owner.Name} must hold its Explorer COM object through ShellComProxy.");
        CheckShellComProxy(Get("UltraWinBar.Utilities.ShellComProxy`1").MakeGenericType(typeof(object)), Get("UltraWinBar.Utilities.ExplorerMonitor"));
        CheckShellComProxyReentrancy(Get("UltraWinBar.Utilities.ShellComProxy`1").MakeGenericType(typeof(object)));
        var explorer = Get("UltraWinBar.Utilities.ExplorerMonitor");
        var restarted = explorer.GetEvent("ExplorerRestarted", BindingFlags.Static | BindingFlags.Public);
        bool laterHandlerRan = false;
        EventHandler failing = (_, __) => throw new InvalidOperationException("class not registered yet");
        EventHandler later = (_, __) => laterHandlerRan = true;
        restarted.AddEventHandler(null, failing);
        restarted.AddEventHandler(null, later);
        try { explorer.GetMethod("RaiseExplorerRestarted", Private).Invoke(null, null); }
        finally
        {
            restarted.RemoveEventHandler(null, failing);
            restarted.RemoveEventHandler(null, later);
        }
        if (!laterHandlerRan) throw new Exception("A failing Explorer-restart subscriber must not skip the others.");
        Console.WriteLine("PASS: Explorer-restart recovery backs off 2/5/15/60 s and isolates failing subscribers.");

        var themeTrigger = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Taskbar")?.GetMethod("IsRelevantThemeTrigger", Private)
            ?? throw new Exception("System theme reload trigger filter is missing.");
        bool Trigger(int msg, int wParam, string category)
        {
            IntPtr text = category == null ? IntPtr.Zero : Marshal.StringToHGlobalUni(category);
            try { return (bool)themeTrigger.Invoke(null, new object[] { msg, (IntPtr)wParam, text }); }
            finally { if (text != IntPtr.Zero) Marshal.FreeHGlobal(text); }
        }
        const int sysColorChange = 0x15, settingChange = 0x1A;
        if (!Trigger(sysColorChange, 0, null) || !Trigger(settingChange, 0, "ImmersiveColorSet") ||
            !Trigger(settingChange, 0x43, null) || !Trigger(settingChange, 0x2A, null) ||
            Trigger(settingChange, 0, "Environment") || Trigger(settingChange, 0, "Policy") || Trigger(settingChange, 0, null))
            throw new Exception("System theme must reload only for color, high-contrast and metrics broadcasts.");
        var unpinnedFilter = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Controls.NotifyIconList")?.GetMethod("UnpinnedNotifyIcons_Filter", Private);
        if (unpinnedFilter == null) throw new Exception("The shared unpinned-icons filter must be static so it retains no tray list.");
        Console.WriteLine("PASS: the System theme reloads only for color-relevant broadcasts and the shared tray filter retains no panel.");

        // An installed mouse hook whose owner forgot Dispose must keep its callback thunk alive.
        var hookRef = InstallAbandonedHook();
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        if (!DisposeHook(hookRef)) throw new Exception("An installed mouse hook must stay rooted until Dispose.");
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        if (hookRef.IsAlive) throw new Exception("A disposed mouse hook must be released.");
        Console.WriteLine("PASS: an installed mouse hook stays rooted until Dispose, so a dropped owner cannot free its callback.");

        // Without PreserveSig the CCW writes a phantom [out, retval] through whatever the caller left in a register.
        foreach (var name in new[] { "UltraWinBar.Utilities.IAppVisibility", "UltraWinBar.Utilities.IAppVisibilityEvents" })
            foreach (var method in Get(name).GetMethods())
                if (method.ReturnType != typeof(int) || (method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) == 0)
                    throw new Exception($"{name}.{method.Name} must be [PreserveSig] returning an HRESULT.");
        if (monitor.GetField("_launcherVisibility", BindingFlags.Instance | BindingFlags.NonPublic)?.FieldType.GetGenericArguments().FirstOrDefault()?.Name != "LauncherVisibility")
            throw new Exception("Start monitoring must not use ManagedShell's AppVisibilityHelper sink.");
        var launcherType = Get("UltraWinBar.Utilities.LauncherVisibility");
        using (var launcher = (IDisposable)Activator.CreateInstance(launcherType, true))
        {
            if ((uint)launcherType.GetField("_cookie", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(launcher) == 0)
                throw new Exception("The launcher visibility sink must be accepted by Advise.");
            launcherType.GetMethod("IsVisible").Invoke(launcher, null);
        }
        Console.WriteLine("PASS: launcher visibility COM interop preserves HRESULT signatures and its sink is accepted by Explorer.");

        var healthType = Get("UltraWinBar.Utilities.HealthReporter");
        using (var health = (IDisposable)Activator.CreateInstance(healthType, new object[] { null }))
        {
            string snapshot = (string)healthType.GetMethod("Snapshot", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(health, null);
            foreach (var field in new[] { "handles=", "gdi=", "user=", "threads=", "privateMB=", "gcHeapMB=", "winEventHooks=", "mouseHooks=", "settingsSubscribers=" })
                if (!snapshot.Contains(field)) throw new Exception($"Health snapshot is missing {field}: {snapshot}");
        }
        Console.WriteLine("PASS: the periodic health snapshot reports handles, GDI/USER objects, threads, memory, hooks and subscribers.");

        // Never instantiate Settings here: any Settings object is wired to the user's real settings file.
        var ranksOf = Get("UltraWinBar.Utilities.Settings").GetMethod("RanksOf", Private);
        var ranks = (Dictionary<string, int>)ranksOf.Invoke(null, new object[] { new List<string> { "a", "b", null, "a" } });
        if (ranks.Count != 2 || ranks["a"] != 0 || ranks["b"] != 1) throw new Exception("Task order ranks must match the first position in the saved order.");
        Console.WriteLine("PASS: task sorting looks up precomputed ranks (first position wins, as IndexOf did).");

        // UI objects must reach app-lifetime singletons only weakly (App itself lives as long as they do).
        var root = new System.IO.DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !System.IO.File.Exists(System.IO.Path.Combine(root.FullName, "README.md"))) root = root.Parent;
        foreach (var folder in new[] { "UI", System.IO.Path.Combine("Shell", "Panels"), System.IO.Path.Combine("Shell", "Dialogs") })
            foreach (var file in System.IO.Directory.EnumerateFiles(System.IO.Path.Combine(root.FullName, "UltraWinBar", folder), "*.cs", System.IO.SearchOption.AllDirectories))
                if (System.Text.RegularExpressions.Regex.IsMatch(System.IO.File.ReadAllText(file), @"Settings\.Instance\.PropertyChanged\s*\+=|Instance\??\.Changed\s*\+="))
                    throw new Exception($"{file} subscribes strongly to an app-lifetime singleton; use WeakSubscriptions.");
        Exception weakError = null;
        var weakThread = new Thread(() =>
        {
            try
            {
                var subscribe = Get("UltraWinBar.Utilities.WeakSubscriptions").GetMethod("SubscribeSettings", Private);
                WeakReference listener = SubscribeListener(subscribe);
                for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
                if (listener.IsAlive) throw new Exception("A settings listener that never unsubscribes must still be collectable.");
            }
            catch (Exception error) { weakError = error; }
        });
        weakThread.SetApartmentState(ApartmentState.STA);
        weakThread.Start();
        weakThread.Join();
        if (weakError != null) throw weakError;
        Console.WriteLine("PASS: UI objects subscribe to Settings and virtual desktops weakly, so a missed unsubscribe cannot leak a panel.");

        CheckImmersiveShellHelperReset();
    }

    // R9-B: ImmersiveShellHelper.Reset() must drop the static Explorer COM caches (dead forever
    // after an Explorer restart otherwise) and be idempotent; its disconnect-HRESULT classifier
    // must trigger a reset-and-retry only for a genuinely severed proxy.
    private static void CheckImmersiveShellHelperReset()
    {
        var helper = typeof(ImmersiveShellHelper);

        var isDisconnectedHr = helper.GetMethod("IsDisconnected", Private, null, new[] { typeof(int) }, null)
            ?? throw new Exception("ImmersiveShellHelper disconnect-HRESULT classifier is missing.");
        var isDisconnectedEx = helper.GetMethod("IsDisconnected", Private, null, new[] { typeof(Exception) }, null)
            ?? throw new Exception("ImmersiveShellHelper disconnect-exception classifier is missing.");

        // RPC_E_DISCONNECTED, RPC_S_SERVER_UNAVAILABLE, CO_E_OBJNOTCONNECTED, RPC_E_SERVER_DIED, RPC_E_SERVER_DIED_DNE.
        int[] disconnected = { unchecked((int)0x80010108), unchecked((int)0x800706BA), unchecked((int)0x800401FD), unchecked((int)0x80010007), unchecked((int)0x80010012) };
        int[] other = { 0, 1, unchecked((int)0x80070057), unchecked((int)0x80004005) };
        foreach (int hr in disconnected)
            if (!(bool)isDisconnectedHr.Invoke(null, new object[] { hr }) || !(bool)isDisconnectedEx.Invoke(null, new object[] { new COMException("x", hr) }))
                throw new Exception($"0x{hr:X8} must be treated as a severed Explorer proxy.");
        foreach (int hr in other)
            if ((bool)isDisconnectedHr.Invoke(null, new object[] { hr }) || (bool)isDisconnectedEx.Invoke(null, new object[] { new COMException("x", hr) }))
                throw new Exception($"0x{hr:X8} must not reset the ImmersiveShellHelper cache.");
        if (!(bool)isDisconnectedEx.Invoke(null, new object[] { new InvalidComObjectException() }))
            throw new Exception("A released ImmersiveShellHelper RCW must be treated as disconnected.");

        // Reset() is written to cover exactly these static caches; catches a field renamed without
        // updating Reset() (or vice versa) at the field-list level.
        string[] cacheFields =
        {
            "_immersiveShell", "_shellExperienceManagerFactory", "_actionCenterExperienceManager",
            "_controlCenterExperienceManager", "_networkFlyoutExperienceManager", "_networkFlyoutExperienceManager_20H1",
            "_trayBatteryFlyoutExperienceManager", "_trayClockFlyoutExperienceManager", "_trayMtcUvcFlyoutExperienceManager",
        };
        foreach (var name in cacheFields)
            if (helper.GetField(name, Private) == null) throw new Exception($"ImmersiveShellHelper.{name} is missing.");

        // Behavioral proof, on the live desktop: GetImmersiveShell() caches the RCW until Reset()
        // drops it, at which point the next call must create a fresh one (not reuse a dead proxy).
        var immersiveShellField = helper.GetField("_immersiveShell", Private);
        var shell = ImmersiveShellHelper.GetImmersiveShell();
        if (shell == null) throw new Exception("GetImmersiveShell() must succeed on a live desktop for this check to be meaningful.");
        if (!ReferenceEquals(shell, ImmersiveShellHelper.GetImmersiveShell()))
            throw new Exception("GetImmersiveShell() must cache the ImmersiveShell RCW between calls.");

        ImmersiveShellHelper.Reset();
        if (immersiveShellField.GetValue(null) != null)
            throw new Exception("Reset() must clear the cached ImmersiveShell.");

        var shellAfterReset = ImmersiveShellHelper.GetImmersiveShell();
        if (shellAfterReset == null) throw new Exception("GetImmersiveShell() must recover after Reset().");
        if (ReferenceEquals(shell, shellAfterReset))
            throw new Exception("Reset() must force a fresh ImmersiveShell RCW instead of reusing the pre-restart one.");

        // Idempotent: a live cache, then an already-empty one, must both reset without throwing.
        ImmersiveShellHelper.Reset();
        ImmersiveShellHelper.Reset();
        if (immersiveShellField.GetValue(null) != null)
            throw new Exception("A second Reset() must not resurrect a cached field.");

        Console.WriteLine("PASS: ImmersiveShellHelper's disconnect classifier recognises only severed-Explorer-proxy HRESULTs/exceptions, and Reset() clears the cached ImmersiveShell (forcing recreation) and is idempotent.");
    }

    // Unavailable at start -> backoff -> recreated (Recovered); severed HRESULT -> dropped with backoff;
    // Explorer restart -> dropped and recreated at once.
    private static void CheckShellComProxy(Type proxyType, Type explorerMonitor)
    {
        int attempts = 0, recovered = 0, dropped = 0, released = 0;
        bool available = false;
        Func<object> create = () => { attempts++; return available ? new object() : throw new COMException("class not registered", unchecked((int)0x80040154)); };
        Action<object> release = _ => released++;
        var proxy = (IDisposable)Activator.CreateInstance(proxyType, BindingFlags.Instance | BindingFlags.NonPublic, null, new object[] { "test proxy", create, release }, null);
        try
        {
            proxyType.GetEvent("Recovered", BindingFlags.Instance | BindingFlags.NonPublic).GetAddMethod(true).Invoke(proxy, new object[] { new Action(() => recovered++) });
            proxyType.GetEvent("Dropped", BindingFlags.Instance | BindingFlags.NonPublic).GetAddMethod(true).Invoke(proxy, new object[] { new Action(() => dropped++) });
            var get = proxyType.GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic);
            var check = proxyType.GetMethod("Check", BindingFlags.Instance | BindingFlags.NonPublic);
            if (get.Invoke(proxy, null) != null || attempts != 2) throw new Exception("An unavailable Explorer COM object must be retried on first use.");
            available = true;
            if (get.Invoke(proxy, null) != null || attempts != 2) throw new Exception("Explorer COM recreation must honour its backoff.");
            proxyType.GetField("nextRetryUtc", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(proxy, DateTime.MinValue);
            if (get.Invoke(proxy, null) == null || recovered != 1) throw new Exception("A recreated Explorer COM object must raise Recovered.");
            check.Invoke(proxy, new object[] { unchecked((int)0x80010108) });
            if (dropped != 1 || released != 1 || get.Invoke(proxy, null) != null) throw new Exception("A severed proxy must be dropped, released and backed off.");
            explorerMonitor.GetMethod("RaiseExplorerRestarted", BindingFlags.Static | BindingFlags.NonPublic).Invoke(null, null);
            if (get.Invoke(proxy, null) == null || recovered != 2) throw new Exception("An Explorer restart must recreate the object immediately.");
        }
        finally { proxy.Dispose(); }
        if (released != 2) throw new Exception("Disposing the proxy must release its object.");
    }

    // create() running outside the lock must not let a re-entrant/concurrent caller (STA message
    // pump re-entering Get() on the same thread) start a second activation, and must release
    // whatever it built if the slot is no longer available (disposed) by the time it finishes.
    private static void CheckShellComProxyReentrancy(Type proxyType)
    {
        var get = proxyType.GetMethod("Get", BindingFlags.Instance | BindingFlags.NonPublic);
        var valueField = proxyType.GetField("value", BindingFlags.Instance | BindingFlags.NonPublic);
        var nextRetryField = proxyType.GetField("nextRetryUtc", BindingFlags.Instance | BindingFlags.NonPublic);

        // Scenario 1: create() re-enters Get() on the same thread (STA activation pumping messages).
        {
            int attempts = 0, released = 0;
            object proxyRef = null;
            object innerResult = "not invoked";
            bool armReentry = false;
            Func<object> create = () =>
            {
                attempts++;
                if (armReentry)
                {
                    armReentry = false; // recurse only once
                    innerResult = get.Invoke(proxyRef, null);
                }
                return new object();
            };
            Action<object> release = _ => released++;
            var proxy = Activator.CreateInstance(proxyType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { "reentrancy test", create, release }, null);
            proxyRef = proxy;
            try
            {
                valueField.SetValue(proxy, null); // as if the object had died
                nextRetryField.SetValue(proxy, DateTime.MinValue);
                armReentry = true;
                attempts = 0;
                object outerResult = get.Invoke(proxy, null);
                if (attempts != 1)
                    throw new Exception($"A caller re-entering Get() while create() is in flight must not start a second activation (attempts={attempts}).");
                if (innerResult != null)
                    throw new Exception("A re-entrant/concurrent Get() during create() must return null, not a half-built object.");
                if (outerResult == null)
                    throw new Exception("The call whose create() is actually running must receive the created object.");
                if (released != 0)
                    throw new Exception("The one created object must not be released while it is still the live value.");
            }
            finally { ((IDisposable)proxy).Dispose(); }
            if (released != 1) throw new Exception("Disposing the proxy must release the object it created.");
        }

        // Scenario 2: the proxy is disposed while create() is still running; the object it builds
        // arrives too late and must be released instead of leaked or overwriting a disposed slot.
        {
            int released = 0;
            object proxyRef = null;
            bool disposeDuringCreate = false;
            Func<object> create = () =>
            {
                if (disposeDuringCreate)
                {
                    disposeDuringCreate = false;
                    ((IDisposable)proxyRef).Dispose();
                }
                return new object();
            };
            Action<object> release = _ => released++;
            var proxy = Activator.CreateInstance(proxyType, BindingFlags.Instance | BindingFlags.NonPublic, null,
                new object[] { "dispose-during-create test", create, release }, null);
            proxyRef = proxy;
            valueField.SetValue(proxy, null);
            nextRetryField.SetValue(proxy, DateTime.MinValue);
            disposeDuringCreate = true;
            object result = get.Invoke(proxy, null);
            if (result != null) throw new Exception("Get() must return null when the proxy is disposed while its create() is still running.");
            if (released != 1) throw new Exception($"An object created after Dispose() started must be released, not leaked (released={released}).");
        }
        Console.WriteLine("PASS: ShellComProxy.Get() serializes create() against re-entrant/concurrent callers and releases objects that arrive after the slot is gone.");
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference SubscribeListener(MethodInfo subscribe)
    {
        var listener = new SettingsListener();
        subscribe.Invoke(null, new object[] { new EventHandler<PropertyChangedEventArgs>(listener.OnChanged) });
        return new WeakReference(listener);
    }

    private sealed class SettingsListener
    {
        internal int Changes;
        internal void OnChanged(object sender, PropertyChangedEventArgs e) => Changes++;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference InstallAbandonedHook()
    {
        var hook = new LowLevelMouseHook();
        if (!hook.Initialize()) throw new Exception("Test mouse hook could not be installed.");
        return new WeakReference(hook);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static bool DisposeHook(WeakReference hookRef)
    {
        if (!(hookRef.Target is LowLevelMouseHook hook)) return false;
        hook.Dispose();
        return true;
    }

    // Mirrors NotifyIconList/TaskList: a view over a long-lived collection whose sorter holds its panel.
    private static bool OwnerStaysReachable(ObservableCollection<Item> source, bool detach)
    {
        WeakReference owner = CreateView(source, detach);
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        return owner.IsAlive;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private static WeakReference CreateView(ObservableCollection<Item> source, bool detach)
    {
        var panel = new object();
        var view = new ListCollectionView(source) { CustomSort = new PanelSorter(panel) };
        if (detach)
        {
            view.CustomSort = null;
            view.DetachFromSourceCollection();
        }
        return new WeakReference(panel);
    }

    private sealed class PanelSorter : System.Collections.IComparer
    {
        private readonly object _panel;
        internal PanelSorter(object panel) => _panel = panel;
        public int Compare(object x, object y) => _panel == null ? 0 : 0;
    }

    private sealed class Item
    {
        internal Item(string name, bool visible) { Name = name; Visible = visible; }
        internal string Name { get; }
        internal bool Visible { get; set; }
    }
}
