using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows.Forms;
using System.Windows.Threading;
using Microsoft.Win32;
using UltraWinBar.Utilities;

// R9-H (K12): guards for the shared native-callback layer (CallbackGuard/WinEventHook/WinEventHub/
// NativeCallback/ShellComProxy in ManagedShell.Common.Native). Complements InteropChecks, which
// covers the interop-signature and trust-boundary classes.
internal static class NativeCallbackChecks
{
    internal static void Run(System.IO.DirectoryInfo repositoryRoot)
    {
        CheckNoDirectHookOrClassRegistration(repositoryRoot);
        CheckNativeWindowsOverrideOnThreadException();
        CheckWinEventHubSharesOneHookPerEventFlags();
        CheckNativeCallbackEnumWindowsWrap();
        CheckInputHookHostSharesOneHookAcrossSubscribers();
        CheckInputHookHostMarshalsHookData();
        CheckRegistryValueWatchFiresReArmsAndDisposes();
    }

    // K12 / review section 6: SetWinEventHook, SetWindowsHookEx and RegisterClass/RegisterClassEx
    // must only be called from the primitives that root the callback delegate and guard exceptions.
    // R9-L (K18) adds RegNotifyChangeKeyValue: must only be called from RegistryValueWatch.
    // Scans call sites (not P/Invoke declarations: NativeMethods legitimately declares these for the
    // primitives to call, so a declaration-site scan can't tell the two apart).
    private static void CheckNoDirectHookOrClassRegistration(System.IO.DirectoryInfo repositoryRoot)
    {
        string ultraWinBarRoot = Path.Combine(repositoryRoot.FullName, "UltraWinBar");

        // Explicit allow-list: the primitives themselves, plus the one legitimate window-class
        // registration site (TrayService registers Shell_TrayWnd/TrayNotifyWnd; there is no separate
        // "window class" primitive here, WinForms' NativeWindow/NativeWindowEx register their own).
        // No dedicated hook thread installs a raw hook directly: DesktopActivationHookThread only
        // hosts LowLevelMouseHook's own SetWindowsHookEx call, on its own thread; InputHookHost shares
        // that same LowLevelMouseHook across subscribers instead of installing its own.
        var allowList = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            Path.Combine(ultraWinBarRoot, "Core", "Input", "LowLevelMouseHook.cs"),
            Path.Combine(ultraWinBarRoot, "Support", "vendor", "ManagedShell", "src", "ManagedShell.Common", "Native", "WinEventHook.cs"),
            Path.Combine(ultraWinBarRoot, "Support", "vendor", "ManagedShell", "src", "ManagedShell.WindowsTray", "TrayService.cs"),
            Path.Combine(ultraWinBarRoot, "Core", "Infrastructure", "Native", "RegistryValueWatch.cs"),
        };

        var bannedCalls = new Regex(@"\b(SetWinEventHook|SetWindowsHookEx|RegisterClassEx|RegisterClass|RegNotifyChangeKeyValue)\s*\(", RegexOptions.Compiled);
        var scanDirs = new[]
        {
            Path.Combine(ultraWinBarRoot, "Core"),
            Path.Combine(ultraWinBarRoot, "Shell"),
            Path.Combine(ultraWinBarRoot, "UI"),
            Path.Combine(ultraWinBarRoot, "Support", "vendor", "ManagedShell", "src"),
        };

        var violations = new List<string>();
        foreach (var dir in scanDirs.Where(Directory.Exists))
        {
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (allowList.Contains(file)) continue;
                foreach (var line in File.ReadLines(file))
                {
                    if (line.Contains("extern")) continue; // P/Invoke declaration, not a call site.
                    if (bannedCalls.IsMatch(line)) violations.Add($"{file}: {line.Trim()}");
                }
            }
        }
        if (violations.Count > 0)
            throw new Exception("Direct hook/window-class/registry-watch registration outside the allow-listed primitives:\n  " + string.Join("\n  ", violations));
        Console.WriteLine("PASS: SetWinEventHook/SetWindowsHookEx/RegisterClass/RegNotifyChangeKeyValue are called only from WinEventHook, LowLevelMouseHook, TrayService's single window-class registration, and RegistryValueWatch.");
    }

    // K12 / Н6: a NativeWindow subclass with the framework's default (empty) OnThreadException
    // silently swallows WndProc exceptions. Not DeclaredOnly: a class inheriting a real override
    // (e.g. ShellWindow from NativeWindowEx) is already covered and must not be forced to redeclare it.
    private static void CheckNativeWindowsOverrideOnThreadException()
    {
        var assemblies = new[] { "ManagedShell", "ManagedShell.AppBar", "ManagedShell.Common", "ManagedShell.Interop",
            "ManagedShell.ShellFolders", "ManagedShell.UWPInterop", "ManagedShell.WindowsTasks", "ManagedShell.WindowsTray" }
            .Select(Assembly.Load).Append(typeof(TaskAssignmentManager).Assembly).ToArray();

        var offenders = new List<string>();
        var checkedTypes = new List<string>();
        foreach (var assembly in assemblies)
        {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException error) { types = error.Types.Where(t => t != null).ToArray(); }
            foreach (var type in types)
            {
                if (!typeof(NativeWindow).IsAssignableFrom(type) || type == typeof(NativeWindow)) continue;
                var method = type.GetMethod("OnThreadException", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                checkedTypes.Add(type.FullName);
                if (method == null || method.DeclaringType == typeof(NativeWindow))
                    offenders.Add(type.FullName);
            }
        }
        if (checkedTypes.Count == 0)
            throw new Exception("No NativeWindow-derived type was found; the scan itself is broken.");
        if (offenders.Count > 0)
            throw new Exception("NativeWindow-derived types with no OnThreadException override anywhere in their hierarchy " +
                "(WndProc exceptions vanish silently): " + string.Join(", ", offenders));
        Console.WriteLine($"PASS: every NativeWindow-derived type ({checkedTypes.Count} checked) overrides OnThreadException somewhere in its hierarchy.");
    }

    // K12, review section 5: two UI-thread subscribers to the same (event, flags) must share one
    // underlying WinEventHook; disposing both must uninstall it. Proven via WinEventHook.InstalledCount.
    private static void CheckWinEventHubSharesOneHookPerEventFlags()
    {
        var commonAssembly = Assembly.Load("ManagedShell.Common");
        var hubType = commonAssembly.GetType("ManagedShell.Common.Native.WinEventHub")
            ?? throw new Exception("WinEventHub is missing.");
        var hookType = commonAssembly.GetType("ManagedShell.Common.Native.WinEventHook")
            ?? throw new Exception("WinEventHook is missing.");
        var handlerType = hookType.GetNestedType("Handler", BindingFlags.NonPublic)
            ?? throw new Exception("WinEventHook.Handler is missing.");
        var installedCount = hookType.GetProperty("InstalledCount", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("WinEventHook.InstalledCount is missing.");
        var subscribe = hubType.GetMethod("Subscribe", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("WinEventHub.Subscribe is missing.");

        int InstalledCount() => (int)installedCount.GetValue(null);

        // A private, never-fired event id: real WinEvents in the same process cannot land here and
        // perturb InstalledCount for an unrelated bucket while this check runs.
        const uint testEventId = 0x7FFE;
        int callsA = 0, callsB = 0;
        void HandlerA(uint type, IntPtr hwnd, int obj, int child) => callsA++;
        void HandlerB(uint type, IntPtr hwnd, int obj, int child) => callsB++;
        Action<uint, IntPtr, int, int> handlerAFunc = HandlerA;
        Action<uint, IntPtr, int, int> handlerBFunc = HandlerB;
        Delegate handlerA = Delegate.CreateDelegate(handlerType, handlerAFunc.Target, handlerAFunc.Method);
        Delegate handlerB = Delegate.CreateDelegate(handlerType, handlerBFunc.Target, handlerBFunc.Method);

        int before = InstalledCount();
        object subA = subscribe.Invoke(null, new object[] { "hub test A", testEventId, handlerA, (uint)0 });
        int afterFirst = InstalledCount();
        if (afterFirst != before + 1)
            throw new Exception("The first subscriber to a new (event, flags) pair must install exactly one WinEventHook.");

        object subB = subscribe.Invoke(null, new object[] { "hub test B", testEventId, handlerB, (uint)0 });
        int afterSecond = InstalledCount();
        if (afterSecond != afterFirst)
            throw new Exception("A second subscriber to the same (event, flags) pair must share the existing WinEventHook, not install another.");

        ((IDisposable)subA).Dispose();
        int afterFirstDispose = InstalledCount();
        if (afterFirstDispose != afterSecond)
            throw new Exception("Disposing one of two subscribers must not uninstall the shared hook while the other is still subscribed.");

        ((IDisposable)subB).Dispose();
        int afterBothDisposed = InstalledCount();
        if (afterBothDisposed != before)
            throw new Exception("Disposing the last subscriber to an (event, flags) pair must uninstall its shared WinEventHook.");

        Console.WriteLine("PASS: WinEventHub installs one WinEventHook per (event, flags) pair shared by all its subscribers, and removes it once the last one disposes.");
    }

    // K12: NativeCallback.Wrap's EnumWindows-shaped overload reports an escaping exception via
    // CallbackGuard and stops enumeration (returns false) instead of throwing into native code.
    private static void CheckNativeCallbackEnumWindowsWrap()
    {
        var commonAssembly = Assembly.Load("ManagedShell.Common");
        var nativeCallbackType = commonAssembly.GetType("ManagedShell.Common.Native.NativeCallback")
            ?? throw new Exception("NativeCallback is missing.");
        var callBackPtrType = Assembly.Load("ManagedShell.Interop").GetType("ManagedShell.Interop.NativeMethods+CallBackPtr")
            ?? throw new Exception("NativeMethods.CallBackPtr is missing.");
        var wrap = nativeCallbackType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == "Wrap" && m.GetParameters()[1].ParameterType == callBackPtrType);
        var failureCount = commonAssembly.GetType("ManagedShell.Common.Native.CallbackGuard")
            .GetMethod("FailureCount", BindingFlags.Static | BindingFlags.NonPublic);

        const string source = "NativeCallbackChecks: EnumWindows wrap test";
        Delegate throwing = Delegate.CreateDelegate(callBackPtrType, typeof(NativeCallbackChecks)
            .GetMethod(nameof(ThrowingEnumCallback), BindingFlags.Static | BindingFlags.NonPublic));
        Delegate wrapped = (Delegate)wrap.Invoke(null, new object[] { source, throwing });

        int before = (int)failureCount.Invoke(null, new object[] { source });
        object result = wrapped.DynamicInvoke(IntPtr.Zero, IntPtr.Zero);
        int after = (int)failureCount.Invoke(null, new object[] { source });

        if ((bool)result) throw new Exception("A wrapped EnumWindows callback that throws must stop enumeration (return false).");
        if (after != before + 1) throw new Exception("A wrapped EnumWindows callback that throws must report the failure via CallbackGuard.");
        Console.WriteLine("PASS: NativeCallback.Wrap's EnumWindows overload reports an escaping exception via CallbackGuard and stops enumeration.");
    }

    private static bool ThrowingEnumCallback(IntPtr hwnd, IntPtr lParam) => throw new InvalidOperationException("test");

    // R9-L (K18): InputHookHost shares one physical WH_MOUSE_LL hook (via LowLevelMouseHook,
    // installed on its own dedicated thread) across every subscriber, installing it for the first
    // and removing it once the last disposes. Proven via LowLevelMouseHook.InstalledCount, the same
    // technique CheckWinEventHubSharesOneHookPerEventFlags uses for WinEventHub above.
    private static void CheckInputHookHostSharesOneHookAcrossSubscribers()
    {
        static void HandlerA(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e) { }
        static void HandlerB(object sender, LowLevelMouseHook.LowLevelMouseEventArgs e) { }

        int before = InputHookHost.InstalledHookCount;
        var subA = InputHookHost.SubscribeMouse("NativeCallbackChecks: host test A", HandlerA);
        int afterFirst = InputHookHost.InstalledHookCount;
        if (afterFirst != before + 1)
            throw new Exception("The first subscriber must install exactly one shared low-level mouse hook.");

        var subB = InputHookHost.SubscribeMouse("NativeCallbackChecks: host test B", HandlerB);
        int afterSecond = InputHookHost.InstalledHookCount;
        if (afterSecond != afterFirst)
            throw new Exception("A second subscriber must share the existing hook, not install another.");

        subA.Dispose();
        int afterFirstDispose = InputHookHost.InstalledHookCount;
        if (afterFirstDispose != afterSecond)
            throw new Exception("Disposing one of two subscribers must not remove the shared hook while the other is still subscribed.");

        subB.Dispose();
        int afterBothDisposed = InputHookHost.InstalledHookCount;
        if (afterBothDisposed != before)
            throw new Exception("Disposing the last subscriber must remove the shared hook.");

        Console.WriteLine("PASS: InputHookHost installs one shared low-level mouse hook for two subscribers and removes it once both dispose.");
    }

    // R9-L (K18): the hook callback must marshal MSLLHOOKSTRUCT by value (Marshal.PtrToStructure<T>,
    // no boxing) rather than share native memory with the subscriber. Feeds a synthetic event through
    // LowLevelMouseHook's real dispatch path (LowLevelMouseHook.TestDispatch writes a genuine native
    // buffer and calls the actual hook proc) - never SendInput or a real mouse/keyboard event.
    private static void CheckInputHookHostMarshalsHookData()
    {
        var hook = new LowLevelMouseHook();
        LowLevelMouseHook.LowLevelMouseEventArgs received = null;
        hook.LowLevelMouseEvent += (sender, e) => received = e;

        var sample = new LowLevelMouseHook.MSLLHOOKSTRUCT
        {
            pt = new LowLevelMouseHook.POINT { X = 123, Y = 456 },
            mouseData = unchecked((int)0x00A50000),
            flags = 0,
            time = 987654,
            dwExtraInfo = UIntPtr.Zero
        };
        hook.TestDispatch(ManagedShell.Interop.NativeMethods.WM.MOUSEMOVE, sample);

        if (received == null) throw new Exception("A synthetic dispatch did not reach the subscriber.");
        if (received.Message != ManagedShell.Interop.NativeMethods.WM.MOUSEMOVE ||
            received.HookStruct.pt.X != 123 || received.HookStruct.pt.Y != 456 ||
            received.HookStruct.mouseData != sample.mouseData || received.HookStruct.time != 987654)
            throw new Exception("The hook callback did not marshal MSLLHOOKSTRUCT fields correctly.");
        Console.WriteLine("PASS: the hook callback marshals MSLLHOOKSTRUCT by value (Marshal.PtrToStructure<T>) without losing data.");
    }

    // R9-L (K18, Н11): RegistryValueWatch replaces two copies that had drifted apart. Watches a
    // scratch key created (and deleted) just for this test, under HKCU so no admin rights are needed.
    private static void CheckRegistryValueWatchFiresReArmsAndDisposes()
    {
        string keyPath = @"Software\UltraWinBar-Test-" + Guid.NewGuid().ToString("N");
        using (var seed = Registry.CurrentUser.CreateSubKey(keyPath))
            seed.SetValue("probe", 0, RegistryValueKind.DWord);
        try
        {
            // RegistryValueWatch marshals its callback to a Dispatcher; give it one with a real
            // message loop on a dedicated thread; the test thread pumps it via Invoke to drain
            // notifications synchronously instead of guessing at a delay.
            Dispatcher ownerDispatcher = null;
            var ready = new ManualResetEventSlim(false);
            var ownerThread = new Thread(() =>
            {
                ownerDispatcher = Dispatcher.CurrentDispatcher;
                ready.Set();
                Dispatcher.Run();
            })
            { IsBackground = true };
            ownerThread.Start();
            ready.Wait();

            var watchKey = Registry.CurrentUser.OpenSubKey(keyPath, writable: false)
                ?? throw new Exception("Scratch registry key did not open for watching.");
            int fireCount = 0;
            var watch = new RegistryValueWatch(watchKey, RegistryValueWatch.ValueChangeFilter, ownerDispatcher,
                () => Interlocked.Increment(ref fireCount), "NativeCallbackChecks: registry watch test");
            try
            {
                using (var writer = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
                    writer.SetValue("probe", 1, RegistryValueKind.DWord);
                if (!WaitForFireCount(ownerDispatcher, ref fireCount, 1, TimeSpan.FromSeconds(5)))
                    throw new Exception("RegistryValueWatch did not fire on a value change under the scratch key.");

                using (var writer = Registry.CurrentUser.OpenSubKey(keyPath, writable: true))
                    writer.SetValue("probe", 2, RegistryValueKind.DWord);
                if (!WaitForFireCount(ownerDispatcher, ref fireCount, 2, TimeSpan.FromSeconds(5)))
                    throw new Exception("RegistryValueWatch did not re-arm after its first notification.");
            }
            finally
            {
                watch.Dispose();
                ownerDispatcher.InvokeShutdown();
            }
            Console.WriteLine("PASS: RegistryValueWatch fires on a value change, re-arms for the next one, and disposes cleanly.");
        }
        finally
        {
            Registry.CurrentUser.DeleteSubKeyTree(keyPath, throwOnMissingSubKey: false);
        }
    }

    // RegNotifyChangeKeyValue's wait callback runs on a thread pool thread on its own schedule, so a
    // fixed number of dispatcher pumps is not reliable; poll, pumping "dispatcher" on each iteration
    // so its BeginInvoke'd notifications (posted at Normal priority) actually get to run.
    private static bool WaitForFireCount(Dispatcher dispatcher, ref int fireCount, int expected, TimeSpan timeout)
    {
        DateTime deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            dispatcher.Invoke(new Action(() => { }), DispatcherPriority.Normal);
            if (Volatile.Read(ref fireCount) >= expected) return true;
            Thread.Sleep(25);
        }
        return Volatile.Read(ref fireCount) >= expected;
    }
}
