using System;
using System.Runtime.InteropServices;

namespace RepoWorkshop.Services;

/// <summary>The Win32 SHFILEOPSTRUCTW, used by <see cref="RecycleBin"/>.</summary>
[StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
internal struct ShFileOpStruct {
    /// <summary>Owner window for any UI (none is shown).</summary>
    public IntPtr Hwnd;

    /// <summary>The operation, e.g. FO_DELETE.</summary>
    public uint Func;

    /// <summary>Source paths, each null-terminated, ending with an extra null.</summary>
    public string From;

    /// <summary>Destination (unused for delete).</summary>
    public string? To;

    /// <summary>FOF_* flags.</summary>
    public ushort Flags;

    /// <summary>Set if the user aborted.</summary>
    [MarshalAs(UnmanagedType.Bool)]
    public bool AnyOperationsAborted;

    /// <summary>Unused.</summary>
    public IntPtr NameMappings;

    /// <summary>Unused.</summary>
    public string? ProgressTitle;
}
