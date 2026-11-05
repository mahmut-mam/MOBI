using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using MobiStore.Core;

namespace MobiStore.Server;

public record DatabaseProfile(string Name, string Title);
public sealed class DatabaseCatalog
{
    private readonly string connection;
    private readonly string file;
    private readonly object gate = new();
    private List<DatabaseProfile> profiles;
    public DatabaseCatalog(IConfiguration configuration, IWebHostEnvironment environment)
    {
        connection = configuration.GetConnectionString("Store")!;
        file = configuration["DatabaseCatalogFile"] ?? Path.Combine(environment.ContentRootPath, "database-catalog.json");
        var initial = new SqlConnectionStringBuilder(connection).InitialCatalog;
        profiles = File.Exists(file) ? JsonSerializer.Deserialize<List<DatabaseProfile>>(File.ReadAllText(file))! : [];
        if (!profiles.Any(x => x.Name == initial)) profiles.Add(new(initial, initial));
    }
    public DatabaseProfile[] List() { lock (gate) return profiles.ToArray(); }
    public string Connection(string? database)
    {
        var cs = new SqlConnectionStringBuilder(connection);
        if (!string.IsNullOrEmpty(database))
        {
            lock (gate) Rules.Require(profiles.Any(x => x.Name == database), "قاعدة البيانات غير مسجلة على هذا الخادم");
            cs.InitialCatalog = database;
        }
        return cs.ConnectionString;
    }
    public async Task<DatabaseProfile> Add(DatabaseProfile profile, bool create)
    {
        Rules.Require(Regex.IsMatch(profile.Name, "^MobiStore_[A-Za-z0-9_]{1,80}$"), "اسم القاعدة يبدأ بـ MobiStore_ ويحتوي حروفًا إنكليزية وأرقامًا وشرطة سفلية");
        Rules.Require(!string.IsNullOrWhiteSpace(profile.Title) && profile.Title.Length <= 100, "أدخل اسمًا وصفيًا حتى 100 حرف");
        lock (gate) Rules.Require(!profiles.Any(x => x.Name == profile.Name), "قاعدة البيانات مسجلة بالفعل");
        var cs = new SqlConnectionStringBuilder(connection) { InitialCatalog = profile.Name };
        if (create)
        {
            var master = new SqlConnectionStringBuilder(connection) { InitialCatalog = "master" };
            await using var sql = new SqlConnection(master.ConnectionString);
            await sql.OpenAsync();
            await using var exists = new SqlCommand("SELECT COUNT(*) FROM sys.databases WHERE name=@name", sql);
            exists.Parameters.AddWithValue("@name", profile.Name);
            Rules.Require(Convert.ToInt32(await exists.ExecuteScalarAsync()) == 0, "القاعدة موجودة؛ استخدم إضافة قاعدة موجودة");
            await using var command = new SqlCommand($"CREATE DATABASE [{profile.Name}]", sql);
            await command.ExecuteNonQueryAsync();
            await using var db = new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(cs.ConnectionString).Options);
            await db.Database.EnsureCreatedAsync();
        }
        // Registration only accepts the application schema, and never upgrades an attached database.
        await using (var sql = new SqlConnection(cs.ConnectionString))
        {
            await sql.OpenAsync();
            await using var check = new SqlCommand("SELECT COUNT(*) FROM sys.tables WHERE name IN ('Companies','Users','LoginSessions','Products','Sales','CustomerEntries')", sql);
            Rules.Require(Convert.ToInt32(await check.ExecuteScalarAsync()) == 6, "هذه ليست قاعدة موبي ستور متوافقة");
        }
        lock (gate)
        {
            Rules.Require(!profiles.Any(x => x.Name == profile.Name), "قاعدة البيانات مسجلة بالفعل");
            var next = profiles.Append(profile).ToList();
            File.WriteAllText(file + ".tmp", JsonSerializer.Serialize(next));
            File.Move(file + ".tmp", file, true);
            profiles = next;
        }
        return profile;
    }
    public static void Local(HttpContext context) => Rules.Require(context.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip), "إدارة القواعد متاحة من جهاز السيرفر فقط");
}
