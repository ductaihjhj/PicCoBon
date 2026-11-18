using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PickleballClub.Models;

#nullable disable

namespace PickleballClub.Migrations;

[DbContext(typeof(PickleballContext))]
[Migration("20261109010500_V2_LedgerTypedColumnsAndRowVersion")]
public partial class V2_LedgerTypedColumnsAndRowVersion : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Step 1: Expand typed date/time, decimal(18,2), RowVersion, PaymentTransaction ledger, and InventoryMovement tables/columns
        migrationBuilder.Sql(@"
-- 1. Expand tb_Booking with SQL date, time(0), decimal(18,2), and rowversion
IF COL_LENGTH('tb_Booking', 'BookingDateValue') IS NULL
    ALTER TABLE tb_Booking ADD BookingDateValue DATE NULL;

IF COL_LENGTH('tb_Booking', 'StartTimeValue') IS NULL
    ALTER TABLE tb_Booking ADD StartTimeValue TIME(0) NULL;

IF COL_LENGTH('tb_Booking', 'AmountDueDec') IS NULL
    ALTER TABLE tb_Booking ADD AmountDueDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_Booking_AmountDueDec DEFAULT (0);

IF COL_LENGTH('tb_Booking', 'PaidAmountDec') IS NULL
    ALTER TABLE tb_Booking ADD PaidAmountDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_Booking_PaidAmountDec DEFAULT (0);

IF COL_LENGTH('tb_Booking', 'RowVersion') IS NULL
    ALTER TABLE tb_Booking ADD RowVersion ROWVERSION NOT NULL;

-- 2. Expand tb_CourtLock with SQL date, time(0), and rowversion
IF COL_LENGTH('tb_CourtLock', 'LockDateValue') IS NULL
    ALTER TABLE tb_CourtLock ADD LockDateValue DATE NULL;

IF COL_LENGTH('tb_CourtLock', 'StartTimeValue') IS NULL
    ALTER TABLE tb_CourtLock ADD StartTimeValue TIME(0) NULL;

IF COL_LENGTH('tb_CourtLock', 'RowVersion') IS NULL
    ALTER TABLE tb_CourtLock ADD RowVersion ROWVERSION NOT NULL;

-- 3. Expand tb_Product with decimal(18,2) and rowversion
IF COL_LENGTH('tb_Product', 'PriceDec') IS NULL
    ALTER TABLE tb_Product ADD PriceDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_Product_PriceDec DEFAULT (0);

IF COL_LENGTH('tb_Product', 'RowVersion') IS NULL
    ALTER TABLE tb_Product ADD RowVersion ROWVERSION NOT NULL;

-- 4. Expand tb_Order with decimal(18,2) and rowversion
IF COL_LENGTH('tb_Order', 'TotalAmountDec') IS NULL
    ALTER TABLE tb_Order ADD TotalAmountDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_Order_TotalAmountDec DEFAULT (0);

IF COL_LENGTH('tb_Order', 'PaidAmountDec') IS NULL
    ALTER TABLE tb_Order ADD PaidAmountDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_Order_PaidAmountDec DEFAULT (0);

IF COL_LENGTH('tb_Order', 'RowVersion') IS NULL
    ALTER TABLE tb_Order ADD RowVersion ROWVERSION NOT NULL;

-- 5. Expand tb_CmsConfig with rowversion
IF COL_LENGTH('tb_CmsConfig', 'RowVersion') IS NULL
    ALTER TABLE tb_CmsConfig ADD RowVersion ROWVERSION NOT NULL;

-- 6. Create or expand tb_PaymentTransaction ledger
IF OBJECT_ID('tb_PaymentTransaction', 'U') IS NULL
BEGIN
    CREATE TABLE tb_PaymentTransaction (
        TransactionId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BookingId INT NULL,
        OrderId INT NULL,
        BookingCode NVARCHAR(30) NULL,
        Amount INT NOT NULL,
        AmountDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_AmountDec DEFAULT (0),
        Currency NVARCHAR(10) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_Currency DEFAULT ('VND'),
        TransactionType NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_Type DEFAULT ('payment'),
        PaymentMethod NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_Method DEFAULT ('cash'),
        [Status] NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_Status DEFAULT ('completed'),
        IdempotencyKey NVARCHAR(100) NULL,
        ReferenceCode NVARCHAR(100) NULL,
        Note NVARCHAR(300) NULL,
        ActorAccountId INT NULL,
        RecordedBy NVARCHAR(100) NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_tb_PaymentTransaction_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_tb_PaymentTransaction_tb_Booking FOREIGN KEY (BookingId) REFERENCES tb_Booking(BookingId),
        CONSTRAINT FK_tb_PaymentTransaction_tb_Order FOREIGN KEY (OrderId) REFERENCES tb_Order(OrderId)
    );
END
ELSE
BEGIN
    IF COL_LENGTH('tb_PaymentTransaction', 'AmountDec') IS NULL
        ALTER TABLE tb_PaymentTransaction ADD AmountDec DECIMAL(18, 2) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_AmountDec DEFAULT (0);
    IF COL_LENGTH('tb_PaymentTransaction', 'Currency') IS NULL
        ALTER TABLE tb_PaymentTransaction ADD Currency NVARCHAR(10) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_Currency DEFAULT ('VND');
    IF COL_LENGTH('tb_PaymentTransaction', 'TransactionType') IS NULL
        ALTER TABLE tb_PaymentTransaction ADD TransactionType NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_PaymentTransaction_Type DEFAULT ('payment');
    IF COL_LENGTH('tb_PaymentTransaction', 'IdempotencyKey') IS NULL
        ALTER TABLE tb_PaymentTransaction ADD IdempotencyKey NVARCHAR(100) NULL;
    IF COL_LENGTH('tb_PaymentTransaction', 'ActorAccountId') IS NULL
        ALTER TABLE tb_PaymentTransaction ADD ActorAccountId INT NULL;
END

-- 7. Create tb_InventoryMovement audit ledger
IF OBJECT_ID('tb_InventoryMovement', 'U') IS NULL
BEGIN
    CREATE TABLE tb_InventoryMovement (
        MovementId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        ProductId INT NOT NULL,
        OrderId INT NULL,
        BookingId INT NULL,
        ChangeQty INT NOT NULL,
        BalanceAfter INT NOT NULL,
        Reason NVARCHAR(200) NOT NULL,
        ActorAccountId INT NULL,
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_tb_InventoryMovement_CreatedAt DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_tb_InventoryMovement_tb_Product FOREIGN KEY (ProductId) REFERENCES tb_Product(ProductId),
        CONSTRAINT FK_tb_InventoryMovement_tb_Order FOREIGN KEY (OrderId) REFERENCES tb_Order(OrderId),
        CONSTRAINT FK_tb_InventoryMovement_tb_Booking FOREIGN KEY (BookingId) REFERENCES tb_Booking(BookingId)
    );
END
");

        // Step 2: Backfill typed date/time/decimal columns in a separate SQL batch (after ALTER TABLE columns exist)
        migrationBuilder.Sql(@"
UPDATE tb_Booking
SET AmountDue = CASE WHEN AmountDue > 0 THEN AmountDue
                     WHEN (ISNULL(CourtFee, 0) + ISNULL(PosTotal, 0) - ISNULL(DiscountAmount, 0)) > 0
                     THEN (ISNULL(CourtFee, 0) + ISNULL(PosTotal, 0) - ISNULL(DiscountAmount, 0))
                     ELSE 0 END;

UPDATE tb_Booking
SET BookingDateValue = TRY_CONVERT(DATE, BookingDate),
    StartTimeValue = TRY_CONVERT(TIME(0), StartTime),
    AmountDueDec = CAST(AmountDue AS DECIMAL(18, 2)),
    PaidAmountDec = CAST(PaidAmount AS DECIMAL(18, 2))
WHERE BookingDateValue IS NULL OR StartTimeValue IS NULL OR AmountDueDec <> AmountDue OR PaidAmountDec <> PaidAmount;

UPDATE tb_CourtLock
SET LockDateValue = TRY_CONVERT(DATE, LockDate),
    StartTimeValue = TRY_CONVERT(TIME(0), StartTime)
WHERE LockDateValue IS NULL OR StartTimeValue IS NULL;

UPDATE tb_Product
SET UnitInStock = COALESCE(UnitInStock, Quantity, 0),
    Quantity = COALESCE(UnitInStock, Quantity, 0),
    PriceDec = CAST(ISNULL(Price, 0) AS DECIMAL(18, 2))
WHERE UnitInStock IS NULL OR Quantity IS NULL OR PriceDec <> ISNULL(Price, 0);

UPDATE tb_Order
SET TotalAmountDec = CAST(ISNULL(TotalAmount, 0) AS DECIMAL(18, 2)),
    PaidAmountDec = CAST(PaidAmount AS DECIMAL(18, 2))
WHERE TotalAmountDec <> ISNULL(TotalAmount, 0) OR PaidAmountDec <> PaidAmount;

UPDATE tb_PaymentTransaction
SET AmountDec = CAST(Amount AS DECIMAL(18, 2))
WHERE AmountDec <> Amount;

-- 8. Indexes & Unique Filtered Constraints for Concurrency & Idempotency
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_Booking_BookingCode_Unique' AND object_id = OBJECT_ID('tb_Booking'))
   AND NOT EXISTS (SELECT 1 FROM sys.indexes WHERE is_unique = 1 AND name LIKE '%BookingCode%' AND object_id = OBJECT_ID('tb_Booking'))
BEGIN
    CREATE UNIQUE INDEX IX_tb_Booking_BookingCode_Unique ON tb_Booking(BookingCode);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_Booking_Court_Date_Status' AND object_id = OBJECT_ID('tb_Booking'))
BEGIN
    CREATE INDEX IX_tb_Booking_Court_Date_Status ON tb_Booking(CourtId, BookingDate, [Status]);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_Booking_IdempotencyKey' AND object_id = OBJECT_ID('tb_Booking'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_Booking_IdempotencyKey ON tb_Booking(IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_CourtLock_Court_Date' AND object_id = OBJECT_ID('tb_CourtLock'))
BEGIN
    CREATE INDEX IX_tb_CourtLock_Court_Date ON tb_CourtLock(CourtId, LockDate);
END

-- Drop non-unique index on tb_Order.IdempotencyKey if it existed previously, then create unique filtered index
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_Order_IdempotencyKey' AND is_unique = 0 AND object_id = OBJECT_ID('tb_Order'))
BEGIN
    DROP INDEX IX_tb_Order_IdempotencyKey ON tb_Order;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_Order_IdempotencyKey' AND object_id = OBJECT_ID('tb_Order'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_Order_IdempotencyKey ON tb_Order(IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PaymentTransaction_IdempotencyKey' AND object_id = OBJECT_ID('tb_PaymentTransaction'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_PaymentTransaction_IdempotencyKey ON tb_PaymentTransaction(IdempotencyKey) WHERE IdempotencyKey IS NOT NULL;
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_PaymentTransaction_CreatedAt' AND object_id = OBJECT_ID('tb_PaymentTransaction'))
BEGIN
    CREATE INDEX IX_tb_PaymentTransaction_CreatedAt ON tb_PaymentTransaction(CreatedAt);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_PaymentTransaction_BookingId' AND object_id = OBJECT_ID('tb_PaymentTransaction'))
BEGIN
    CREATE INDEX IX_tb_PaymentTransaction_BookingId ON tb_PaymentTransaction(BookingId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_PaymentTransaction_OrderId' AND object_id = OBJECT_ID('tb_PaymentTransaction'))
BEGIN
    CREATE INDEX IX_tb_PaymentTransaction_OrderId ON tb_PaymentTransaction(OrderId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_InventoryMovement_ProductId' AND object_id = OBJECT_ID('tb_InventoryMovement'))
BEGIN
    CREATE INDEX IX_tb_InventoryMovement_ProductId ON tb_InventoryMovement(ProductId);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_AuditLog_CreatedAt' AND object_id = OBJECT_ID('tb_AuditLog'))
BEGIN
    CREATE INDEX IX_tb_AuditLog_CreatedAt ON tb_AuditLog(CreatedAt);
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_PaymentTransaction_IdempotencyKey' AND object_id = OBJECT_ID('tb_PaymentTransaction'))
    DROP INDEX UX_tb_PaymentTransaction_IdempotencyKey ON tb_PaymentTransaction;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_Order_IdempotencyKey' AND object_id = OBJECT_ID('tb_Order'))
    DROP INDEX UX_tb_Order_IdempotencyKey ON tb_Order;
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_Booking_IdempotencyKey' AND object_id = OBJECT_ID('tb_Booking'))
    DROP INDEX UX_tb_Booking_IdempotencyKey ON tb_Booking;
IF OBJECT_ID('tb_InventoryMovement', 'U') IS NOT NULL
    DROP TABLE tb_InventoryMovement;
");
    }
}
