using System.IO;

namespace VRCLearn.App.Services;

public static class LicenseText
{
    public static string Read()
    {
        using var stream = typeof(LicenseText).Assembly.GetManifestResourceStream("ThirdPartyNotices")
            ?? throw new InvalidOperationException("ThirdPartyNotices resource is missing.");
        using var reader = new StreamReader(stream);

        return reader.ReadToEnd();
    }
}
