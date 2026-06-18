using System;
using System.Security.Cryptography;

namespace Stranichnik.Sync.Serialization;

public sealed class Sha256SyncContentHasher : ISyncContentHasher
{
    public string ComputeHash(ReadOnlySpan<byte> canonicalUtf8Json)
    {
        Span<byte> hash = stackalloc byte[SHA256.HashSizeInBytes];
        SHA256.HashData(canonicalUtf8Json, hash);

        return "sha256:" + ToLowerHex(hash);
    }

    private static string ToLowerHex(ReadOnlySpan<byte> bytes)
    {
        const string HexAlphabet = "0123456789abcdef";

        var chars = new char[bytes.Length * 2];

        for (var i = 0; i < bytes.Length; i++)
        {
            var value = bytes[i];
            chars[i * 2] = HexAlphabet[value >> 4];
            chars[(i * 2) + 1] = HexAlphabet[value & 0x0f];
        }

        return new string(chars);
    }
}
