using System;

namespace Stranichnik.Icons;

public interface IIconImageProcessor
{
    ProcessedIconImage Process(ReadOnlyMemory<byte> originalBytes);
}
