namespace Stranichnik.Search.Internal;

internal readonly record struct SearchToken(
    string Value,
    int Position);
