namespace VRCLearn.App.Plugins;

internal sealed class PluginServiceRegistry
{
    private readonly Lock _gate = new();
    private readonly Dictionary<Type, (string Owner, object Service)> _services = [];

    public void Register<T>(string owner, T service) where T : class
    {
        lock (_gate)
        {
            if (_services.TryGetValue(typeof(T), out var existing))
            {
                if (existing.Owner.Length == 0)
                {
                    return;
                }

                if (existing.Owner != owner)
                {
                    throw new InvalidOperationException(
                        $"service {typeof(T).Name} is already provided by {existing.Owner}");
                }
            }

            _services[typeof(T)] = (owner, service);
        }
    }

    public T? Get<T>() where T : class
    {
        lock (_gate)
        {
            return _services.TryGetValue(typeof(T), out var existing)
                ? existing.Service as T
                : null;
        }
    }

    public void RemoveOwner(string owner)
    {
        lock (_gate)
        {
            foreach (var key in _services
                         .Where(x => string.Equals(x.Value.Owner, owner, StringComparison.Ordinal))
                         .Select(x => x.Key)
                         .ToList())
            {
                _services.Remove(key);
            }
        }
    }
}
