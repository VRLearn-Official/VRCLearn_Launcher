using VRCLearn.Core.IO;
using VRCLearn.Core.Packages;

namespace VRCLearn.Core.Projects;

public interface IPackageInstaller
{
    Task InstallPackageAsync(
        IFileIo project,
        PackageInfo package,
        string destinationRelativePath,
        CancellationToken cancellationToken);
}
