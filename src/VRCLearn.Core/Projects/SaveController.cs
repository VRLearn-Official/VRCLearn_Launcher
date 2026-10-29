namespace VRCLearn.Core.Projects;

public sealed class SaveController<T>(T value)
{
    public T Value { get; } = value;

    public bool Changed { get; private set; }

    public T AsMutable()
    {
        Changed = true;
        return Value;
    }

    public async Task SaveAsync(Func<T, Task> save)
    {
        if (!Changed)
        {
            return;
        }

        await save(Value).ConfigureAwait(false);
        Changed = false;
    }
}
