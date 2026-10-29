namespace VRCLearn.Core.Hashing;

public static class Sha256Hex
{
    public static string Of(ReadOnlySpan<byte> bytes) =>
        FromDigest(System.Security.Cryptography.SHA256.HashData(bytes));

    public static string FromDigest(ReadOnlySpan<byte> digest) =>
        Convert.ToHexStringLower(digest);
}
