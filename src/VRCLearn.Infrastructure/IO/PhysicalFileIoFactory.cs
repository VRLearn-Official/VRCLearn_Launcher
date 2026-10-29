using VRCLearn.Core.IO;

namespace VRCLearn.Infrastructure.IO;

public sealed class PhysicalFileIoFactory : IFileIoFactory
{
    public IFileIo For(string rootDirectory) => new PhysicalFileIo(rootDirectory);
}
