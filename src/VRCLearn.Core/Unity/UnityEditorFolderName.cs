namespace VRCLearn.Core.Unity;

/// <summary>形如 <c>2022.3.22f1</c>、<c>2019.4.31f1c1</c>（中国版），后面可再跟 <c>-</c> 开头的后缀。</summary>
public static class UnityEditorFolderName
{
    private static readonly char[] ReleaseTypes = ['a', 'b', 'f', 'p', 'c', 'x'];

    public static bool IsValid(string? name)
    {
        if (string.IsNullOrEmpty(name))
        {
            return false;
        }

        var index = 0;

        if (!ConsumeDigits(name, ref index) || !Consume(name, ref index, '.')
            || !ConsumeDigits(name, ref index) || !Consume(name, ref index, '.')
            || !ConsumeDigits(name, ref index))
        {
            return false;
        }

        if (!ConsumeAny(name, ref index, ReleaseTypes) || !ConsumeDigits(name, ref index))
        {
            return false;
        }

        ConsumeChinaRevision(name, ref index);

        return index == name.Length || name[index] == '-';
    }

    private static void ConsumeChinaRevision(string name, ref int index)
    {
        var probe = index;

        if (Consume(name, ref probe, 'c') && ConsumeDigits(name, ref probe))
        {
            index = probe;
        }
    }

    private static bool Consume(string name, ref int index, char expected)
    {
        if (index >= name.Length || name[index] != expected)
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool ConsumeAny(string name, ref int index, char[] expected)
    {
        if (index >= name.Length || Array.IndexOf(expected, name[index]) < 0)
        {
            return false;
        }

        index++;
        return true;
    }

    private static bool ConsumeDigits(string name, ref int index)
    {
        var start = index;

        while (index < name.Length && char.IsAsciiDigit(name[index]))
        {
            index++;
        }

        return index > start;
    }
}
