namespace Stranichnik.Views;

public sealed record SettingsSyncRemoteProblemViewModel(
    string Id,
    string RelativePath,
    string ReasonCode,
    int SeenCount);
