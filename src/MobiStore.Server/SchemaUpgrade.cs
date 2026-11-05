using Microsoft.EntityFrameworkCore;

namespace MobiStore.Server;

public static class SchemaUpgrade
{
    public static async Task<bool> IsCurrent(StoreDb db)
    {
        var connection = db.Database.GetDbConnection(); await db.Database.OpenConnectionAsync();
        try { await using var command = connection.CreateCommand(); command.CommandText = "SELECT COUNT(*) FROM sys.tables WHERE name = 'AppRecords' AND schema_id = SCHEMA_ID('dbo')"; return Convert.ToInt32(await command.ExecuteScalarAsync()) == 1; }
        finally { await db.Database.CloseConnectionAsync(); }
    }
    // Additive upgrade. No existing columns or financial records are rewritten.
    public static Task Apply(StoreDb db) => db.Database.ExecuteSqlRawAsync("""
        IF OBJECT_ID(N'dbo.AppRecords', N'U') IS NULL
        BEGIN
            CREATE TABLE dbo.AppRecords (
                [Key] nvarchar(256) NOT NULL CONSTRAINT PK_AppRecords PRIMARY KEY,
                [Json] nvarchar(max) NOT NULL,
                UpdatedUtc datetime2 NOT NULL);
        END
        """);
}
