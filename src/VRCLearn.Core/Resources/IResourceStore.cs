namespace VRCLearn.Core.Resources;

public interface IResourceStore
{
    ResourceCollection Current { get; }

    void Reload();

    void Save();

    string CoverPath(ResourceDefinition resource);

    void SaveCover(ResourceDefinition resource, ReadOnlyMemory<byte> png);

    void DeleteCover(ResourceDefinition resource);
}
