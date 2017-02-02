using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Runtime.InteropServices;

namespace GARbro.GUI.Interop
{
    [ComImport, Guid ("F8383852-FCD3-11d1-A6B9-006097DF5BD4")]
    internal class ProgressDialogRCW
    {
    }

    [ComImport, Guid ("EBBC7C04-315E-11d2-B62F-006097DF5BD4"), CoClass (typeof(ProgressDialogRCW))]
    internal interface ProgressDialog : IProgressDialog
    {
    }

    [Flags]
    internal enum ProgressDialogFlags : uint
    {
        Normal          = 0x00000000,
        Modal           = 0x00000001,
        AutoTime        = 0x00000002,
        NoTime          = 0x00000004,
        NoMinimize      = 0x00000008,
        NoProgressBar   = 0x00000010,
        MarqueeProgress = 0x00000020,
        NoCancel        = 0x00000040
    }

    [Flags]
    internal enum ProgressTimerAction : uint
    {
        Reset  = 0x00000001,
        Pause  = 0x00000002,
        Resume = 0x00000003
    }

    [ComImport, Guid ("EBBC7C04-315E-11d2-B62F-006097DF5BD4"), InterfaceType (ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IProgressDialog
    {

        [PreserveSig]
        void StartProgressDialog(
            IntPtr hwndParent,
            [MarshalAs(UnmanagedType.IUnknown)]
			object punkEnableModless,
            ProgressDialogFlags dwFlags,
            IntPtr pvResevered
            );

        [PreserveSig]
        void StopProgressDialog();

        [PreserveSig]
        void SetTitle(
            [MarshalAs(UnmanagedType.LPWStr)]
			string pwzTitle
            );

        [PreserveSig]
        void SetAnimation(
            IntPtr hInstAnimation,
            ushort idAnimation
            );

        [PreserveSig]
        [return: MarshalAs(UnmanagedType.Bool)]
        bool HasUserCancelled();

        [PreserveSig]
        void SetProgress(
            uint dwCompleted,
            uint dwTotal
            );
        [PreserveSig]
        void SetProgress64(
            ulong ullCompleted,
            ulong ullTotal
            );

        [PreserveSig]
        void SetLine(
            uint dwLineNum,
            [MarshalAs(UnmanagedType.LPWStr)]
			string pwzString,
            [MarshalAs(UnmanagedType.VariantBool)]
			bool fCompactPath,
            IntPtr pvResevered
            );

        [PreserveSig]
        void SetCancelMsg(
            [MarshalAs(UnmanagedType.LPWStr)]
			string pwzCancelMsg,
            object pvResevered
            );

        [PreserveSig]
        void Timer(
            ProgressTimerAction dwTimerAction,
            object pvResevered
            );
    }

    [ComImport, Guid ("00000114-0000-0000-C000-000000000046"), InterfaceType (ComInterfaceType.InterfaceIsIUnknown)]
    internal interface IOleWindow
    {
        [PreserveSig]
        void GetWindow (out IntPtr phwnd);

        [PreserveSig]
        void ContextSensitiveHelp ([MarshalAs(UnmanagedType.Bool)] bool fEnterMode);
    }
}
