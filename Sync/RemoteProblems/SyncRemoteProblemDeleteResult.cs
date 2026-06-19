namespace Stranichnik.Sync.RemoteProblems;

public sealed record SyncRemoteProblemDeleteResult(
    SyncRemoteProblemDeleteStatus Status)
{
    public static SyncRemoteProblemDeleteResult DeletedOrMissing()
    {
        return new SyncRemoteProblemDeleteResult(SyncRemoteProblemDeleteStatus.DeletedOrMissing);
    }

    public static SyncRemoteProblemDeleteResult RemoteChanged()
    {
        return new SyncRemoteProblemDeleteResult(SyncRemoteProblemDeleteStatus.RemoteChanged);
    }
}
