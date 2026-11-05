-- Additive upgrade for the early 0.1 development database only. Does not reconstruct historical balances.
IF COL_LENGTH('dbo.Receipts', 'BalanceAfter') IS NULL
    ALTER TABLE dbo.Receipts ADD BalanceAfter decimal(19,4) NOT NULL CONSTRAINT DF_Receipt_BalanceAfter DEFAULT 0;
IF COL_LENGTH('dbo.Receipts', 'CustomerName') IS NULL
    ALTER TABLE dbo.Receipts ADD CustomerName nvarchar(256) NOT NULL CONSTRAINT DF_Receipt_CustomerName DEFAULT N'';
IF COL_LENGTH('dbo.Receipts', 'ManagerName') IS NULL
    ALTER TABLE dbo.Receipts ADD ManagerName nvarchar(256) NOT NULL CONSTRAINT DF_Receipt_ManagerName DEFAULT N'';
