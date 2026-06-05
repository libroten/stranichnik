using System;

namespace Stranichnik.Icons;

public sealed record ProcessedIconImage(
    string MimeType,
    int Width,
    int Height,
    ReadOnlyMemory<byte> Bytes);
