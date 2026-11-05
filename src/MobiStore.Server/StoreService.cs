using System.Data;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MobiStore.Core;

namespace MobiStore.Server;

public sealed class StoreService(StoreDb db)
{
    public async Task<string> BaseCurrency() => (await db.Companies.SingleAsync()).BaseCurrency;
    public async Task<decimal> Balance(Guid customer) => await db.CustomerEntries.Where(x => x.CustomerId == customer).SumAsync(x => x.Debit - x.Credit);
    public async Task<CashSession> Session(Guid id, User user)
    {
        var session = await db.CashSessions.SingleOrDefaultAsync(x => x.Id == id && x.ClosedUtc == null);
        Rules.Require(session is not null && session.UserId == user.Id, "لا توجد وردية مفتوحة لهذا المستخدم");
        return session!;
    }
    public static string Number(string prefix, Guid id) => $"{prefix}-{DateTime.UtcNow:yyyyMMdd}-{id:N}";
    private void Journal(Guid id, params (string Account, decimal Debit, decimal Credit)[] lines)
    {
        Rules.Balanced(lines.Select(x => (x.Debit, x.Credit)));
        db.JournalLines.AddRange(lines.Where(x => x.Debit != 0 || x.Credit != 0).Select(x => new JournalLine { OperationId = id, Account = x.Account, Debit = x.Debit, Credit = x.Credit }));
    }
    private void Ledger(Guid customer, Guid operation, string number, string kind, decimal debit, decimal credit)
        => db.CustomerEntries.Add(new() { CustomerId = customer, OperationId = operation, Number = number, Kind = kind, Debit = debit, Credit = credit });
    private void Cash(Guid? session, Guid id, string currency, decimal amount, decimal basis, string kind)
        => db.CashMovements.Add(new() { SessionId = session, OperationId = id, Currency = currency, Amount = amount, AmountBase = basis, Kind = kind });
    public async Task<OperationResult> Run(Guid id, string kind, object payload, User user, Func<string, Task<decimal>> action)
    {
        Rules.Require(id != Guid.Empty, "معرف العملية مطلوب");
        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload))));
        await using var tx = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        var existing = await db.Operations.SingleOrDefaultAsync(x => x.Id == id);
        if (existing is not null)
        {
            Rules.Require(existing.Kind == kind && existing.Fingerprint == fingerprint && existing.UserId == user.Id, "معرف العملية مستخدم لطلب مختلف");
            return new(existing.Id, existing.Number, existing.TotalBase, "العملية مسجلة سابقًا؛ لم تتكرر");
        }
        var number = Number(kind, id);
        var total = await action(number);
        db.Operations.Add(new() { Id = id, Kind = kind, Number = number, UserId = user.Id, Fingerprint = fingerprint, TotalBase = total, Details = JsonSerializer.Serialize(payload) });
        await db.SaveChangesAsync();
        await tx.CommitAsync();
        return new(id, number, total, "تم تسجيل العملية");
    }
    public Task<OperationResult> Open(OpenSessionRequest req, User user) => Run(req.RequestId, "SHIFT", req, user, async number =>
    {
        Rules.Require(!await db.CashSessions.AnyAsync(x => x.UserId == user.Id && x.ClosedUtc == null), "توجد وردية مفتوحة بالفعل");
        db.CashSessions.Add(new() { Id = req.RequestId, UserId = user.Id, Number = number });
        return 0;
    });
    public Task<OperationResult> Purchase(PurchaseRequest req, User user) => Run(req.RequestId, "BUY", req, user, async number =>
    {
        Rules.Require(req.Lines.Length is > 0 and <= 100, "أدخل بنود الشراء");
        Rules.Require(req.Lines.Select(x => x.UnitId).Distinct().Count() == req.Lines.Length, "اجمع كمية الوحدة في سطر شراء واحد");
        Rules.Require(!string.IsNullOrWhiteSpace(req.Supplier), "اسم المورد مطلوب");
        Rules.Require(await db.Warehouses.AnyAsync(x => x.Id == req.WarehouseId), "المستودع غير موجود");
        var basis = await BaseCurrency(); decimal total = 0;
        foreach (var line in req.Lines)
        {
            Rules.Require(line.UnitCost > 0, "تكلفة الشراء يجب أن تكون موجبة");
            var unit = await db.Units.Include(x => x.Product).SingleAsync(x => x.Id == line.UnitId);
            var qty = Rules.UnitQuantity(line.Quantity, unit.Factor);
            var cost = Rules.ToBase(Rules.Round(line.Quantity * line.UnitCost), req.Currency, req.Rate, basis);
            var stock = await db.Stocks.SingleOrDefaultAsync(x => x.ProductId == unit.ProductId && x.WarehouseId == req.WarehouseId);
            var allQty = await db.Stocks.Where(x => x.ProductId == unit.ProductId).SumAsync(x => x.Quantity);
            unit.Product.AverageCost = Rules.Round((allQty * unit.Product.AverageCost + cost) / (allQty + qty));
            if (stock is null) db.Stocks.Add(new() { ProductId = unit.ProductId, WarehouseId = req.WarehouseId, Quantity = qty });
            else stock.Quantity += qty;
            total += cost;
            // Save each line inside the same transaction so repeated units use the updated weighted cost.
            await db.SaveChangesAsync();
        }
        Journal(req.RequestId, ("INVENTORY", total, 0), ("SUPPLIERS", 0, total));
        db.ChangeTracker.DetectChanges();
        return total;
    });
    public Task<OperationResult> Sell(SaleRequest req, User user) => Run(req.RequestId, "SALE", req, user, async number =>
    {
        await Session(req.SessionId, user);
        Rules.Require(req.Lines.Length is > 0 and <= 100, "أدخل بنود البيع");
        Rules.Require(req.Lines.Select(x => x.UnitId).Distinct().Count() == req.Lines.Length, "اجمع كمية الوحدة المتكررة في سطر واحد");
        Rules.Require(req.CashPaid >= 0, "الدفعة لا يمكن أن تكون سالبة");
        Rules.Require(req.Rate > 0, "سعر الصرف يجب أن يكون موجبًا");
        var customer = await db.Customers.SingleAsync(x => x.Id == req.CustomerId);
        var basis = await BaseCurrency();
        _ = Rules.ToBase(0, req.Currency, req.Rate, basis);
        var sale = new Sale { Id = req.RequestId, Number = number, UserId = user.Id, CustomerId = req.CustomerId, SessionId = req.SessionId,
            WarehouseId = req.WarehouseId, Currency = req.Currency, Rate = req.Rate, DueDate = req.DueDate };
        decimal cost = 0;
        foreach (var line in req.Lines)
        {
            var unit = await db.Units.Include(x => x.Product).SingleAsync(x => x.Id == line.UnitId);
            var quantity = Rules.UnitQuantity(line.Quantity, unit.Factor);
            var lineTotal = Rules.LineTotal(line.Quantity, line.UnitPrice, line.Discount);
            var officialBasePrice = await StoreExtensions.BasePrice(db, customer, unit.Product) * unit.Factor;
            var officialPrice = Rules.Round(req.Currency == basis ? officialBasePrice : basis == "USD" ? officialBasePrice * req.Rate : officialBasePrice / req.Rate);
            if (!user.IsAdmin && !user.Permissions.Split(',').Contains(Access.Discount))
                Rules.Require(line.UnitPrice >= officialPrice, "تخفيض سعر المادة يحتاج صلاحية الخصم");
            var discountPercent = officialPrice <= 0 ? 0 : Math.Max(0, (officialPrice * line.Quantity - lineTotal) / (officialPrice * line.Quantity) * 100);
            Rules.Require(user.IsAdmin || line.UnitPrice > 0, "البيع بسعر صفر متاح للمدير فقط");
            Rules.Require(user.IsAdmin || (discountPercent == 0 && line.Discount == 0 || user.Permissions.Split(',').Contains(Access.Discount) && discountPercent <= user.MaxDiscountPercent), "الخصم يتجاوز صلاحيتك");
            var updated = await db.Stocks.Where(x => x.ProductId == unit.ProductId && x.WarehouseId == req.WarehouseId && x.Quantity >= quantity)
                .ExecuteUpdateAsync(s => s.SetProperty(x => x.Quantity, x => x.Quantity - quantity));
            Rules.Require(updated == 1, $"رصيد غير كافٍ للمادة: {unit.Product.Name}");
            var lineBase = Rules.ToBase(lineTotal, req.Currency, req.Rate, basis);
            var lineCost = Rules.Round(quantity * unit.Product.AverageCost);
            sale.Lines.Add(new() { ProductId = unit.ProductId, UnitId = unit.Id, ProductName = unit.Product.Name, UnitName = unit.Name,
                Factor = unit.Factor, Quantity = line.Quantity, Price = line.UnitPrice, Discount = line.Discount, TotalBase = lineBase, CostBase = lineCost });
            sale.Total += lineTotal; sale.TotalBase += lineBase; cost += lineCost;
        }
        sale.PaidBase = Rules.ToBase(req.CashPaid, req.PaymentCurrency, req.PaymentRate, basis);
        Rules.Require(sale.PaidBase <= sale.TotalBase, "الدفعة أكبر من إجمالي الفاتورة");
        var debt = sale.TotalBase - sale.PaidBase;
        if (debt > 0)
        {
            Rules.Require(user.IsAdmin || user.Permissions.Split(',').Contains(Access.Credit), "لا تملك صلاحية البيع الآجل");
            Rules.Require(customer.CreditLimit > 0 && await Balance(customer.Id) + debt <= customer.CreditLimit, "تجاوز حد مديونية العميل");
            Rules.Require(req.DueDate is not null, "موعد الاستحقاق مطلوب للبيع الآجل");
        }
        db.Sales.Add(sale);
        Ledger(customer.Id, sale.Id, number, "بيع", sale.TotalBase, 0);
        Journal(sale.Id, ("RECEIVABLES", sale.TotalBase, 0), ("SALES", 0, sale.TotalBase), ("COST_OF_SALES", cost, 0), ("INVENTORY", 0, cost));
        if (sale.PaidBase > 0)
        {
            Ledger(customer.Id, sale.Id, number, "دفعة فاتورة", 0, sale.PaidBase);
            Cash(sale.SessionId, sale.Id, req.PaymentCurrency, req.CashPaid, sale.PaidBase, "بيع نقدي");
            Journal(sale.Id, ($"CASH:{user.Id}", sale.PaidBase, 0), ("RECEIVABLES", 0, sale.PaidBase));
        }
        return sale.TotalBase;
    });
    public Task<OperationResult> Collect(ReceiptRequest req, User user) => Run(req.RequestId, "RCPT", req, user, async number =>
    {
        Rules.Require(user.IsAdmin, "التحصيل من التطبيق للمدير فقط");
        await Session(req.SessionId, user);
        Rules.Require(req.Amount > 0 && req.Notes.Length <= 256, "تحقق من المبلغ والبيان");
        Rules.Require(await db.Customers.AnyAsync(x => x.Id == req.CustomerId), "العميل غير موجود");
        var amount = Rules.ToBase(req.Amount, req.Currency, req.Rate, await BaseCurrency());
        var balanceBefore = await Balance(req.CustomerId);
        Rules.Require(amount <= balanceBefore, "التحصيل أكبر من الدين المتبقي");
        var outstanding = await db.Sales.Where(x => x.CustomerId == req.CustomerId && x.TotalBase > x.PaidBase + x.ReturnedBase).OrderBy(x => x.CreatedUtc).ToListAsync();
        var remaining = amount;
        foreach (var invoice in outstanding)
        {
            var allocated = Math.Min(remaining, invoice.TotalBase - invoice.PaidBase - invoice.ReturnedBase);
            invoice.PaidBase += allocated; remaining -= allocated;
            if (remaining == 0) break;
        }
        db.Receipts.Add(new() { Id = req.RequestId, Number = number, CustomerId = req.CustomerId, UserId = user.Id, SessionId = req.SessionId,
            Currency = req.Currency, Amount = req.Amount, Rate = req.Rate, AmountBase = amount, Notes = req.Notes,
            BalanceAfter = balanceBefore - amount, CustomerName = (await db.Customers.SingleAsync(x => x.Id == req.CustomerId)).Name, ManagerName = user.DisplayName });
        Ledger(req.CustomerId, req.RequestId, number, "قبض", 0, amount);
        Cash(req.SessionId, req.RequestId, req.Currency, req.Amount, amount, "تحصيل مدير");
        Journal(req.RequestId, ($"CASH:{user.Id}", amount, 0), ("RECEIVABLES", 0, amount));
        return amount;
    });
    public Task<OperationResult> Return(ReturnRequest req, User user) => Run(req.RequestId, "RETURN", req, user, async number =>
    {
        var sale = await db.Sales.Include(x => x.Lines).SingleAsync(x => x.Id == req.SaleId);
        Rules.Require(req.Lines.Length > 0 && req.Lines.Select(x => x.SaleLineId).Distinct().Count() == req.Lines.Length, "بنود المرتجع غير صحيحة");
        decimal total = 0, cost = 0;
        foreach (var input in req.Lines)
        {
            var line = sale.Lines.Single(x => x.Id == input.SaleLineId);
            Rules.Require(input.Quantity > 0 && input.Quantity + line.Returned <= line.Quantity, "كمية المرتجع أكبر من المتبقي");
            var cumulative = Rules.Round(line.TotalBase * (line.Returned + input.Quantity) / line.Quantity);
            total += cumulative - Rules.Round(line.TotalBase * line.Returned / line.Quantity);
            var returnedCost = Rules.Round(line.CostBase * (line.Returned + input.Quantity) / line.Quantity) - Rules.Round(line.CostBase * line.Returned / line.Quantity);
            cost += returnedCost;
            line.Returned += input.Quantity;
            var stock = await db.Stocks.SingleAsync(x => x.ProductId == line.ProductId && x.WarehouseId == sale.WarehouseId);
            var qty = Rules.UnitQuantity(input.Quantity, line.Factor);
            var allQty = await db.Stocks.Where(x => x.ProductId == line.ProductId).SumAsync(x => x.Quantity);
            var product = await db.Products.SingleAsync(x => x.Id == line.ProductId);
            product.AverageCost = Rules.Round((allQty * product.AverageCost + returnedCost) / (allQty + qty));
            stock.Quantity += qty;
            await db.SaveChangesAsync();
        }
        sale.ReturnedBase += total;
        Ledger(sale.CustomerId, req.RequestId, number, "مرتجع", 0, total);
        Journal(req.RequestId, ("SALES", total, 0), ("RECEIVABLES", 0, total), ("INVENTORY", cost, 0), ("COST_OF_SALES", 0, cost));
        // Cash refunds are intentionally a separate, auditable operation; this creates customer credit only.
        return total;
    });
    public Task<OperationResult> Close(CloseRequest req, User user) => Run(req.RequestId, "CLOSE", req, user, async number =>
    {
        var session = await db.CashSessions.SingleAsync(x => x.Id == req.SessionId && x.ClosedUtc == null);
        Rules.Require(user.IsAdmin, "اعتماد إغلاق الصناديق للمدير فقط");
        Rules.Require(req.Counts.Length == 2 && req.Counts.Select(x => x.Currency).Order().SequenceEqual(new[] { "SYP", "USD" }), "أدخل جرد الدولار والليرة مرة واحدة");
        var movements = await db.CashMovements.Where(x => x.SessionId == session.Id).ToListAsync();
        decimal total = 0;
        foreach (var count in req.Counts)
        {
            Rules.Require(count.Amount >= 0, "الجرد لا يمكن أن يكون سالبًا");
            var rows = movements.Where(x => x.Currency == count.Currency).ToArray();
            var expected = rows.Sum(x => x.Amount); var expectedBase = rows.Sum(x => x.AmountBase);
            // First version rejects discrepancies instead of silently approving an accounting difference.
            Rules.Require(count.Amount == expected, $"يوجد فرق في {count.Currency}: المتوقع {expected}. تسوية العجز والفائض غير متاحة بعد");
            Cash(session.Id, req.RequestId, count.Currency, -expected, -expectedBase, "تسليم للصندوق الرئيسي");
            Cash(null, req.RequestId, count.Currency, expected, expectedBase, "استلام من صندوق مستخدم");
            total += expectedBase;
        }
        Journal(req.RequestId, ("CASH:MAIN", total, 0), ($"CASH:{session.UserId}", 0, total));
        session.ClosedUtc = DateTime.UtcNow;
        return total;
    });
}
