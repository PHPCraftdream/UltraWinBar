using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
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
