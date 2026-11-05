using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace MobiStore.Server;

public record BackupInfo(string File, DateTime CreatedUtc, bool Verified);
public record BackupSettings(string Directory);
public static class BackupService
{
    private static readonly SemaphoreSlim Gate = new(1);
    public static async Task<BackupInfo> Create(StoreDb db, bool record = true)
    {
        await Gate.WaitAsync();
        try
        {
            var cs = new SqlConnectionStringBuilder(db.Database.GetConnectionString());
            var name = cs.InitialCatalog;
            await using var sql = new SqlConnection(cs.ConnectionString); await sql.OpenAsync();
            var settings = record ? await StoreExtensions.Read<BackupSettings>(db, "backup:settings") : null;
            await using var directory = new SqlCommand("SELECT CONVERT(nvarchar(4000), SERVERPROPERTY('InstanceDefaultBackupPath'))", sql);
            var folder = !string.IsNullOrWhiteSpace(settings?.Directory) ? settings.Directory : Convert.ToString(await directory.ExecuteScalarAsync());
            if (string.IsNullOrWhiteSpace(folder)) throw new MobiStore.Core.RuleException("تعذر تحديد مجلد نسخ SQL؛ اضبط مجلد النسخ الافتراضي على السيرفر");
            var file = Path.Combine(folder, name + "_" + DateTime.UtcNow.ToString("yyyyMMdd_HHmmss") + "_" + Guid.NewGuid().ToString("N")[..8] + ".bak");
            await using var command = new SqlCommand($"BACKUP DATABASE [{name.Replace("]", "]]")}] TO DISK=@file WITH COPY_ONLY, CHECKSUM; RESTORE VERIFYONLY FROM DISK=@file WITH CHECKSUM;", sql) { CommandTimeout = 300 };
            command.Parameters.AddWithValue("@file", file); await command.ExecuteNonQueryAsync();
            var info = new BackupInfo(file, DateTime.UtcNow, true);
            if (record) { await StoreExtensions.Write(db, "backup:last", info); await db.SaveChangesAsync(); } return info;
        }
        finally { Gate.Release(); }
    }
    public static void MapBackups(this WebApplication app)
    {
        app.MapGet("/api/backups/settings", async (StoreDb db, HttpContext ctx) => { Security.Admin(ctx); return await StoreExtensions.Read<BackupSettings>(db, "backup:settings") ?? new(""); });
        app.MapPost("/api/backups/settings", async (BackupSettings req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Admin(ctx); DatabaseCatalog.Local(ctx);
            MobiStore.Core.Rules.Require(req.Directory.Length <= 1000 && (req.Directory == "" || Path.IsPathFullyQualified(req.Directory)) && !req.Directory.Any(char.IsControl), "أدخل مسار مجلد كاملًا على السيرفر أو اتركه فارغًا لاستخدام مجلد SQL الافتراضي");
            return await service.Run(Guid.NewGuid(), "BACKUP_PATH", req, Security.Current(ctx), async _ => { await StoreExtensions.Write(db, "backup:settings", req); return 0; });
        });
        app.MapGet("/api/backups", async (StoreDb db, HttpContext ctx) => { Security.Admin(ctx); return await StoreExtensions.Read<BackupInfo>(db, "backup:last"); });
        app.MapPost("/api/backups", async (StoreDb db, HttpContext ctx) => { Security.Admin(ctx); DatabaseCatalog.Local(ctx); return await Create(db); });
        app.MapPost("/api/backups/restore-test", async (StoreDb db, HttpContext ctx) =>
        {
            Security.Admin(ctx); DatabaseCatalog.Local(ctx);
            var backup = await StoreExtensions.Read<BackupInfo>(db, "backup:last") ?? throw new MobiStore.Core.RuleException("أنشئ نسخة احتياطية أولًا");
            await Gate.WaitAsync();
            try
            {
                var cs = new SqlConnectionStringBuilder(db.Database.GetConnectionString()) { InitialCatalog = "master" };
                await using var sql = new SqlConnection(cs.ConnectionString); await sql.OpenAsync();
                await using var paths = new SqlCommand("SELECT CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultDataPath')), CONVERT(nvarchar(4000),SERVERPROPERTY('InstanceDefaultLogPath'))", sql);
                string data, log;
                await using (var reader = await paths.ExecuteReaderAsync()) { await reader.ReadAsync(); data = reader.GetString(0); log = reader.GetString(1); }
                var target = "MobiStore_RestoreTest_" + Guid.NewGuid().ToString("N");
                await using var files = new SqlCommand("RESTORE FILELISTONLY FROM DISK=@file", sql); files.Parameters.AddWithValue("@file", backup.File);
                var moves = new List<string>(); static string Literal(string text) => "N'" + text.Replace("'", "''") + "'";
                await using (var reader = await files.ExecuteReaderAsync())
                    while (await reader.ReadAsync())
                    {
                        var logical = reader.GetString(reader.GetOrdinal("LogicalName")); var kind = reader.GetString(reader.GetOrdinal("Type"));
                        MobiStore.Core.Rules.Require(kind is "D" or "L", "نسخة الاستعادة تحتوي نوع ملفات غير مدعوم");
                        var destination = Path.Combine(kind == "L" ? log : data, target + "_" + moves.Count + (kind == "L" ? ".ldf" : ".mdf"));
                        moves.Add("MOVE " + Literal(logical) + " TO " + Literal(destination));
                    }
                await using var restore = new SqlCommand($"RESTORE DATABASE [{target}] FROM DISK=@file WITH CHECKSUM, " + string.Join(", ", moves), sql) { CommandTimeout = 300 };
                restore.Parameters.AddWithValue("@file", backup.File); await restore.ExecuteNonQueryAsync();
                cs.InitialCatalog = target;
                await using var restored = new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(cs.ConnectionString).Options);
                var company = await restored.Companies.AsNoTracking().SingleAsync(); var products = await restored.Products.CountAsync();
                return Results.Ok(new { database = target, company = company.Name, company.BaseCurrency, products, message = "نجحت الاستعادة إلى قاعدة اختبار منفصلة. لم تُستبدل القاعدة الأصلية." });
            }
            finally { Gate.Release(); }
        });
    }
}
public sealed class DailyBackups(IServiceScopeFactory scopes, ILogger<DailyBackups> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMinutes(30));
        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            using var scope = scopes.CreateScope(); var catalog = scope.ServiceProvider.GetRequiredService<DatabaseCatalog>();
            foreach (var profile in catalog.List())
            {
                try
                {
                    await using var db = new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(catalog.Connection(profile.Name)).Options);
                    if (!await db.Companies.AnyAsync(stoppingToken)) continue;
                    var previous = await StoreExtensions.Read<BackupInfo>(db, "backup:last");
                    if (previous is null || previous.CreatedUtc < DateTime.UtcNow.AddHours(-24)) await BackupService.Create(db);
                }
                catch (Exception ex) { logger.LogError(ex, "Daily backup failed for {Database}", profile.Name); }
            }
        }
    }
}
