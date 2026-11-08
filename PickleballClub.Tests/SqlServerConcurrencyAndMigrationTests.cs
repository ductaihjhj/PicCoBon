using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PickleballClub.Models;
using PickleballClub.Services;
using Xunit;

namespace PickleballClub.Tests;

public class SqlServerConcurrencyAndMigrationTests
{
    private const string MasterConnectionString =
        "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "PickleballClub";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string BuildDbConn(string dbName) =>
        $"Server=.\\SQLEXPRESS;Database={dbName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    private static PickleballContext CreateSqlContext(string dbName)
    {
        var opts = new DbContextOptionsBuilder<PickleballContext>()
            .UseSqlServer(BuildDbConn(dbName))
            .Options;
        return new PickleballContext(opts);
    }

    private static BookingService CreateBookingService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:QrSigningKey"] = "SqlConcurrency-HMAC-SecretKey-2026-SpikePickleball-V2!"
            })
            .Build();
        var qr = new QrTokenService(config, new TestHostEnv());
        var pricing = new PricingService();
        var settings = new ClubSettingsService();
        return new BookingService(pricing, qr, settings);
    }

    private static async Task DropTempDatabaseSafeAsync(string dbName)
    {
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(MasterConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
IF DB_ID(N'{dbName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{dbName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{dbName}];
END";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task CreateEmptySqlDatabaseAsync(string dbName)
    {
        await DropTempDatabaseSafeAsync(dbName);
        await using var conn = new SqlConnection(MasterConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $"CREATE DATABASE [{dbName}];";
        await cmd.ExecuteNonQueryAsync();
    }

    private static async Task SeedOperationalDataAsync(string dbName)
    {
        await using var ctx = CreateSqlContext(dbName);
        await ctx.Database.MigrateAsync();

        ctx.TbCourts.AddRange(
            new TbCourt
            {
                CourtName = "Sân VIP 01 (Arena A)",
                CourtType = "Indoor",
                TypeLabel = "Trong nhà VIP",
                Surface = "Cushion 8 Lớp",
                BasePrice = 120_000,
                Surcharge = 20_000,
                IsActive = true
            },
            new TbCourt
            {
                CourtName = "Sân Tiêu Chuẩn 02 (Arena B)",
                CourtType = "Outdoor",
                TypeLabel = "Ngoài trời",
                Surface = "Hard Court",
                BasePrice = 120_000,
                Surcharge = 0,
                IsActive = true
            });

        ctx.TbProductCategories.Add(new TbProductCategory
        {
            Title = "Nước uống & Phụ kiện",
            Alias = "nuoc-uong-phu-kien",
            IsActive = true
        });
        await ctx.SaveChangesAsync();

        int catId = await ctx.TbProductCategories.Select(c => c.CategoryProductId).FirstAsync();
        ctx.TbProducts.Add(new TbProduct
        {
            Title = "Bóng Thi Đấu Franklin X-40",
            CategoryProductId = catId,
            Price = 65_000,
            UnitInStock = 3,
            Quantity = 3,
            IsActive = true,
            Icon = "lucide:circle-dot"
        });

        await ctx.SaveChangesAsync();
    }

    // =========================================================================
    // 1. P2-01 & P2-02: Clean EF Migrations on Empty DB + Non-Destructive Legacy Upgrade
    // =========================================================================
    [Fact]
    public async Task Migrations_ApplyCleanlyOnEmptyDb_And_UpgradeLegacyPreV2SchemaWithoutDataLoss()
    {
        string emptyDb = "PickleballClub_Test_Empty_" + Guid.NewGuid().ToString("N")[..8];
        string legacyDb = "PickleballClub_Test_Legacy_" + Guid.NewGuid().ToString("N")[..8];

        try
        {
            // Part A: Fresh empty DB -> MigrateAsync
            await using (var freshCtx = CreateSqlContext(emptyDb))
            {
                await freshCtx.Database.MigrateAsync();
                var applied = (await freshCtx.Database.GetAppliedMigrationsAsync()).ToList();
                Assert.Contains("20261109010000_V2_BaselineAndSafeExpand", applied);
                Assert.Contains("20261109010500_V2_LedgerTypedColumnsAndRowVersion", applied);
                Assert.Equal(3, await freshCtx.TbRoles.CountAsync());
            }

            // Part B: Pre-existing Legacy DB (short Password col, missing V2 columns, existing live customer data)
            await CreateEmptySqlDatabaseAsync(legacyDb);
            await using (var conn = new SqlConnection(BuildDbConn(legacyDb)))
            {
                await conn.OpenAsync();
                await using var cmd = conn.CreateCommand();
                cmd.CommandText = @"
CREATE TABLE tb_Role (RoleId INT IDENTITY(1,1) PRIMARY KEY, RoleName NVARCHAR(50), Description NVARCHAR(250));
SET IDENTITY_INSERT tb_Role ON;
INSERT INTO tb_Role (RoleId, RoleName, Description) VALUES (1, N'Admin', N'Admin'), (2, N'Customer', N'Customer');
SET IDENTITY_INSERT tb_Role OFF;

CREATE TABLE tb_Account (
    AccountId INT IDENTITY(1,1) PRIMARY KEY,
    Username NVARCHAR(50),
    [Password] NVARCHAR(50),
    FullName NVARCHAR(150),
    Phone NVARCHAR(50),
    Email NVARCHAR(150),
    Avatar NVARCHAR(500),
    Gender NVARCHAR(30),
    BirthDate NVARCHAR(20),
    [Address] NVARCHAR(250),
    SkillLevel NVARCHAR(100),
    DominantHand NVARCHAR(50),
    PaddleBrand NVARCHAR(150),
    PlayStyle NVARCHAR(150),
    EmergencyContact NVARCHAR(150),
    Bio NVARCHAR(500),
    MemberTier NVARCHAR(50),
    RoleId INT REFERENCES tb_Role(RoleId),
    LastLogin NVARCHAR(20),
    IsActive BIT NOT NULL DEFAULT 1
);
INSERT INTO tb_Account (Username, [Password], FullName, Phone, RoleId, IsActive)
VALUES (N'legacy_member', N'e10adc3949ba59abbe56e057f20f883e', N'Hội viên Cũ', N'0909111222', 2, 1);

CREATE TABLE tb_Court (
    CourtId INT IDENTITY(1,1) PRIMARY KEY,
    CourtName NVARCHAR(150) NOT NULL,
    Alias NVARCHAR(150),
    CourtType NVARCHAR(50) NOT NULL,
    TypeLabel NVARCHAR(250),
    Surface NVARCHAR(150),
    Image NVARCHAR(500),
    [Description] NVARCHAR(500),
    BasePrice INT NOT NULL DEFAULT 120000,
    Surcharge INT NOT NULL DEFAULT 0,
    Position INT NOT NULL DEFAULT 1,
    IsActive BIT NOT NULL DEFAULT 1
);
INSERT INTO tb_Court (CourtName, CourtType, BasePrice, Surcharge, IsActive)
VALUES (N'Sân Legacy 01', N'Indoor', 120000, 20000, 1);

CREATE TABLE tb_Booking (
    BookingId INT IDENTITY(1,1) PRIMARY KEY,
    BookingCode NVARCHAR(20) NOT NULL,
    CourtId INT NOT NULL REFERENCES tb_Court(CourtId),
    AccountId INT NULL REFERENCES tb_Account(AccountId),
    CustomerName NVARCHAR(150),
    Phone NVARCHAR(30),
    BookingDate NVARCHAR(20) NOT NULL,
    StartTime NVARCHAR(10) NOT NULL,
    Duration INT NOT NULL DEFAULT 60,
    Players INT NOT NULL DEFAULT 4,
    SkillLevel NVARCHAR(50),
    CourtFee INT NOT NULL DEFAULT 0,
    PosTotal INT NOT NULL DEFAULT 0,
    PosItemsJson NVARCHAR(MAX),
    Status NVARCHAR(20) NOT NULL DEFAULT N'approved',
    CreatedDate DATETIME NULL DEFAULT GETDATE()
);
INSERT INTO tb_Booking (BookingCode, CourtId, AccountId, CustomerName, Phone, BookingDate, StartTime, Duration, CourtFee, PosTotal, Status)
VALUES (N'PB-LEG001', 1, 1, N'Hội viên Cũ', N'0909111222', N'2026-11-18', N'18:00', 90, 240000, 20000, N'approved');
";
                await cmd.ExecuteNonQueryAsync();
            }

            // Upgrade legacy DB via EF Core MigrateAsync -> Zero data loss + automatic V2 column backfill!
            await using (var upgradedCtx = CreateSqlContext(legacyDb))
            {
                await upgradedCtx.Database.MigrateAsync();

                var acc = await upgradedCtx.TbAccounts.SingleAsync(a => a.Username == "legacy_member");
                Assert.Equal("Hội viên Cũ", acc.FullName);
                Assert.False(string.IsNullOrEmpty(acc.SecurityStamp));
                Assert.Equal(3, await upgradedCtx.TbRoles.CountAsync()); // Staff(3) added safely

                var bk = await upgradedCtx.TbBookings.SingleAsync(b => b.BookingCode == "PB-LEG001");
                Assert.Equal(260_000, bk.AmountDue); // Backfilled CourtFee(240k) + PosTotal(20k)
                Assert.Equal(260_000m, bk.AmountDueDec);
                Assert.Equal(new DateOnly(2026, 11, 18), bk.BookingDateValue);
                Assert.Equal(new TimeOnly(18, 0), bk.StartTimeValue);
                Assert.NotNull(bk.RowVersion);
                Assert.NotEmpty(bk.RowVersion!);
            }
        }
        finally
        {
            await DropTempDatabaseSafeAsync(emptyDb);
            await DropTempDatabaseSafeAsync(legacyDb);
        }
    }

    // =========================================================================
    // 2. P0-08 & P1-10: True Parallel SQL Server Concurrency — Double-Booking Prevention
    // =========================================================================
    [Fact]
    public async Task ParallelBookings_SameCourtOverlappingTime_ExactlyOneSucceeds_OthersGet409()
    {
        string dbName = "PickleballClub_Test_BkConc_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await SeedOperationalDataAsync(dbName);
            int courtId;
            await using (var setup = CreateSqlContext(dbName))
            {
                courtId = await setup.TbCourts.OrderBy(c => c.CourtId).Select(c => c.CourtId).FirstAsync();
            }

            var bookingService = CreateBookingService();
            var refNow = new DateTime(2026, 11, 10, 9, 0, 0);
            string targetDate = "2026-11-20";

            using var startGate = new ManualResetEventSlim(false);
            int workerCount = 10;

            var tasks = Enumerable.Range(0, workerCount).Select(i => Task.Run(async () =>
            {
                startGate.Wait();
                await using var ctx = CreateSqlContext(dbName);
                // Half request 18:00..19:00, half request overlapping 18:30..19:30 on the SAME court!
                string start = (i % 2 == 0) ? "18:00" : "18:30";
                return await bookingService.CreateBookingAsync(ctx, new BookingCreateInput
                {
                    CourtId = courtId,
                    CustomerName = $"Parallel VĐV {i}",
                    Phone = $"09123456{i:D2}",
                    BookingDate = targetDate,
                    StartTime = start,
                    Duration = 60,
                    Players = 4,
                    ReferenceNow = refNow
                });
            })).ToArray();

            startGate.Set();
            var results = await Task.WhenAll(tasks);

            int successCount = results.Count(r => r.Success);
            int conflictCount = results.Count(r => !r.Success && r.StatusCode == 409);

            Assert.Equal(1, successCount);
            Assert.Equal(workerCount - 1, conflictCount);

            await using (var verifyCtx = CreateSqlContext(dbName))
            {
                int persistedCount = await verifyCtx.TbBookings.CountAsync(b => b.CourtId == courtId && b.BookingDate == targetDate);
                Assert.Equal(1, persistedCount);
            }
        }
        finally
        {
            await DropTempDatabaseSafeAsync(dbName);
        }
    }

    // =========================================================================
    // 3. P1-10 & P1-11: Parallel Booking vs Maintenance Lock & Parallel Extend vs Booking
    // =========================================================================
    [Fact]
    public async Task ParallelBookingVsLock_And_ParallelExtendVsAdjacentBooking_MutualExclusionHolds()
    {
        string dbName = "PickleballClub_Test_LockExt_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await SeedOperationalDataAsync(dbName);
            int courtId;
            await using (var setup = CreateSqlContext(dbName))
            {
                courtId = await setup.TbCourts.OrderBy(c => c.CourtId).Select(c => c.CourtId).FirstAsync();
            }

            var bookingService = CreateBookingService();
            var refNow = new DateTime(2026, 11, 10, 9, 0, 0);

            // Case A: Booking [19:00..20:00] racing against Maintenance Lock [19:30..20:30]
            using (var gateA = new ManualResetEventSlim(false))
            {
                var bookTask = Task.Run(async () =>
                {
                    gateA.Wait();
                    await using var ctx = CreateSqlContext(dbName);
                    var r = await bookingService.CreateBookingAsync(ctx, new BookingCreateInput
                    {
                        CourtId = courtId,
                        CustomerName = "Race Booking",
                        Phone = "0988777666",
                        BookingDate = "2026-11-21",
                        StartTime = "19:00",
                        Duration = 60,
                        ReferenceNow = refNow
                    });
                    return (r.Success, r.StatusCode);
                });

                var lockTask = Task.Run(async () =>
                {
                    gateA.Wait();
                    await using var ctx = CreateSqlContext(dbName);
                    var r = await bookingService.CreateCourtLockAsync(ctx, courtId, "2026-11-21", "19:30", 60, "Bảo trì lưới", 1, "admin");
                    return (r.Success, r.StatusCode);
                });

                gateA.Set();
                var resA = await Task.WhenAll(bookTask, lockTask);
                Assert.Equal(1, resA.Count(x => x.Success));
                Assert.Equal(1, resA.Count(x => !x.Success && x.StatusCode == 409));
            }

            // Case B: Existing booking [15:00..16:00] extending +30m (-> 16:30) racing against new booking [16:00..17:00]
            string baseBookingCode;
            await using (var seedCtx = CreateSqlContext(dbName))
            {
                var baseRes = await bookingService.CreateBookingAsync(seedCtx, new BookingCreateInput
                {
                    CourtId = courtId,
                    CustomerName = "Ca Gốc 15h",
                    Phone = "0977111222",
                    BookingDate = "2026-11-22",
                    StartTime = "15:00",
                    Duration = 60,
                    ActorRoleId = 3,
                    AutoApproveWalkIn = true,
                    ReferenceNow = refNow
                });
                Assert.True(baseRes.Success);
                baseBookingCode = baseRes.Booking!.BookingCode;
            }

            using (var gateB = new ManualResetEventSlim(false))
            {
                var extendTask = Task.Run(async () =>
                {
                    gateB.Wait();
                    await using var ctx = CreateSqlContext(dbName);
                    return await bookingService.ExtendBookingDurationAsync(ctx, baseBookingCode, 30, 1, "admin");
                });

                var adjacentBookTask = Task.Run(async () =>
                {
                    gateB.Wait();
                    await using var ctx = CreateSqlContext(dbName);
                    return await bookingService.CreateBookingAsync(ctx, new BookingCreateInput
                    {
                        CourtId = courtId,
                        CustomerName = "Ca Kế Tiếp 16h",
                        Phone = "0977333444",
                        BookingDate = "2026-11-22",
                        StartTime = "16:00",
                        Duration = 60,
                        ReferenceNow = refNow
                    });
                });

                gateB.Set();
                var resB = await Task.WhenAll(extendTask, adjacentBookTask);
                Assert.Equal(1, resB.Count(x => x.Success));
                Assert.Equal(1, resB.Count(x => !x.Success && x.StatusCode == 409));
            }
        }
        finally
        {
            await DropTempDatabaseSafeAsync(dbName);
        }
    }

    // =========================================================================
    // 4. P0-10 & P1-15: Parallel Pro Shop Stock Decrement (Never Negative) + Idempotency + Restock
    // =========================================================================
    [Fact]
    public async Task ParallelProShopOrders_LastStockItems_NeverNegative_IdempotencyAndCancelRestockWork()
    {
        string dbName = "PickleballClub_Test_StockConc_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await SeedOperationalDataAsync(dbName);
            int productId;
            await using (var setup = CreateSqlContext(dbName))
            {
                productId = await setup.TbProducts.Select(p => p.ProductId).FirstAsync();
            }

            var orderService = new OrderService();
            using var startGate = new ManualResetEventSlim(false);
            int buyerCount = 10; // Stock is strictly 3!

            var tasks = Enumerable.Range(0, buyerCount).Select(i => Task.Run(async () =>
            {
                startGate.Wait();
                await using var ctx = CreateSqlContext(dbName);
                return await orderService.CreateOrderAsync(ctx, new CreateShopOrderInput
                {
                    CustomerName = $"Khách ProShop {i}",
                    Phone = $"09334455{i:D2}",
                    IdempotencyKey = $"idem-shop-{i}",
                    Items = new List<OrderItemInput>
                    {
                        new() { ProductId = productId, Qty = 1 }
                    }
                });
            })).ToArray();

            startGate.Set();
            var results = await Task.WhenAll(tasks);

            var winners = results.Where(r => r.Success).ToList();
            var losers = results.Where(r => !r.Success).ToList();

            Assert.Equal(3, winners.Count);
            Assert.Equal(7, losers.Count);
            Assert.All(losers, l => Assert.Equal(409, l.StatusCode));

            await using (var verifyCtx = CreateSqlContext(dbName))
            {
                var product = await verifyCtx.TbProducts.AsNoTracking().SingleAsync(p => p.ProductId == productId);
                Assert.Equal(0, product.UnitInStock);
                Assert.Equal(0, product.Quantity);

                int saleMovements = await verifyCtx.TbInventoryMovements.CountAsync(m => m.ProductId == productId && m.ChangeQty == -1);
                Assert.Equal(3, saleMovements);

                // Replay idempotency key of winner #0 -> returns existing order without failing or altering stock
                string winnerIdem = winners[0].Order!.IdempotencyKey!;
                var replay = await orderService.CreateOrderAsync(verifyCtx, new CreateShopOrderInput
                {
                    CustomerName = "Replay Caller",
                    Phone = "0933445500",
                    IdempotencyKey = winnerIdem,
                    Items = new List<OrderItemInput> { new() { ProductId = productId, Qty = 1 } }
                });
                Assert.True(replay.Success);
                Assert.True(replay.IsIdempotentReplay);
                Assert.Equal(winners[0].Order!.Code, replay.Order!.Code);

                // Cancel unpaid winner #0 -> restocks +1 atomically
                var cancelRes = await orderService.CancelOrderAndRestockAsync(
                    verifyCtx, winners[0].Order!.Code!, "Khách đổi ý", 1, "admin");
                Assert.True(cancelRes.Success);
                Assert.Equal("cancelled", cancelRes.Order!.Status);

                var restockedProduct = await verifyCtx.TbProducts.AsNoTracking().SingleAsync(p => p.ProductId == productId);
                Assert.Equal(1, restockedProduct.UnitInStock);
                Assert.Equal(1, await verifyCtx.TbInventoryMovements.CountAsync(m => m.ProductId == productId && m.ChangeQty == 1));
            }
        }
        finally
        {
            await DropTempDatabaseSafeAsync(dbName);
        }
    }

    // =========================================================================
    // 5. P1-04, P1-05, P1-06, P1-07: Ledger Separation (Finish != Paid), Partial, Transfer Ref, Refund & Lifecycle
    // =========================================================================
    [Fact]
    public async Task PaymentLedger_SeparateFinishVsPartialCollectVsRefund_TransferRefRequired_And_LifecycleReconcile()
    {
        string dbName = "PickleballClub_Test_Ledger_" + Guid.NewGuid().ToString("N")[..8];
        try
        {
            await SeedOperationalDataAsync(dbName);
            await using var ctx = CreateSqlContext(dbName);
            int courtId = await ctx.TbCourts.OrderBy(c => c.CourtId).Select(c => c.CourtId).FirstAsync();
            var bookingService = CreateBookingService();

            // 1. Create booking at 18:00 (Peak VIP: 120k + 20k + 30k = 170,000đ)
            var createRes = await bookingService.CreateBookingAsync(ctx, new BookingCreateInput
            {
                CourtId = courtId,
                CustomerName = "Nguyễn Văn Ledger",
                Phone = "0905123456",
                BookingDate = "2026-11-18",
                StartTime = "18:00",
                Duration = 60,
                ReferenceNow = new DateTime(2026, 11, 18, 10, 0, 0)
            });
            Assert.True(createRes.Success);
            string code = createRes.Booking!.BookingCode;
            Assert.Equal(170_000, createRes.Booking.AmountDue);

            // 2. Pending booking CANNOT check-in before approval (P1-07)
            var prematureCheckIn = await bookingService.CheckInBookingAsync(
                ctx, createRes.Booking.CheckInToken!, 1, "admin", null, new DateTime(2026, 11, 18, 17, 50, 0));
            Assert.False(prematureCheckIn.Success);
            Assert.Equal(409, prematureCheckIn.StatusCode);

            // 3. Approve -> Check-in within window -> status becomes "playing"
            Assert.True((await bookingService.ApproveBookingAsync(ctx, code, 1, "admin")).Success);
            var validCheckIn = await bookingService.CheckInBookingAsync(
                ctx, createRes.Booking.CheckInToken!, 1, "admin", null, new DateTime(2026, 11, 18, 17, 50, 0));
            Assert.True(validCheckIn.Success);
            Assert.Equal("playing", validCheckIn.Booking!.Status);

            // 4. FinishSession closes session ("completed") WITHOUT faking payment! (P1-05)
            var finishRes = await bookingService.FinishBookingSessionAsync(ctx, code, 1, "admin");
            Assert.True(finishRes.Success);
            Assert.Equal("completed", finishRes.Booking!.Status);
            Assert.Equal(0, finishRes.Booking.PaidAmount);
            Assert.Equal("unpaid", finishRes.Booking.PaymentStatus);
            Assert.Equal(0, await ctx.TbPaymentTransactions.CountAsync(t => t.BookingCode == code));

            // 5. Bank transfer WITHOUT ReferenceCode must be rejected 400 (P1-05)
            var missingRef = await bookingService.RecordBookingPaymentAsync(
                ctx, code, 70_000, "transfer", referenceCode: "", note: "Thiếu mã GD", idempotencyKey: "pay-bad", 1, "admin");
            Assert.False(missingRef.Success);
            Assert.Equal(400, missingRef.StatusCode);

            // 6. Partial cash payment 100,000đ -> PaidAmount = 100k, PaymentStatus = "partial"
            var partPay = await bookingService.RecordBookingPaymentAsync(
                ctx, code, 100_000, "cash", null, "Thu tiền mặt đợt 1", "pay-1", 1, "admin");
            Assert.True(partPay.Success);
            Assert.Equal(100_000, partPay.Booking!.PaidAmount);
            Assert.Equal("partial", partPay.Booking.PaymentStatus);

            // 7. Remaining transfer payment 70,000đ with ReferenceCode -> PaidAmount = 170k, PaymentStatus = "paid"
            var finalPay = await bookingService.RecordBookingPaymentAsync(
                ctx, code, 70_000, "transfer", "FT2611189988", "Chuyển khoản phần còn lại", "pay-2", 1, "admin");
            Assert.True(finalPay.Success);
            Assert.Equal(170_000, finalPay.Booking!.PaidAmount);
            Assert.Equal("paid", finalPay.Booking.PaymentStatus);

            // 8. Overpayment guard -> rejected
            var overPay = await bookingService.RecordBookingPaymentAsync(
                ctx, code, 10_000, "cash", null, "Thu lố", "pay-over", 1, "admin");
            Assert.False(overPay.Success);

            // 9. Counter-entry Refund 50,000đ -> PaidAmount = 120k, PaymentStatus = "partial", immutable ledger count = 3
            var refundRes = await bookingService.RefundBookingPaymentAsync(
                ctx, code, 50_000, "cash", null, "Hoàn 20 phút mưa lớn", "ref-1", 1, "admin");
            Assert.True(refundRes.Success);
            Assert.Equal(120_000, refundRes.Booking!.PaidAmount);
            Assert.Equal("partial", refundRes.Booking.PaymentStatus);
            Assert.Equal(3, await ctx.TbPaymentTransactions.CountAsync(t => t.BookingCode == code));

            // 10. Lifecycle Reconciliation: approved without check-in NEVER becomes playing; past grace -> no_show
            var noShowBk = await bookingService.CreateBookingAsync(ctx, new BookingCreateInput
            {
                CourtId = courtId,
                CustomerName = "Khách Vắng Mặt",
                Phone = "0905999888",
                BookingDate = "2026-11-19",
                StartTime = "08:00",
                Duration = 60,
                ActorRoleId = 3,
                AutoApproveWalkIn = true,
                ReferenceNow = new DateTime(2026, 11, 19, 7, 0, 0)
            });
            Assert.True(noShowBk.Success);

            // At 08:20 (during slot, no check-in) -> still approved, NEVER auto-playing!
            await bookingService.ReconcileBookingLifecycleAsync(ctx, new DateTime(2026, 11, 19, 8, 20, 0));
            var midCheck = await ctx.TbBookings.AsNoTracking().SingleAsync(b => b.BookingCode == noShowBk.Booking!.BookingCode);
            Assert.Equal("approved", midCheck.Status);

            // At 09:30 (past grace window without check-in) -> marked no_show!
            await bookingService.ReconcileBookingLifecycleAsync(ctx, new DateTime(2026, 11, 19, 9, 30, 0));
            var afterGrace = await ctx.TbBookings.AsNoTracking().SingleAsync(b => b.BookingCode == noShowBk.Booking!.BookingCode);
            Assert.Equal("no_show", afterGrace.Status);
        }
        finally
        {
            await DropTempDatabaseSafeAsync(dbName);
        }
    }
}
