using System;
using System.Text.Json;

namespace Stranichnik.Sync.Serialization;

public sealed class SystemTextSyncJsonSerializer : ISyncJsonSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = false
    };

    public byte[] Serialize<T>(T value)
    {
        return JsonSerializer.SerializeToUtf8Bytes(value, Options);
    }

    public T Deserialize<T>(ReadOnlySpan<byte> utf8Json)
    {
        var value = JsonSerializer.Deserialize<T>(utf8Json, Options);

        if (value is null)
            throw new JsonException("Sync JSON deserialized to null.");

        return value;
    }
}
