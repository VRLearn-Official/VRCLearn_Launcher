using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace VRCLearn.Infrastructure.Unity;

internal static class FileLock
{
    private const int ErrorSharingViolation = 32;
    private const int ErrorLockViolation = 33;

    private const uint LockfileFailImmediately = 0x00000001;
    private const uint LockfileExclusiveLock = 0x00000002;

    private const uint LockRange = uint.MaxValue;

    public static bool IsLocked(string path)
    {
        FileStream stream;

        try
        {
            stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
        }
        catch (IOException exception) when ((exception.HResult & 0xFFFF) == ErrorSharingViolation)
        {
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }

        using (stream)
        {
            var overlapped = new NativeOverlapped();

            if (LockFileEx(stream.SafeFileHandle, LockfileExclusiveLock | LockfileFailImmediately, 0, LockRange, LockRange, ref overlapped))
            {
                UnlockFileEx(stream.SafeFileHandle, 0, LockRange, LockRange, ref overlapped);
                return false;
            }

            return Marshal.GetLastWin32Error() == ErrorLockViolation;
        }
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockFileEx(
        SafeFileHandle handle,
        uint flags,
        uint reserved,
        uint countLow,
        uint countHigh,
        ref NativeOverlapped overlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnlockFileEx(
        SafeFileHandle handle,
        uint reserved,
        uint countLow,
        uint countHigh,
        ref NativeOverlapped overlapped);
}
