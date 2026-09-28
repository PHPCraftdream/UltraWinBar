using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UltraWinBar.Utilities
{
    // Windows 10 2004–22H2 ABI; other builds must not use these vtables.
    // All methods are PreserveSig: no hidden [out, retval]; BOOL outs marshal as 4 bytes.
    public sealed class DesktopActions : IDisposable
    {
        public static bool IsSupported => Environment.OSVersion.Version.Build >= 19041 && Environment.OSVersion.Version.Build <= 19045;
        private object shell;
        private IViews views;
        private IManager manager;
        private IPins pins;

        public DesktopActions()
        {
            if (!IsSupported) throw new PlatformNotSupportedException();
            try
            {
                shell = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("C2F03A33-21F5-47FA-B4BB-156362A2F239")));
                views = Query<IViews>(typeof(IViews).GUID);
                manager = Query<IManager>(new Guid("C5E0CDCA-7B6E-41B2-9FC4-D93975CC467B"));
                pins = Query<IPins>(new Guid("B5A399E7-1C87-46B8-88E9-FC5747B171BD"));
            }
            catch { Dispose(); throw; }
        }

        private T Query<T>(Guid service)
        {
            Guid iid = typeof(T).GUID;
            Marshal.ThrowExceptionForHR(((IServices)shell).QueryService(ref service, ref iid, out object result));
            return (T)result;
        }

        public static IReadOnlyList<(Guid Id, string Name)> GetDesktops()
        {
            var result = new List<(Guid, string)>();
            using var root = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops");
            if (root?.GetValue("VirtualDesktopIDs") is not byte[] bytes) return result;
            for (int offset = 0; offset + 16 <= bytes.Length; offset += 16)
            {
                var id = new Guid(new ReadOnlySpan<byte>(bytes, offset, 16));
                using var desktop = root.OpenSubKey(id.ToString("B"));
                result.Add((id, desktop?.GetValue("Name") as string));
            }
            return result;
        }

        private IView View(IntPtr hwnd)
        {
            Marshal.ThrowExceptionForHR(views.GetViewForHwnd(hwnd, out var view));
            return view;
        }

        private string AppId(IView view)
        {
            Marshal.ThrowExceptionForHR(view.GetAppUserModelId(out string id));
            if (string.IsNullOrWhiteSpace(id)) throw new InvalidOperationException("Application ID is unavailable.");
            return id;
        }

        public bool IsApplicationPinned(IntPtr hwnd)
        {
            var view = View(hwnd);
            try { return IsApplicationIdPinned(AppId(view)); }
            finally { Marshal.ReleaseComObject(view); }
        }

        public string GetApplicationId(IntPtr hwnd)
        {
            var view = View(hwnd);
            try { return AppId(view); }
            finally { Marshal.ReleaseComObject(view); }
        }

        public bool IsApplicationIdPinned(string id)
        {
            Marshal.ThrowExceptionForHR(pins.IsAppIdPinned(id, out bool pinned));
            return pinned;
        }

        public void SetApplicationIdPinned(string id, bool pinned)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Application ID is required.", nameof(id));
            Marshal.ThrowExceptionForHR(pinned ? pins.PinAppID(id) : pins.UnpinAppID(id));
            if (IsApplicationIdPinned(id) != pinned) throw new InvalidOperationException("Pin state was not applied.");
        }

        public void SetApplicationPinned(IntPtr hwnd, bool pinned)
        {
            var view = View(hwnd);
            try
            {
                string id = AppId(view);
                SetApplicationIdPinned(id, pinned);
            }
            finally { Marshal.ReleaseComObject(view); }
        }

        public void MoveWindow(IntPtr hwnd, Guid destination)
        {
            var view = View(hwnd);
            IDesktop desktop = null;
            try
            {
                if (IsApplicationIdPinned(AppId(view))) throw new InvalidOperationException("Unpin the application before moving it.");
                Marshal.ThrowExceptionForHR(manager.CanViewMoveDesktops(view, out bool canMove));
                if (!canMove) throw new InvalidOperationException("Window cannot change desktops.");
                Marshal.ThrowExceptionForHR(manager.FindDesktop(ref destination, out desktop));
                Marshal.ThrowExceptionForHR(manager.MoveViewToDesktop(view, desktop));
            }
            finally
            {
                if (desktop != null) Marshal.ReleaseComObject(desktop);
                Marshal.ReleaseComObject(view);
            }
        }

        public void SwitchDesktop(Guid destination)
        {
            Marshal.ThrowExceptionForHR(manager.FindDesktop(ref destination, out IDesktop desktop));
            if (desktop == null) throw new InvalidOperationException("Desktop was not found.");
            try { Marshal.ThrowExceptionForHR(manager.SwitchDesktop(desktop)); }
            finally { Marshal.ReleaseComObject(desktop); }
        }

        public void Dispose()
        {
            foreach (object value in new object[] { pins, manager, views, shell })
                if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value);
            pins = null; manager = null; views = null; shell = null;
        }

        [ComImport, Guid("6D5140C1-7436-11CE-8034-00AA006009FA"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IServices
        {
            [PreserveSig] int QueryService(ref Guid service, ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object result);
        }
        [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IViews
        {
            [PreserveSig] int GetViews(); [PreserveSig] int GetViewsByZOrder(); [PreserveSig] int GetViewsByAppUserModelId();
            [PreserveSig] int GetViewForHwnd(IntPtr hwnd, out IView view);
        }
        [ComImport, Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IView
        {
            // IInspectable prefix, followed by the IApplicationView prefix.
            // Vtable placeholders only; never called.
            [PreserveSig] int GetIids(); [PreserveSig] int GetRuntimeClassName(); [PreserveSig] int GetTrustLevel();
            [PreserveSig] int SetFocus(); [PreserveSig] int SwitchTo(); [PreserveSig] int TryInvokeBack(); [PreserveSig] int GetThumbnailWindow();
            [PreserveSig] int GetMonitor(); [PreserveSig] int GetVisibility(); [PreserveSig] int SetCloak(); [PreserveSig] int GetPosition();
            [PreserveSig] int SetPosition(); [PreserveSig] int InsertAfterWindow(); [PreserveSig] int GetExtendedFramePosition();
            [PreserveSig] int GetAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }
        [ComImport, Guid("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktop { }
        [ComImport, Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IManager
        {
            [PreserveSig] int GetCount(out int count);
            [PreserveSig] int MoveViewToDesktop(IView view, IDesktop desktop);
            [PreserveSig] int CanViewMoveDesktops(IView view, [MarshalAs(UnmanagedType.Bool)] out bool canMove);
            [PreserveSig] int GetCurrentDesktop(); [PreserveSig] int GetDesktops(); [PreserveSig] int GetAdjacentDesktop();
            [PreserveSig] int SwitchDesktop(IDesktop desktop); [PreserveSig] int CreateDesktop(); [PreserveSig] int RemoveDesktop();
            [PreserveSig] int FindDesktop(ref Guid id, out IDesktop desktop);
        }
        [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPins
        {
            [PreserveSig] int IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string id, [MarshalAs(UnmanagedType.Bool)] out bool pinned);
            [PreserveSig] int PinAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
            [PreserveSig] int UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
        }
    }
}
