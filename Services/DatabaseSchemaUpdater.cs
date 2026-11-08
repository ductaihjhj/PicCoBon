using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PickleballClub.Models;

namespace PickleballClub.Services;

public static class DatabaseSchemaUpdater
{
    public static async Task EnsureSchemaUpgradedAsync(PickleballContext context, ILogger? logger = null)
    {
        if (!context.Database.IsRelational())
        {
            await context.Database.EnsureCreatedAsync();
            return;
        }

        const string sqlBatch1 = @"
-- 1. Expand tb_Account.Password to NVARCHAR(256) for PBKDF2 hashes & LastLogin to NVARCHAR(50)
IF COL_LENGTH('tb_Account', 'Password') IS NOT NULL
BEGIN
    ALTER TABLE tb_Account ALTER COLUMN [Password] NVARCHAR(256) NULL;
END

IF COL_LENGTH('tb_Account', 'LastLogin') IS NOT NULL
BEGIN
    ALTER TABLE tb_Account ALTER COLUMN [LastLogin] NVARCHAR(50) NULL;
END

-- 2. Ensure tb_Role has Admin(1), Customer(2), Staff(3)
IF OBJECT_ID('tb_Role', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM tb_Role WHERE RoleId = 1)
    BEGIN
        SET IDENTITY_INSERT tb_Role ON;
        INSERT INTO tb_Role (RoleId, RoleName, Description) VALUES (1, N'Admin', N'Quản trị viên hệ thống');
        SET IDENTITY_INSERT tb_Role OFF;
    END
    IF NOT EXISTS (SELECT 1 FROM tb_Role WHERE RoleId = 2)
    BEGIN
        SET IDENTITY_INSERT tb_Role ON;
        INSERT INTO tb_Role (RoleId, RoleName, Description) VALUES (2, N'Customer', N'Hội viên / Khách hàng');
        SET IDENTITY_INSERT tb_Role OFF;
    END
    IF NOT EXISTS (SELECT 1 FROM tb_Role WHERE RoleId = 3)
    BEGIN
        SET IDENTITY_INSERT tb_Role ON;
        INSERT INTO tb_Role (RoleId, RoleName, Description) VALUES (3, N'Staff', N'Nhân viên vận hành / Thu ngân');
        SET IDENTITY_INSERT tb_Role OFF;
    END
END

-- 3. Add payment, QR, breakdown, and lifecycle columns to tb_Booking
IF OBJECT_ID('tb_Booking', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('tb_Booking', 'PaymentMethod') IS NULL
        ALTER TABLE tb_Booking ADD PaymentMethod NVARCHAR(20) NULL CONSTRAINT DF_tb_Booking_PaymentMethod DEFAULT ('cash');

    IF COL_LENGTH('tb_Booking', 'DiscountAmount') IS NULL
        ALTER TABLE tb_Booking ADD DiscountAmount INT NOT NULL CONSTRAINT DF_tb_Booking_DiscountAmount DEFAULT (0);

    IF COL_LENGTH('tb_Booking', 'PosTotal') IS NULL
        ALTER TABLE tb_Booking ADD PosTotal INT NOT NULL CONSTRAINT DF_tb_Booking_PosTotal DEFAULT (0);

    IF COL_LENGTH('tb_Booking', 'AmountDue') IS NULL
        ALTER TABLE tb_Booking ADD AmountDue INT NOT NULL CONSTRAINT DF_tb_Booking_AmountDue DEFAULT (0);

    IF COL_LENGTH('tb_Booking', 'PaidAmount') IS NULL
        ALTER TABLE tb_Booking ADD PaidAmount INT NOT NULL CONSTRAINT DF_tb_Booking_PaidAmount DEFAULT (0);

    IF COL_LENGTH('tb_Booking', 'PaymentStatus') IS NULL
        ALTER TABLE tb_Booking ADD PaymentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_Booking_PaymentStatus DEFAULT ('unpaid');

    IF COL_LENGTH('tb_Booking', 'PricingBreakdownJson') IS NULL
        ALTER TABLE tb_Booking ADD PricingBreakdownJson NVARCHAR(MAX) NULL;

    IF COL_LENGTH('tb_Booking', 'CheckInToken') IS NULL
        ALTER TABLE tb_Booking ADD CheckInToken NVARCHAR(256) NULL;

    IF COL_LENGTH('tb_Booking', 'CheckedInAt') IS NULL
        ALTER TABLE tb_Booking ADD CheckedInAt DATETIME NULL;

    IF COL_LENGTH('tb_Booking', 'CompletedAt') IS NULL
        ALTER TABLE tb_Booking ADD CompletedAt DATETIME NULL;

    IF COL_LENGTH('tb_Booking', 'CancelledAt') IS NULL
        ALTER TABLE tb_Booking ADD CancelledAt DATETIME NULL;

    IF COL_LENGTH('tb_Booking', 'CancelReason') IS NULL
        ALTER TABLE tb_Booking ADD CancelReason NVARCHAR(300) NULL;
END
";

        const string sqlBatch2 = @"
-- Backfill AmountDue and PaidAmount safely on existing bookings where AmountDue = 0
IF OBJECT_ID('tb_Booking', 'U') IS NOT NULL
BEGIN
    UPDATE tb_Booking
    SET AmountDue = CASE WHEN (ISNULL(CourtFee, 0) + ISNULL(PosTotal, 0) - ISNULL(DiscountAmount, 0)) > 0
                         THEN (ISNULL(CourtFee, 0) + ISNULL(PosTotal, 0) - ISNULL(DiscountAmount, 0))
                         ELSE 0 END
    WHERE AmountDue = 0 AND (ISNULL(CourtFee, 0) > 0 OR ISNULL(PosTotal, 0) > 0);

    -- Only mark historically completed bookings as paid if they had TotalPaid > 0
    UPDATE tb_Booking
    SET PaidAmount = ISNULL(TotalPaid, 0),
        PaymentStatus = 'paid'
    WHERE Status = 'completed' AND ISNULL(TotalPaid, 0) > 0 AND PaidAmount = 0;

    -- For non-completed bookings where PaidAmount = 0, keep TotalPaid = 0 so unpaid bookings aren't counted as paid
    UPDATE tb_Booking
    SET TotalPaid = PaidAmount
    WHERE Status IN ('pending', 'approved', 'playing', 'rejected', 'cancelled', 'expired') AND PaidAmount = 0;
END

-- 4. Add BookingId, BookingCode, PaidAmount, PaymentStatus, PaymentMethod, IdempotencyKey to tb_Order
IF OBJECT_ID('tb_Order', 'U') IS NOT NULL
BEGIN
    IF COL_LENGTH('tb_Order', 'BookingId') IS NULL
        ALTER TABLE tb_Order ADD BookingId INT NULL;

    IF COL_LENGTH('tb_Order', 'BookingCode') IS NULL
        ALTER TABLE tb_Order ADD BookingCode NVARCHAR(30) NULL;

    IF COL_LENGTH('tb_Order', 'PaidAmount') IS NULL
        ALTER TABLE tb_Order ADD PaidAmount INT NOT NULL CONSTRAINT DF_tb_Order_PaidAmount DEFAULT (0);

    IF COL_LENGTH('tb_Order', 'PaymentStatus') IS NULL
        ALTER TABLE tb_Order ADD PaymentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_Order_PaymentStatus DEFAULT ('unpaid');

    IF COL_LENGTH('tb_Order', 'PaymentMethod') IS NULL
        ALTER TABLE tb_Order ADD PaymentMethod NVARCHAR(20) NULL CONSTRAINT DF_tb_Order_PaymentMethod DEFAULT ('cash');

    IF COL_LENGTH('tb_Order', 'IdempotencyKey') IS NULL
        ALTER TABLE tb_Order ADD IdempotencyKey NVARCHAR(100) NULL;
END

-- 5. Create tb_PaymentTransaction table if not exists
IF OBJECT_ID('tb_PaymentTransaction', 'U') IS NULL
BEGIN
    CREATE TABLE tb_PaymentTransaction (
        TransactionId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BookingId INT NULL,
        OrderId INT NULL,
        BookingCode NVARCHAR(30) NULL,
        Amount INT NOT NULL,
        PaymentMethod NVARCHAR(20) NOT NULL DEFAULT ('cash'),
        Status NVARCHAR(20) NOT NULL DEFAULT ('completed'),
        ReferenceCode NVARCHAR(100) NULL,
        Note NVARCHAR(300) NULL,
        RecordedBy NVARCHAR(100) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT (GETDATE()),
        CONSTRAINT FK_tb_PaymentTransaction_tb_Booking FOREIGN KEY (BookingId) REFERENCES tb_Booking(BookingId),
        CONSTRAINT FK_tb_PaymentTransaction_tb_Order FOREIGN KEY (OrderId) REFERENCES tb_Order(OrderId)
    );
    CREATE INDEX IX_tb_PaymentTransaction_BookingId ON tb_PaymentTransaction(BookingId);
    CREATE INDEX IX_tb_PaymentTransaction_OrderId ON tb_PaymentTransaction(OrderId);
END

-- 6. Create tb_AuditLog table if not exists
IF OBJECT_ID('tb_AuditLog', 'U') IS NULL
BEGIN
    CREATE TABLE tb_AuditLog (
        AuditId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Action] NVARCHAR(100) NOT NULL,
        EntityType NVARCHAR(50) NULL,
        EntityId NVARCHAR(50) NULL,
        ActorAccountId INT NULL,
        ActorUsername NVARCHAR(100) NULL,
        Details NVARCHAR(MAX) NULL,
        IpAddress NVARCHAR(64) NULL,
        CreatedAt DATETIME NOT NULL DEFAULT (GETDATE())
    );
    CREATE INDEX IX_tb_AuditLog_CreatedAt ON tb_AuditLog(CreatedAt);
END

-- 7. Indexes for concurrency and lookup
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_Booking_Court_Date_Status' AND object_id = OBJECT_ID('tb_Booking'))
BEGIN
    CREATE INDEX IX_tb_Booking_Court_Date_Status ON tb_Booking(CourtId, BookingDate, Status);
END
";

        try
        {
            await context.Database.ExecuteSqlRawAsync(sqlBatch1);
            await context.Database.ExecuteSqlRawAsync(sqlBatch2);
        }
        catch (Exception ex)
        {
            logger?.LogWarning(ex, "Non-fatal error while running DatabaseSchemaUpdater.");
        }
    }
}
