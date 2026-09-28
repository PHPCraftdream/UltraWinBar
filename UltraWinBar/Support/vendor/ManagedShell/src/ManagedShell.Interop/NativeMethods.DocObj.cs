using System;
using System.Runtime.InteropServices;

namespace ManagedShell.Interop
{
    public partial class NativeMethods
    {
        [ComImport, Guid("b722bccb-4e68-101b-a2bc-00aa00404770"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        public interface IOleCommandTarget
        {
            [PreserveSig()]
            int QueryStatus(ref Guid pguidCmdGroup, int cCmds, [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] IntPtr[] prgCmds, ref IntPtr pCmdText);

            [PreserveSig()]
            int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdExecOpt, [MarshalAs(UnmanagedType.Struct)] ref object pvaIn, [MarshalAs(UnmanagedType.Struct)] ref object pvaOut);
        }

        public const int OLECMDID_NEW = 2;
        public const int OLECMDID_SAVE = 3;
        public const int OLECMDEXECOPT_DODEFAULT = 0;
    }
}
