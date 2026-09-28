using ManagedShell.AppBar;
using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.Common.SupportingClasses;
using ManagedShell.UWPInterop;
using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace UltraWinBar.Utilities
{
    public partial class StartMenuMonitor
    {
        #region Immersive launcher interfaces

        // Most managed interface definitions c/o https://github.com/MishaProductions/CustomShell/

        enum IMMERSIVE_MONITOR_FILTER_FLAGS
        {
            IMMERSIVE_MONITOR_FILTER_FLAGS_NONE = 0x0,
            IMMERSIVE_MONITOR_FILTER_FLAGS_DISABLE_TRAY = 0x1,
        }

        [ComImport]
        [Guid("880b26f8-9197-43d0-8045-8702d0d72000")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveMonitor
        {
            [PreserveSig] int GetIdentity(out uint pIdentity);
            [PreserveSig] int Append([MarshalAs(UnmanagedType.IUnknown)] object unknown);
            [PreserveSig] int GetHandle(out nint phMonitor);
            [PreserveSig] int IsConnected([MarshalAs(UnmanagedType.Bool)] out bool pfConnected);
            [PreserveSig] int IsPrimary([MarshalAs(UnmanagedType.Bool)] out bool pfPrimary);
            [PreserveSig] int GetTrustLevel(out uint level);
            [PreserveSig] int GetDisplayRect(out ManagedShell.Interop.NativeMethods.Rect prcDisplayRect);
            [PreserveSig] int GetOrientation(out uint pdwOrientation);
            [PreserveSig] int GetWorkArea(out ManagedShell.Interop.NativeMethods.Rect prcWorkArea);
            [PreserveSig] int IsEqual(IImmersiveMonitor pMonitor, [MarshalAs(UnmanagedType.Bool)] out bool pfEqual);
            [PreserveSig] int GetTrustLevel2(out uint level);
            [PreserveSig] int GetEffectiveDpi(out uint dpiX, out uint dpiY);
            [PreserveSig] int GetFilterFlags(out IMMERSIVE_MONITOR_FILTER_FLAGS flags);
        }

        enum IMMERSIVE_MONITOR_MOVE_DIRECTION
        {
            IMMD_PREVIOUS = 0,
            IMMD_NEXT = 1
        }

        [ComImport]
        [Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveMonitorManager
        {
            [PreserveSig] int GetCount(out uint pcMonitors);
            [PreserveSig] int GetConnectedCount(out uint pcMonitors);
            [PreserveSig] int GetAt(uint idxMonitor, out IImmersiveMonitor monitor);
            [PreserveSig] int GetFromHandle(nint monitor, out IImmersiveMonitor monitor2);
            [PreserveSig] int GetFromIdentity(uint identity, out IImmersiveMonitor monitor);
            [PreserveSig] int GetImmersiveProxyMonitor(out IImmersiveMonitor monitor);
            [PreserveSig] int QueryService(nint monit, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            [PreserveSig] int QueryServiceByIdentity(uint monit, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            [PreserveSig] int QueryServiceFromWindow(nint hwnd, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            [PreserveSig] int QueryServiceFromPoint(nint point, ref Guid guidService, ref Guid riid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
            [PreserveSig] int GetNextImmersiveMonitor(IMMERSIVE_MONITOR_MOVE_DIRECTION direction, IImmersiveMonitor monitor, out IImmersiveMonitor monitorout);
            [PreserveSig] int GetMonitorArray([MarshalAs(UnmanagedType.IUnknown)] out object array);
            [PreserveSig] int SetFilter([MarshalAs(UnmanagedType.IUnknown)] object filter);
        }

        enum IMMERSIVELAUNCHERSHOWMETHOD
        {
            ILSM_INVALID = 0x0,
            ILSM_HSHELLTASKMAN = 0x1,
            ILSM_IMMERSIVEBACKGROUND = 0x4,
            ILSM_APPCLOSED = 0x6,
            ILSM_STARTBUTTON = 0xB,
            ILSM_RETAILDEMO_EDUCATIONAPP = 0xC,
            ILSM_BACK = 0xD,
            ILSM_SESSIONONUNLOCK = 0xE
        }

        enum IMMERSIVELAUNCHERSHOWFLAGS
        {
            ILSF_NONE = 0x0,
            ILSF_IGNORE_SET_FOREGROUND_ERROR = 0x4,
        }

        enum IMMERSIVELAUNCHERDISMISSMETHOD
        {
            ILDM_INVALID = 0x0,
            ILDM_HSHELLTASKMAN = 0x1,
            ILDM_STARTCHARM = 0x2,
            ILDM_BACKGESTURE = 0x3,
            ILDM_ESCAPEKEY = 0x4,
            ILDM_SHOWDESKTOP = 0x5,
            ILDM_STARTTIP = 0x6,
            ILDM_GENERIC_NONANIMATING = 0x7,
        }

        [ComImport]
        [Guid("d8d60399-a0f1-f987-5551-321fd1b49864")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveLauncher_Win10RS1
        {
            [PreserveSig] int ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERSHOWFLAGS showFlags);
            [PreserveSig] int Dismiss(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            [PreserveSig] int Dismiss2(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            [PreserveSig] int DismissSynchronouslyWithoutTransition();
            [PreserveSig] int IsVisible([MarshalAs(UnmanagedType.Bool)] out bool p0);
            [PreserveSig] int OnStartButtonPressed(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            [PreserveSig] int SetForeground();
            [PreserveSig] int ConnectToMonitor(IImmersiveMonitor monitor);
            [PreserveSig] int GetMonitor(out IImmersiveMonitor monitor);
            [PreserveSig] int OnFirstSignAnimationFinished();
            [PreserveSig] int Prelaunch();
        }

        [ComImport]
        [Guid("93f91f5a-a4ca-4205-9beb-ce4d17c708f9")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IImmersiveLauncher_Win81
        {
            [PreserveSig] int ShowStartView(IMMERSIVELAUNCHERSHOWMETHOD showMethod, IMMERSIVELAUNCHERSHOWFLAGS showFlags);
            [PreserveSig] int Unknown2();
            [PreserveSig] int Unknown3();
            [PreserveSig] int Unknown4();
            [PreserveSig] int Unknown5();
            [PreserveSig] int Dismiss(IMMERSIVELAUNCHERDISMISSMETHOD dismissMethod);
            [PreserveSig] int Unknown7();
            [PreserveSig] int IsVisible([MarshalAs(UnmanagedType.Bool)] out bool p0);
            [PreserveSig] int Unknown9();
            [PreserveSig] int Unknown10();
            [PreserveSig] int Unknown11();
            [PreserveSig] int Unknown12();
            [PreserveSig] int Unknown13();
            [PreserveSig] int Unknown14();
            [PreserveSig] int Unknown15();
            [PreserveSig] int ConnectToMonitor(IImmersiveMonitor monitor);
            [PreserveSig] int GetMonitor(out IImmersiveMonitor monitor);
        }

        static Guid CLSID_ImmersiveMonitorManager = new Guid("47094e3a-0cf2-430f-806f-cf9e4f0f12dd");
        static Guid IID_ImmersiveMonitorManager = new Guid("4d4c1e64-e410-4faa-bafa-59ca069bfec2");
        static Guid CLSID_ImmersiveLauncher = new Guid("6f86e01c-c649-4d61-be23-f1322ddeca9d");
        static Guid IID_ImmersiveLauncher_Win10RS1 = new Guid("d8d60399-a0f1-f987-5551-321fd1b49864");
        static Guid IID_ImmersiveLauncher_Win81 = new Guid("93f91f5a-a4ca-4205-9beb-ce4d17c708f9");
        #endregion
    }
}
