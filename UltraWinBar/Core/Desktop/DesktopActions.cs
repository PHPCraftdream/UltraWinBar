using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace UltraWinBar.Utilities
{
    // Windows 10 2004–22H2 ABI; other builds must not use these vtables.
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
            return (T)((IServices)shell).QueryService(ref service, ref iid);
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
            try { return pins.IsAppIdPinned(AppId(view)); }
            finally { Marshal.ReleaseComObject(view); }
        }

        public string GetApplicationId(IntPtr hwnd)
        {
            var view = View(hwnd);
            try { return AppId(view); }
            finally { Marshal.ReleaseComObject(view); }
        }

        public bool IsApplicationIdPinned(string id) => pins.IsAppIdPinned(id);

        public void SetApplicationIdPinned(string id, bool pinned)
        {
            if (string.IsNullOrWhiteSpace(id)) throw new ArgumentException("Application ID is required.", nameof(id));
            if (pinned) pins.PinAppID(id); else pins.UnpinAppID(id);
            if (pins.IsAppIdPinned(id) != pinned) throw new InvalidOperationException("Pin state was not applied.");
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
                if (pins.IsAppIdPinned(AppId(view))) throw new InvalidOperationException("Unpin the application before moving it.");
                if (!manager.CanViewMoveDesktops(view)) throw new InvalidOperationException("Window cannot change desktops.");
                desktop = manager.FindDesktop(ref destination);
                manager.MoveViewToDesktop(view, desktop);
            }
            finally
            {
                if (desktop != null) Marshal.ReleaseComObject(desktop);
                Marshal.ReleaseComObject(view);
            }
        }

        public void SwitchDesktop(Guid destination)
        {
            IDesktop desktop = manager.FindDesktop(ref destination);
            if (desktop == null) throw new InvalidOperationException("Desktop was not found.");
            try { manager.SwitchDesktop(desktop); }
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
            [return: MarshalAs(UnmanagedType.IUnknown)] object QueryService(ref Guid service, ref Guid iid);
        }
        [ComImport, Guid("1841C6D7-4F9D-42C0-AF41-8747538F10E5"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IViews
        {
            void GetViews(); void GetViewsByZOrder(); void GetViewsByAppUserModelId();
            [PreserveSig] int GetViewForHwnd(IntPtr hwnd, out IView view);
        }
        [ComImport, Guid("372E1D3B-38D3-42E4-A15B-8AB2B178F513"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IView
        {
            // IInspectable prefix, followed by the IApplicationView prefix.
            void GetIids(); void GetRuntimeClassName(); void GetTrustLevel();
            void SetFocus(); void SwitchTo(); void TryInvokeBack(); void GetThumbnailWindow();
            void GetMonitor(); void GetVisibility(); void SetCloak(); void GetPosition();
            void SetPosition(); void InsertAfterWindow(); void GetExtendedFramePosition();
            [PreserveSig] int GetAppUserModelId([MarshalAs(UnmanagedType.LPWStr)] out string id);
        }
        [ComImport, Guid("FF72FFDD-BE7E-43FC-9C03-AD81681E88E4"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDesktop { }
        [ComImport, Guid("F31574D6-B682-4CDC-BD56-1827860ABEC6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IManager
        {
            int GetCount();
            void MoveViewToDesktop(IView view, IDesktop desktop);
            bool CanViewMoveDesktops(IView view);
            void GetCurrentDesktop(); void GetDesktops(); void GetAdjacentDesktop();
            void SwitchDesktop(IDesktop desktop); void CreateDesktop(); void RemoveDesktop();
            IDesktop FindDesktop(ref Guid id);
        }
        [ComImport, Guid("4CE81583-1E4C-4632-A621-07A53543148F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IPins
        {
            bool IsAppIdPinned([MarshalAs(UnmanagedType.LPWStr)] string id);
            void PinAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
            void UnpinAppID([MarshalAs(UnmanagedType.LPWStr)] string id);
        }
    }
}
