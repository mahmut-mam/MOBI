namespace MobiStore.Core;

public static class Access
{
    public const string Catalog = "catalog.manage", Customers = "customers.manage", Purchase = "purchase.create",
        Sale = "sale.create", Discount = "sale.discount", Credit = "sale.credit", Return = "sale.return",
        Costs = "cost.view", Accounts = "accounts.view", Close = "cash.close", Users = "users.manage",
        Hr = "hr.manage", Publish = "catalog.publish", Collect = "customer.collect";
    public static readonly string[] All = [Catalog, Customers, Purchase, Sale, Discount, Credit, Return, Costs, Accounts, Close, Users, Hr, Publish, Collect];
}

public static class Rules
{
    public static decimal Round(decimal n) => decimal.Round(n, 4, MidpointRounding.AwayFromZero);
    public static void Precision(decimal n) => Require(n == Round(n) && Math.Abs(n) < 1_000_000_000_000_000m, "الأرقام تدعم أربع منازل عشرية وبحد أقصى 15 خانة صحيحة");
    public static decimal ToBase(decimal amount, string currency, decimal rate, string baseCurrency)
    {
        Precision(amount); Precision(rate);
        Require(currency is "USD" or "SYP", "العملة غير مدعومة");
        Require(baseCurrency is "USD" or "SYP", "العملة الأساسية غير مدعومة");
        Require(rate > 0, "سعر الصرف يجب أن يكون موجبًا");
        return Round(currency == baseCurrency ? amount : currency == "SYP" ? amount / rate : amount * rate);
    }
    public static void Require(bool condition, string message) { if (!condition) throw new RuleException(message); }
    public static decimal UnitQuantity(decimal quantity, decimal factor)
    {
        Precision(quantity); Precision(factor);
        Require(quantity > 0 && factor > 0, "الكمية وعامل التحويل يجب أن يكونا موجبين");
        return Round(quantity * factor);
    }
    public static decimal LineTotal(decimal quantity, decimal price, decimal discount)
    {
        Precision(quantity); Precision(price); Precision(discount);
        Require(quantity > 0 && price >= 0 && discount >= 0 && discount <= quantity * price, "قيم سطر الفاتورة غير صحيحة");
        return Round(quantity * price - discount);
    }
    public static void Balanced(IEnumerable<(decimal Debit, decimal Credit)> lines)
        => Require(lines.All(l => l.Debit >= 0 && l.Credit >= 0 && !(l.Debit > 0 && l.Credit > 0)) &&
            lines.Sum(l => l.Debit) == lines.Sum(l => l.Credit), "القيد غير متوازن");
}
public sealed class RuleException(string message) : Exception(message);
public record SetupRequest(string ShopName, string BaseCurrency, string Username, string DisplayName, string Password);
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, Guid UserId, string DisplayName, bool IsAdmin, string[] Permissions);
public record SetupStatus(bool Installed, string ShopName, string BaseCurrency);
public record UserInput(string Username, string DisplayName, string Password, bool IsAdmin, string[] Permissions, decimal MaxDiscountPercent);
public record ProductInput(string Code, string Name, string Barcode, string Unit, decimal Factor, decimal RetailPrice, decimal WholesalePrice);
public record CustomerInput(string Name, string Phone, decimal CreditLimit, int DueDays, bool Wholesale);
public record UnitView(Guid Id, string Name, string Barcode, decimal Factor);
public record ProductView(Guid Id, string Code, string Name, decimal Stock, decimal RetailPrice, decimal WholesalePrice, decimal? Cost, UnitView[] Units);
public record CustomerView(Guid Id, string Name, string Phone, decimal? Balance, decimal CreditLimit, int DueDays, bool Wholesale);
public record UserView(Guid Id, string Username, string DisplayName, bool IsAdmin, bool Active);
public record PurchaseRequest(Guid RequestId, Guid WarehouseId, string Supplier, string Currency, decimal Rate, PurchaseLineInput[] Lines);
public record PurchaseLineInput(Guid UnitId, decimal Quantity, decimal UnitCost);
public record SaleLineInput(Guid UnitId, decimal Quantity, decimal UnitPrice, decimal Discount);
public record SaleRequest(Guid RequestId, Guid SessionId, Guid WarehouseId, Guid CustomerId, string Currency, decimal Rate,
    decimal CashPaid, string PaymentCurrency, decimal PaymentRate, DateOnly? DueDate, SaleLineInput[] Lines);
public record ReceiptRequest(Guid RequestId, Guid SessionId, Guid CustomerId, string Currency, decimal Amount, decimal Rate, string Notes);
public record CloseRequest(Guid RequestId, Guid SessionId, CashCount[] Counts);
public record CashCount(string Currency, decimal Amount);
public record OpenSessionRequest(Guid RequestId);
public record ReturnRequest(Guid RequestId, Guid SaleId, ReturnLineInput[] Lines);
public record ReturnLineInput(Guid SaleLineId, decimal Quantity);
public record OperationResult(Guid Id, string Number, decimal TotalBase, string Message);
public record SessionView(Guid Id, Guid UserId, string Owner, string Number, DateTime OpenedUtc, CashCount[] Expected);
public record WarehouseView(Guid Id, string Name);
public record LastPriceView(string Number, DateTime DateUtc, string Currency, string Unit, decimal Price, decimal Discount, decimal Quantity);
public record InvoiceView(Guid Id, string Number, string Customer, string Seller, DateTime CreatedUtc, string Currency, decimal Total, decimal TotalBase, decimal PaidBase, DateOnly? DueDate);
public record LedgerView(string Number, DateTime CreatedUtc, string Type, decimal Debit, decimal Credit, decimal Balance);
public record DueView(string Number, string Customer, DateOnly DueDate, decimal Remaining, bool Overdue);
public record DashboardView(decimal SalesToday, decimal CustomerDebt, decimal CashBase, int Products, int OpenSessions, int DueInvoices, string BaseCurrency);
public record EmployeeInput(string Name, decimal MonthlySalary, decimal SalesTarget, decimal TargetBonus);
public record AttendanceInput(Guid EmployeeId, DateOnly Date, bool Present, string Notes);
public record EmployeeView(Guid Id, string Name, decimal MonthlySalary, decimal SalesTarget, decimal TargetBonus);
public record PayrollView(string Employee, decimal Salary, decimal NetSales, decimal Target, decimal SuggestedBonus, int AttendanceDays);
public record ProductDetails(Guid ProductId, string Brand, string Models, string Color, string ImageUrl, bool Published);
public record ProductCardInput(ProductInput Product, ProductDetails Details);
public record ImageInput(string Data, string ContentType);
public record ImageView(string Url);
public record PriceInput(Guid CustomerId, Guid ProductId, decimal Price);
public record UserUpdate(string DisplayName, bool Active, string[] Permissions, decimal MaxDiscountPercent, string Reason);
public record StockMoveRequest(Guid RequestId, Guid FromWarehouseId, Guid ToWarehouseId, Guid UnitId, decimal Quantity, string Reason);
public record StockCountRequest(Guid RequestId, Guid WarehouseId, Guid ProductId, decimal Expected, decimal Counted, string Reason);
public record StockView(Guid ProductId, string Product, Guid WarehouseId, string Warehouse, decimal Quantity);
public record RateInput(decimal Rate);
public record PurchaseReturnRequest(Guid RequestId, Guid PurchaseId, PurchaseLineInput[] Lines);
public record SupplierPaymentRequest(Guid RequestId, Guid SessionId, string Supplier, decimal Amount, string Currency, decimal Rate, string Notes);
public record SupplierView(string Name, decimal Balance);
public record SupplierLedgerView(string Number, DateTime CreatedUtc, string Kind, decimal Debit, decimal Credit);
