using System.IO;
using Stranichnik.Diagnostics;
using Stranichnik.Settings;

namespace Stranichnik.Storage.Sqlite;

public static class SqliteBookmarkTreeStoreFactory
{
    public static SqliteBookmarkTreeStore CreateDefault(bool useSampleData)
    {
        Logs.Print($"SQLite database path: {AppDataPaths.DatabasePath}");

        var databaseFileExistedBeforeStartup = File.Exists(AppDataPaths.DatabasePath);
        var connectionFactory = new SqliteConnectionFactory(AppDataPaths.DatabasePath);
        var migrator = new SqliteDatabaseMigrator(connectionFactory);
        var migrationResult = migrator.Migrate();

        Logs.Print(migrationResult.MigrationApplied
            ? $"SQLite migration applied: {migrationResult.PreviousVersion} -> {migrationResult.CurrentVersion}."
            : $"SQLite schema version: {migrationResult.CurrentVersion}.");

        var deviceId = migrator.GetMetadataValue("device_id");
        var store = new SqliteBookmarkTreeStore(connectionFactory, modifiedDeviceId: deviceId);
        var wasSeeded = useSampleData && new SqliteSampleDataSeeder(store)
            .SeedIfNewDatabase(
                databaseFileExistedBeforeStartup,
                SampleBookmarkRecordsFactory.Create());

        Logs.Print(wasSeeded
            ? "SQLite sample data seeded."
            : "SQLite sample data seed skipped.");

        return store;
    }
}
