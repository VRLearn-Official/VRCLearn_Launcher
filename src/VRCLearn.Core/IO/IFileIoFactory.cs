namespace VRCLearn.Core.IO;

public interface IFileIoFactory
{
    IFileIo For(string rootDirectory);
}
