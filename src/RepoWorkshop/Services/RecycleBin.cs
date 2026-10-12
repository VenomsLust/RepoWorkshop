using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace RepoWorkshop.Services;

/// <summary>Moves files to the Windows Recycle Bin, so deletions can be undone.</summary>
internal static class RecycleBin {
    /// <summary>SHFileOperation: delete.</summary>
    private const uint FoDelete = 3;

    /// <summary>No progress UI.</summary>
    private const ushort FofSilent = 0x0004;

    /// <summary>Don't ask for confirmation (the app already asked).</summary>
    private const ushort FofNoConfirmation = 0x0010;

    /// <summary>Send to the Recycle Bin instead of deleting permanently.</summary>
    private const ushort FofAllowUndo = 0x0040;

    /// <summary>No error dialogs; failures are reported through the return value.</summary>
    private const ushort FofNoErrorUi = 0x0400;

    /// <summary>Moves the existing files among <paramref name="paths"/> to the Recycle Bin.</summary>
    /// <exception cref="IOException">The operation failed or was aborted.</exception>
    public static void Send(IEnumerable<string> paths) {
        List<string> existing = paths.Where(File.Exists).Select(Path.GetFullPath).ToList();
        if (existing.Count == 0) {
            return;
        }

        ShFileOpStruct op = new() {
            Func = FoDelete,
            From = string.Join('\0', existing) + "\0\0",
            Flags = FofAllowUndo | FofNoConfirmation | FofSilent | FofNoErrorUi,
        };
        int result = SHFileOperation(ref op);
        if (result != 0 || op.AnyOperationsAborted) {
            throw new IOException($"Could not move the files to the Recycle Bin (error 0x{result:X}).");
        }
    }

    /// <summary>The Win32 shell file operation.</summary>
    [DllImport("shell32.dll", CharSet = CharSet.Unicode, EntryPoint = "SHFileOperationW")]
    private static extern int SHFileOperation(ref ShFileOpStruct fileOp);
}
