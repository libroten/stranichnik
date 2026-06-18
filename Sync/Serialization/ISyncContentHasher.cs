using System;

namespace Stranichnik.Sync.Serialization;

public interface ISyncContentHasher
{
    string ComputeHash(ReadOnlySpan<byte> canonicalUtf8Json);
}
