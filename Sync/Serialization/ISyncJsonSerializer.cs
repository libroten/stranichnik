using System;

namespace Stranichnik.Sync.Serialization;

public interface ISyncJsonSerializer
{
    byte[] Serialize<T>(T value);

    T Deserialize<T>(ReadOnlySpan<byte> utf8Json);
}
