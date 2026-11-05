using Microsoft.EntityFrameworkCore;

namespace MobiStore.Server;

public class Company { public int Id { get; set; } public string Name { get; set; } = ""; public string BaseCurrency { get; set; } = "USD"; }
public class User
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Username { get; set; } = ""; public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = ""; public bool IsAdmin { get; set; } public bool Active { get; set; } = true;
    public string Permissions { get; set; } = ""; public decimal MaxDiscountPercent { get; set; }
}
public class LoginSession { public Guid Id { get; set; } = Guid.NewGuid(); public string TokenHash { get; set; } = ""; public Guid UserId { get; set; } public DateTime ExpiresUtc { get; set; } }
public class Warehouse { public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; }
public class Product
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Code { get; set; } = ""; public string Name { get; set; } = "";
    public decimal RetailPrice { get; set; } public decimal WholesalePrice { get; set; } public decimal AverageCost { get; set; }
    public string ImagePath { get; set; } = ""; public bool Published { get; set; }
}
public class ProductUnit { public Guid Id { get; set; } = Guid.NewGuid(); public Guid ProductId { get; set; } public Product Product { get; set; } = null!; public string Name { get; set; } = "قطعة"; public string Barcode { get; set; } = ""; public decimal Factor { get; set; } = 1; }
public class Stock { public Guid ProductId { get; set; } public Guid WarehouseId { get; set; } public decimal Quantity { get; set; } }
public class Customer
{
    public Guid Id { get; set; } = Guid.NewGuid(); public string Name { get; set; } = ""; public string Phone { get; set; } = "";
    public decimal CreditLimit { get; set; } public int DueDays { get; set; } = 30; public bool Wholesale { get; set; }
}
public class CashSession
{
    public Guid Id { get; set; } public Guid UserId { get; set; } public string Number { get; set; } = "";
    public DateTime OpenedUtc { get; set; } = DateTime.UtcNow; public DateTime? ClosedUtc { get; set; }
}
public class CashMovement
{
    public long Id { get; set; } public Guid? SessionId { get; set; } public Guid OperationId { get; set; }
    public string Currency { get; set; } = "USD"; public decimal Amount { get; set; } public decimal AmountBase { get; set; }
    public string Kind { get; set; } = ""; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public class Sale
{
    public Guid Id { get; set; } public string Number { get; set; } = ""; public Guid UserId { get; set; } public Guid CustomerId { get; set; }
    public Guid SessionId { get; set; } public Guid WarehouseId { get; set; } public string Currency { get; set; } = "USD"; public decimal Rate { get; set; }
    public decimal Total { get; set; } public decimal TotalBase { get; set; } public decimal PaidBase { get; set; } public decimal ReturnedBase { get; set; }
    public DateOnly? DueDate { get; set; } public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
    public List<SaleLine> Lines { get; set; } = [];
}
public class SaleLine
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid SaleId { get; set; } public Guid ProductId { get; set; }
    public Guid UnitId { get; set; } public string ProductName { get; set; } = ""; public string UnitName { get; set; } = "";
    public decimal Factor { get; set; } public decimal Quantity { get; set; } public decimal Returned { get; set; }
    public decimal Price { get; set; } public decimal Discount { get; set; } public decimal TotalBase { get; set; } public decimal CostBase { get; set; }
}
public class CustomerEntry
{
    public long Id { get; set; } public Guid CustomerId { get; set; } public Guid OperationId { get; set; }
    public string Number { get; set; } = ""; public string Kind { get; set; } = ""; public decimal Debit { get; set; } public decimal Credit { get; set; }
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public class Receipt
{
    public Guid Id { get; set; } public string Number { get; set; } = ""; public Guid CustomerId { get; set; } public Guid UserId { get; set; }
    public Guid SessionId { get; set; } public string Currency { get; set; } = "USD"; public decimal Amount { get; set; } public decimal Rate { get; set; }
    public decimal AmountBase { get; set; } public decimal BalanceAfter { get; set; }
    public string CustomerName { get; set; } = ""; public string ManagerName { get; set; } = "";
    public string Notes { get; set; } = ""; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public class JournalLine
{
    public long Id { get; set; } public Guid OperationId { get; set; } public string Account { get; set; } = "";
    public decimal Debit { get; set; } public decimal Credit { get; set; } public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public class Operation
{
    public Guid Id { get; set; } public string Kind { get; set; } = ""; public string Number { get; set; } = "";
    public string Fingerprint { get; set; } = ""; public decimal TotalBase { get; set; } public Guid UserId { get; set; }
    public string Details { get; set; } = ""; public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}
public class Employee
{
    public Guid Id { get; set; } = Guid.NewGuid(); public Guid? UserId { get; set; } public string Name { get; set; } = "";
    public decimal MonthlySalary { get; set; } public decimal SalesTarget { get; set; } public decimal TargetBonus { get; set; }
}
public class Attendance { public Guid EmployeeId { get; set; } public DateOnly Date { get; set; } public bool Present { get; set; } public string Notes { get; set; } = ""; }
public class AppRecord
{
    public string Key { get; set; } = "";
    public string Json { get; set; } = "";
    public DateTime UpdatedUtc { get; set; } = DateTime.UtcNow;
}

public sealed class StoreDb(DbContextOptions<StoreDb> options) : DbContext(options)
{
    public DbSet<Company> Companies => Set<Company>(); public DbSet<User> Users => Set<User>(); public DbSet<LoginSession> LoginSessions => Set<LoginSession>();
    public DbSet<Product> Products => Set<Product>(); public DbSet<ProductUnit> Units => Set<ProductUnit>(); public DbSet<Stock> Stocks => Set<Stock>();
    public DbSet<Warehouse> Warehouses => Set<Warehouse>(); public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<CashSession> CashSessions => Set<CashSession>(); public DbSet<CashMovement> CashMovements => Set<CashMovement>();
    public DbSet<Sale> Sales => Set<Sale>(); public DbSet<SaleLine> SaleLines => Set<SaleLine>(); public DbSet<CustomerEntry> CustomerEntries => Set<CustomerEntry>();
    public DbSet<Receipt> Receipts => Set<Receipt>(); public DbSet<JournalLine> JournalLines => Set<JournalLine>(); public DbSet<Operation> Operations => Set<Operation>();
    public DbSet<Employee> Employees => Set<Employee>(); public DbSet<Attendance> Attendance => Set<Attendance>();
    public DbSet<AppRecord> AppRecords => Set<AppRecord>();
    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<AppRecord>().HasKey(x => x.Key);
        m.Entity<User>().HasIndex(x => x.Username).IsUnique(); m.Entity<LoginSession>().HasIndex(x => x.TokenHash).IsUnique();
        m.Entity<Product>().HasIndex(x => x.Code).IsUnique(); m.Entity<ProductUnit>().HasIndex(x => x.Barcode).IsUnique().HasFilter("[Barcode] <> ''");
        m.Entity<Stock>().HasKey(x => new { x.ProductId, x.WarehouseId }); m.Entity<Attendance>().HasKey(x => new { x.EmployeeId, x.Date });
        m.Entity<CashSession>().HasIndex(x => x.UserId).IsUnique().HasFilter("[ClosedUtc] IS NULL");
        m.Entity<Employee>().HasIndex(x => x.UserId).IsUnique().HasFilter("[UserId] IS NOT NULL");
        m.Entity<CustomerEntry>().HasIndex(x => new { x.CustomerId, x.CreatedUtc });
        m.Entity<CashMovement>().HasIndex(x => x.SessionId);
        m.Entity<Sale>().HasMany(x => x.Lines).WithOne().HasForeignKey(x => x.SaleId);
        foreach (var entity in m.Model.GetEntityTypes())
            foreach (var p in entity.GetProperties())
            {
                if (p.ClrType == typeof(decimal)) { p.SetPrecision(19); p.SetScale(4); }
                if (p.ClrType == typeof(string) && p.Name is not "Details" and not "Json") p.SetMaxLength(p.Name == "Permissions" ? 4000 : 256);
            }
    }
}
