using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using ManagedShell.AppBar;
using ManagedShell.Interop;
using UltraWinBar.Utilities;

// Mechanical guard for the interop bug class behind the 2026-09-28 crash: signatures the
// marshaller silently rewrites (hidden [out, retval], VARIANT_BOOL, VARIANT) or truncates.
internal static class InteropChecks
{
    private const BindingFlags All = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public |
                                     BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
    private static readonly Regex HandleName = new Regex(@"^(h[A-Z]\w*|(?i:hwnd|hkl|wparam|lparam|lresult|hook|\w*handle))$");

    internal static void Run()
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

        var guard = typeof(TaskAssignmentManager).Assembly.GetType("UltraWinBar.Utilities.CallbackGuard")
            ?? throw new Exception("Native callback exception barrier is missing.");
        var report = guard.GetMethod("Report", BindingFlags.Static | BindingFlags.NonPublic);
        var failureCount = guard.GetMethod("FailureCount", BindingFlags.Static | BindingFlags.NonPublic);
        for (int i = 0; i < 25; i++) report.Invoke(null, new object[] { "test source", new InvalidOperationException("x") });
        if ((int)failureCount.Invoke(null, new object[] { "test source" }) != 25)
            throw new Exception("Callback failures must be counted per source without throwing.");
        Console.WriteLine("PASS: native callback failures are reported per source without escaping.");

        // Hooks only through the rooted, exception-safe primitives.
        var hookOwners = new HashSet<string> { "UltraWinBar.Utilities.WinEventHook", "UltraWinBar.Utilities.LowLevelMouseHook" };
        foreach (var type in typeof(TaskAssignmentManager).Assembly.GetTypes())
            foreach (var method in type.GetMethods(All).Where(m => (m.Attributes & MethodAttributes.PinvokeImpl) != 0))
                if ((method.Name == "SetWinEventHook" || method.Name == "SetWindowsHookEx") && !hookOwners.Contains(type.FullName))
                    throw new Exception($"{type.FullName} installs hooks directly; use WinEventHook or LowLevelMouseHook.");
        Console.WriteLine("PASS: hooks are installed only through the rooted, exception-safe hook primitives.");

        var tray = Assembly.Load("ManagedShell.WindowsTray");
        var hasPayload = tray.GetType("ManagedShell.WindowsTray.TrayService")?.GetMethod("HasPayload", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("Tray WM_COPYDATA payload validation is missing.");
        var interop = Assembly.Load("ManagedShell.Interop");
        Type copyDataType = interop.GetType("ManagedShell.Interop.NativeMethods+COPYDATASTRUCT");
        Type trayDataType = interop.GetType("ManagedShell.Interop.NativeMethods+SHELLTRAYDATA");
        if (Marshal.SizeOf(trayDataType) != 964)
            throw new Exception("SHELLTRAYDATA must match the 964-byte TRAYNOTIFYDATAW shell32 sends.");
        bool Accepts(IntPtr data, int size)
        {
            object copyData = Activator.CreateInstance(copyDataType);
            copyDataType.GetField("lpData").SetValue(copyData, data);
            copyDataType.GetField("cbData").SetValue(copyData, size);
            return (bool)hasPayload.Invoke(null, new[] { copyData, trayDataType });
        }
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

        // R9-A / Н1: the tray window procedure is native-facing (RegisterClass callback) and must
        // never let an exception escape into user32; it must report failures instead of crashing.
        var trayServiceType = Assembly.Load("ManagedShell.WindowsTray").GetType("ManagedShell.WindowsTray.TrayService")
            ?? throw new Exception("TrayService is missing.");
        var trayService = Activator.CreateInstance(trayServiceType);
        var wndProc = trayServiceType.GetMethod("WndProc", BindingFlags.Instance | BindingFlags.NonPublic)
            ?? throw new Exception("TrayService.WndProc is missing.");
        var wndProcFailureCount = trayServiceType.GetField("wndProcFailureCount", BindingFlags.Static | BindingFlags.NonPublic)
            ?? throw new Exception("TrayService.WndProc exception barrier counter is missing.");
        int before = (int)wndProcFailureCount.GetValue(null);
        // WM_WINDOWPOSCHANGED with a null lParam: WINDOWPOS.FromMessage marshals null and throws
        // NullReferenceException unboxing it; this must be caught, not thrown into user32.
        wndProc.Invoke(trayService, new object[] { IntPtr.Zero, (int)NativeMethods.WM.WINDOWPOSCHANGED, IntPtr.Zero, IntPtr.Zero });
        int after = (int)wndProcFailureCount.GetValue(null);
        if (after != before + 1)
            throw new Exception("TrayService.WndProc must catch exceptions from its body and report them via the rate-limited counter.");
        Console.WriteLine("PASS: TrayService.WndProc catches a malformed message payload and reports it instead of crashing.");
    }

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
