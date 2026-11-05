using System.Security.Cryptography;
using System.Threading.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using MobiStore.Core;
using MobiStore.Server;

var builder = WebApplication.CreateBuilder(args);
builder.Configuration.AddJsonFile("appsettings.Local.json", optional: true);
if (await AdminRecovery.Handle(builder.Configuration, args)) return;
builder.Services.AddHttpContextAccessor();
builder.Services.AddSingleton<DatabaseCatalog>();
builder.Services.AddDbContext<StoreDb>((sp, o) => o.UseSqlServer(sp.GetRequiredService<DatabaseCatalog>().Connection(
    sp.GetRequiredService<IHttpContextAccessor>().HttpContext?.Request.Headers["X-MobiStore-Database"].ToString())));
builder.Services.AddScoped<StoreService>();
builder.Services.AddHostedService<DailyBackups>();
builder.Services.AddRateLimiter(o => o.AddPolicy("login", ctx => RateLimitPartition.GetFixedWindowLimiter(
    ctx.Connection.RemoteIpAddress?.ToString() ?? "local", _ => new FixedWindowRateLimiterOptions { PermitLimit = 10, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 })));
var app = builder.Build();
app.MapStoreExtensions();
app.MapSuppliers();
app.MapBackups();
app.MapWebPages();
app.Use(async (ctx, next) =>
{
    try { await next(); }
    catch (RuleException e) { ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = e.Message }); }
    catch (ForbiddenException) { ctx.Response.StatusCode = 403; await ctx.Response.WriteAsJsonAsync(new { error = "لا تملك صلاحية هذه العملية" }); }
    catch (DbUpdateException e)
    {
        app.Logger.LogWarning(e, "Database constraint rejected request");
        ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "تعارض بيانات أو رمز مكرر. حدّث البيانات وأعد المحاولة" });
    }
    catch (SqlException e) when (e.Number is 1205 or 2601 or 2627)
    {
        ctx.Response.StatusCode = 409; await ctx.Response.WriteAsJsonAsync(new { error = "تزامنت عملية أخرى. أعد الطلب بنفس معرف العملية" });
    }
    catch (InvalidOperationException e)
    {
        app.Logger.LogWarning(e, "Invalid operation");
        ctx.Response.StatusCode = 400; await ctx.Response.WriteAsJsonAsync(new { error = "السجل غير موجود أو تغيرت حالته. حدّث البيانات" });
    }
});
app.UseRateLimiter();
app.Use(Security.Authenticate);
app.MapGet("/api/databases", (DatabaseCatalog catalog) => catalog.List());
app.MapPost("/api/databases/create", async (DatabaseProfile profile, DatabaseCatalog catalog, HttpContext ctx) =>
{ DatabaseCatalog.Local(ctx); return await catalog.Add(profile, true); });
app.MapPost("/api/databases/register", async (DatabaseProfile profile, DatabaseCatalog catalog, HttpContext ctx) =>
{ DatabaseCatalog.Local(ctx); return await catalog.Add(profile, false); });
app.MapGet("/health", () => new { status = "ok", product = "MobiStore", version = "0.2.0" });
app.MapGet("/api/setup", async (StoreDb db) =>
{
    var c = await db.Companies.AsNoTracking().FirstOrDefaultAsync();
    return new SetupStatus(c is not null, c?.Name ?? "", c?.BaseCurrency ?? "USD");
});
app.MapPost("/api/setup", async (SetupRequest req, StoreDb db, HttpContext ctx) =>
{
    Rules.Require(ctx.Connection.RemoteIpAddress is { } ip && System.Net.IPAddress.IsLoopback(ip), "الإعداد الأول يتم من السيرفر محليًا فقط");
    Rules.Require(req.BaseCurrency is "USD" or "SYP", "اختر الدولار أو الليرة");
    Rules.Require(!string.IsNullOrWhiteSpace(req.ShopName) && req.ShopName.Length <= 100 && req.Username.Length is >= 3 and <= 50 && req.DisplayName.Length is > 0 and <= 100, "تحقق من بيانات المنشأة والمدير");
    await using var tx = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
    Rules.Require(!await db.Companies.AnyAsync(), "تم إعداد المنشأة بالفعل");
    db.Companies.Add(new() { Name = req.ShopName.Trim(), BaseCurrency = req.BaseCurrency });
    var user = new User { Username = req.Username.Trim().ToLowerInvariant(), DisplayName = req.DisplayName.Trim(), PasswordHash = Security.HashPassword(req.Password), IsAdmin = true, Permissions = string.Join(',', Access.All), MaxDiscountPercent = 100 };
    db.Users.Add(user); db.Warehouses.Add(new() { Name = "المستودع الرئيسي" });
    db.Customers.Add(new() { Name = "عميل نقدي", DueDays = 0 });
    db.Employees.Add(new() { UserId = user.Id, Name = user.DisplayName });
    await db.SaveChangesAsync(); await tx.CommitAsync();
    return Results.Ok(new { message = "تم إعداد المنشأة. سجل الدخول" });
}).RequireRateLimiting("login");
app.MapPost("/api/login", async (LoginRequest req, StoreDb db) =>
{
    var name = req.Username.Trim().ToLowerInvariant();
    var user = await db.Users.SingleOrDefaultAsync(x => x.Username == name && x.Active);
    // Same PBKDF2 work for unknown accounts to avoid a quick username-existence signal.
    var valid = Security.Verify(req.Password, user?.PasswordHash ?? "210000:AAAAAAAAAAAAAAAAAAAAAA==:AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA=");
    if (user is null || !valid) return Results.Json(new { error = "اسم المستخدم أو كلمة المرور غير صحيحة" }, statusCode: 401);
    var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(48));
    db.LoginSessions.Add(new() { UserId = user.Id, TokenHash = Security.TokenHash(token), ExpiresUtc = DateTime.UtcNow.AddHours(8) });
    await db.SaveChangesAsync();
    return Results.Ok(new LoginResponse(token, user.Id, user.DisplayName, user.IsAdmin, user.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)));
}).RequireRateLimiting("login");
app.MapPost("/api/logout", async (StoreDb db, HttpContext ctx) =>
{
    var hash = Security.TokenHash(ctx.Request.Headers.Authorization.ToString()[7..]);
    await db.LoginSessions.Where(x => x.TokenHash == hash).ExecuteDeleteAsync(); return Results.NoContent();
});
app.MapGet("/api/session", async (StoreDb db, HttpContext ctx) =>
{
    var user = Security.Current(ctx);
    var company = await db.Companies.AsNoTracking().SingleAsync();
    return new { user = new LoginResponse("", user.Id, user.DisplayName, user.IsAdmin, user.Permissions.Split(',', StringSplitOptions.RemoveEmptyEntries)),
        company = new SetupStatus(true, company.Name, company.BaseCurrency), serverUtc = DateTime.UtcNow };
});
app.MapGet("/api/permissions", (HttpContext ctx) => { Security.Admin(ctx); return Access.All; });
app.MapGet("/api/users", async (StoreDb db, HttpContext ctx) =>
{
    Security.Admin(ctx); return await db.Users.Select(x => new UserView(x.Id, x.Username, x.DisplayName, x.IsAdmin, x.Active)).ToListAsync();
});
app.MapPost("/api/users", async (UserInput req, StoreDb db, HttpContext ctx) =>
{
    Security.Admin(ctx);
    Rules.Require(req.Username.Length is >= 3 and <= 50 && req.DisplayName.Length is > 0 and <= 100, "تحقق من الاسم واسم الدخول");
    Rules.Require(req.Permissions.All(Access.All.Contains) && req.MaxDiscountPercent is >= 0 and <= 100, "صلاحيات أو حد خصم غير صحيح");
    var user = new User { Username = req.Username.Trim().ToLowerInvariant(), DisplayName = req.DisplayName.Trim(), PasswordHash = Security.HashPassword(req.Password), IsAdmin = req.IsAdmin, Permissions = string.Join(',', req.Permissions.Distinct()), MaxDiscountPercent = req.MaxDiscountPercent };
    db.Users.Add(user); db.Employees.Add(new() { Name = user.DisplayName, UserId = user.Id }); await db.SaveChangesAsync(); return new { user.Id };
});
app.MapGet("/api/warehouses", async (StoreDb db) => await db.Warehouses.Select(x => new WarehouseView(x.Id, x.Name)).ToListAsync());
app.MapPost("/api/warehouses", async (WarehouseView req, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Catalog); Rules.Require(req.Name.Length is > 0 and <= 100, "اسم المستودع مطلوب");
    var item = new Warehouse { Name = req.Name }; db.Warehouses.Add(item); await db.SaveChangesAsync(); return new WarehouseView(item.Id, item.Name);
});
app.MapGet("/api/products", async (StoreDb db, HttpContext ctx) =>
{
    var user = Security.Current(ctx); var canCost = user.IsAdmin || user.Permissions.Split(',').Contains(Access.Costs);
    var products = await db.Products.AsNoTracking().OrderBy(x => x.Code).ToListAsync();
    var units = await db.Units.AsNoTracking().ToListAsync();
    var stocks = await db.Stocks.AsNoTracking().GroupBy(x => x.ProductId).Select(g => new { Id = g.Key, Qty = g.Sum(x => x.Quantity) }).ToDictionaryAsync(x => x.Id, x => x.Qty);
    return products.Select(p => new ProductView(p.Id, p.Code, p.Name, stocks.GetValueOrDefault(p.Id), p.RetailPrice, p.WholesalePrice,
        canCost ? p.AverageCost : null, units.Where(u => u.ProductId == p.Id).Select(u => new UnitView(u.Id, u.Name, u.Barcode, u.Factor)).ToArray()));
});
app.MapPost("/api/products", async (ProductInput req, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Catalog);
    Rules.Require(req.Code.Length is > 0 and <= 50 && req.Name.Length is > 0 and <= 150 && req.Unit.Length is > 0 and <= 30 && req.Barcode.Length <= 80, "تحقق من بيانات المادة");
    Rules.Require(req.Factor == 1 && req.RetailPrice >= 0 && req.WholesalePrice >= 0, "الوحدة الأولى أساسية ومعاملها 1 والأسعار غير سالبة");
    var product = new Product { Code = req.Code.Trim(), Name = req.Name.Trim(), RetailPrice = req.RetailPrice, WholesalePrice = req.WholesalePrice };
    db.Products.Add(product); db.Units.Add(new() { ProductId = product.Id, Name = req.Unit, Barcode = req.Barcode.Trim(), Factor = 1 });
    db.AppRecords.Add(new() { Key = "product-created:" + product.Id.ToString("N"), Json = System.Text.Json.JsonSerializer.Serialize(new { createdUtc = DateTime.UtcNow }) });
    await db.SaveChangesAsync(); return new { product.Id };
});
app.MapPost("/api/products/{id:guid}/units", async (Guid id, UnitView req, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Catalog); Rules.Require(req.Factor > 0 && req.Name.Length is > 0 and <= 30 && req.Barcode.Length <= 80, "تحقق من الوحدة");
    Rules.Require(await db.Products.AnyAsync(x => x.Id == id), "المادة غير موجودة");
    var unit = new ProductUnit { ProductId = id, Name = req.Name, Barcode = req.Barcode, Factor = req.Factor }; db.Units.Add(unit); await db.SaveChangesAsync(); return new { unit.Id };
});
app.MapGet("/api/customers", async (StoreDb db, HttpContext ctx) =>
{
    var u = Security.Current(ctx); var canView = u.IsAdmin || u.Permissions.Split(',').Contains(Access.Accounts);
    var balances = await db.CustomerEntries.GroupBy(x => x.CustomerId).Select(g => new { Id = g.Key, Balance = g.Sum(x => x.Debit - x.Credit) }).ToDictionaryAsync(x => x.Id, x => x.Balance);
    var customers = await db.Customers.AsNoTracking().ToListAsync();
    return customers.Select(x => new CustomerView(x.Id, x.Name, x.Phone, canView ? balances.GetValueOrDefault(x.Id) : null, x.CreditLimit, x.DueDays, x.Wholesale));
});
app.MapPost("/api/customers", async (CustomerInput req, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Customers); Rules.Require(req.Name.Length is > 0 and <= 150 && req.Phone.Length <= 50 && req.CreditLimit >= 0 && req.DueDays is >= 0 and <= 3650, "تحقق من بيانات العميل");
    var c = new Customer { Name = req.Name.Trim(), Phone = req.Phone.Trim(), CreditLimit = req.CreditLimit, DueDays = req.DueDays, Wholesale = req.Wholesale }; db.Customers.Add(c); await db.SaveChangesAsync(); return new { c.Id };
});
app.MapGet("/api/customers/{id:guid}/ledger", async (Guid id, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Accounts); var rows = await db.CustomerEntries.Where(x => x.CustomerId == id).OrderBy(x => x.Id).ToListAsync();
    decimal balance = 0; return rows.Select(x => new LedgerView(x.Number, x.CreatedUtc, x.Kind, x.Debit, x.Credit, balance += x.Debit - x.Credit)).ToArray();
});
app.MapGet("/api/customers/{customer:guid}/last-price/{product:guid}", async (Guid customer, Guid product, StoreDb db) =>
{
    var row = await (from line in db.SaleLines join sale in db.Sales on line.SaleId equals sale.Id
        where sale.CustomerId == customer && line.ProductId == product
        orderby sale.CreatedUtc descending
        select new LastPriceView(sale.Number, sale.CreatedUtc, sale.Currency, line.UnitName, line.Price, line.Discount, line.Quantity)).FirstOrDefaultAsync();
    return row is null ? Results.NoContent() : Results.Ok(row);
});
app.MapGet("/api/invoices", async (StoreDb db, HttpContext ctx) =>
{
    var user = Security.Current(ctx); var all = user.IsAdmin || user.Permissions.Split(',').Contains(Access.Accounts);
    return await (from sale in db.Sales join customer in db.Customers on sale.CustomerId equals customer.Id
        join seller in db.Users on sale.UserId equals seller.Id where all || sale.UserId == user.Id
        orderby sale.CreatedUtc descending select new InvoiceView(sale.Id, sale.Number, customer.Name, seller.DisplayName, sale.CreatedUtc, sale.Currency, sale.Total, sale.TotalBase, sale.PaidBase, sale.DueDate)).Take(200).ToListAsync();
});
app.MapGet("/api/invoices/{id:guid}", async (Guid id, StoreDb db, HttpContext ctx) =>
{
    var sale = await db.Sales.AsNoTracking().Include(x => x.Lines).SingleAsync(x => x.Id == id);
    var user = Security.Current(ctx); Rules.Require(user.IsAdmin || sale.UserId == user.Id || user.Permissions.Split(',').Contains(Access.Accounts), "الفاتورة خارج صلاحيتك");
    return new { sale.Id, sale.Number, sale.CreatedUtc, sale.Currency, sale.Total, sale.TotalBase, sale.PaidBase, sale.DueDate,
        Lines = sale.Lines.Select(l => new { l.Id, l.ProductName, l.UnitName, l.Quantity, l.Returned, l.Price, l.Discount }) };
});
app.MapGet("/api/sessions", async (StoreDb db, HttpContext ctx) =>
{
    var user = Security.Current(ctx); var sessions = await db.CashSessions.Where(x => x.ClosedUtc == null && (user.IsAdmin || x.UserId == user.Id)).ToListAsync();
    var names = await db.Users.ToDictionaryAsync(x => x.Id, x => x.DisplayName);
    var result = new List<SessionView>();
    foreach (var session in sessions)
    {
        var sums = await db.CashMovements.Where(x => x.SessionId == session.Id).GroupBy(x => x.Currency).Select(g => new CashCount(g.Key, g.Sum(x => x.Amount))).ToArrayAsync();
        result.Add(new(session.Id, session.UserId, names[session.UserId], session.Number, session.OpenedUtc, [new("USD", sums.FirstOrDefault(x => x.Currency == "USD")?.Amount ?? 0), new("SYP", sums.FirstOrDefault(x => x.Currency == "SYP")?.Amount ?? 0)]));
    }
    return result;
});
app.MapPost("/api/sessions/open", (OpenSessionRequest req, StoreService service, HttpContext ctx) => service.Open(req, Security.Current(ctx)));
app.MapPost("/api/purchases", (PurchaseRequest req, StoreService service, HttpContext ctx) => { Security.Demand(ctx, Access.Purchase); return service.Purchase(req, Security.Current(ctx)); });
app.MapPost("/api/sales", (SaleRequest req, StoreService service, HttpContext ctx) => { Security.Demand(ctx, Access.Sale); return service.Sell(req, Security.Current(ctx)); });
app.MapPost("/api/receipts", (ReceiptRequest req, StoreService service, HttpContext ctx) => { Security.Admin(ctx); return service.Collect(req, Security.Current(ctx)); });
app.MapPost("/api/returns", (ReturnRequest req, StoreService service, HttpContext ctx) => { Security.Demand(ctx, Access.Return); return service.Return(req, Security.Current(ctx)); });
app.MapPost("/api/sessions/close", (CloseRequest req, StoreService service, HttpContext ctx) => { Security.Admin(ctx); return service.Close(req, Security.Current(ctx)); });
app.MapGet("/api/receipts/{id:guid}", async (Guid id, StoreDb db, StoreService service, HttpContext ctx) =>
{
    Security.Admin(ctx); var r = await db.Receipts.AsNoTracking().SingleAsync(x => x.Id == id);
    return new { r.Number, r.CreatedUtc, Customer = r.CustomerName,
        Manager = r.ManagerName, r.Currency, r.Amount, r.Rate, r.AmountBase, r.Notes, Balance = r.BalanceAfter, BaseCurrency = await service.BaseCurrency() };
});
app.MapGet("/api/dashboard", async (StoreDb db, StoreService service, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Accounts); var start = DateTime.UtcNow.Date; var today = DateOnly.FromDateTime(DateTime.Now);
    return new DashboardView(await db.Sales.Where(x => x.CreatedUtc >= start).SumAsync(x => x.TotalBase), await db.CustomerEntries.SumAsync(x => x.Debit - x.Credit),
        await db.CashMovements.Where(x => x.SessionId == null).SumAsync(x => x.AmountBase), await db.Products.CountAsync(), await db.CashSessions.CountAsync(x => x.ClosedUtc == null),
        await db.Sales.CountAsync(x => x.DueDate <= today && x.PaidBase + x.ReturnedBase < x.TotalBase), await service.BaseCurrency());
});
app.MapGet("/api/due", async (StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Accounts); var today = DateOnly.FromDateTime(DateTime.Now);
    return await (from sale in db.Sales join customer in db.Customers on sale.CustomerId equals customer.Id
        where sale.DueDate != null && sale.TotalBase > sale.PaidBase + sale.ReturnedBase orderby sale.DueDate
        select new DueView(sale.Number, customer.Name, sale.DueDate!.Value, sale.TotalBase - sale.PaidBase - sale.ReturnedBase, sale.DueDate < today)).ToListAsync();
});
app.MapGet("/api/journal", async (StoreDb db, HttpContext ctx) => { Security.Demand(ctx, Access.Accounts); return await db.JournalLines.OrderByDescending(x => x.Id).Take(300).ToListAsync(); });
app.MapGet("/api/employees", async (StoreDb db, HttpContext ctx) => { Security.Demand(ctx, Access.Hr); return await db.Employees.Select(x => new EmployeeView(x.Id, x.Name, x.MonthlySalary, x.SalesTarget, x.TargetBonus)).ToListAsync(); });
app.MapPut("/api/employees/{id:guid}", async (Guid id, EmployeeInput req, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Hr); Rules.Require(req.Name.Length is > 0 and <= 100 && req.MonthlySalary >= 0 && req.SalesTarget >= 0 && req.TargetBonus >= 0, "تحقق من الراتب والهدف والمكافأة");
    var employee = await db.Employees.SingleAsync(x => x.Id == id); employee.Name = req.Name; employee.MonthlySalary = req.MonthlySalary; employee.SalesTarget = req.SalesTarget; employee.TargetBonus = req.TargetBonus;
    await db.SaveChangesAsync(); return Results.NoContent();
});
app.MapPost("/api/attendance", async (AttendanceInput req, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Hr); Rules.Require(await db.Employees.AnyAsync(x => x.Id == req.EmployeeId) && req.Notes.Length <= 256, "تحقق من الموظف والبيان");
    var row = await db.Attendance.FindAsync(req.EmployeeId, req.Date);
    if (row is null) { row = new() { EmployeeId = req.EmployeeId, Date = req.Date }; db.Attendance.Add(row); }
    row.Present = req.Present; row.Notes = req.Notes; await db.SaveChangesAsync(); return Results.NoContent();
});
app.MapGet("/api/payroll", async (int year, int month, StoreDb db, HttpContext ctx) =>
{
    Security.Demand(ctx, Access.Hr); Rules.Require(year is >= 2020 and <= 2100 && month is >= 1 and <= 12, "الفترة غير صحيحة");
    var from = new DateTime(year, month, 1, 0, 0, 0, DateTimeKind.Utc); var to = from.AddMonths(1);
    var employees = await db.Employees.ToListAsync(); var result = new List<PayrollView>();
    foreach (var e in employees)
    {
        // Net of returns on the month's invoices, including later returns; result is a proposal, not a posted payroll.
        var lines = await db.SaleLines.Where(l => db.Sales.Any(s => s.Id == l.SaleId && s.UserId == e.UserId && s.CreatedUtc >= from && s.CreatedUtc < to)).ToListAsync();
        var net = lines.Sum(l => Rules.Round(l.TotalBase * (l.Quantity - l.Returned) / l.Quantity));
        var days = await db.Attendance.CountAsync(x => x.EmployeeId == e.Id && x.Date >= DateOnly.FromDateTime(from) && x.Date < DateOnly.FromDateTime(to) && x.Present);
        result.Add(new(e.Name, e.MonthlySalary, net, e.SalesTarget, e.SalesTarget > 0 && net >= e.SalesTarget ? e.TargetBonus : 0, days));
    }
    return result;
});
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<StoreDb>();
    if (args.Length == 2 && args[0] == "--schema")
    {
        await File.WriteAllTextAsync(args[1], db.Database.GenerateCreateScript());
        return;
    }
    await db.Database.EnsureCreatedAsync();
    foreach (var profile in scope.ServiceProvider.GetRequiredService<DatabaseCatalog>().List())
    {
        var connection = scope.ServiceProvider.GetRequiredService<DatabaseCatalog>().Connection(profile.Name);
        await using var registered = new StoreDb(new DbContextOptionsBuilder<StoreDb>().UseSqlServer(connection).Options);
        if (!await SchemaUpgrade.IsCurrent(registered))
        {
            var backup = await registered.Companies.AnyAsync() ? await BackupService.Create(registered, false) : null;
            await SchemaUpgrade.Apply(registered);
            if (backup is not null) { await StoreExtensions.Write(registered, "backup:last", backup); await registered.SaveChangesAsync(); }
        }
    }
}
app.Run();
