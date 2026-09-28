using System;
using System.Runtime.InteropServices;
using ManagedShell.ShellFolders.Enums;

namespace ManagedShell.ShellFolders.Interfaces
{
    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe")]
    public interface IShellItem
    {
        // UltraWinBar: PreserveSig so failing HRESULTs don't turn into exceptions on this RCW.
        [PreserveSig]
        int BindToHandler(IntPtr pbc,
            [MarshalAs(UnmanagedType.LPStruct)] Guid bhid,
            [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
            out IntPtr ppv);

        [PreserveSig]
        int GetParent(out IShellItem ppsi);

        [PreserveSig]
        int GetDisplayName(SIGDN sigdnName, out IntPtr ppszName);

        [PreserveSig]
        int GetAttributes(SFGAO sfgaoMask, out SFGAO psfgaoAttribs);

        [PreserveSig]
        int Compare(IShellItem psi, uint hint, out int piOrder);
    };
}
