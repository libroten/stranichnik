namespace Stranichnik.Icons;

public sealed record IconProcessingOptions(
    int OutputSize = 64,
    int MaxInputBytes = 1024 * 1024);
