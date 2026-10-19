using ManagedShell.Common.Helpers;
using ManagedShell.Common.Logging;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Threading;
using static ManagedShell.Interop.NativeMethods;

namespace ManagedShell.WindowsTray
{
    public  class ExplorerTrayService
    {
        private SystrayDelegate trayDelegate;

        public ExplorerTrayService()
        {
        }

        internal void SetSystrayCallback(SystrayDelegate theDelegate)
        {
            trayDelegate = theDelegate;
        }

        // UltraWinBar: reading waits on Explorer's UI thread (2-3 s at logon), so it runs on its own thread. The items go to
        // the dispatcher thread, which owns the tray collections, in Explorer's order; completed runs after them.
        // False when there is nothing to read (we are the shell), and completed is never called.
        internal bool BeginRun(Dispatcher dispatcher, Action completed)
        {
            if (EnvironmentHelper.IsAppRunningAsShell || trayDelegate == null)
            {
                return false;
            }

            bool autoTrayEnabled = GetAutoTrayEnabled();
            // Found before our own Shell_TrayWnd exists, so it cannot be taken for Explorer's.
            IntPtr toolbarHwnd = FindExplorerTrayToolbarHwnd();

            var reader = new Thread(() =>
            {
                long started = Environment.TickCount64;
                List<SafeNotifyIconData> items = ReadItems(autoTrayEnabled, toolbarHwnd);
                ShellLogger.Debug($"ExplorerTrayService: Read {items.Count} icons in {Environment.TickCount64 - started} ms");
                try
                {
                    dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                    {
                        Deliver(items);
                        completed();
                    }));
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"ExplorerTrayService: Unable to hand over items: {e.Message}");
                }
            })
            {
                IsBackground = true,
                Name = "ExplorerTrayService: read"
            };
            reader.SetApartmentState(ApartmentState.STA);
            reader.Start();
            return true;
        }

        private List<SafeNotifyIconData> ReadItems(bool autoTrayEnabled, IntPtr toolbarHwnd)
        {
            var items = new List<SafeNotifyIconData>();

            if (autoTrayEnabled)
            {
                // we can't get tray icons that are in the hidden area, so disable that temporarily if enabled
                TrayNotify trayNotify = null;
                try
                {
                    trayNotify = new TrayNotify();

                    SetAutoTrayEnabled(trayNotify, false);
                    try
                    {
                        GetTrayItems(toolbarHwnd, items);
                    }
                    finally
                    {
                        SetAutoTrayEnabled(trayNotify, true);
                    }
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"ExplorerTrayService: Unable to get items using ITrayNotify: {e.Message}");
                }
                finally
                {
                    if (trayNotify != null)
                    {
                        Marshal.ReleaseComObject(trayNotify);
                    }
                }
            }
            else
            {
                try
                {
                    GetTrayItems(toolbarHwnd, items);
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"ExplorerTrayService: Unable to get items: {e.Message}");
                }
            }

            return items;
        }

        private void Deliver(List<SafeNotifyIconData> items)
        {
            foreach (SafeNotifyIconData nid in items)
            {
                try
                {
                    if (!trayDelegate((uint)NIM.NIM_ADD, nid))
                    {
                        ShellLogger.Debug("ExplorerTrayService: Ignored notify icon message");
                    }
                }
                catch (Exception e)
                {
                    ShellLogger.Debug($"ExplorerTrayService: Unable to add {nid.szTip}: {e.Message}");
                }
            }
        }

        private void GetTrayItems(IntPtr toolbarHwnd, List<SafeNotifyIconData> items)
        {
            if (toolbarHwnd == IntPtr.Zero)
            {
                return;
            }

            int count = GetNumTrayIcons(toolbarHwnd);

            if (count < 1)
            {
                return;
            }

            GetWindowThreadProcessId(toolbarHwnd, out var processId);
            IntPtr hProcess = OpenProcess(ProcessAccessFlags.All, false, (int)processId);
            if (hProcess == IntPtr.Zero)
            {
                ShellLogger.Debug("ExplorerTrayService: OpenProcess failed; skipping tray item import");
                return;
            }

            try
            {
                IntPtr hBuffer = VirtualAllocEx(hProcess, IntPtr.Zero, (uint)Marshal.SizeOf(new TBBUTTON()), AllocationType.Commit,
                    MemoryProtection.ReadWrite);
                if (hBuffer == IntPtr.Zero)
                {
                    ShellLogger.Debug("ExplorerTrayService: VirtualAllocEx failed; skipping tray item import");
                    return;
                }

                try
                {
                    for (int i = 0; i < count; i++)
                    {
                        TrayItem trayItem = GetTrayItem(i, hBuffer, hProcess, toolbarHwnd);

                        if (trayItem.hWnd == IntPtr.Zero || !IsWindow(trayItem.hWnd))
                        {
                            ShellLogger.Debug($"ExplorerTrayService: Ignored notify icon {trayItem.szIconText} due to invalid handle");
                            continue;
                        }

                        items.Add(GetTrayItemIconData(trayItem));
                    }
                }
                finally
                {
                    VirtualFreeEx(hProcess, hBuffer, 0, AllocationType.Release);
                }
            }
            finally
            {
                CloseHandle(hProcess);
            }
        }

        private IntPtr FindExplorerTrayToolbarHwnd()
        {
            IntPtr hwnd = FindWindow("Shell_TrayWnd", "");

            if (hwnd != IntPtr.Zero)
            {
                hwnd = FindWindowEx(hwnd, IntPtr.Zero, "TrayNotifyWnd", "");

                if (hwnd != IntPtr.Zero)
                {
                    hwnd = FindWindowEx(hwnd, IntPtr.Zero, "SysPager", "");

                    if (hwnd != IntPtr.Zero)
                    {
                        hwnd = FindWindowEx(hwnd, IntPtr.Zero, "ToolbarWindow32", IntPtr.Zero);

                        return hwnd;
                    }
                }
            }

            return IntPtr.Zero;
        }

        private int GetNumTrayIcons(IntPtr toolbarHwnd)
        {
            return (int)SendMessage(toolbarHwnd, (int)TB.BUTTONCOUNT, IntPtr.Zero, IntPtr.Zero);
        }

        private TrayItem GetTrayItem(int i, IntPtr hBuffer, IntPtr hProcess, IntPtr toolbarHwnd)
        {
            TBBUTTON tbButton = new TBBUTTON();
            TrayItem trayItem = new TrayItem();
            IntPtr hTBButton = Marshal.AllocHGlobal(Marshal.SizeOf(tbButton));
            IntPtr hTrayItem = Marshal.AllocHGlobal(Marshal.SizeOf(trayItem));

            try
            {
                IntPtr msgSuccess = SendMessage(toolbarHwnd, (int)TB.GETBUTTON, (IntPtr)i, hBuffer);
                if (ReadProcessMemory(hProcess, hBuffer, hTBButton, Marshal.SizeOf(tbButton), out _))
                {
                    tbButton = (TBBUTTON)Marshal.PtrToStructure(hTBButton, typeof(TBBUTTON));

                    if (tbButton.dwData != UIntPtr.Zero)
                    {
                        if (ReadProcessMemory(hProcess, tbButton.dwData, hTrayItem, Marshal.SizeOf(trayItem), out _))
                        {
                            trayItem = (TrayItem)Marshal.PtrToStructure(hTrayItem, typeof(TrayItem));

                            if ((tbButton.fsState & TBSTATE_HIDDEN) != 0)
                            {
                                trayItem.dwState = 1;
                            }
                            else
                            {
                                trayItem.dwState = 0;
                            }

                            ShellLogger.Debug(
                                $"ExplorerTrayService: Got tray item: {trayItem.szIconText}");
                        }
                    }
                }

                return trayItem;
            }
            finally
            {
                Marshal.FreeHGlobal(hTBButton);
                Marshal.FreeHGlobal(hTrayItem);
            }
        }

        private SafeNotifyIconData GetTrayItemIconData(TrayItem trayItem)
        {
            SafeNotifyIconData nid = new SafeNotifyIconData();

            nid.hWnd = trayItem.hWnd;
            nid.uID = trayItem.uID;
            nid.uCallbackMessage = trayItem.uCallbackMessage;
            nid.szTip = trayItem.szIconText;
            nid.hIcon = trayItem.hIcon;
            nid.uVersion = trayItem.uVersion;
            nid.guidItem = trayItem.guidItem;
            nid.dwState = (int)trayItem.dwState;
            nid.uFlags = NIF.GUID | NIF.MESSAGE | NIF.TIP | NIF.STATE;

            if (nid.hIcon != IntPtr.Zero)
            {
                nid.uFlags |= NIF.ICON;
            }
            else
            {
                ShellLogger.Warning($"ExplorerTrayService: Unable to use {trayItem.szIconText} icon handle for NOTIFYICONDATA struct");
            }

            return nid;
        }

        private bool GetAutoTrayEnabled()
        {
            int enableAutoTray = 1;

            try
            {
                RegistryKey explorerKey = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Explorer", false);

                if (explorerKey != null)
                {
                    var enableAutoTrayValue = explorerKey.GetValue("EnableAutoTray");

                    if (enableAutoTrayValue != null)
                    {
                        enableAutoTray = Convert.ToInt32(enableAutoTrayValue);
                    }
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"ExplorerTrayService: Unable to get EnableAutoTray setting: {e.Message}");
            }

            return enableAutoTray == 1;
        }

        private void SetAutoTrayEnabled(TrayNotify trayNotify, bool enabled)
        {
            try
            {
                if (EnvironmentHelper.IsWindows8OrBetter)
                {
                    var trayNotifyInstance = (ITrayNotify)trayNotify;
                    trayNotifyInstance.EnableAutoTray(enabled);
                }
                else
                {
                    var trayNotifyInstance = (ITrayNotifyLegacy)trayNotify;
                    trayNotifyInstance.EnableAutoTray(enabled);
                }
            }
            catch (Exception e)
            {
                ShellLogger.Debug($"ExplorerTrayService: Unable to set EnableAutoTray setting: {e.Message}");
            }
        }

        private const byte TBSTATE_HIDDEN = 8;

        private enum TB : uint
        {
            GETBUTTON = WM.USER + 23,
            BUTTONCOUNT = WM.USER + 24
        }

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        public struct TrayItem
        {
            public IntPtr hWnd;
            public uint uID;
            public uint uCallbackMessage;
            public uint dwState;
            public uint uVersion;
            public IntPtr hIcon;
            public IntPtr uIconDemoteTimerID;
            public uint dwUserPref;
            public uint dwLastSoundTime;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szExeName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
            public string szIconText;
            public uint uNumSeconds;
            public Guid guidItem;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct TBBUTTON
        {
            public int iBitmap;
            public int idCommand;
            [StructLayout(LayoutKind.Explicit)]
            private struct TBBUTTON_U
            {
                [FieldOffset(0)] public byte fsState;
                [FieldOffset(1)] public byte fsStyle;
                [FieldOffset(0)] private IntPtr bReserved;
            }
            private TBBUTTON_U union;
            public byte fsState { get { return union.fsState; } set { union.fsState = value; } }
            public byte fsStyle { get { return union.fsStyle; } set { union.fsStyle = value; } }
            public UIntPtr dwData;
            public IntPtr iString;
        }
    }
}
