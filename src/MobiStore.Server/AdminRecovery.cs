using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
namespace MobiStore.Server;
public static class AdminRecovery
{
 public static async Task<bool> Handle(IConfiguration config, string[] args)
 {
if (args.Length == 0 || args[0] is not "--list-admins" and not "--reset-admin") return false;
var connection = config.GetConnectionString("Store")!;
await using var db = new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(connection).Options);
if (args.Length == 1 && args[0] == "--list-admins")
{
    Console.WriteLine(JsonSerializer.Serialize(new { database = db.Database.GetDbConnection().Database, users = await db.Users.Where(x => x.IsAdmin && x.Active).Select(x => new { x.Id, x.Username, x.DisplayName }).ToListAsync() }));
    return true;
}
if (args.Length != 2 || args[0] != "--reset-admin") return false;
await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
var user = await db.Users.SingleAsync(x => x.Username == args[1] && x.IsAdmin && x.Active);
var password = "Mobi!" + Convert.ToHexString(RandomNumberGenerator.GetBytes(10)).ToLowerInvariant();
user.PasswordHash = Security.HashPassword(password);
await db.LoginSessions.Where(x => x.UserId == user.Id).ExecuteDeleteAsync();
db.Operations.Add(new Operation { Id = Guid.NewGuid(), Kind = "ADMIN_PASSWORD_RESET", Number = "RESET-" + Guid.NewGuid().ToString("N"), UserId = user.Id, Details = "{\"reason\":\"Local administrator recovery requested by account owner\"}" });
await db.SaveChangesAsync(); await tx.CommitAsync();
if (!Security.Verify(password, user.PasswordHash)) throw new InvalidOperationException("Password verification failed");
Console.WriteLine(JsonSerializer.Serialize(new { database = db.Database.GetDbConnection().Database, username = user.Username, password, sessionsRevoked = true }));

 return true;
 }
}
