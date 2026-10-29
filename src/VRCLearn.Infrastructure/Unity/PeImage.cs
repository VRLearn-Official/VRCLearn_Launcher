using System.Reflection.PortableExecutable;
using System.Runtime.InteropServices;
using VRCLearn.Core.Unity;
using VRCLearn.Core.Versioning;

namespace VRCLearn.Infrastructure.Unity;

internal static class PeImage
{
    private const string UnityVersionField = @"\StringFileInfo\040904b0\Unity Version";

    public static UnityVersion? ReadUnityVersion(string executablePath)
    {
        var versionInfo = ReadVersionString(executablePath, UnityVersionField);

        var version = versionInfo?.Split('_')[0];

        return UnityVersion.TryParse(version);
    }

    public static ChipArchitecture? ReadArchitecture(string executablePath)
    {
        try
        {
            using var file = File.OpenRead(executablePath);
            using var pe = new PEReader(file);

            return pe.PEHeaders.CoffHeader.Machine switch
            {
                Machine.Amd64 => ChipArchitecture.X86_64,
                Machine.Arm64 => ChipArchitecture.Arm64,
                _ => null,
            };
        }
        catch (Exception exception) when (exception is IOException
            or UnauthorizedAccessException
            or BadImageFormatException)
        {
            return null;
        }
    }

    private static string? ReadVersionString(string executablePath, string field)
    {
        try
        {
            var size = GetFileVersionInfoSize(executablePath, out _);

            if (size == 0)
            {
                return null;
            }

            var info = new byte[size];

            if (!GetFileVersionInfo(executablePath, 0, size, info) || !QueryValue(info, field, out var value))
            {
                return null;
            }

            return value;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static bool QueryValue(byte[] info, string field, out string? value)
    {
        value = null;

        if (!VerQueryValue(info, field, out var pointer, out var length) || pointer == nint.Zero || length == 0)
        {
            return false;
        }

        value = Marshal.PtrToStringUni(pointer, (int)length).TrimEnd('\0');
        return true;
    }

    [DllImport("version.dll", EntryPoint = "GetFileVersionInfoSizeW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern uint GetFileVersionInfoSize(string fileName, out uint handle);

    [DllImport("version.dll", EntryPoint = "GetFileVersionInfoW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetFileVersionInfo(string fileName, uint handle, uint length, byte[] data);

    [DllImport("version.dll", EntryPoint = "VerQueryValueW", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool VerQueryValue(byte[] block, string subBlock, out nint buffer, out uint length);
}
