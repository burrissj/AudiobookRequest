using System.Runtime.InteropServices;

namespace AudiobookRequest.Core.Library;

public static class DiskSpace
{
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string directory, out ulong freeBytesAvailable, out ulong totalBytes, out ulong totalFreeBytes);

    /// <summary>
    /// Free bytes available to this process on the volume holding <paramref name="path"/> (walks up to the
    /// nearest existing folder first). Works for UNC shares, unlike DriveInfo. Null if it can't be determined.
    /// </summary>
    public static long? GetFreeBytes(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        try
        {
            var probe = Path.GetFullPath(path);
            while (!Directory.Exists(probe))
            {
                var parent = Path.GetDirectoryName(probe);
                if (string.IsNullOrEmpty(parent))
                {
                    return null;
                }
                probe = parent;
            }

            if (OperatingSystem.IsWindows())
            {
                return GetDiskFreeSpaceEx(probe, out var free, out _, out _) ? (long)free : null;
            }
            return new DriveInfo(probe).AvailableFreeSpace;
        }
        catch
        {
            return null;
        }
    }
}
