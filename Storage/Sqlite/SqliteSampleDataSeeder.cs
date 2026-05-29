namespace Stranichnik.Storage.Sqlite;

public sealed class SqliteSampleDataSeeder
{
    private readonly SqliteBookmarkTreeStore _store;

    public SqliteSampleDataSeeder(SqliteBookmarkTreeStore store)
    {
        _store = store;
    }

    public bool SeedIfNewDatabase(
        bool databaseFileExistedBeforeStartup,
        BookmarkTreeSnapshot snapshot)
    {
        if (databaseFileExistedBeforeStartup)
            return false;

        _store.InsertSeedItems(snapshot);
        return true;
    }
}
