using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using MobiStore.Core;

namespace MobiStore.Server;

public static class Security
{
    public static string HashPassword(string password)
    {
        Rules.Require(password.Length >= 12 && password.Length <= 128, "كلمة المرور يجب أن تكون بين 12 و128 حرفًا");
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, 210000, HashAlgorithmName.SHA512, 32);
        return $"210000:{Convert.ToBase64String(salt)}:{Convert.ToBase64String(hash)}";
    }
    public static bool Verify(string password, string stored)
    {
        try
        {
            var p = stored.Split(':');
            var actual = Rfc2898DeriveBytes.Pbkdf2(password, Convert.FromBase64String(p[1]), int.Parse(p[0]), HashAlgorithmName.SHA512, 32);
            return CryptographicOperations.FixedTimeEquals(actual, Convert.FromBase64String(p[2]));
        }
        catch (Exception e) when (e is FormatException or IndexOutOfRangeException) { return false; }
    }
    public static string TokenHash(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
    public static User Current(HttpContext ctx) => (User)ctx.Items["user"]!;
    public static void Demand(HttpContext ctx, string permission)
    {
        var user = Current(ctx);
        if (!user.IsAdmin && !user.Permissions.Split(',').Contains(permission)) throw new ForbiddenException();
    }
    public static void Admin(HttpContext ctx) { if (!Current(ctx).IsAdmin) throw new ForbiddenException(); }
    public static async Task Authenticate(HttpContext ctx, RequestDelegate next)
    {
        var publicPath = ctx.Request.Path.Value is "/health" or "/api/setup" or "/api/login" or "/api/databases" or "/api/databases/create" or "/api/databases/register";
        if (publicPath || ctx.Request.Path.Value is "/catalog" or "/api/public/catalog" or "/mobile" or "/brand.svg" || ctx.Request.Path.StartsWithSegments("/api/public/images")) { await next(ctx); return; }
        var header = ctx.Request.Headers.Authorization.ToString();
        if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) { ctx.Response.StatusCode = 401; return; }
        var db = ctx.RequestServices.GetRequiredService<StoreDb>();
        var hash = TokenHash(header[7..]);
        var login = await db.LoginSessions.AsNoTracking().SingleOrDefaultAsync(x => x.TokenHash == hash && x.ExpiresUtc > DateTime.UtcNow);
        var user = login is null ? null : await db.Users.AsNoTracking().SingleOrDefaultAsync(x => x.Id == login.UserId && x.Active);
        if (user is null) { ctx.Response.StatusCode = 401; return; }
        ctx.Items["user"] = user;
        await next(ctx);
    }
}
public sealed class ForbiddenException : Exception;
