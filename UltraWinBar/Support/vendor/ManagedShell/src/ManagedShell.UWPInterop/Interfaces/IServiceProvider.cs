using System;
using System.Runtime.InteropServices;

namespace ManagedShell.UWPInterop.Interfaces
{
    [ComImport]
    [Guid("6d5140c1-7436-11ce-8034-00aa006009fa")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    public interface IServiceProvider
    {
        // UltraWinBar: PreserveSig so failing HRESULTs don't turn into exceptions on this RCW;
        // callers already compare the return to S_OK/0.
        [PreserveSig]
        int QueryService(ref Guid guidService, ref Guid riid,
                   [MarshalAs(UnmanagedType.Interface)] out object ppvObject);
    }
}
