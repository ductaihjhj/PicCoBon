using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using PickleballClub.Models;

#nullable disable

namespace PickleballClub.Migrations;

[DbContext(typeof(PickleballContext))]
[Migration("20261109010000_V2_BaselineAndSafeExpand")]
public partial class V2_BaselineAndSafeExpand : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Idempotent baseline schema creation so both fresh test/prod DBs and legacy pre-EF DBs migrate cleanly without data loss.
        migrationBuilder.Sql(@"
IF OBJECT_ID('tb_Role', 'U') IS NULL
BEGIN
    CREATE TABLE tb_Role (
        RoleId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        RoleName NVARCHAR(50) NULL,
        Description NVARCHAR(250) NULL
    );
END

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

IF OBJECT_ID('tb_Account', 'U') IS NULL
BEGIN
    CREATE TABLE tb_Account (
        AccountId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Username NVARCHAR(50) NULL,
        [Password] NVARCHAR(256) NULL,
        FullName NVARCHAR(150) NULL,
        Phone NVARCHAR(50) NULL,
        Email NVARCHAR(150) NULL,
        Avatar NVARCHAR(500) NULL,
        Gender NVARCHAR(30) NULL,
        BirthDate NVARCHAR(20) NULL,
        [Address] NVARCHAR(250) NULL,
        SkillLevel NVARCHAR(100) NULL,
        DominantHand NVARCHAR(50) NULL,
        PaddleBrand NVARCHAR(150) NULL,
        PlayStyle NVARCHAR(150) NULL,
        EmergencyContact NVARCHAR(150) NULL,
        Bio NVARCHAR(500) NULL,
        MemberTier NVARCHAR(50) NULL,
        RoleId INT NULL,
        LastLogin NVARCHAR(50) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tb_Account_IsActive DEFAULT (1),
        MustChangePassword BIT NOT NULL CONSTRAINT DF_tb_Account_MustChangePassword DEFAULT (0),
        SecurityStamp NVARCHAR(64) NULL,
        CONSTRAINT FK_tb_Account_tb_Role FOREIGN KEY (RoleId) REFERENCES tb_Role(RoleId)
    );
END
ELSE
BEGIN
    ALTER TABLE tb_Account ALTER COLUMN [Password] NVARCHAR(256) NULL;
    ALTER TABLE tb_Account ALTER COLUMN [LastLogin] NVARCHAR(50) NULL;
    IF COL_LENGTH('tb_Account', 'MustChangePassword') IS NULL
        ALTER TABLE tb_Account ADD MustChangePassword BIT NOT NULL CONSTRAINT DF_tb_Account_MustChangePassword DEFAULT (0);
    IF COL_LENGTH('tb_Account', 'SecurityStamp') IS NULL
        ALTER TABLE tb_Account ADD SecurityStamp NVARCHAR(64) NULL;
END

EXEC(N'UPDATE tb_Account SET SecurityStamp = LOWER(REPLACE(CONVERT(NVARCHAR(36), NEWID()), ''-'', '''')) WHERE SecurityStamp IS NULL OR SecurityStamp = '''';');

IF OBJECT_ID('tb_Menu', 'U') IS NULL
BEGIN
    CREATE TABLE tb_Menu (
        MenuId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title NVARCHAR(150) NULL,
        Alias NVARCHAR(150) NULL,
        [Description] NVARCHAR(500) NULL,
        Levels INT NULL,
        ParentId INT NULL,
        Position INT NULL,
        CreatedDate DATETIME NULL,
        CreatedBy NVARCHAR(150) NULL,
        ModifiedDate DATETIME NULL,
        ModifiedBy NVARCHAR(150) NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tb_Menu_IsActive DEFAULT (1)
    );
END

IF OBJECT_ID('tb_Court', 'U') IS NULL
BEGIN
    CREATE TABLE tb_Court (
        CourtId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        CourtName NVARCHAR(150) NOT NULL,
        Alias NVARCHAR(150) NULL,
        CourtType NVARCHAR(50) NOT NULL,
        TypeLabel NVARCHAR(250) NULL,
        Surface NVARCHAR(150) NULL,
        Image NVARCHAR(500) NULL,
        [Description] NVARCHAR(500) NULL,
        BasePrice INT NOT NULL CONSTRAINT DF_tb_Court_BasePrice DEFAULT (120000),
        Surcharge INT NOT NULL CONSTRAINT DF_tb_Court_Surcharge DEFAULT (0),
        Position INT NOT NULL CONSTRAINT DF_tb_Court_Position DEFAULT (1),
        IsActive BIT NOT NULL CONSTRAINT DF_tb_Court_IsActive DEFAULT (1)
    );
END

IF OBJECT_ID('tb_OrderStatus', 'U') IS NULL
BEGIN
    CREATE TABLE tb_OrderStatus (
        OrderStatusId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        [Name] NVARCHAR(50) NULL,
        [Description] NVARCHAR(250) NULL
    );
END

IF NOT EXISTS (SELECT 1 FROM tb_OrderStatus WHERE OrderStatusId = 1)
BEGIN
    SET IDENTITY_INSERT tb_OrderStatus ON;
    INSERT INTO tb_OrderStatus (OrderStatusId, [Name], [Description]) VALUES
        (1, N'pending', N'Chờ duyệt'),
        (2, N'approved', N'Đã duyệt'),
        (3, N'playing', N'Đang thi đấu'),
        (4, N'completed', N'Hoàn thành'),
        (5, N'rejected', N'Đã hủy / Từ chối');
    SET IDENTITY_INSERT tb_OrderStatus OFF;
END

IF OBJECT_ID('tb_ProductCategory', 'U') IS NULL
BEGIN
    CREATE TABLE tb_ProductCategory (
        CategoryProductId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title NVARCHAR(150) NULL,
        Alias NVARCHAR(150) NULL,
        [Description] NVARCHAR(500) NULL,
        Icon NVARCHAR(250) NULL,
        Position INT NULL,
        CreatedDate DATETIME NULL,
        IsActive BIT NOT NULL CONSTRAINT DF_tb_ProductCategory_IsActive DEFAULT (1)
    );
END

IF OBJECT_ID('tb_Product', 'U') IS NULL
BEGIN
    CREATE TABLE tb_Product (
        ProductId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Title NVARCHAR(250) NULL,
        Alias NVARCHAR(250) NULL,
        CategoryProductId INT NULL,
        [Description] NVARCHAR(4000) NULL,
        Detail NVARCHAR(MAX) NULL,
        Image NVARCHAR(500) NULL,
        Icon NVARCHAR(300) NULL,
        Price INT NULL CONSTRAINT DF_tb_Product_Price DEFAULT (0),
        PriceSale INT NULL CONSTRAINT DF_tb_Product_PriceSale DEFAULT (0),
        Quantity INT NULL CONSTRAINT DF_tb_Product_Quantity DEFAULT (100),
        CreatedDate DATETIME2 NULL,
        CreatedBy NVARCHAR(150) NULL,
        ModifiedDate DATETIME2 NULL,
        ModifiedBy NVARCHAR(150) NULL,
        IsNew BIT NOT NULL CONSTRAINT DF_tb_Product_IsNew DEFAULT (1),
        IsBestSeller BIT NOT NULL CONSTRAINT DF_tb_Product_IsBestSeller DEFAULT (0),
        UnitInStock INT NULL CONSTRAINT DF_tb_Product_UnitInStock DEFAULT (100),
        IsActive BIT NOT NULL CONSTRAINT DF_tb_Product_IsActive DEFAULT (1),
        Star INT NULL CONSTRAINT DF_tb_Product_Star DEFAULT (5),
        CONSTRAINT FK_tb_Product_tb_ProductCategory FOREIGN KEY (CategoryProductId) REFERENCES tb_ProductCategory(CategoryProductId)
    );
END

IF OBJECT_ID('tb_Booking', 'U') IS NULL
BEGIN
    CREATE TABLE tb_Booking (
        BookingId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        BookingCode NVARCHAR(30) NOT NULL,
        CourtId INT NOT NULL,
        AccountId INT NULL,
        CustomerName NVARCHAR(150) NULL,
        Phone NVARCHAR(50) NULL,
        BookingDate NVARCHAR(20) NOT NULL,
        StartTime NVARCHAR(10) NOT NULL,
        Duration INT NOT NULL CONSTRAINT DF_tb_Booking_Duration DEFAULT (60),
        Players INT NOT NULL CONSTRAINT DF_tb_Booking_Players DEFAULT (4),
        SkillLevel NVARCHAR(100) NULL,
        PaymentMethod NVARCHAR(20) NULL CONSTRAINT DF_tb_Booking_PaymentMethod DEFAULT ('cash'),
        CourtFee INT NOT NULL CONSTRAINT DF_tb_Booking_CourtFee DEFAULT (0),
        DiscountAmount INT NOT NULL CONSTRAINT DF_tb_Booking_DiscountAmount DEFAULT (0),
        PosTotal INT NOT NULL CONSTRAINT DF_tb_Booking_PosTotal DEFAULT (0),
        AmountDue INT NOT NULL CONSTRAINT DF_tb_Booking_AmountDue DEFAULT (0),
        PaidAmount INT NOT NULL CONSTRAINT DF_tb_Booking_PaidAmount DEFAULT (0),
        TotalPaid INT NOT NULL CONSTRAINT DF_tb_Booking_TotalPaid DEFAULT (0),
        PaymentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_Booking_PaymentStatus DEFAULT ('unpaid'),
        PosItemsJson NVARCHAR(MAX) NULL,
        PricingBreakdownJson NVARCHAR(MAX) NULL,
        CheckInToken NVARCHAR(256) NULL,
        AccessTokenHash NVARCHAR(128) NULL,
        IdempotencyKey NVARCHAR(100) NULL,
        CheckedInAt DATETIME2 NULL,
        CompletedAt DATETIME2 NULL,
        CancelledAt DATETIME2 NULL,
        CancelReason NVARCHAR(300) NULL,
        [Status] NVARCHAR(30) NOT NULL CONSTRAINT DF_tb_Booking_Status DEFAULT ('pending'),
        OrderStatusId INT NULL CONSTRAINT DF_tb_Booking_OrderStatusId DEFAULT (1),
        CreatedDate DATETIME2 NULL CONSTRAINT DF_tb_Booking_CreatedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_tb_Booking_tb_Account FOREIGN KEY (AccountId) REFERENCES tb_Account(AccountId),
        CONSTRAINT FK_tb_Booking_tb_Court FOREIGN KEY (CourtId) REFERENCES tb_Court(CourtId),
        CONSTRAINT FK_tb_Booking_tb_OrderStatus FOREIGN KEY (OrderStatusId) REFERENCES tb_OrderStatus(OrderStatusId)
    );
END
ELSE
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
    IF COL_LENGTH('tb_Booking', 'AccessTokenHash') IS NULL
        ALTER TABLE tb_Booking ADD AccessTokenHash NVARCHAR(128) NULL;
    IF COL_LENGTH('tb_Booking', 'IdempotencyKey') IS NULL
        ALTER TABLE tb_Booking ADD IdempotencyKey NVARCHAR(100) NULL;
    IF COL_LENGTH('tb_Booking', 'CheckedInAt') IS NULL
        ALTER TABLE tb_Booking ADD CheckedInAt DATETIME2 NULL;
    IF COL_LENGTH('tb_Booking', 'CompletedAt') IS NULL
        ALTER TABLE tb_Booking ADD CompletedAt DATETIME2 NULL;
    IF COL_LENGTH('tb_Booking', 'CancelledAt') IS NULL
        ALTER TABLE tb_Booking ADD CancelledAt DATETIME2 NULL;
    IF COL_LENGTH('tb_Booking', 'TotalPaid') IS NULL
        ALTER TABLE tb_Booking ADD TotalPaid INT NOT NULL CONSTRAINT DF_tb_Booking_TotalPaid DEFAULT (0);
    IF COL_LENGTH('tb_Booking', 'OrderStatusId') IS NULL
        ALTER TABLE tb_Booking ADD OrderStatusId INT NULL CONSTRAINT DF_tb_Booking_OrderStatusId DEFAULT (1);
    IF COL_LENGTH('tb_Booking', 'CancelReason') IS NULL
        ALTER TABLE tb_Booking ADD CancelReason NVARCHAR(300) NULL;
END

IF OBJECT_ID('tb_CourtLock', 'U') IS NULL
BEGIN
    CREATE TABLE tb_CourtLock (
        LockId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        LockCode NVARCHAR(30) NOT NULL,
        CourtId INT NOT NULL,
        LockDate NVARCHAR(20) NOT NULL,
        StartTime NVARCHAR(10) NOT NULL,
        Duration INT NOT NULL CONSTRAINT DF_tb_CourtLock_Duration DEFAULT (60),
        Reason NVARCHAR(250) NULL,
        CreatedDate DATETIME2 NULL CONSTRAINT DF_tb_CourtLock_CreatedDate DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT FK_tb_CourtLock_tb_Court FOREIGN KEY (CourtId) REFERENCES tb_Court(CourtId)
    );
END

IF OBJECT_ID('tb_CmsConfig', 'U') IS NULL
BEGIN
    CREATE TABLE tb_CmsConfig (
        ConfigKey NVARCHAR(100) NOT NULL PRIMARY KEY,
        ConfigValue NVARCHAR(MAX) NOT NULL,
        [Description] NVARCHAR(250) NULL,
        UpdatedAt DATETIME2 NULL,
        UpdatedBy NVARCHAR(100) NULL
    );
END
ELSE
BEGIN
    IF COL_LENGTH('tb_CmsConfig', 'UpdatedAt') IS NULL
        ALTER TABLE tb_CmsConfig ADD UpdatedAt DATETIME2 NULL;
    IF COL_LENGTH('tb_CmsConfig', 'UpdatedBy') IS NULL
        ALTER TABLE tb_CmsConfig ADD UpdatedBy NVARCHAR(100) NULL;
END

IF OBJECT_ID('tb_Order', 'U') IS NULL
BEGIN
    CREATE TABLE tb_Order (
        OrderId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        Code NVARCHAR(50) NULL,
        BookingId INT NULL,
        AccountId INT NULL,
        BookingCode NVARCHAR(30) NULL,
        CustomerName NVARCHAR(150) NULL,
        Phone NVARCHAR(50) NULL,
        [Address] NVARCHAR(250) NULL,
        TotalAmount INT NULL CONSTRAINT DF_tb_Order_TotalAmount DEFAULT (0),
        PaidAmount INT NOT NULL CONSTRAINT DF_tb_Order_PaidAmount DEFAULT (0),
        PaymentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_Order_PaymentStatus DEFAULT ('unpaid'),
        [Status] NVARCHAR(30) NOT NULL CONSTRAINT DF_tb_Order_Status DEFAULT ('confirmed'),
        PaymentMethod NVARCHAR(20) NULL CONSTRAINT DF_tb_Order_PaymentMethod DEFAULT ('cash'),
        IdempotencyKey NVARCHAR(100) NULL,
        Quanlity INT NULL,
        OrderStatusId INT NULL CONSTRAINT DF_tb_Order_OrderStatusId DEFAULT (1),
        CreatedDate DATETIME2 NULL,
        CreatedBy NVARCHAR(150) NULL,
        ModifiedDate DATETIME2 NULL,
        ModifiedBy NVARCHAR(150) NULL,
        CONSTRAINT FK_tb_Order_tb_Booking FOREIGN KEY (BookingId) REFERENCES tb_Booking(BookingId),
        CONSTRAINT FK_tb_Order_tb_Account FOREIGN KEY (AccountId) REFERENCES tb_Account(AccountId),
        CONSTRAINT FK_tb_Order_tb_OrderStatus FOREIGN KEY (OrderStatusId) REFERENCES tb_OrderStatus(OrderStatusId)
    );
END
ELSE
BEGIN
    IF COL_LENGTH('tb_Order', 'BookingId') IS NULL
        ALTER TABLE tb_Order ADD BookingId INT NULL;
    IF COL_LENGTH('tb_Order', 'AccountId') IS NULL
        ALTER TABLE tb_Order ADD AccountId INT NULL;
    IF COL_LENGTH('tb_Order', 'BookingCode') IS NULL
        ALTER TABLE tb_Order ADD BookingCode NVARCHAR(30) NULL;
    IF COL_LENGTH('tb_Order', 'PaidAmount') IS NULL
        ALTER TABLE tb_Order ADD PaidAmount INT NOT NULL CONSTRAINT DF_tb_Order_PaidAmount DEFAULT (0);
    IF COL_LENGTH('tb_Order', 'PaymentStatus') IS NULL
        ALTER TABLE tb_Order ADD PaymentStatus NVARCHAR(20) NOT NULL CONSTRAINT DF_tb_Order_PaymentStatus DEFAULT ('unpaid');
    IF COL_LENGTH('tb_Order', 'Status') IS NULL
        ALTER TABLE tb_Order ADD [Status] NVARCHAR(30) NOT NULL CONSTRAINT DF_tb_Order_Status DEFAULT ('confirmed');
    IF COL_LENGTH('tb_Order', 'PaymentMethod') IS NULL
        ALTER TABLE tb_Order ADD PaymentMethod NVARCHAR(20) NULL CONSTRAINT DF_tb_Order_PaymentMethod DEFAULT ('cash');
    IF COL_LENGTH('tb_Order', 'IdempotencyKey') IS NULL
        ALTER TABLE tb_Order ADD IdempotencyKey NVARCHAR(100) NULL;
END

IF OBJECT_ID('tb_OrderDetail', 'U') IS NULL
BEGIN
    CREATE TABLE tb_OrderDetail (
        OrderDetailId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
        OrderId INT NULL,
        ProductId INT NULL,
        Price INT NULL,
        Quantity INT NULL,
        CONSTRAINT FK_tb_OrderDetail_tb_Order FOREIGN KEY (OrderId) REFERENCES tb_Order(OrderId),
        CONSTRAINT FK_tb_OrderDetail_tb_Product FOREIGN KEY (ProductId) REFERENCES tb_Product(ProductId)
    );
END

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
        CreatedAt DATETIME2 NOT NULL CONSTRAINT DF_tb_AuditLog_CreatedAt DEFAULT (SYSUTCDATETIME())
    );
END
");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        // Non-destructive Down: do not drop existing core club tables containing user data.
    }
}
