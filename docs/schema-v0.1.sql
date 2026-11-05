CREATE TABLE [Attendance] (
    [EmployeeId] uniqueidentifier NOT NULL,
    [Date] date NOT NULL,
    [Present] bit NOT NULL,
    [Notes] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_Attendance] PRIMARY KEY ([EmployeeId], [Date])
);
GO


CREATE TABLE [CashMovements] (
    [Id] bigint NOT NULL IDENTITY,
    [SessionId] uniqueidentifier NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [Currency] nvarchar(256) NOT NULL,
    [Amount] decimal(19,4) NOT NULL,
    [AmountBase] decimal(19,4) NOT NULL,
    [Kind] nvarchar(256) NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_CashMovements] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [CashSessions] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Number] nvarchar(256) NOT NULL,
    [OpenedUtc] datetime2 NOT NULL,
    [ClosedUtc] datetime2 NULL,
    CONSTRAINT [PK_CashSessions] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Companies] (
    [Id] int NOT NULL IDENTITY,
    [Name] nvarchar(256) NOT NULL,
    [BaseCurrency] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_Companies] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [CustomerEntries] (
    [Id] bigint NOT NULL IDENTITY,
    [CustomerId] uniqueidentifier NOT NULL,
    [OperationId] uniqueidentifier NOT NULL,
    [Number] nvarchar(256) NOT NULL,
    [Kind] nvarchar(256) NOT NULL,
    [Debit] decimal(19,4) NOT NULL,
    [Credit] decimal(19,4) NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_CustomerEntries] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Customers] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [Phone] nvarchar(256) NOT NULL,
    [CreditLimit] decimal(19,4) NOT NULL,
    [DueDays] int NOT NULL,
    [Wholesale] bit NOT NULL,
    CONSTRAINT [PK_Customers] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Employees] (
    [Id] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NULL,
    [Name] nvarchar(256) NOT NULL,
    [MonthlySalary] decimal(19,4) NOT NULL,
    [SalesTarget] decimal(19,4) NOT NULL,
    [TargetBonus] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_Employees] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [JournalLines] (
    [Id] bigint NOT NULL IDENTITY,
    [OperationId] uniqueidentifier NOT NULL,
    [Account] nvarchar(256) NOT NULL,
    [Debit] decimal(19,4) NOT NULL,
    [Credit] decimal(19,4) NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_JournalLines] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [LoginSessions] (
    [Id] uniqueidentifier NOT NULL,
    [TokenHash] nvarchar(256) NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [ExpiresUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_LoginSessions] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Operations] (
    [Id] uniqueidentifier NOT NULL,
    [Kind] nvarchar(256) NOT NULL,
    [Number] nvarchar(256) NOT NULL,
    [Fingerprint] nvarchar(256) NOT NULL,
    [TotalBase] decimal(19,4) NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [Details] nvarchar(max) NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Operations] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Products] (
    [Id] uniqueidentifier NOT NULL,
    [Code] nvarchar(256) NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [RetailPrice] decimal(19,4) NOT NULL,
    [WholesalePrice] decimal(19,4) NOT NULL,
    [AverageCost] decimal(19,4) NOT NULL,
    [ImagePath] nvarchar(256) NOT NULL,
    [Published] bit NOT NULL,
    CONSTRAINT [PK_Products] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Receipts] (
    [Id] uniqueidentifier NOT NULL,
    [Number] nvarchar(256) NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [SessionId] uniqueidentifier NOT NULL,
    [Currency] nvarchar(256) NOT NULL,
    [Amount] decimal(19,4) NOT NULL,
    [Rate] decimal(19,4) NOT NULL,
    [AmountBase] decimal(19,4) NOT NULL,
    [BalanceAfter] decimal(19,4) NOT NULL,
    [CustomerName] nvarchar(256) NOT NULL,
    [ManagerName] nvarchar(256) NOT NULL,
    [Notes] nvarchar(256) NOT NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Receipts] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Sales] (
    [Id] uniqueidentifier NOT NULL,
    [Number] nvarchar(256) NOT NULL,
    [UserId] uniqueidentifier NOT NULL,
    [CustomerId] uniqueidentifier NOT NULL,
    [SessionId] uniqueidentifier NOT NULL,
    [WarehouseId] uniqueidentifier NOT NULL,
    [Currency] nvarchar(256) NOT NULL,
    [Rate] decimal(19,4) NOT NULL,
    [Total] decimal(19,4) NOT NULL,
    [TotalBase] decimal(19,4) NOT NULL,
    [PaidBase] decimal(19,4) NOT NULL,
    [ReturnedBase] decimal(19,4) NOT NULL,
    [DueDate] date NULL,
    [CreatedUtc] datetime2 NOT NULL,
    CONSTRAINT [PK_Sales] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Stocks] (
    [ProductId] uniqueidentifier NOT NULL,
    [WarehouseId] uniqueidentifier NOT NULL,
    [Quantity] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_Stocks] PRIMARY KEY ([ProductId], [WarehouseId])
);
GO


CREATE TABLE [Users] (
    [Id] uniqueidentifier NOT NULL,
    [Username] nvarchar(256) NOT NULL,
    [DisplayName] nvarchar(256) NOT NULL,
    [PasswordHash] nvarchar(256) NOT NULL,
    [IsAdmin] bit NOT NULL,
    [Active] bit NOT NULL,
    [Permissions] nvarchar(4000) NOT NULL,
    [MaxDiscountPercent] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_Users] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Warehouses] (
    [Id] uniqueidentifier NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    CONSTRAINT [PK_Warehouses] PRIMARY KEY ([Id])
);
GO


CREATE TABLE [Units] (
    [Id] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [Name] nvarchar(256) NOT NULL,
    [Barcode] nvarchar(256) NOT NULL,
    [Factor] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_Units] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_Units_Products_ProductId] FOREIGN KEY ([ProductId]) REFERENCES [Products] ([Id]) ON DELETE CASCADE
);
GO


CREATE TABLE [SaleLines] (
    [Id] uniqueidentifier NOT NULL,
    [SaleId] uniqueidentifier NOT NULL,
    [ProductId] uniqueidentifier NOT NULL,
    [UnitId] uniqueidentifier NOT NULL,
    [ProductName] nvarchar(256) NOT NULL,
    [UnitName] nvarchar(256) NOT NULL,
    [Factor] decimal(19,4) NOT NULL,
    [Quantity] decimal(19,4) NOT NULL,
    [Returned] decimal(19,4) NOT NULL,
    [Price] decimal(19,4) NOT NULL,
    [Discount] decimal(19,4) NOT NULL,
    [TotalBase] decimal(19,4) NOT NULL,
    [CostBase] decimal(19,4) NOT NULL,
    CONSTRAINT [PK_SaleLines] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_SaleLines_Sales_SaleId] FOREIGN KEY ([SaleId]) REFERENCES [Sales] ([Id]) ON DELETE CASCADE
);
GO


CREATE INDEX [IX_CashMovements_SessionId] ON [CashMovements] ([SessionId]);
GO


CREATE UNIQUE INDEX [IX_CashSessions_UserId] ON [CashSessions] ([UserId]) WHERE [ClosedUtc] IS NULL;
GO


CREATE INDEX [IX_CustomerEntries_CustomerId_CreatedUtc] ON [CustomerEntries] ([CustomerId], [CreatedUtc]);
GO


CREATE UNIQUE INDEX [IX_Employees_UserId] ON [Employees] ([UserId]) WHERE [UserId] IS NOT NULL;
GO


CREATE UNIQUE INDEX [IX_LoginSessions_TokenHash] ON [LoginSessions] ([TokenHash]);
GO


CREATE UNIQUE INDEX [IX_Products_Code] ON [Products] ([Code]);
GO


CREATE INDEX [IX_SaleLines_SaleId] ON [SaleLines] ([SaleId]);
GO


CREATE UNIQUE INDEX [IX_Units_Barcode] ON [Units] ([Barcode]) WHERE [Barcode] <> '';
GO


CREATE INDEX [IX_Units_ProductId] ON [Units] ([ProductId]);
GO


CREATE UNIQUE INDEX [IX_Users_Username] ON [Users] ([Username]);
GO


