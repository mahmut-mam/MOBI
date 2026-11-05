using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MobiStore.Core;

namespace MobiStore.Server;

public static class StoreExtensions
{
    public static bool ValidImage(string url) => url == "" || (url.StartsWith("/api/public/images/", StringComparison.Ordinal) && !url.Contains('\\') && !url.Contains('\r') && !url.Contains('\n')) || Uri.TryCreate(url, UriKind.Absolute, out var uri) && uri.Scheme == "https";
    public static async Task<T?> Read<T>(StoreDb db, string key) where T : class
    {
        var value = await db.AppRecords.AsNoTracking().Where(x => x.Key == key).Select(x => x.Json).SingleOrDefaultAsync();
        return value is null ? null : JsonSerializer.Deserialize<T>(value);
    }
    public static async Task Write(StoreDb db, string key, object value)
    {
        var row = await db.AppRecords.SingleOrDefaultAsync(x => x.Key == key);
        if (row is null) { row = new AppRecord { Key = key }; db.AppRecords.Add(row); }
        row.Json = JsonSerializer.Serialize(value); row.UpdatedUtc = DateTime.UtcNow;
    }
    public static string PriceKey(Guid customer, Guid product) => $"price:{customer:N}:{product:N}";
    public static async Task<decimal> BasePrice(StoreDb db, Customer customer, Product product)
        => (await Read<PriceInput>(db, PriceKey(customer.Id, product.Id)))?.Price ?? (customer.Wholesale ? product.WholesalePrice : product.RetailPrice);
    public static void MapStoreExtensions(this WebApplication app)
    {
        app.MapPost("/api/images", async (ImageInput req, StoreDb db, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Catalog); Rules.Require(req.Data.Length <= 2800000, "الصورة أكبر من 2 ميغابايت");
            byte[] bytes; try { bytes = Convert.FromBase64String(req.Data); } catch (FormatException) { throw new RuleException("ملف الصورة غير صحيح"); }
            Rules.Require(bytes.Length is > 3 and <= 2097152, "حجم الصورة غير صحيح");
            var png = bytes.Length >= 8 && bytes.Take(8).SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
            var jpg = bytes[0] == 255 && bytes[1] == 216 && bytes[2] == 255;
            Rules.Require(png || jpg, "تقبل الصور PNG وJPEG فقط");
            var id = Guid.NewGuid(); await Write(db, "image:" + id.ToString("N"), new ImageInput(req.Data, png ? "image/png" : "image/jpeg")); await db.SaveChangesAsync();
            return new ImageView("/api/public/images/" + id + "?database=" + Uri.EscapeDataString(db.Database.GetDbConnection().Database));
        });
        app.MapGet("/api/public/images/{id:guid}", async (Guid id, string database, DatabaseCatalog catalog) =>
        {
            await using var db = new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(catalog.Connection(database)).Options);
            var image = await Read<ImageInput>(db, "image:" + id.ToString("N"));
            return image is null ? Results.NotFound() : Results.Bytes(Convert.FromBase64String(image.Data), image.ContentType);
        });
        app.MapPut("/api/products/{id:guid}/card", async (Guid id, ProductCardInput req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Catalog); var p = req.Product; var d = req.Details;
            Rules.Require(d.ProductId == id && p.Code.Length is > 0 and <= 50 && p.Name.Length is > 0 and <= 150 && p.RetailPrice >= 0 && p.WholesalePrice >= 0 && d.Brand.Length <= 100 && d.Models.Length <= 1000 && d.Color.Length <= 100 && d.ImageUrl.Length <= 1000 && ValidImage(d.ImageUrl), "تحقق من بيانات البطاقة والأسعار والصورة");
            Rules.Precision(p.RetailPrice); Rules.Precision(p.WholesalePrice);
            return await service.Run(Guid.NewGuid(), "PRODUCT_CARD", new { id, req }, Security.Current(ctx), async _ =>
            {
                var old = await Read<ProductDetails>(db, "product:" + id.ToString("N"));
                if (d.Published != (old?.Published ?? false)) Security.Demand(ctx, Access.Publish);
                var product = await db.Products.SingleAsync(x => x.Id == id);
                product.Code = p.Code.Trim(); product.Name = p.Name.Trim(); product.RetailPrice = p.RetailPrice; product.WholesalePrice = p.WholesalePrice; product.Published = d.Published;
                await Write(db, "product:" + id.ToString("N"), d); return 0;
            });
        });
        app.MapGet("/api/announcements", async (DateTime since, StoreDb db, HttpContext ctx) =>
        {
            Security.Admin(ctx);
            var keys = await db.AppRecords.Where(x => x.Key.StartsWith("product-created:") && x.UpdatedUtc >= since).Select(x => x.Key).ToListAsync();
            var ids = keys.Select(x => Guid.ParseExact(x[16..], "N")).ToArray();
            var items = await db.Products.Where(x => ids.Contains(x.Id)).ToListAsync(); var output = new List<object>();
            foreach (var p in items)
            {
                var detail = await Read<ProductDetails>(db, "product:" + p.Id.ToString("N"));
                output.Add(new { p.Id, p.Code, p.Name, Price = p.RetailPrice, detail?.ImageUrl });
            }
            return output;
        });
        app.MapGet("/api/products/{id:guid}/details", async (Guid id, StoreDb db) =>
            await Read<ProductDetails>(db, "product:" + id.ToString("N")) ?? new(id, "", "", "", "", false));
        app.MapPut("/api/products/{id:guid}/details", async (Guid id, ProductDetails req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Catalog);
            Rules.Require(req.ProductId == id && req.Brand.Length <= 100 && req.Models.Length <= 1000 && req.Color.Length <= 100 && req.ImageUrl.Length <= 1000, "تحقق من بيانات المادة");
            Rules.Require(ValidImage(req.ImageUrl), "اختر صورة مرفوعة أو رابط HTTPS");
            var user = Security.Current(ctx);
            var old = await Read<ProductDetails>(db, "product:" + id.ToString("N"));
            if (req.Published != (old?.Published ?? false)) Security.Demand(ctx, Access.Publish);
            var product = await db.Products.SingleAsync(x => x.Id == id);
            await service.Run(Guid.NewGuid(), "PRODUCT_DETAILS", req, user, async _ =>
            { await Write(db, "product:" + id.ToString("N"), req); product.Published = req.Published; return 0; });
            return Results.NoContent();
        });
        app.MapPut("/api/products/{id:guid}", async (Guid id, ProductInput req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Catalog);
            Rules.Require(req.Code.Length is > 0 and <= 50 && req.Name.Length is > 0 and <= 150 && req.RetailPrice >= 0 && req.WholesalePrice >= 0, "تحقق من اسم المادة وأسعارها");
            Rules.Precision(req.RetailPrice); Rules.Precision(req.WholesalePrice);
            return await service.Run(Guid.NewGuid(), "PRODUCT_EDIT", new { id, req }, Security.Current(ctx), async _ =>
            {
                var p = await db.Products.SingleAsync(x => x.Id == id);
                p.Code = req.Code.Trim(); p.Name = req.Name.Trim(); p.RetailPrice = req.RetailPrice; p.WholesalePrice = req.WholesalePrice;
                return 0;
            });
        });
        app.MapGet("/api/prices/{customer:guid}/{product:guid}", async (Guid customer, Guid product, StoreDb db) =>
            new PriceInput(customer, product, await BasePrice(db, await db.Customers.SingleAsync(x => x.Id == customer), await db.Products.SingleAsync(x => x.Id == product))));
        app.MapPost("/api/prices", async (PriceInput req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Catalog); Rules.Precision(req.Price); Rules.Require(req.Price >= 0, "السعر لا يكون سالبًا");
            Rules.Require(await db.Customers.AnyAsync(x => x.Id == req.CustomerId) && await db.Products.AnyAsync(x => x.Id == req.ProductId), "المادة أو العميل غير موجود");
            return await service.Run(Guid.NewGuid(), "PRICE", req, Security.Current(ctx), async _ => { await Write(db, PriceKey(req.CustomerId, req.ProductId), req); return 0; });
        });
        app.MapGet("/api/stocks", async (StoreDb db) => await (from s in db.Stocks join p in db.Products on s.ProductId equals p.Id join w in db.Warehouses on s.WarehouseId equals w.Id select new StockView(p.Id, p.Name, w.Id, w.Name, s.Quantity)).ToListAsync());
        app.MapPost("/api/stocks/transfer", async (StockMoveRequest req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Catalog);
            return await service.Run(req.RequestId, "TRANSFER", req, Security.Current(ctx), async _ =>
            {
                Rules.Require(req.FromWarehouseId != req.ToWarehouseId && req.Reason.Length is > 0 and <= 500, "حدد مستودعين مختلفين وسبب التحويل");
                Rules.Require(await db.Warehouses.AnyAsync(x => x.Id == req.ToWarehouseId), "المستودع الهدف غير موجود");
                var unit = await db.Units.SingleAsync(x => x.Id == req.UnitId); var qty = Rules.UnitQuantity(req.Quantity, unit.Factor);
                var updated = await db.Stocks.Where(x => x.ProductId == unit.ProductId && x.WarehouseId == req.FromWarehouseId && x.Quantity >= qty).ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - qty));
                Rules.Require(updated == 1, "الرصيد غير كافٍ للتحويل");
                var target = await db.Stocks.SingleOrDefaultAsync(x => x.ProductId == unit.ProductId && x.WarehouseId == req.ToWarehouseId);
                if (target is null) db.Stocks.Add(new() { ProductId = unit.ProductId, WarehouseId = req.ToWarehouseId, Quantity = qty }); else target.Quantity += qty;
                return 0;
            });
        });
        app.MapPost("/api/stocks/count", async (StockCountRequest req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Admin(ctx); Rules.Precision(req.Counted); Rules.Require(req.Counted >= 0 && req.Reason.Length is > 0 and <= 500, "أدخل الجرد الفعلي وسبب التسوية");
            return await service.Run(req.RequestId, "COUNT", req, Security.Current(ctx), async _ =>
            {
                var stock = await db.Stocks.SingleOrDefaultAsync(x => x.ProductId == req.ProductId && x.WarehouseId == req.WarehouseId);
                Rules.Require((stock?.Quantity ?? 0) == req.Expected, "تغير الرصيد منذ فتح الجرد؛ حدّث البيانات قبل الحفظ");
                var p = await db.Products.SingleAsync(x => x.Id == req.ProductId);
                Rules.Require(await db.Warehouses.AnyAsync(x => x.Id == req.WarehouseId), "المستودع غير موجود");
                var difference = req.Counted - (stock?.Quantity ?? 0); var value = Rules.Round(Math.Abs(difference) * p.AverageCost);
                if (stock is null) db.Stocks.Add(new() { ProductId = p.Id, WarehouseId = req.WarehouseId, Quantity = req.Counted }); else stock.Quantity = req.Counted;
                if (value > 0) db.JournalLines.AddRange(new JournalLine { OperationId = req.RequestId, Account = "INVENTORY", Debit = difference > 0 ? value : 0, Credit = difference < 0 ? value : 0 }, new JournalLine { OperationId = req.RequestId, Account = "STOCK_DIFFERENCE", Debit = difference < 0 ? value : 0, Credit = difference > 0 ? value : 0 });
                return value;
            });
        });
        app.MapGet("/api/exchange-rate", async (StoreDb db) => await Read<RateInput>(db, "exchange-rate") ?? new(1));
        app.MapPost("/api/exchange-rate", async (RateInput req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Admin(ctx); Rules.Precision(req.Rate); Rules.Require(req.Rate > 0, "سعر الصرف يجب أن يكون موجبًا");
            return await service.Run(Guid.NewGuid(), "RATE", req, Security.Current(ctx), async _ => { await Write(db, "exchange-rate", req); return 0; });
        });
        app.MapGet("/api/audit", async (StoreDb db, HttpContext ctx) =>
        {
            Security.Admin(ctx); return await (from o in db.Operations join u in db.Users on o.UserId equals u.Id orderby o.CreatedUtc descending select new { o.Id, o.Number, o.Kind, User = u.DisplayName, o.CreatedUtc, o.TotalBase }).Take(300).ToListAsync();
        });
        app.MapGet("/api/audit/{id:guid}", async (Guid id, StoreDb db, HttpContext ctx) =>
        { Security.Admin(ctx); var row = await db.Operations.AsNoTracking().SingleAsync(x => x.Id == id); return new { row.Number, row.Kind, row.Details, row.CreatedUtc }; });
        app.MapPut("/api/users/{id:guid}", async (Guid id, UserUpdate req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Admin(ctx); var current = Security.Current(ctx);
            Rules.Require(req.DisplayName.Length is > 0 and <= 100 && req.Reason.Length is > 0 and <= 500 && req.Permissions.All(Access.All.Contains) && req.MaxDiscountPercent is >= 0 and <= 100, "تحقق من البيانات والصلاحيات وسبب التعديل");
            return await service.Run(Guid.NewGuid(), "USER_EDIT", new { id, req }, current, async _ =>
            {
                var user = await db.Users.SingleAsync(x => x.Id == id);
                Rules.Require(!user.IsAdmin || req.Active, "لا يمكن تعطيل المدير من هذه النافذة");
                user.DisplayName = req.DisplayName.Trim(); user.Active = req.Active; user.Permissions = string.Join(',', req.Permissions.Distinct()); user.MaxDiscountPercent = req.MaxDiscountPercent;
                await db.LoginSessions.Where(x => x.UserId == id).ExecuteDeleteAsync(); return 0;
            });
        });
        app.MapGet("/api/users/{id:guid}", async (Guid id, StoreDb db, HttpContext ctx) =>
        {
            Security.Admin(ctx); var user = await db.Users.SingleAsync(x => x.Id == id);
            return new UserUpdate(user.DisplayName, user.Active, user.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries), user.MaxDiscountPercent, "");
        });
        app.MapGet("/api/drafts", async (StoreDb db, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Sale); var prefix = "draft:" + Security.Current(ctx).Id.ToString("N") + ":";
            return await db.AppRecords.Where(x => x.Key.StartsWith(prefix)).Select(x => new { x.Key, x.Json, x.UpdatedUtc }).ToListAsync();
        });
        app.MapPost("/api/drafts/{id:guid}", async (Guid id, SaleRequest req, StoreDb db, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Sale); Rules.Require(req.Lines.Length <= 100, "عدد البنود كبير");
            await Write(db, "draft:" + Security.Current(ctx).Id.ToString("N") + ":" + id.ToString("N"), req); await db.SaveChangesAsync(); return Results.NoContent();
        });
        app.MapDelete("/api/drafts/{id:guid}", async (Guid id, StoreDb db, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Sale); var key = "draft:" + Security.Current(ctx).Id.ToString("N") + ":" + id.ToString("N");
            await db.AppRecords.Where(x => x.Key == key).ExecuteDeleteAsync(); return Results.NoContent();
        });
    }
}
