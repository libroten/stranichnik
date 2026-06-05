using System;
using System.Security.Cryptography;

namespace Stranichnik.Icons;

public static class IconHash
{
    public const string Sha256Algorithm = "sha256";

    public static string ComputeSha256(ReadOnlySpan<byte> bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }
}
