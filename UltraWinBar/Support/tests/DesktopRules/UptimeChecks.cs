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
        var desktopHr = Get("UltraWinBar.Utilities.VirtualDesktopContext").GetMethod("IsDisconnected", Private, null, new[] { typeof(int) }, null)
            ?? throw new Exception("Virtual desktop HRESULT disconnect check is missing.");
        var startError = Get("UltraWinBar.Utilities.StartMenuMonitor").GetMethod("IsComDisconnected", Private)
            ?? throw new Exception("Start menu COM disconnect check is missing.");
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

        foreach (var owner in new[] { Get("UltraWinBar.Utilities.VirtualDesktopContext"), monitor })
        {
            var next = owner.GetMethods(Private).Single(m => m.Name.StartsWith("Next") && m.Name.EndsWith("RetryDelay"));
            var delay = TimeSpan.FromSeconds(2);
            var sequence = new List<double>();
            for (int i = 0; i < 5; i++) sequence.Add((delay = (TimeSpan)next.Invoke(null, new object[] { delay })).TotalSeconds);
            if (!sequence.SequenceEqual(new double[] { 5, 15, 60, 60, 60 }))
                throw new Exception($"{owner.Name} COM recreation must back off 2/5/15/60 s: {string.Join(",", sequence)}");
        }
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
