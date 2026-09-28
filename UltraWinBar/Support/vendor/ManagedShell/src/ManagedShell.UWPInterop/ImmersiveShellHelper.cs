using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using ManagedShell.UWPInterop.Interfaces;
using System;
using System.Runtime.InteropServices;

namespace ManagedShell.UWPInterop
{
    public static class ImmersiveShellHelper
    {
        private static Guid CLSID_ShellExperienceManagerFactory = new Guid("2e8fcb18-a0ee-41ad-8ef8-77fb3a370ca5");
        private static Guid IID_ActionCenterExperienceManager = new Guid("df65db57-d504-456e-8bd7-004ce308d8d9");
        private static Guid IID_ControlCenterExperienceManager = new Guid("d669a58e-6b18-4d1d-9004-a8862adb0a20");
        private static Guid IID_NetworkFlyoutExperienceManager = new Guid("e44f17e6-ab85-409c-8d01-17d74bec150e");
        private static Guid IID_NetworkFlyoutExperienceManager_20H1 = new Guid("c9ddc674-b44b-4c67-9d79-2b237d9be05a");
        private static Guid IID_TrayBatteryFlyoutExperienceManager = new Guid("0a73aedc-1c68-410d-8d53-63af80951e8f");
        private static Guid IID_TrayClockFlyoutExperienceManager = new Guid("b1604325-6b59-427b-bf1b-80a2db02d3d8");
        private static Guid IID_TrayMtcUvcFlyoutExperienceManager = new Guid("7154c95d-c519-49bd-a97e-645bbfabe111");

        // Explorer-hosted proxies: RPC_E_DISCONNECTED, RPC_S_SERVER_UNAVAILABLE, CO_E_OBJNOTCONNECTED,
        // RPC_E_SERVER_DIED, RPC_E_SERVER_DIED_DNE all mean the server side (explorer.exe) is gone.
        private const int RPC_E_DISCONNECTED = unchecked((int)0x80010108);
        private const int RPC_S_SERVER_UNAVAILABLE = unchecked((int)0x800706BA);
        private const int CO_E_OBJNOTCONNECTED = unchecked((int)0x800401FD);
        private const int RPC_E_SERVER_DIED = unchecked((int)0x80010007);
        private const int RPC_E_SERVER_DIED_DNE = unchecked((int)0x80010012);

        private static readonly object _gate = new object();

        private static Interfaces.IServiceProvider _immersiveShell;
        private static IShellExperienceManagerFactory _shellExperienceManagerFactory;
        private static IActionCenterExperienceManager _actionCenterExperienceManager;
        private static IControlCenterExperienceManager _controlCenterExperienceManager;
        private static INetworkFlyoutExperienceManager _networkFlyoutExperienceManager;
        private static INetworkFlyoutExperienceManager_20H1 _networkFlyoutExperienceManager_20H1;
        private static ITrayBatteryFlyoutExperienceManager _trayBatteryFlyoutExperienceManager;
        private static ITrayClockFlyoutExperienceManager _trayClockFlyoutExperienceManager;
        private static ITrayMtcUvcFlyoutExperienceManager _trayMtcUvcFlyoutExperienceManager;

        // Drops every cached Explorer-hosted COM object. Call after explorer.exe restarts, since the
        // old proxies are permanently dead; the next use recreates them lazily. Idempotent.
        public static void Reset()
        {
            lock (_gate)
            {
                Release(ref _trayMtcUvcFlyoutExperienceManager);
                Release(ref _trayClockFlyoutExperienceManager);
                Release(ref _trayBatteryFlyoutExperienceManager);
                Release(ref _networkFlyoutExperienceManager_20H1);
                Release(ref _networkFlyoutExperienceManager);
                Release(ref _controlCenterExperienceManager);
                Release(ref _actionCenterExperienceManager);
                Release(ref _shellExperienceManagerFactory);
                Release(ref _immersiveShell);
            }
        }

        // Clears a cached field and releases its RCW; safe on an already-null field.
        private static void Release<T>(ref T comObject) where T : class
        {
            T stale = comObject;
            comObject = null;
            if (stale == null) return;

            try
            {
                if (Marshal.IsComObject(stale))
                {
                    Marshal.FinalReleaseComObject(stale);
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ImmersiveShell: Unable to release {typeof(T).Name}: {ex.Message}");
            }
        }

        private static bool IsDisconnected(int hr) =>
            hr == RPC_E_DISCONNECTED || hr == RPC_S_SERVER_UNAVAILABLE || hr == CO_E_OBJNOTCONNECTED ||
            hr == RPC_E_SERVER_DIED || hr == RPC_E_SERVER_DIED_DNE;

        private static bool IsDisconnected(Exception ex) =>
            ex is InvalidComObjectException || (ex is COMException && IsDisconnected(ex.HResult));

        // Runs a call against a cached Explorer-hosted object; on a severed proxy, drops every
        // cache and retries once so callers stay simple (no per-call-site reset/retry logic).
        private static void InvokeWithDisconnectRetry(string opName, Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex) when (IsDisconnected(ex))
            {
                ShellLogger.Warning($"ImmersiveShell: {opName} found a severed Explorer proxy ({ex.Message}); resetting and retrying.");
                Reset();
                try
                {
                    action();
                }
                catch (Exception ex2)
                {
                    ShellLogger.Warning($"ImmersiveShell: {opName} failed after reset: {ex2}");
                }
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ImmersiveShell: {opName} failed: {ex}");
            }
        }

        public static void AllowExplorerFocus()
        {
            // When invoking a flyout, the shell will attempt to make it the foreground window.
            // When that fails, the flyout may not show, or input may not work as expected.
            // Explicity allow Explorer to do this so that flyouts work consistently.
            try
            {
                Interop.NativeMethods.GetWindowThreadProcessId(Interop.NativeMethods.FindWindowEx(IntPtr.Zero, IntPtr.Zero, "Progman", "Program Manager"), out uint procId);
                Interop.NativeMethods.AllowSetForegroundWindow(procId);
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ImmersiveShell: Unable to allow Explorer to set the foreground window: {ex}");
            }
        }

        #region Interface helpers
        public static Interfaces.IServiceProvider GetImmersiveShell()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: ImmersiveShell unsupported");
                return null;
            }

            try
            {
                _immersiveShell ??= (Interfaces.IServiceProvider)new CImmersiveShell();
                return _immersiveShell;
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ImmersiveShell: Unable to create ImmersiveShell: {ex}");
                return null;
            }
        }

        public static IShellExperienceManagerFactory GetShellExperienceManagerFactory()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: IShellExperienceManagerFactory unsupported");
                return null;
            }

            try
            {
                if (GetImmersiveShell().QueryService(CLSID_ShellExperienceManagerFactory, CLSID_ShellExperienceManagerFactory, out object factoryObj) == 0)
                {
                    return (IShellExperienceManagerFactory)factoryObj;
                }

                ShellLogger.Warning("ImmersiveShell: Unable to query IShellExperienceManagerFactory");
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ImmersiveShell: Unable to create IShellExperienceManagerFactory: {ex}");
            }
            return null;
        }

        internal static IntPtr GetExperienceManagerFromFactory(string experienceManager)
        {
            _shellExperienceManagerFactory ??= GetShellExperienceManagerFactory();
            if (_shellExperienceManagerFactory == null) return IntPtr.Zero;

            IntPtr hString = IntPtr.Zero;
            try
            {
                if (NativeMethods.WindowsCreateString(experienceManager, experienceManager.Length, ref hString) != 0)
                {
                    ShellLogger.Warning("ImmersiveShell: Unable to create experience manager string");
                    return IntPtr.Zero;
                }

                _shellExperienceManagerFactory.GetExperienceManager(hString, out IntPtr pExperienceManagerInterface);
                return pExperienceManagerInterface;
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ImmersiveShell: Unable to create experience manager: {ex}");
                return IntPtr.Zero;
            }
            finally
            {
                // Must run even if GetExperienceManager threw, or the HSTRING leaks.
                if (hString != IntPtr.Zero) NativeMethods.WindowsDeleteString(hString);
            }
        }

        // Queries iid off the factory-created experience manager and wraps it as T, releasing both
        // raw COM references (the factory's pp and QueryInterface's out) so only the returned RCW
        // holds a reference.
        private static T QueryExperienceManager<T>(string experienceManagerName, ref Guid iid, string typeName) where T : class
        {
            IntPtr pExperienceManagerInterface = IntPtr.Zero;
            IntPtr pManager = IntPtr.Zero;
            try
            {
                pExperienceManagerInterface = GetExperienceManagerFromFactory(experienceManagerName);
                if (pExperienceManagerInterface == IntPtr.Zero) return null;

                if (Marshal.QueryInterface(pExperienceManagerInterface, ref iid, out pManager) == 0)
                {
                    return (T)Marshal.GetObjectForIUnknown(pManager);
                }

                ShellLogger.Warning($"ImmersiveShell: Unable to query {typeName}");
            }
            catch (Exception ex)
            {
                ShellLogger.Warning($"ImmersiveShell: Unable to get {typeName}: {ex}");
            }
            finally
            {
                if (pExperienceManagerInterface != IntPtr.Zero) Marshal.Release(pExperienceManagerInterface);
                if (pManager != IntPtr.Zero) Marshal.Release(pManager);
            }
            return null;
        }

        public static IActionCenterExperienceManager GetActionCenterExperienceManager()
        {
            if (!EnvironmentHelper.IsWindows10RS4OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: IActionCenterExperienceManager unsupported");
                return null;
            }

            return QueryExperienceManager<IActionCenterExperienceManager>(
                "Windows.Internal.ShellExperience.ActionCenter", ref IID_ActionCenterExperienceManager, nameof(IActionCenterExperienceManager));
        }

        public static IControlCenterExperienceManager GetControlCenterExperienceManager()
        {
            if (!EnvironmentHelper.IsWindows11OrBetter || EnvironmentHelper.IsWindows1124H2OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: IControlCenterExperienceManager unsupported");
                return null;
            }

            return QueryExperienceManager<IControlCenterExperienceManager>(
                "Windows.Internal.ShellExperience.ControlCenter", ref IID_ControlCenterExperienceManager, nameof(IControlCenterExperienceManager));
        }

        internal static INetworkFlyoutExperienceManager GetNetworkExperienceManager()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter || EnvironmentHelper.IsWindows1020H1OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: INetworkFlyoutExperienceManager unsupported");
                return null;
            }

            return QueryExperienceManager<INetworkFlyoutExperienceManager>(
                "Windows.Internal.ShellExperience.NetworkFlyout", ref IID_NetworkFlyoutExperienceManager, nameof(INetworkFlyoutExperienceManager));
        }

        internal static INetworkFlyoutExperienceManager_20H1 GetNetworkExperienceManager_20H1()
        {
            if (!EnvironmentHelper.IsWindows1020H1OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: INetworkFlyoutExperienceManager_20H1 unsupported");
                return null;
            }

            return QueryExperienceManager<INetworkFlyoutExperienceManager_20H1>(
                "Windows.Internal.ShellExperience.NetworkFlyout", ref IID_NetworkFlyoutExperienceManager_20H1, nameof(INetworkFlyoutExperienceManager_20H1));
        }

        internal static ITrayBatteryFlyoutExperienceManager GetBatteryExperienceManager()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter || EnvironmentHelper.IsWindows1124H2OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: ITrayBatteryFlyoutExperienceManager unsupported");
                return null;
            }

            return QueryExperienceManager<ITrayBatteryFlyoutExperienceManager>(
                "Windows.Internal.ShellExperience.TrayBatteryFlyout", ref IID_TrayBatteryFlyoutExperienceManager, nameof(ITrayBatteryFlyoutExperienceManager));
        }

        internal static ITrayClockFlyoutExperienceManager GetTrayClockFlyoutExperienceManager()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: ITrayClockFlyoutExperienceManager unsupported");
                return null;
            }

            return QueryExperienceManager<ITrayClockFlyoutExperienceManager>(
                "Windows.Internal.ShellExperience.TrayClockFlyout", ref IID_TrayClockFlyoutExperienceManager, nameof(ITrayClockFlyoutExperienceManager));
        }

        internal static ITrayMtcUvcFlyoutExperienceManager GetMtcUtcExperienceManager()
        {
            if (!EnvironmentHelper.IsWindows10OrBetter)
            {
                ShellLogger.Error("ImmersiveShell: ITrayMtcUvcFlyoutExperienceManager unsupported");
                return null;
            }

            return QueryExperienceManager<ITrayMtcUvcFlyoutExperienceManager>(
                "Windows.Internal.ShellExperience.MtcUvc", ref IID_TrayMtcUvcFlyoutExperienceManager, nameof(ITrayMtcUvcFlyoutExperienceManager));
        }
        #endregion

        #region Experience manager helpers
        public static void ShowBatteryFlyout(Interop.NativeMethods.Rect anchorRect)
        {
            AllowExplorerFocus();
            InvokeWithDisconnectRetry("show battery flyout", () =>
            {
                _trayBatteryFlyoutExperienceManager ??= GetBatteryExperienceManager();
                _trayBatteryFlyoutExperienceManager?.ShowFlyout(new Windows.Foundation.Rect(anchorRect.Left, anchorRect.Top, anchorRect.Width, anchorRect.Height));
            });
        }

        public static void ShowClockFlyout(Interop.NativeMethods.Rect anchorRect)
        {
            AllowExplorerFocus();
            InvokeWithDisconnectRetry("show clock flyout", () =>
            {
                _trayClockFlyoutExperienceManager ??= GetTrayClockFlyoutExperienceManager();
                _trayClockFlyoutExperienceManager?.ShowFlyout(new Windows.Foundation.Rect(anchorRect.Left, anchorRect.Top, anchorRect.Width, anchorRect.Height));
            });
        }

        public static void ShowSoundFlyout(Interop.NativeMethods.Rect anchorRect)
        {
            AllowExplorerFocus();
            InvokeWithDisconnectRetry("show sound flyout", () =>
            {
                _trayMtcUvcFlyoutExperienceManager ??= GetMtcUtcExperienceManager();
                _trayMtcUvcFlyoutExperienceManager?.ShowFlyout(new Windows.Foundation.Rect(anchorRect.Left, anchorRect.Top, anchorRect.Width, anchorRect.Height));
            });
        }

        public static void ShowNetworkFlyout(Interop.NativeMethods.Rect anchorRect)
        {
            AllowExplorerFocus();
            InvokeWithDisconnectRetry("show network flyout", () =>
            {
                if (EnvironmentHelper.IsWindows1020H1OrBetter)
                {
                    _networkFlyoutExperienceManager_20H1 ??= GetNetworkExperienceManager_20H1();
                    _networkFlyoutExperienceManager_20H1?.ShowFlyout(new Windows.Foundation.Rect(anchorRect.Left, anchorRect.Top, anchorRect.Width, anchorRect.Height), 0);
                }
                else
                {
                    _networkFlyoutExperienceManager ??= GetNetworkExperienceManager();
                    _networkFlyoutExperienceManager?.ShowFlyout(new Windows.Foundation.Rect(anchorRect.Left, anchorRect.Top, anchorRect.Width, anchorRect.Height));
                }
            });
        }

        public static void ShowActionCenter()
        {
            AllowExplorerFocus();
            InvokeWithDisconnectRetry("show action center", () =>
            {
                _actionCenterExperienceManager ??= GetActionCenterExperienceManager();
                _actionCenterExperienceManager?.HotKeyInvoked(0);
            });
        }

        public static void ShowControlCenter()
        {
            AllowExplorerFocus();
            InvokeWithDisconnectRetry("show control center", () =>
            {
                _controlCenterExperienceManager ??= GetControlCenterExperienceManager();
                _controlCenterExperienceManager?.HotKeyInvoked(0);
            });
        }
        #endregion
    }
}
