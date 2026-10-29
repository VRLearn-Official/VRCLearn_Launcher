namespace VRCLearn.App.ViewModels;

/// <summary>延时后执行一次；期间再 Run 一次就把上一次作废。</summary>
public sealed class TransientAction(TimeSpan delay, Action expired)
{
    private int _generation;

    public void Run()
    {
        var generation = ++_generation;

        _ = ExpireAsync(generation);
    }

    public void Cancel() => _generation++;

    private async Task ExpireAsync(int generation)
    {
        await Task.Delay(delay);

        if (generation == _generation)
        {
            expired();
        }
    }
}
