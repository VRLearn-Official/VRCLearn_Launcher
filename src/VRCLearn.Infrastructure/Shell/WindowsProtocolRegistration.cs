using System.Text.Json;
using Microsoft.Win32;
using VRCLearn.Core.Templates;

namespace VRCLearn.Infrastructure.Shell;

public sealed class WindowsProtocolRegistration
{
    private const string ClassesKey = @"Software\Classes";
    private const string BackupKey = @"Software\VRCLearnLauncher\ProtocolBackup";
    private const string StateSuffix = "|state";

    /// 注册表认协议的标记值，内容为空即可。
    private const string ProtocolValueName = "URL Protocol";

    /// 命令行参数名，须与解析端一致。
    private const string LinkArgument = "link";

    private const string TemplateArgument = "template";

    public const string ExtensionName = TemplateFormat.Extension;

    private const string LegacyExtensionName = TemplateFormat.LegacyExtension;

    private const string ProgId = "VRCLearnLauncher.Template";

    private const string LegacyProgId = "VRCLearnLauncher.AlcomTemplate";

    public const string VccScheme = "vcc";

    private readonly string _executablePath;

    public WindowsProtocolRegistration(string executablePath) => _executablePath = executablePath;

    public event EventHandler? Applied;

    public void Apply(bool useForVcc, bool useForTemplate)
    {
        if (useForVcc)
        {
            Register(VccScheme);
        }
        else
        {
            Unregister(VccScheme);
        }

        if (useForTemplate)
        {
            RegisterFileType();
        }
        else
        {
            UnregisterFileType();
        }

        Applied?.Invoke(this, EventArgs.Empty);
    }

    private void RegisterFileType()
    {
        ReleaseFileType(LegacyExtensionName, LegacyProgId);
        ClaimFileType(ExtensionName, ProgId, "VRCLearn Project Template");
    }

    private void UnregisterFileType()
    {
        ReleaseFileType(ExtensionName, ProgId);
        ReleaseFileType(LegacyExtensionName, LegacyProgId);
    }

    private void ClaimFileType(string extension, string progId, string display)
    {
        if (CurrentProgId(extension) is { Length: > 0 } existing && existing != progId && Backup(extension) is null)
        {
            SaveBackup(extension, existing);
        }

        using (var key = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{extension}", writable: true))
        {
            key.SetValue(null, progId);
        }

        WriteCommand(progId, display, TemplateArgument, isProtocol: false);
    }

    private static void ReleaseFileType(string extension, string progId)
    {
        if (Backup(extension) is { } previous)
        {
            using (var key = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{extension}", writable: true))
            {
                key.SetValue(null, previous);
            }

            RemoveBackup(extension);
        }
        else if (CurrentProgId(extension) == progId)
        {
            Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\{extension}", throwOnMissingSubKey: false);
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\{progId}", throwOnMissingSubKey: false);
    }

    public static string DescribeFileType() => CurrentProgId(ExtensionName) ?? "";

    private static string? CurrentProgId(string extension)
    {
        using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{extension}");

        return key?.GetValue(null) as string;
    }

    public bool IsOurs(string scheme) =>
        CurrentCommand(scheme) is { } command && IsOurCommand(command);

    public static string Describe(string scheme) => CurrentCommand(scheme) ?? "";

    public void Register(string scheme)
    {
        if (CurrentCommand(scheme) is { } existing && !IsOurCommand(existing) && Backup(scheme) is null)
        {
            using var key = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{scheme}");

            SaveProtocolBackup(
                scheme,
                new ProtocolBackupState(
                    existing,
                    key?.GetValue(null) as string,
                    key?.GetValue(ProtocolValueName) is not null,
                    ReadIcon(scheme)));
        }

        WriteCommand(scheme, $"URL:{scheme} protocol", LinkArgument, isProtocol: true);
    }

    private void WriteCommand(string keyName, string display, string argument, bool isProtocol)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{keyName}", writable: true);

        key.SetValue(null, display);

        if (isProtocol)
        {
            key.SetValue(ProtocolValueName, "");
        }

        using (var icon = key.CreateSubKey(@"DefaultIcon", writable: true))
        {
            icon.SetValue(null, $"\"{_executablePath}\",0");
        }

        using var command = key.CreateSubKey(@"shell\open\command", writable: true);
        command.SetValue(null, $"\"{_executablePath}\" {argument} \"%1\"");
    }

    public void Unregister(string scheme)
    {
        if (ReadProtocolBackup(scheme) is { } previous)
        {
            RestoreProtocol(scheme, previous);
            RemoveBackup(scheme);
            return;
        }

        if (!IsOurs(scheme))
        {
            return;
        }

        Registry.CurrentUser.DeleteSubKeyTree($@"{ClassesKey}\{scheme}", throwOnMissingSubKey: false);
    }

    private static void RestoreProtocol(string scheme, ProtocolBackupState previous)
    {
        using var key = Registry.CurrentUser.CreateSubKey($@"{ClassesKey}\{scheme}", writable: true);

        using (var command = key.CreateSubKey(@"shell\open\command", writable: true))
        {
            command.SetValue(null, previous.Command);
        }

        if (previous.Legacy)
        {
            key.DeleteValue(ProtocolValueName, throwOnMissingValue: false);
            key.DeleteSubKeyTree("DefaultIcon", throwOnMissingSubKey: false);
            return;
        }

        if (previous.Name is null)
        {
            key.DeleteValue("", throwOnMissingValue: false);
        }
        else
        {
            key.SetValue(null, previous.Name);
        }

        if (previous.IsProtocol)
        {
            key.SetValue(ProtocolValueName, "");
        }
        else
        {
            key.DeleteValue(ProtocolValueName, throwOnMissingValue: false);
        }

        if (previous.Icon is null)
        {
            key.DeleteSubKeyTree("DefaultIcon", throwOnMissingSubKey: false);
        }
        else
        {
            using var icon = key.CreateSubKey(@"DefaultIcon", writable: true);
            icon.SetValue(null, previous.Icon);
        }
    }

    private static string? CurrentCommand(string scheme)
    {
        using var command = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{scheme}\shell\open\command");

        return command?.GetValue(null) as string;
    }

    private bool IsOurCommand(string command) =>
        command.Contains($"\"{_executablePath}\"", StringComparison.OrdinalIgnoreCase);

    private static string? Backup(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(BackupKey);

        return key?.GetValue(scheme) as string;
    }

    private static void SaveBackup(string scheme, string command)
    {
        using var key = Registry.CurrentUser.CreateSubKey(BackupKey, writable: true);

        key.SetValue(scheme, command);
    }

    private static void SaveProtocolBackup(string scheme, ProtocolBackupState state)
    {
        SaveBackup(scheme, state.Command);
        SaveBackup(scheme + StateSuffix, JsonSerializer.Serialize(state));
    }

    private static ProtocolBackupState? ReadProtocolBackup(string scheme)
    {
        if (Backup(scheme + StateSuffix) is { } serialized)
        {
            try
            {
                return JsonSerializer.Deserialize<ProtocolBackupState>(serialized);
            }
            catch (JsonException)
            {
            }
        }

        return Backup(scheme) is { } command
            ? new ProtocolBackupState(command, null, true, null, Legacy: true)
            : null;
    }

    private static string? ReadIcon(string scheme)
    {
        using var icon = Registry.CurrentUser.OpenSubKey($@"{ClassesKey}\{scheme}\DefaultIcon");

        return icon?.GetValue(null) as string;
    }

    private static void RemoveBackup(string scheme)
    {
        using var key = Registry.CurrentUser.OpenSubKey(BackupKey, writable: true);

        key?.DeleteValue(scheme, throwOnMissingValue: false);
        key?.DeleteValue(scheme + StateSuffix, throwOnMissingValue: false);
    }

    private sealed record ProtocolBackupState(
        string Command,
        string? Name,
        bool IsProtocol,
        string? Icon,
        bool Legacy = false);
}
