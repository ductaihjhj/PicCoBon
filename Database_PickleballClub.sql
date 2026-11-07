-- ============================================================================
-- ĐỒ ÁN MÔN HỌC: PHÁT TRIỂN ỨNG DỤNG WEB (ASP.NET CORE MVC & SQL SERVER)
-- CƠ SỞ DỮ LIỆU: PickleballClub (Theo chuẩn cấu trúc học phần Harmic)
-- Server: .\SQLEXPRESS
-- ============================================================================

USE master;
GO

IF DB_ID('PickleballClub') IS NOT NULL
BEGIN
    ALTER DATABASE PickleballClub SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE PickleballClub;
END
GO

CREATE DATABASE PickleballClub;
GO

USE PickleballClub;
GO

-- 1. Bảng Phân quyền (tb_Role)
CREATE TABLE tb_Role (
    RoleId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    RoleName NVARCHAR(50) NULL,
    Description NVARCHAR(250) NULL
);
GO

-- 2. Bảng Tài khoản Quản trị & Hội viên (tb_Account)
CREATE TABLE tb_Account (
    AccountId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Username NVARCHAR(50) NULL,
    Password NVARCHAR(50) NULL,
    FullName NVARCHAR(150) NULL,
    Phone NVARCHAR(50) NULL,
    Email NVARCHAR(150) NULL,
    Avatar NVARCHAR(500) NULL,
    Gender NVARCHAR(30) NULL,
    BirthDate NVARCHAR(20) NULL,
    Address NVARCHAR(250) NULL,
    SkillLevel NVARCHAR(100) NULL,
    DominantHand NVARCHAR(50) NULL,
    PaddleBrand NVARCHAR(150) NULL,
    PlayStyle NVARCHAR(150) NULL,
    EmergencyContact NVARCHAR(150) NULL,
    Bio NVARCHAR(500) NULL,
    MemberTier NVARCHAR(50) NULL,
    RoleId INT NULL,
    LastLogin NCHAR(10) NULL,
    IsActive BIT NOT NULL DEFAULT 1,
    CONSTRAINT FK_tb_Account_tb_Role FOREIGN KEY (RoleId) REFERENCES tb_Role(RoleId)
);
GO

-- 3. Bảng Menu điều hướng Website (tb_Menu)
CREATE TABLE tb_Menu (
    MenuId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Title NVARCHAR(150) NULL,
    Alias NVARCHAR(150) NULL,
    Description NVARCHAR(500) NULL,
    Levels INT NULL DEFAULT 1,
    ParentId INT NULL DEFAULT 0,
    Position INT NULL DEFAULT 1,
    CreatedDate DATETIME NULL DEFAULT GETDATE(),
    CreatedBy NVARCHAR(150) NULL,
    ModifiedDate DATETIME NULL DEFAULT GETDATE(),
    ModifiedBy NVARCHAR(150) NULL,
    IsActive BIT NOT NULL DEFAULT 1
);
GO

-- 4. Bảng Danh mục Sân đấu Pickleball (tb_Court)
CREATE TABLE tb_Court (
    CourtId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    CourtName NVARCHAR(150) NOT NULL,
    Alias NVARCHAR(150) NULL,
    CourtType NVARCHAR(50) NOT NULL DEFAULT 'Indoor',
    TypeLabel NVARCHAR(250) NULL,
    Surface NVARCHAR(150) NULL,
    Image NVARCHAR(500) NULL,
    BasePrice INT NOT NULL DEFAULT 120000,
    Surcharge INT NOT NULL DEFAULT 0,
    Description NVARCHAR(500) NULL,
    IsActive BIT NOT NULL DEFAULT 1
);
GO

-- 5. Bảng Trạng thái Đơn đặt sân / Đơn hàng (tb_OrderStatus)
CREATE TABLE tb_OrderStatus (
    OrderStatusId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Name NVARCHAR(50) NOT NULL,
    Description NVARCHAR(250) NULL
);
GO

-- 6. Bảng Lịch Đặt Sân (tb_Booking)
CREATE TABLE tb_Booking (
    BookingId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    BookingCode NVARCHAR(30) NOT NULL UNIQUE,
    CourtId INT NOT NULL,
    AccountId INT NULL,
    CustomerName NVARCHAR(150) NOT NULL,
    Phone NVARCHAR(50) NOT NULL,
    BookingDate NVARCHAR(20) NOT NULL,
    StartTime NVARCHAR(10) NOT NULL,
    Duration INT NOT NULL DEFAULT 60,
    Players INT NOT NULL DEFAULT 4,
    SkillLevel NVARCHAR(100) NULL DEFAULT N'Phong trào',
    CourtFee INT NOT NULL DEFAULT 0,
    PosItemsJson NVARCHAR(MAX) NOT NULL DEFAULT N'[]',
    TotalPaid INT NOT NULL DEFAULT 0,
    Status NVARCHAR(30) NOT NULL DEFAULT 'pending',
    OrderStatusId INT NULL,
    CreatedDate DATETIME NULL DEFAULT GETDATE(),
    CONSTRAINT FK_tb_Booking_tb_Court FOREIGN KEY (CourtId) REFERENCES tb_Court(CourtId),
    CONSTRAINT FK_tb_Booking_tb_Account FOREIGN KEY (AccountId) REFERENCES tb_Account(AccountId),
    CONSTRAINT FK_tb_Booking_tb_OrderStatus FOREIGN KEY (OrderStatusId) REFERENCES tb_OrderStatus(OrderStatusId)
);
GO

-- 7. Bảng Khóa sân Bảo trì / Giải đấu (tb_CourtLock)
CREATE TABLE tb_CourtLock (
    LockId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    LockCode NVARCHAR(30) NOT NULL,
    CourtId INT NOT NULL,
    LockDate NVARCHAR(20) NOT NULL,
    StartTime NVARCHAR(10) NOT NULL,
    Duration INT NOT NULL DEFAULT 60,
    Reason NVARCHAR(250) NULL DEFAULT N'Bảo trì định kỳ',
    CreatedDate DATETIME NULL DEFAULT GETDATE(),
    CONSTRAINT FK_tb_CourtLock_tb_Court FOREIGN KEY (CourtId) REFERENCES tb_Court(CourtId)
);
GO

-- 8. Bảng Danh mục Sản phẩm Pro Shop & F&B (tb_ProductCategory)
CREATE TABLE tb_ProductCategory (
    CategoryProductId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Title NVARCHAR(150) NULL,
    Alias NVARCHAR(150) NULL,
    Description NVARCHAR(500) NULL,
    Icon NVARCHAR(250) NULL,
    Position INT NULL DEFAULT 1,
    CreatedDate DATETIME NULL DEFAULT GETDATE(),
    IsActive BIT NOT NULL DEFAULT 1
);
GO

-- 9. Bảng Sản phẩm & Dịch vụ F&B / Thuê vợt (tb_Product)
CREATE TABLE tb_Product (
    ProductId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Title NVARCHAR(250) NULL,
    Alias NVARCHAR(250) NULL,
    CategoryProductId INT NULL,
    Description NVARCHAR(4000) NULL,
    Detail NVARCHAR(MAX) NULL,
    Image NVARCHAR(500) NULL,
    Icon NVARCHAR(300) NULL,
    Price INT NULL DEFAULT 0,
    PriceSale INT NULL DEFAULT 0,
    Quantity INT NULL DEFAULT 100,
    CreatedDate DATETIME NULL DEFAULT GETDATE(),
    CreatedBy NVARCHAR(150) NULL,
    ModifiedDate DATETIME NULL DEFAULT GETDATE(),
    ModifiedBy NVARCHAR(150) NULL,
    IsNew BIT NOT NULL DEFAULT 1,
    IsBestSeller BIT NOT NULL DEFAULT 0,
    UnitInStock INT NULL DEFAULT 100,
    IsActive BIT NOT NULL DEFAULT 1,
    Star INT NULL DEFAULT 5,
    CONSTRAINT FK_tb_Product_tb_ProductCategory FOREIGN KEY (CategoryProductId) REFERENCES tb_ProductCategory(CategoryProductId)
);
GO

-- 10. Bảng Hóa đơn Dịch vụ / F&B (tb_Order & tb_OrderDetail)
CREATE TABLE tb_Order (
    OrderId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    Code NVARCHAR(50) NULL,
    CustomerName NVARCHAR(150) NULL,
    Phone NVARCHAR(50) NULL,
    Address NVARCHAR(250) NULL,
    TotalAmount INT NULL DEFAULT 0,
    Quanlity INT NULL DEFAULT 1,
    OrderStatusId INT NULL,
    CreatedDate DATETIME NULL DEFAULT GETDATE(),
    CreatedBy NVARCHAR(150) NULL,
    ModifiedDate DATETIME NULL DEFAULT GETDATE(),
    ModifiedBy NVARCHAR(150) NULL,
    CONSTRAINT FK_tb_Order_tb_OrderStatus FOREIGN KEY (OrderStatusId) REFERENCES tb_OrderStatus(OrderStatusId)
);
GO

CREATE TABLE tb_OrderDetail (
    OrderDetailId INT IDENTITY(1,1) NOT NULL PRIMARY KEY,
    OrderId INT NULL,
    ProductId INT NULL,
    Price INT NULL,
    Quantity INT NULL,
    CONSTRAINT FK_tb_OrderDetail_tb_Order FOREIGN KEY (OrderId) REFERENCES tb_Order(OrderId),
    CONSTRAINT FK_tb_OrderDetail_tb_Product FOREIGN KEY (ProductId) REFERENCES tb_Product(ProductId)
);
GO

-- 11. Bảng Cấu hình Giao diện CMS (tb_CmsConfig)
CREATE TABLE tb_CmsConfig (
    ConfigKey NVARCHAR(100) NOT NULL PRIMARY KEY,
    ConfigValue NVARCHAR(MAX) NOT NULL,
    Description NVARCHAR(250) NULL
);
GO

-- ============================================================================
-- KHỞI TẠO DỮ LIỆU DANH MỤC CẤU TRÚC (KHÔNG CHỨA ĐƠN ĐẶT SÂN MẪU GIẢ LẬP)
-- ============================================================================

-- 1. Vai trò hệ thống
INSERT INTO tb_Role (RoleName, Description) VALUES
(N'Admin', N'Ban Quản Trị CLB Pickleball'),
(N'Customer', N'Hội viên & Khách đặt sân');

-- 2. Tài khoản Quản trị viên mặc định (Password: admin123 -> MD5: 0192023a7bbd73250516f069df18b500)
INSERT INTO tb_Account (Username, Password, FullName, Phone, Email, RoleId, IsActive) VALUES
(N'admin', N'0192023a7bbd73250516f069df18b500', N'Quản Trị Viên CLB', N'0988889999', N'admin@pickleball.vn', 1, 1);

-- 3. Menu điều hướng chuẩn ViewComponent MenuTop
INSERT INTO tb_Menu (Title, Alias, Description, Levels, ParentId, Position, IsActive) VALUES
(N'Trang chủ', N'', N'Trang chủ CLB', 1, 0, 1, 1),
(N'Đặt sân trực tuyến', N'Booking', N'Đặt lịch sân USAPA', 1, 0, 2, 1),
(N'Cửa hàng & Dịch vụ', N'Product', N'Pro Shop & F&B', 1, 0, 3, 1),
(N'Tra cứu vé QR', N'Lookup', N'Tra cứu mã vé điện tử', 1, 0, 4, 1);

-- 4. Trạng thái đơn
INSERT INTO tb_OrderStatus (Name, Description) VALUES
(N'pending', N'Chờ duyệt'),
(N'approved', N'Đã duyệt'),
(N'playing', N'Đang thi đấu'),
(N'completed', N'Hoàn thành'),
(N'rejected', N'Đã hủy');

-- 5. Danh mục 4 Sân Pickleball chuẩn USAPA
INSERT INTO tb_Court (CourtName, Alias, CourtType, TypeLabel, Surface, Image, BasePrice, Surcharge, Description, IsActive) VALUES
(N'Sân VIP 01 (Arena A)', N'san-vip-01-arena-a', N'Indoor', N'Trong nhà · Mái vòm & Đèn LED Pro', N'USAPA Cushion 8 lớp', N'/assets/court-vip-arena.png', 120000, 20000, N'Sân mái vòm cách nhiệt, đèn LED 1000 Lux chống lóa', 1),
(N'Sân VIP 02 (Arena B)', N'san-vip-02-arena-b', N'Indoor', N'Trong nhà · Mái vòm & Đèn LED Pro', N'USAPA Cushion 8 lớp', N'/assets/court-vip-arena.png', 120000, 20000, N'Sân mái vòm tiêu chuẩn giải đấu chuyên nghiệp', 1),
(N'Sân Tiêu Chuẩn 03', N'san-tieu-chuan-03', N'Outdoor', N'Ngoài trời · Khán đài thoáng mát', N'Acrylic Hard Court', N'/assets/hero-spike-pickleball.png', 120000, 0, N'Sân ngoài trời không gian thoáng đãng', 1),
(N'Sân Tiêu Chuẩn 04', N'san-tieu-chuan-04', N'Outdoor', N'Ngoài trời · Khán đài thoáng mát', N'Acrylic Hard Court', N'/assets/hero-spike-pickleball.png', 120000, 0, N'Sân ngoài trời chuẩn luyện tập & giao lưu', 1);

-- 6. Danh mục Sản phẩm Pro Shop & F&B
INSERT INTO tb_ProductCategory (Title, Alias, Description, Icon, Position, IsActive) VALUES
(N'Nước uống & Điện giải', N'drink', N'Nước khoáng, bù khoáng ion', N'mdi:cup-water', 1, 1),
(N'Đồ ăn nhẹ & Năng lượng', N'food', N'Mì trộn, thanh năng lượng', N'mdi:noodles', 2, 1),
(N'Thuê Vợt & Phụ kiện', N'other', N'Vợt Carbon T700, bóng Franklin X-40', N'mdi:table-tennis', 3, 1);

-- 7. Sản phẩm cửa hàng
INSERT INTO tb_Product (Title, Alias, CategoryProductId, Description, Image, Icon, Price, PriceSale, Quantity, UnitInStock, IsNew, IsBestSeller, IsActive, Star) VALUES
(N'Revive Điện Giải Chanh Muối', N'revive-dien-giai-chanh-muoi', 1, N'Bù nước và khoáng chất tức thì trên sân', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:bottle-soda-classic-outline" width="26"></iconify-icon>', 15000, 15000, 100, 100, 1, 1, 1, 5),
(N'Pocari Sweat Ion Supply', N'pocari-sweat-ion-supply', 1, N'Thức uống bổ sung ion chuẩn Nhật Bản', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:cup-water" width="26"></iconify-icon>', 20000, 20000, 100, 100, 1, 1, 1, 5),
(N'Nước Khoáng Lạnh Aquafina', N'nuoc-khoang-lanh-aquafina', 1, N'Nước suối tinh khiết ướp lạnh', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:water-outline" width="26"></iconify-icon>', 10000, 10000, 150, 150, 0, 1, 1, 5),
(N'Heineken Silver Ướp Lạnh', N'heineken-silver-uop-lanh', 1, N'Giải khát sau trận đấu', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:beer-outline" width="26"></iconify-icon>', 25000, 25000, 80, 80, 0, 0, 1, 5),
(N'Nước Tăng Lực Red Bull', N'nuoc-tang-luc-red-bull', 1, N'Tăng cường sự tập trung và thể lực', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:lightning-bolt" width="26"></iconify-icon>', 20000, 20000, 100, 100, 0, 1, 1, 5),
(N'Trà Chanh Giã Tay Ca Lớn', N'tra-chanh-gia-tay-ca-lon', 1, N'Trà chanh tươi mát lạnh tại sân', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:tea-outline" width="26"></iconify-icon>', 18000, 18000, 60, 60, 1, 1, 1, 5),
(N'Mì Trộn Trứng & Xúc Xích', N'mi-tron-trung-xuc-xich', 2, N'Bữa ăn nạp năng lượng nhanh tại CLB', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:noodles" width="26"></iconify-icon>', 35000, 35000, 50, 50, 1, 1, 1, 5),
(N'Chuối & Thanh Năng Lượng', N'chuoi-thanh-nang-luong', 2, N'Chống chuột rút và bổ sung Kali', N'/assets/court-vip-arena.png', N'<iconify-icon icon="mdi:food-apple-outline" width="26"></iconify-icon>', 25000, 25000, 50, 50, 1, 0, 1, 5),
(N'Thuê Vợt Carbon T700 Pro', N'thue-vot-carbon-t700-pro', 3, N'Vợt sợi Carbon nhám tạo xoáy chuẩn thi đấu', N'/assets/hero-spike-pickleball.png', N'<iconify-icon icon="mdi:table-tennis" width="26"></iconify-icon>', 40000, 40000, 30, 30, 1, 1, 1, 5),
(N'Bóng Thi Đấu Franklin X-40', N'bong-thi-dau-franklin-x40', 3, N'Bóng tiêu chuẩn giải US Open Pickleball', N'/assets/hero-spike-pickleball.png', N'<iconify-icon icon="mdi:tennis-ball" width="26"></iconify-icon>', 65000, 65000, 60, 60, 1, 1, 1, 5);

-- 8. Cấu hình CMS mặc định
INSERT INTO tb_CmsConfig (ConfigKey, ConfigValue, Description) VALUES
(N'heroTag', N'USAPA STANDARD ARENA', N'Nhãn phụ trên Hero'),
(N'heroSubtag', N'Tổ hợp 4 sân tiêu chuẩn thi đấu quốc tế', N'Mô tả ngắn'),
(N'heroTitle', N'Nâng Tầm Trải Nghiệm Pickleball Đỉnh Cao', N'Tiêu đề chính Hero'),
(N'heroSubtitle', N'Mặt sân Cushion 8 lớp giảm chấn bảo vệ khớp gối, hệ thống chiếu sáng LED chống lóa 1000 Lux và đặt lịch nhận mã QR tức thì.', N'Nội dung mô tả Hero'),
(N'courtPrice', N'120000', N'Giá sân tiêu chuẩn mỗi giờ'),
(N'openTime', N'06:00', N'Giờ mở cửa'),
(N'closeTime', N'22:00', N'Giờ đóng cửa'),
(N'hotline', N'0901 234 567', N'Số điện thoại Hotline CLB'),
(N'clubTitle', N'Thẻ Hội Viên Spike VIP', N'Tiêu đề gói hội viên'),
(N'clubDesc', N'Giảm ngay 15% giá thuê sân mọi khung giờ, miễn phí nước khoáng điện giải và ưu tiên giữ lịch cố định hàng tuần.', N'Mô tả gói hội viên');
GO
