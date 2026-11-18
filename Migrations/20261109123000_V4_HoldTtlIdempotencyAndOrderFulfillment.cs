using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PickleballClub.Models;

#nullable disable

namespace PickleballClub.Migrations;

[DbContext(typeof(PickleballContext))]
[Migration("20261109123000_V4_HoldTtlIdempotencyAndOrderFulfillment")]
public partial class V4_HoldTtlIdempotencyAndOrderFulfillment : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Step 1: Expand tb_Booking, tb_Order, and create tb_IdempotencyRecord
        migrationBuilder.Sql(@"
-- 1. Expand tb_Booking with HoldExpiresAtUtc and ClientIpHash for V4-A1
IF COL_LENGTH('tb_Booking', 'HoldExpiresAtUtc') IS NULL
    ALTER TABLE tb_Booking ADD HoldExpiresAtUtc DATETIME2 NULL;

IF COL_LENGTH('tb_Booking', 'ClientIpHash') IS NULL
    ALTER TABLE tb_Booking ADD ClientIpHash NVARCHAR(64) NULL;

-- 2. Expand tb_Order with FulfillmentStatus for V4-A4
IF COL_LENGTH('tb_Order', 'FulfillmentStatus') IS NULL
    ALTER TABLE tb_Order ADD FulfillmentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_Order_FulfillmentStatus DEFAULT ('pending');

-- 3. Create tb_IdempotencyRecord for V4-A3 strict idempotency contract
IF OBJECT_ID('tb_IdempotencyRecord', 'U') IS NULL
BEGIN
    CREATE TABLE tb_IdempotencyRecord (
        IdempotencyId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        IdempotencyKey NVARCHAR(100) NOT NULL,
        Scope NVARCHAR(50) NOT NULL,
        ActorKey NVARCHAR(100) NOT NULL,
        TargetEntity NVARCHAR(100) NOT NULL,
        RequestHash NVARCHAR(64) NOT NULL,
        ResultEntityCode NVARCHAR(60) NULL,
        ResultEntityId INT NULL,
        ResponseJson NVARCHAR(MAX) NULL,
        CreatedAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_IdempotencyRecord_CreatedAtUtc DEFAULT (SYSUTCDATETIME()),
        ExpiresAtUtc DATETIME2 NOT NULL CONSTRAINT DF_tb_IdempotencyRecord_ExpiresAtUtc DEFAULT (DATEADD(HOUR, 24, SYSUTCDATETIME()))
    );
END

-- 4. Seed default V4 CMS settings if missing
IF OBJECT_ID('tb_CmsConfig', 'U') IS NOT NULL
BEGIN
    IF NOT EXISTS (SELECT 1 FROM tb_CmsConfig WHERE ConfigKey = 'PendingHoldMinutes')
        INSERT INTO tb_CmsConfig (ConfigKey, ConfigValue, Description, UpdatedAt, UpdatedBy)
        VALUES ('PendingHoldMinutes', '30', N'Thời gian giữ chỗ tạm cho đơn chờ duyệt (phút)', SYSUTCDATETIME(), 'system');

    IF NOT EXISTS (SELECT 1 FROM tb_CmsConfig WHERE ConfigKey = 'MaxActivePendingPerActor')
        INSERT INTO tb_CmsConfig (ConfigKey, ConfigValue, Description, UpdatedAt, UpdatedBy)
        VALUES ('MaxActivePendingPerActor', '3', N'Số đơn chờ duyệt tối đa đồng thời trên mỗi khách/tài khoản', SYSUTCDATETIME(), 'system');
END
");

        // Step 2: Backfill existing rows & create indexes in a separate batch
        migrationBuilder.Sql(@"
-- Backfill legacy pending bookings without HoldExpiresAtUtc so none can hold a slot forever
UPDATE tb_Booking
SET HoldExpiresAtUtc = DATEADD(MINUTE, 30, ISNULL(DATEADD(HOUR, -7, CreatedDate), SYSUTCDATETIME()))
WHERE [Status] = 'pending' AND HoldExpiresAtUtc IS NULL;

-- Backfill FulfillmentStatus on existing orders
UPDATE tb_Order
SET FulfillmentStatus = CASE
    WHEN [Status] = 'cancelled' THEN 'cancelled'
    WHEN [Status] = 'completed' THEN 'delivered'
    ELSE 'pending'
END
WHERE FulfillmentStatus IS NULL OR FulfillmentStatus = '';

-- Indexes for V4-A1 & V4-A3
IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_Booking_PendingHold' AND object_id = OBJECT_ID('tb_Booking'))
BEGIN
    CREATE INDEX IX_tb_Booking_PendingHold ON tb_Booking(CourtId, BookingDate, [Status], HoldExpiresAtUtc);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'UX_tb_IdempotencyRecord_Key' AND object_id = OBJECT_ID('tb_IdempotencyRecord'))
BEGIN
    CREATE UNIQUE INDEX UX_tb_IdempotencyRecord_Key ON tb_IdempotencyRecord(IdempotencyKey);
END

IF NOT EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_IdempotencyRecord_ExpiresAtUtc' AND object_id = OBJECT_ID('tb_IdempotencyRecord'))
BEGIN
    CREATE INDEX IX_tb_IdempotencyRecord_ExpiresAtUtc ON tb_IdempotencyRecord(ExpiresAtUtc);
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql(@"
IF EXISTS (SELECT 1 FROM sys.indexes WHERE name = 'IX_tb_Booking_PendingHold' AND object_id = OBJECT_ID('tb_Booking'))
    DROP INDEX IX_tb_Booking_PendingHold ON tb_Booking;

IF OBJECT_ID('tb_IdempotencyRecord', 'U') IS NOT NULL
    DROP TABLE tb_IdempotencyRecord;
");
    }
}
