using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MobiStore.Core;

namespace MobiStore.Server;
public static class SupplierEndpoints
{
    public static async Task<List<(string Supplier, SupplierLedgerView Line)>> Ledger(StoreDb db)
    {
        var operations = await db.Operations.AsNoTracking().Where(x => x.Kind == "BUY" || x.Kind == "BUY_RETURN" || x.Kind == "SUPPLIER_PAY").OrderBy(x => x.CreatedUtc).ToListAsync();
        var purchases = operations.Where(x => x.Kind == "BUY").ToDictionary(x => x.Id, x => JsonSerializer.Deserialize<PurchaseRequest>(x.Details)!);
        var result = new List<(string, SupplierLedgerView)>();
        foreach (var op in operations)
        {
            string name;
            if (op.Kind == "BUY") name = purchases[op.Id].Supplier.Trim();
            else if (op.Kind == "BUY_RETURN") name = purchases[JsonSerializer.Deserialize<PurchaseReturnRequest>(op.Details)!.PurchaseId].Supplier.Trim();
            else name = JsonSerializer.Deserialize<SupplierPaymentRequest>(op.Details)!.Supplier.Trim();
            result.Add((name, new(op.Number, op.CreatedUtc, op.Kind, op.Kind == "BUY" ? 0 : op.TotalBase, op.Kind == "BUY" ? op.TotalBase : 0)));
        }
        return result;
    }
    public static void MapSuppliers(this WebApplication app)
    {
        app.MapGet("/api/suppliers", async (StoreDb db, HttpContext ctx) =>
        { Security.Demand(ctx, Access.Accounts); return (await Ledger(db)).GroupBy(x => x.Supplier).Select(g => new SupplierView(g.Key, g.Sum(x => x.Line.Credit - x.Line.Debit))).ToArray(); });
        app.MapGet("/api/suppliers/ledger", async (string name, StoreDb db, HttpContext ctx) =>
        { Security.Demand(ctx, Access.Accounts); return (await Ledger(db)).Where(x => x.Supplier == name.Trim()).Select(x => x.Line).ToArray(); });
        app.MapGet("/api/purchases", async (StoreDb db, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Purchase); return await db.Operations.Where(x => x.Kind == "BUY").OrderByDescending(x => x.CreatedUtc).Select(x => new { x.Id, x.Number, x.CreatedUtc, x.TotalBase }).Take(200).ToListAsync();
        });
        app.MapGet("/api/purchases/{id:guid}", async (Guid id, StoreDb db, HttpContext ctx) =>
        { Security.Demand(ctx, Access.Purchase); var op = await db.Operations.SingleAsync(x => x.Id == id && x.Kind == "BUY"); return JsonSerializer.Deserialize<PurchaseRequest>(op.Details); });
        app.MapPost("/api/purchases/return", async (PurchaseReturnRequest req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Demand(ctx, Access.Purchase); Security.Demand(ctx, Access.Return);
            return await service.Run(req.RequestId, "BUY_RETURN", req, Security.Current(ctx), async _ =>
            {
                Rules.Require(req.Lines.Length is > 0 and <= 100 && req.Lines.Select(x => x.UnitId).Distinct().Count() == req.Lines.Length, "أدخل بنود مرتجع غير متكررة");
                var original = JsonSerializer.Deserialize<PurchaseRequest>((await db.Operations.SingleAsync(x => x.Id == req.PurchaseId && x.Kind == "BUY")).Details)!;
                decimal total = 0;
                foreach (var line in req.Lines)
                {
                    var bought = original.Lines.Single(x => x.UnitId == line.UnitId);
                    var key = $"buy-returned:{req.PurchaseId:N}:{line.UnitId:N}";
                    var previous = (await StoreExtensions.Read<ReturnQuantity>(db, key))?.Quantity ?? 0;
                    Rules.Require(line.Quantity > 0 && previous + line.Quantity <= bought.Quantity, "المرتجع يتجاوز الكمية المشتراة المتبقية");
                    var unit = await db.Units.Include(x => x.Product).SingleAsync(x => x.Id == line.UnitId);
                    var qty = Rules.UnitQuantity(line.Quantity, unit.Factor);
                    var cost = Rules.ToBase(Rules.Round(line.Quantity * bought.UnitCost), original.Currency, original.Rate, await service.BaseCurrency());
                    var allQty = await db.Stocks.Where(x => x.ProductId == unit.ProductId).SumAsync(x => x.Quantity);
                    var remainingValue = allQty * unit.Product.AverageCost - cost;
                    Rules.Require(allQty >= qty && remainingValue >= -0.0001m && (allQty != qty || Math.Abs(remainingValue) <= 0.0001m), "قيمة المخزون الحالية لا تسمح بهذا المرتجع؛ راجع المحاسب");
                    var changed = await db.Stocks.Where(x => x.ProductId == unit.ProductId && x.WarehouseId == original.WarehouseId && x.Quantity >= qty).ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - qty));
                    Rules.Require(changed == 1, "الرصيد غير كافٍ لمرتجع الشراء");
                    unit.Product.AverageCost = allQty == qty ? 0 : Rules.Round(Math.Max(0, remainingValue) / (allQty - qty));
                    await StoreExtensions.Write(db, key, new ReturnQuantity(previous + line.Quantity)); total += cost;
                    await db.SaveChangesAsync();
                }
                db.JournalLines.AddRange(new JournalLine { OperationId = req.RequestId, Account = "SUPPLIERS", Debit = total }, new JournalLine { OperationId = req.RequestId, Account = "INVENTORY", Credit = total });
                return total;
            });
        });
        app.MapPost("/api/suppliers/pay", async (SupplierPaymentRequest req, StoreDb db, StoreService service, HttpContext ctx) =>
        {
            Security.Admin(ctx);
            return await service.Run(req.RequestId, "SUPPLIER_PAY", req, Security.Current(ctx), async _ =>
            {
                await service.Session(req.SessionId, Security.Current(ctx));
                Rules.Require(req.Amount > 0 && req.Notes.Length is > 0 and <= 500, "أدخل المبلغ وبيان الدفع");
                var amount = Rules.ToBase(req.Amount, req.Currency, req.Rate, await service.BaseCurrency());
                var balance = (await Ledger(db)).Where(x => x.Supplier == req.Supplier.Trim()).Sum(x => x.Line.Credit - x.Line.Debit);
                Rules.Require(balance >= amount, "المبلغ يتجاوز رصيد المورد");
                var cash = await db.CashMovements.Where(x => x.SessionId == req.SessionId && x.Currency == req.Currency).SumAsync(x => x.Amount);
                Rules.Require(cash >= req.Amount, "لا يوجد نقد كافٍ بهذه العملة في صندوقك");
                db.CashMovements.Add(new() { SessionId = req.SessionId, OperationId = req.RequestId, Currency = req.Currency, Amount = -req.Amount, AmountBase = -amount, Kind = "SUPPLIER_PAY" });
                db.JournalLines.AddRange(new JournalLine { OperationId = req.RequestId, Account = "SUPPLIERS", Debit = amount }, new JournalLine { OperationId = req.RequestId, Account = "CASH", Credit = amount });
                return amount;
            });
        });
    }
    private record ReturnQuantity(decimal Quantity);
}
