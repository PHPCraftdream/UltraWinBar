using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ManagedShell.AppBar;
using ManagedShell.Interop;
using ManagedShell.WindowsTasks;
using System.Threading;
using System.Windows.Threading;
using UltraWinBar.Utilities;

// Mechanical guard for the interop bug class behind the 2026-09-28 crash: signatures the
// marshaller silently rewrites (hidden [out, retval], VARIANT_BOOL, VARIANT) or truncates.
internal static class InteropChecks
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Regex HandleName = new Regex(@"^(h[A-Z]\w*|(?i:hwnd|hkl|wparam|lparam|lresult|hook|\w*handle))$");

    internal static void Run(System.IO.DirectoryInfo repositoryRoot)
    {
        var problems = new List<string>();
        Check(typeof(TaskAssignmentManager).Assembly, problems, strictReturns: true);
        // Vendored ManagedShell: every ComImport interface method (RCW or CCW) must be [PreserveSig]
        // so failing HRESULTs are real return values, not hidden [out, retval] exceptions.
        foreach (var name in new[] { "ManagedShell", "ManagedShell.AppBar", "ManagedShell.Common", "ManagedShell.Interop",
                                     "ManagedShell.ShellFolders", "ManagedShell.UWPInterop", "ManagedShell.WindowsTasks", "ManagedShell.WindowsTray" })
            Check(Assembly.Load(name), problems, strictReturns: true);
        if (problems.Count > 0)
            throw new Exception("Unsafe interop signatures:\n  " + string.Join("\n  ", problems));
        Console.WriteLine("PASS: COM methods are PreserveSig with explicit BOOL/IUnknown marshalling; P/Invoke handles are pointer-sized.");

        // R9-H (K12): CallbackGuard/WinEventHook/ShellComProxy moved to ManagedShell.Common so
        // ManagedShell's own native callbacks share the app's barrier.
        var managedShellCommon = Assembly.Load("ManagedShell.Common");
        var guard = managedShellCommon.GetType("ManagedShell.Common.Native.CallbackGuard")
            ?? throw new Exception("Native callback exception barrier is missing.");
        var report = guard.GetMethod("Report", BindingFlags.Static | BindingFlags.NonPublic);
        var failureCount = guard.GetMethod("FailureCount", BindingFlags.Static | BindingFlags.NonPublic);
        for (int i = 0; i < 25; i++) report.Invoke(null, new object[] { "test source", new InvalidOperationException("x") });
        if ((int)failureCount.Invoke(null, new object[] { "test source" }) != 25)
            throw new Exception("Callback failures must be counted per source without throwing.");
        Console.WriteLine("PASS: native callback failures are reported per source without escaping.");

        // Hook/window-class call sites (K12) are checked by source scan in NativeCallbackChecks,
        // which covers every project including this app assembly (a reflection scan of P/Invoke
        // *declarations* can't tell a primitive's own extern from the shared NativeMethods one that
        // legitimately declares them for everyone to call through a wrapper).

        // R9-M / К20: typed access to the trust-boundary parser (ManagedShell.Interop.
        // CrossProcessMessages) instead of reflecting TrayService.HasPayload by name.
        if (Marshal.SizeOf<NativeMethods.SHELLTRAYDATA>() != 964)
            throw new Exception("SHELLTRAYDATA must match the 964-byte TRAYNOTIFYDATAW shell32 sends.");
        bool Accepts(IntPtr data, int size) => CrossProcessMessages.HasPayload(
            new NativeMethods.COPYDATASTRUCT { lpData = data, cbData = size }, typeof(NativeMethods.SHELLTRAYDATA));
        if (!Accepts((IntPtr)1, 964) || Accepts((IntPtr)1, 963) || Accepts((IntPtr)1, 16) || Accepts(IntPtr.Zero, 964))
            throw new Exception("Tray WM_COPYDATA must reject short or missing payloads before reading them.");
        Console.WriteLine("PASS: tray WM_COPYDATA payloads shorter than their structure are rejected before marshalling.");

        // R9-A / Н1: AppBar message handlers must bail out gracefully when SHLockShared can't map
        // the sender's shared memory (elevated sender, or sender already gone), not dereference null.
        var appBarManager = new AppBarManager(new ExplorerHelper());
        var badAmd = new NativeMethods.APPBARMSGDATAV3 { hSharedMemory = 0, dwSourceProcessId = 0 };

        var getTaskbarPos = typeof(AppBarManager).GetMethod("appBarMessage_GetTaskbarPos", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("AppBarManager.appBarMessage_GetTaskbarPos is missing.");
        object[] getTaskbarPosArgs = { badAmd, false };
        IntPtr getTaskbarPosResult = (IntPtr)getTaskbarPos.Invoke(appBarManager, getTaskbarPosArgs);
        if (getTaskbarPosResult != IntPtr.Zero || (bool)getTaskbarPosArgs[1])
            throw new Exception("ABM_GETTASKBARPOS must fail gracefully when SHLockShared cannot map the sender's shared memory.");

        var querySetPos = typeof(AppBarManager).GetMethod("appBarMessage_QuerySetPos", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("AppBarManager.appBarMessage_QuerySetPos is missing.");
        object[] querySetPosArgs = { badAmd, false };
        IntPtr querySetPosResult = (IntPtr)querySetPos.Invoke(appBarManager, querySetPosArgs);
        if (querySetPosResult != IntPtr.Zero || (bool)querySetPosArgs[1])
            throw new Exception("ABM_QUERYPOS/SETPOS must fail gracefully when SHLockShared cannot map the sender's shared memory.");
        Console.WriteLine("PASS: AppBar handlers bail out on a failed SHLockShared instead of dereferencing null.");

        // R9-A / Н1, R9-H / K12: the tray window procedure is native-facing (RegisterClass callback)
        // and must never let an exception escape into user32. It is wrapped by NativeCallback.Wrap
        // (not a local rate-limited counter anymore), which reports through the shared CallbackGuard.
        var trayServiceType = Assembly.Load("ManagedShell.WindowsTray").GetType("ManagedShell.WindowsTray.TrayService")
            ?? throw new Exception("TrayService is missing.");
        var trayService = Activator.CreateInstance(trayServiceType);
        var wndProcCore = trayServiceType.GetMethod("WndProcCore", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("TrayService.WndProcCore is missing.");
        var nativeCallbackType = managedShellCommon.GetType("ManagedShell.Common.Native.NativeCallback")
            ?? throw new Exception("NativeCallback primitive is missing.");
        var wndProcDelegateType = Assembly.Load("ManagedShell.Interop").GetType("ManagedShell.Interop.NativeMethods+WndProcDelegate")
            ?? throw new Exception("NativeMethods.WndProcDelegate is missing.");
        var wrapWndProc = nativeCallbackType.GetMethods(BindingFlags.Static | BindingFlags.NonPublic)
            .Single(m => m.Name == "Wrap" && m.GetParameters()[1].ParameterType == wndProcDelegateType);
        Delegate boundWndProcCore = Delegate.CreateDelegate(wndProcDelegateType, trayService, wndProcCore);
        const string wndProcSource = "TrayService: WndProc";
        Delegate wrappedWndProc = (Delegate)wrapWndProc.Invoke(null, new object[] { wndProcSource, boundWndProcCore });
        int before = (int)failureCount.Invoke(null, new object[] { wndProcSource });
        // WM_WINDOWPOSCHANGED with a null lParam: WINDOWPOS.FromMessage marshals null and throws
        // NullReferenceException unboxing it; this must be caught, not thrown into user32.
        wrappedWndProc.DynamicInvoke(IntPtr.Zero, (int)NativeMethods.WM.WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero);
        int after = (int)failureCount.Invoke(null, new object[] { wndProcSource });
        if (after != before + 1)
            throw new Exception("TrayService's NativeCallback-wrapped WndProc must report exceptions via CallbackGuard.");
        Console.WriteLine("PASS: TrayService's WndProc, wrapped by NativeCallback, catches a malformed message payload and reports it via CallbackGuard instead of crashing.");
        CheckWinEventHookForeignThreadDispose();
        Console.WriteLine("PASS: WinEventHook.Dispose from a foreign thread keeps the hook and its delegate rooted until the owning thread's Dispatcher actually unhooks it.");

        foreach (var (typeName, assemblyName) in new (string TypeName, string AssemblyName)[]
        {
            ("UltraWinBar.Utilities.ExplorerMonitor+ExplorerMonitorWindow", null),
            ("UltraWinBar.Utilities.HotkeyManager+HotkeyListenerWindow", null),
            ("ManagedShell.Common.SupportingClasses.NativeWindowEx", "ManagedShell.Common"),
        })
        {
            var owner = assemblyName == null ? typeof(TaskAssignmentManager).Assembly : Assembly.Load(assemblyName);
            var type = owner.GetType(typeName) ?? throw new Exception($"{typeName} is missing.");
            var overridden = type.GetMethod("OnThreadException", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            if (overridden == null)
                throw new Exception($"{typeName} must override OnThreadException; NativeWindow's default silently swallows WndProc exceptions.");
        }
        Console.WriteLine("PASS: native windows override OnThreadException instead of silently swallowing WndProc exceptions.");
        // ManagedShell.WindowsTasks: pointer trust boundary (Н5), per-instance Windows collection
        // (Н15), and the Dispose/Initialize hook lifecycle (Н3).
        CheckMemorySafety();
        CheckBoundedStringRead();
        CheckPerInstanceWindowsCollection();
        CheckDisposeInitializeLifecycle(repositoryRoot);
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr VirtualAlloc(IntPtr lpAddress, UIntPtr dwSize, uint flAllocationType, uint flProtect);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualFree(IntPtr lpAddress, UIntPtr dwSize, uint dwFreeType);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool VirtualProtect(IntPtr lpAddress, UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

    private const uint MEM_COMMIT = 0x1000;
    private const uint MEM_RESERVE = 0x2000;
    private const uint MEM_RELEASE = 0x8000;
    private const uint PAGE_READWRITE = 0x04;
    private const uint PAGE_READONLY = 0x02;
    private const uint PAGE_NOACCESS = 0x01;

    // R9-M / К20: typed access to ManagedShell.Interop.MemorySafety (moved there from
    // ManagedShell.WindowsTasks so AppBarManager/TrayService can share it too) instead of
    // reflecting it by its old namespace-qualified name.
    private static void CheckMemorySafety()
    {
        bool IsReadWritable(IntPtr address, int size) => MemorySafety.IsReadWritable(address, size);
        int GetReadableByteCount(IntPtr address, int maxBytes) => MemorySafety.GetReadableByteCount(address, maxBytes);

        // Garbage inputs must be rejected without ever dereferencing the pointer.
        if (IsReadWritable(IntPtr.Zero, 16)) throw new Exception("Null pointer must be rejected.");
        if (IsReadWritable((IntPtr)1, 16)) throw new Exception("Garbage pointer must be rejected.");
        if (GetReadableByteCount(IntPtr.Zero, 16) != 0) throw new Exception("Null pointer must read as 0 bytes.");
        if (GetReadableByteCount((IntPtr)1, 16) != 0) throw new Exception("Garbage pointer must read as 0 bytes.");

        UIntPtr pageSize = (UIntPtr)4096;

        // An unmapped address: VirtualQuery succeeds (it always describes the region a process
        // *could* touch), but State is MEM_FREE, not MEM_COMMIT.
        IntPtr unmapped = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (unmapped == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc failed.");
        if (!VirtualFree(unmapped, UIntPtr.Zero, MEM_RELEASE)) throw new Exception("Test setup: VirtualFree failed.");
        if (IsReadWritable(unmapped, 16)) throw new Exception("Unmapped (freed) address must be rejected.");
        if (GetReadableByteCount(unmapped, 16) != 0) throw new Exception("Unmapped (freed) address must read as 0 bytes.");

        IntPtr rw = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (rw == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc(PAGE_READWRITE) failed.");
        try
        {
            if (!IsReadWritable(rw, 16)) throw new Exception("A committed read-write page must be accepted.");
            if (!IsReadWritable(IntPtr.Add(rw, 4095), 1)) throw new Exception("The last byte of the region must still be accepted.");
            if (IsReadWritable(IntPtr.Add(rw, 4095), 2)) throw new Exception("A struct crossing the region end must be rejected.");
            if (GetReadableByteCount(rw, 8) != 8) throw new Exception("Readable byte count must not be truncated below what fits.");
        }
        finally
        {
            VirtualFree(rw, UIntPtr.Zero, MEM_RELEASE);
        }

        IntPtr ro = VirtualAlloc(IntPtr.Zero, pageSize, MEM_COMMIT | MEM_RESERVE, PAGE_READONLY);
        if (ro == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc(PAGE_READONLY) failed.");
        try
        {
            if (IsReadWritable(ro, 16)) throw new Exception("A read-only page must be rejected for a read+write access (GETMINRECT overwrites the struct in place).");
            if (GetReadableByteCount(ro, 16) != 16) throw new Exception("A read-only page must still be reported as readable.");
        }
        finally
        {
            VirtualFree(ro, UIntPtr.Zero, MEM_RELEASE);
        }

        Console.WriteLine("PASS: MemorySafety rejects null/garbage/unmapped/read-only pointers and structs crossing the region end, before GETMINRECT would dereference them.");
    }

    // R9-M / К20: typed access to ApplicationWindow.ReadBoundedString instead of reflecting it by
    // name. Also verifies the trust-boundary composition end to end: GetReadableByteCount's
    // computed bound, fed straight into ReadBoundedString, must never let the read cross into an
    // adjacent inaccessible page.
    private static void CheckBoundedStringRead()
    {
        string ReadBoundedString(IntPtr ptr, int maxChars) => ApplicationWindow.ReadBoundedString(ptr, maxChars);

        if (ReadBoundedString(IntPtr.Zero, 10) != string.Empty) throw new Exception("Null pointer must read as empty.");

        IntPtr buffer = Marshal.AllocHGlobal(40); // 20 UTF-16 chars
        try
        {
            // No null terminator anywhere in the buffer: the read must stop at maxChars, not run past it.
            for (int i = 0; i < 20; i++) Marshal.WriteInt16(buffer, i * 2, (short)'A');
            string noTerminator = ReadBoundedString(buffer, 10);
            if (noTerminator.Length != 10 || noTerminator.Any(c => c != 'A'))
                throw new Exception($"A buffer without a null terminator must be read up to maxChars, no further: got \"{noTerminator}\".");

            // A terminator inside the requested length must stop the read there.
            Marshal.WriteInt16(buffer, 0, (short)'A');
            Marshal.WriteInt16(buffer, 2, (short)'B');
            Marshal.WriteInt16(buffer, 4, 0);
            Marshal.WriteInt16(buffer, 6, (short)'C');
            string terminated = ReadBoundedString(buffer, 10);
            if (terminated != "AB") throw new Exception($"A null terminator inside maxChars must stop the read there, got \"{terminated}\".");
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }

        // Two adjacent pages, the second PAGE_NOACCESS, the first filled to the very end with no
        // terminator anywhere: GetReadableByteCount must stop exactly at the page boundary, and
        // ReadBoundedString, given that bound, must read the whole first page without touching the second.
        UIntPtr twoPages = (UIntPtr)8192;
        IntPtr region = VirtualAlloc(IntPtr.Zero, twoPages, MEM_COMMIT | MEM_RESERVE, PAGE_READWRITE);
        if (region == IntPtr.Zero) throw new Exception("Test setup: VirtualAlloc(two pages) failed.");
        try
        {
            if (!VirtualProtect(IntPtr.Add(region, 4096), (UIntPtr)4096, PAGE_NOACCESS, out _))
                throw new Exception("Test setup: VirtualProtect(PAGE_NOACCESS) failed.");
            for (int i = 0; i < 2048; i++) Marshal.WriteInt16(region, i * 2, (short)'A');

            int readableBytes = MemorySafety.GetReadableByteCount(region, 4096 * 2);
            if (readableBytes != 4096) throw new Exception($"Readable byte count must stop exactly at the page boundary, got {readableBytes}.");

            string wholePage = ReadBoundedString(region, readableBytes / sizeof(char));
            if (wholePage.Length != 2048 || wholePage.Any(c => c != 'A'))
                throw new Exception($"Reading up to the page-accurate bound must not cross into the unmapped page, got length {wholePage.Length}.");
        }
        finally
        {
            VirtualFree(region, UIntPtr.Zero, MEM_RELEASE);
        }

        Console.WriteLine("PASS: ApplicationWindow.ReadBoundedString never reads past maxChars, stops at an embedded null terminator, and (combined with GetReadableByteCount) never crosses into an adjacent inaccessible page.");
    }

    // R9-M / К20: typed access to TasksService.Windows instead of reflecting it by name.
    private static void CheckPerInstanceWindowsCollection()
    {
        // Constructing a second instance must not throw (a bug class here is an instance-field
        // DependencyProperty.Register, which throws "already registered" on the second instance).
        var first = new TasksService();
        var second = new TasksService();

        if (first.Windows == null || second.Windows == null)
            throw new Exception("TasksService.Windows must never be null; the DependencyProperty default must not leak through unset.");
        if (ReferenceEquals(first.Windows, second.Windows))
            throw new Exception("TasksService.Windows must be a per-instance collection, not a shared DependencyProperty default.");

        Console.WriteLine("PASS: two TasksService instances construct without throwing, and each gets its own Windows collection.");
    }

    private static void CheckDisposeInitializeLifecycle(System.IO.DirectoryInfo repositoryRoot)
    {
        // Initialize/Dispose touch the live shell for real (SetTaskmanWindow, ITaskbarList
        // delegation on the real Explorer taskbar via setTaskbarListHwnd) and must not run for real
        // against a shared machine's desktop from an automated test. Verify the rollback/zeroing
        // shape of the source instead, per the task's documented fallback for this case.
        string source = System.IO.File.ReadAllText(System.IO.Path.Combine(repositoryRoot.FullName,
            "UltraWinBar", "Support", "vendor", "ManagedShell", "src", "ManagedShell.WindowsTasks", "TasksService.cs"));

        int disposeStart = source.IndexOf("public void Dispose()");
        int disposeEnd = source.IndexOf("private void CategoriesChanged()", disposeStart);
        if (disposeStart < 0 || disposeEnd < 0) throw new Exception("Could not locate TasksService.Dispose to check hook zeroing.");
        string disposeBody = source.Substring(disposeStart, disposeEnd - disposeStart);

        // R9-H: cloakEventHook/moveEventHook (raw static IntPtr handles) became instance-owned
        // WinEventHook fields (cloakHook/moveHook); Dispose() must still null its own field after
        // disposing so a following Initialize reinstalls instead of finding a stale reference.
        int cloakDispose = disposeBody.IndexOf("cloakHook?.Dispose();");
        int cloakZero = disposeBody.IndexOf("cloakHook = null;");
        int moveDispose = disposeBody.IndexOf("moveHook?.Dispose();");
        int moveZero = disposeBody.IndexOf("moveHook = null;");
        int hookWinNulled = disposeBody.IndexOf("_HookWin = null;");
        if (cloakDispose < 0 || cloakZero < 0 || cloakDispose > cloakZero)
            throw new Exception("Dispose must null cloakHook after disposing it, so a following Initialize reinstalls it.");
        if (moveDispose < 0 || moveZero < 0 || moveDispose > moveZero)
            throw new Exception("Dispose must null moveHook after disposing it, so a following Initialize reinstalls it.");
        if (hookWinNulled < 0)
            throw new Exception("Dispose must null _HookWin so a following Initialize does not reuse a destroyed window.");
        Console.WriteLine("PASS: TasksService.Dispose disposes and nulls cloakHook/moveHook, and nulls _HookWin.");

        int initStart = source.IndexOf("internal void Initialize(bool withMultiMonTracking)");
        int initEnd = source.IndexOf("internal void SetTaskCategoryProvider", initStart);
        if (initStart < 0 || initEnd < 0) throw new Exception("Could not locate TasksService.Initialize to check rollback.");
        string initBody = source.Substring(initStart, initEnd - initStart);
        int catchIndex = initBody.IndexOf("catch (Exception ex)");
        if (catchIndex < 0) throw new Exception("Initialize must guard hook/window installation with a catch that rolls back.");
        string catchBody = initBody.Substring(catchIndex);

        if (!catchBody.Contains("cloakHookInstalledHere") || !catchBody.Contains("cloakHook = null;"))
            throw new Exception("A failed Initialize must roll back a cloak hook it installed during this attempt.");
        if (!catchBody.Contains("moveHookInstalledHere") || !catchBody.Contains("moveHook = null;"))
            throw new Exception("A failed Initialize must roll back a move hook it installed during this attempt.");
        if (!catchBody.Contains("_HookWin = null;"))
            throw new Exception("A failed Initialize must null _HookWin so a retry does not leave a second hook window registered.");
        Console.WriteLine("PASS: a failed TasksService.Initialize rolls back only what that attempt installed, instead of leaving a second half-registered hook window for the next retry.");
    }

    // Dispose from a thread other than the one that installed the hook must not touch the hook
    // synchronously (UnhookWinEvent fails off-thread): it must marshal to the owner's Dispatcher and
    // keep the hook - and its delegate - rooted (still in `installed`) until that runs.
    private static void CheckWinEventHookForeignThreadDispose()
    {
        Type hookType = Assembly.Load("ManagedShell.Common").GetType("ManagedShell.Common.Native.WinEventHook")
            ?? throw new Exception("WinEventHook primitive is missing.");
        Type handlerType = hookType.GetNestedType("Handler", BindingFlags.NonPublic)
            ?? throw new Exception("WinEventHook.Handler delegate is missing.");
        uint outOfContext = (uint)hookType.GetField("OutOfContext", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
        MethodInfo handlerMethod = typeof(InteropChecks).GetMethod(nameof(NoOpWinEventHandler), BindingFlags.Static | BindingFlags.NonPublic);
        Delegate handler = Delegate.CreateDelegate(handlerType, handlerMethod);
        var ctor = hookType.GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null,
            new[] { typeof(string), typeof(uint), typeof(uint), handlerType, typeof(uint) }, null)
            ?? throw new Exception("WinEventHook constructor signature changed.");
        var isInstalled = hookType.GetProperty("IsInstalled", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("WinEventHook.IsInstalled is missing.");
        var installedCount = hookType.GetProperty("InstalledCount", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("WinEventHook.InstalledCount is missing.");
        var dispose = hookType.GetMethod("Dispose", BindingFlags.Instance | BindingFlags.Public);

        Dispatcher dispatcherA = null;
        object hookRef = null;
        Exception installError = null;
        var ready = new ManualResetEventSlim(false);
        var threadA = new Thread(() =>
        {
            try
            {
                dispatcherA = Dispatcher.CurrentDispatcher;
                hookRef = ctor.Invoke(new object[] { "foreign-thread dispose test", (uint)0x7FFF, (uint)0x7FFF, handler, outOfContext });
            }
            catch (Exception error) { installError = error; }
            finally { ready.Set(); }
            Dispatcher.Run();
        });
        threadA.IsBackground = true;
        threadA.Start();
        ready.Wait();
        if (installError != null) throw installError;
        if (hookRef == null || !(bool)isInstalled.GetValue(hookRef))
            throw new Exception("Test WinEvent hook failed to install; cannot verify foreign-thread Dispose.");

        try
        {
            int installedBefore = (int)installedCount.GetValue(null);

            // Occupy thread A's dispatcher loop, at the same priority Dispose's BeginInvoke uses
            // (Normal), so the marshalled Unhook cannot run until released and stays FIFO-ordered
            // behind this action.
            var block = new ManualResetEventSlim(false);
            dispatcherA.BeginInvoke(DispatcherPriority.Normal, new Action(() => block.Wait()));

            // Dispose from this (foreign) thread: must not unhook synchronously.
            dispose.Invoke(hookRef, null);

            if (!(bool)isInstalled.GetValue(hookRef))
                throw new Exception("Dispose from a foreign thread must not clear the hook before the owning thread unhooks it.");
            if ((int)installedCount.GetValue(null) != installedBefore)
                throw new Exception("Dispose from a foreign thread must keep the hook (and its delegate) rooted until the owning thread unhooks it.");

            // Let thread A drain its queue: the blocking action, then the marshalled Unhook. Same
            // Normal priority as above keeps this strictly behind Unhook in the FIFO queue.
            block.Set();
            dispatcherA.Invoke(new Action(() => { }), DispatcherPriority.Normal);

            if ((bool)isInstalled.GetValue(hookRef))
                throw new Exception("The owning thread must actually unhook once the marshalled Dispose runs.");
            if ((int)installedCount.GetValue(null) != installedBefore - 1)
                throw new Exception("The hook must leave the rooted set once it is actually unhooked.");
        }
        finally
        {
            dispatcherA.InvokeShutdown();
            threadA.Join();
        }
    }

    private static void NoOpWinEventHandler(uint eventType, IntPtr hwnd, int idObject, int idChild) { }

    internal static void Check(Assembly assembly, List<string> problems, bool strictReturns)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException error) { types = error.Types.Where(t => t != null).ToArray(); }
        var implemented = new HashSet<Type>(types.Where(t => t.IsClass).SelectMany(t => t.GetInterfaces()).Where(i => i.IsImport));

        foreach (var type in types)
        {
            if (type.IsInterface && type.IsImport)
            {
                foreach (var method in type.GetMethods(All))
                {
                    string name = $"{type.FullName}.{method.Name}";
                    bool preserveSig = (method.MethodImplementationFlags & MethodImplAttributes.PreserveSig) != 0;
                    if (!preserveSig && method.ReturnType != typeof(void) && (strictReturns || implemented.Contains(type)))
                        problems.Add($"{name}: non-void return without [PreserveSig] becomes a hidden [out, retval]");
                    if (method.ReturnType == typeof(bool) && !HasMarshal(method.ReturnParameter))
                        problems.Add($"{name}: bool return defaults to 2-byte VARIANT_BOOL");
                    foreach (var parameter in method.GetParameters())
                    {
                        Type element = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
                        if (element == typeof(bool) && !HasMarshal(parameter))
                            problems.Add($"{name}({parameter.Name}): bool defaults to 2-byte VARIANT_BOOL");
                        if (element == typeof(object) && !HasMarshal(parameter))
                            problems.Add($"{name}({parameter.Name}): object defaults to VARIANT");
                    }
                }
            }

            foreach (var method in type.GetMethods(All).Where(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0))
            {
                foreach (var parameter in method.GetParameters())
                {
                    CheckHandle($"{type.FullName}.{method.Name}", parameter, problems);
                    if (typeof(Delegate).IsAssignableFrom(parameter.ParameterType) &&
                        parameter.ParameterType.GetMethod("Invoke") is MethodInfo invoke)
                        foreach (var callbackParameter in invoke.GetParameters())
                            CheckHandle(parameter.ParameterType.FullName, callbackParameter, problems);
                }
            }
        }
    }

    private static bool HasMarshal(ParameterInfo parameter) => (parameter.Attributes & ParameterAttributes.HasFieldMarshal) != 0;

    private static void CheckHandle(string owner, ParameterInfo parameter, List<string> problems)
    {
        if (parameter.Name == null || !HandleName.IsMatch(parameter.Name)) return;
        Type element = parameter.ParameterType.IsByRef ? parameter.ParameterType.GetElementType() : parameter.ParameterType;
        if (element == typeof(int) || element == typeof(uint) || element == typeof(short) || element == typeof(ushort))
            problems.Add($"{owner}({parameter.Name}): pointer-sized value declared as {element.Name}");
    }
}
