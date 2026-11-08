using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using PickleballClub.Controllers;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using Xunit;

namespace PickleballClub.Tests;

public class MasterAuditTests
{
    private static PickleballContext CreateInMemoryContext()
    {
        var options = new DbContextOptionsBuilder<PickleballContext>()
            .UseInMemoryDatabase(databaseName: "SpikeAuditDb_" + Guid.NewGuid().ToString("N"))
            .Options;
        var ctx = new PickleballContext(options);

        ctx.TbCourts.AddRange(
            new TbCourt
            {
                CourtId = 1,
                CourtName = "Sân VIP 01 (Arena A)",
                CourtType = "Indoor",
                TypeLabel = "Trong nhà VIP",
                Surface = "Cushion 8 Lớp",
                BasePrice = 120000,
                Surcharge = 20000,
                IsActive = true
            },
            new TbCourt
            {
                CourtId = 3,
                CourtName = "Sân Tiêu Chuẩn 03 (Arena B)",
                CourtType = "Outdoor",
                TypeLabel = "Ngoài trời",
                Surface = "Hard Court",
                BasePrice = 120000,
                Surcharge = 0,
                IsActive = true
            });

        ctx.TbProducts.Add(new TbProduct
        {
            ProductId = 10,
            Title = "Nước Khoáng Điện Giải Revive",
            Price = 20000,
            UnitInStock = 15,
            Quantity = 15,
            IsActive = true,
            Icon = "lucide:cup-soda"
        });

        ctx.SaveChanges();
        return ctx;
    }

    private static QrTokenService CreateQrService()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:QrSigningKey"] = "UnitTest-HMAC-SecretKey-2026-SpikePickleball"
            })
            .Build();
        return new QrTokenService(config);
    }

    // =========================================================================
    // 1. P0-01 & P0-02: Per-Request Auth Isolation & Forged Cookie Rejection
    // =========================================================================
    [Fact]
    public void AuthIsolation_ForgedCookieIsIgnored_And_RequestsAreIsolated()
    {
        var accessor = new HttpContextAccessor();
        Function.Configure(accessor);

        // Request A: Anonymous user sending forged pb_admin_uid=1 cookie
        var anonContext = new DefaultHttpContext();
        anonContext.Request.Headers["Cookie"] = "pb_admin_uid=1";
        accessor.HttpContext = anonContext;

        Assert.False(Function.IsLogin());
        Assert.False(Function.IsAdmin());
        Assert.Equal(0, Function._AccountId);

        // Request B: Authenticated Admin principal via ASP.NET Core Cookie Auth claims
        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "admin"),
            new Claim("FullName", "Ban Quản Trị"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("RoleId", "1")
        };
        var authContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "SpikeCookieAuth"))
        };
        accessor.HttpContext = authContext;

        Assert.True(Function.IsLogin());
        Assert.True(Function.IsAdmin());
        Assert.Equal(1, Function._AccountId);

        // Switch back to Request A -> still anonymous (no shared mutable static leak)
        accessor.HttpContext = anonContext;
        Assert.False(Function.IsLogin());
        Assert.False(Function.IsAdmin());
    }

    // =========================================================================
    // 2. P0-09: PBKDF2 Password Hashing & Legacy MD5 Transparent Upgrade
    // =========================================================================
    [Fact]
    public void PasswordService_HashesWithPbkdf2_And_UpgradesLegacyMd5()
    {
        var pwdService = new PasswordService();
        var account = new TbAccount { AccountId = 5, Username = "member01" };

        // 1. Legacy 32-char MD5 verification triggers SuccessRehashNeeded + upgraded PBKDF2 hash
        string legacyMd5 = HashMD5.GetMD5("secret123");
        var resMd5 = pwdService.VerifyPassword(account, legacyMd5, "secret123", out string? upgradedHash);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, resMd5);
        Assert.False(string.IsNullOrWhiteSpace(upgradedHash));
        Assert.NotEqual(legacyMd5, upgradedHash);
        Assert.True(upgradedHash!.Length > 40);

        // 2. Verify upgraded PBKDF2 hash directly
        var resPbkdf2 = pwdService.VerifyPassword(account, upgradedHash, "secret123", out _);
        Assert.Equal(PasswordVerificationResult.Success, resPbkdf2);

        // 3. Wrong password fails
        var resWrong = pwdService.VerifyPassword(account, upgradedHash, "wrongpass", out _);
        Assert.Equal(PasswordVerificationResult.Failed, resWrong);
    }

    // =========================================================================
    // 3. P1-08 & P1-09: Segmented Cross-Boundary Pricing & VIP Discount
    // =========================================================================
    [Fact]
    public async Task PricingService_SplitsCrossBoundary_1630_To_1830_And_AppliesMemberDiscount()
    {
        using var ctx = CreateInMemoryContext();
        var pricing = new PricingService();
        var vipCourt = await ctx.TbCourts.FindAsync(1); // Base 120k + Surcharge 20k = 140k normal, 170k peak

        // 16:30 -> 18:30 (120 mins):
        // - 16:30-17:00 (30m normal @ 140k/h) = 70,000đ
        // - 17:00-18:30 (90m peak @ 170k/h)   = 255,000đ
        // Gross = 325,000đ
        var quoteStandard = pricing.CalculateQuote(vipCourt, "16:30", 120, memberPaidSpend: 0);
        Assert.Equal(30, quoteStandard.NormalMinutes);
        Assert.Equal(90, quoteStandard.PeakMinutes);
        Assert.Equal(70000, quoteStandard.NormalSubtotal);
        Assert.Equal(255000, quoteStandard.PeakSubtotal);
        Assert.Equal(325000, quoteStandard.GrossCourtFee);
        Assert.Equal(0, quoteStandard.DiscountAmount);
        Assert.Equal(325000, quoteStandard.NetCourtFee);

        // Gold VIP Member (Paid spend >= 5,000,000đ -> 15% discount)
        // 15% of 325,000 = 48,750 -> rounded to nearest 1,000đ = 49,000đ -> Net = 276,000đ
        var quoteGold = pricing.CalculateQuote(vipCourt, "16:30", 120, memberPaidSpend: 5_500_000);
        Assert.Equal("Hội viên Vàng VIP", quoteGold.MemberTier);
        Assert.Equal(15, quoteGold.DiscountPercent);
        Assert.Equal(49000, quoteGold.DiscountAmount);
        Assert.Equal(276000, quoteGold.NetCourtFee);
    }

    // =========================================================================
    // 4. P0-08 & P1-11: Slot Conflict Detection (Bookings & Court Locks)
    // =========================================================================
    [Fact]
    public async Task BookingService_RejectsOverlappingBooking_And_MaintenanceLocks()
    {
        using var ctx = CreateInMemoryContext();
        var pricing = new PricingService();
        var qr = CreateQrService();
        var bookingSvc = new BookingService(ctx, pricing, qr);
        string futureDate = BookingService.GetVietnamNow().AddDays(2).ToString("yyyy-MM-dd");

        // 1. Create initial booking 17:00 - 18:30 (90m) on Court 1
        var first = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 1,
            CustomerName = "Nguyen Van A",
            CustomerPhone = "0901234567",
            Date = futureDate,
            Time = "17:00",
            Duration = 90
        }, currentAccountId: 2, currentRoleId: 2, isStaffOrAdmin: false);

        Assert.True(first.Success);
        Assert.Equal(0, first.Data!.PaidAmount); // P1-05: Unpaid on creation
        Assert.Equal("unpaid", first.Data.PaymentStatus);

        // 2. Attempt overlapping booking 18:00 - 19:00 on Court 1 -> Must fail with 409 Conflict
        var overlap = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 1,
            CustomerName = "Tran Van B",
            CustomerPhone = "0912345678",
            Date = futureDate,
            Time = "18:00",
            Duration = 60
        }, currentAccountId: 3, currentRoleId: 2, isStaffOrAdmin: false);

        Assert.False(overlap.Success);
        Assert.Equal(409, overlap.StatusCode);

        // 3. Add maintenance lock 19:00 - 20:00 on Court 1
        ctx.TbCourtLocks.Add(new TbCourtLock
        {
            LockCode = "LK-TEST01",
            CourtId = 1,
            LockDate = futureDate,
            StartTime = "19:00",
            Duration = 60,
            Reason = "Bảo trì mặt sân"
        });
        await ctx.SaveChangesAsync();

        // 4. Attempt booking during maintenance lock 19:00 - 20:00 -> Must fail with 409
        var lockConflict = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 1,
            CustomerName = "Le Van C",
            CustomerPhone = "0987654321",
            Date = futureDate,
            Time = "19:00",
            Duration = 60
        }, currentAccountId: 4, currentRoleId: 2, isStaffOrAdmin: false);

        Assert.False(lockConflict.Success);
        Assert.Equal(409, lockConflict.StatusCode);
    }

    // =========================================================================
    // 5. P1-10: POS Extend (+30m) Rejects When Next Slot Is Occupied
    // =========================================================================
    [Fact]
    public async Task ExtendBooking_FailsWhenNextSlotIsOccupied_And_SucceedsWhenFree()
    {
        using var ctx = CreateInMemoryContext();
        var bookingSvc = new BookingService(ctx, new PricingService(), CreateQrService());
        string todayIso = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");

        var b1 = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 1,
            CustomerName = "Ca 1",
            CustomerPhone = "0901111222",
            Date = todayIso,
            Time = "17:00",
            Duration = 60,
            AutoApprove = true
        }, 1, 1, isStaffOrAdmin: true);

        var b2 = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 1,
            CustomerName = "Ca 2",
            CustomerPhone = "0903333444",
            Date = todayIso,
            Time = "18:30",
            Duration = 60,
            AutoApprove = true
        }, 1, 1, isStaffOrAdmin: true);

        Assert.True(b1.Success);
        Assert.True(b2.Success);

        // Extend Ca 1 by +30m (17:00 -> 18:30) is free -> Succeeds
        var extOk = await bookingSvc.ExtendBookingAsync(b1.Data!.BookingCode, 30, "admin");
        Assert.True(extOk.Success);
        Assert.Equal(90, extOk.Data!.Duration);

        // Extend Ca 1 by another +30m (17:00 -> 19:00) collides with Ca 2 (18:30) -> Fails with 409
        var extFail = await bookingSvc.ExtendBookingAsync(b1.Data.BookingCode, 30, "admin");
        Assert.False(extFail.Success);
        Assert.Equal(409, extFail.StatusCode);
    }

    // =========================================================================
    // 6. P1-01 & P1-07: Signed QR Token Verification & Strict State Machine
    // =========================================================================
    [Fact]
    public async Task QrToken_DetectsTampering_And_ApprovedDoesNotAutoPlayWithoutCheckIn()
    {
        using var ctx = CreateInMemoryContext();
        var qr = CreateQrService();
        var bookingSvc = new BookingService(ctx, new PricingService(), qr);
        string todayIso = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");

        var created = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 1,
            CustomerName = "Pham D",
            CustomerPhone = "0905555666",
            Date = todayIso,
            Time = "21:00",
            Duration = 60,
            AutoApprove = true
        }, 1, 1, isStaffOrAdmin: true);

        Assert.True(created.Success);
        string code = created.Data!.BookingCode;
        string validToken = created.Data.CheckInToken!;

        // Reconcile status: approved booking must NOT auto-transition to 'playing' without check-in
        await bookingSvc.ReconcileBookingStatusesAsync();
        var afterReconcile = await ctx.TbBookings.FirstAsync(b => b.BookingCode == code);
        Assert.Equal("approved", afterReconcile.Status);

        // Tamper with token signature -> Must fail verification
        string tamperedToken = validToken[..^4] + "FFFF";
        var tamperRes = await bookingSvc.CheckInBookingAsync(tamperedToken, "staff", allowManualCode: false);
        Assert.False(tamperRes.Success);

        // Valid signed token -> Transitions 'approved' to 'playing'
        var checkInRes = await bookingSvc.CheckInBookingAsync(validToken, "staff", allowManualCode: false);
        Assert.True(checkInRes.Success);
        Assert.Equal("playing", checkInRes.Data!.Status);
        Assert.NotNull(checkInRes.Data.CheckedInAt);

        // Second check-in on already playing booking -> Rejected with 409
        var duplicateCheckIn = await bookingSvc.CheckInBookingAsync(validToken, "staff", allowManualCode: false);
        Assert.False(duplicateCheckIn.Success);
        Assert.Equal(409, duplicateCheckIn.StatusCode);
    }

    // =========================================================================
    // 7. P0-07 & P1-12: IDOR Protection on Cancel & 2-Hour Cutoff Policy
    // =========================================================================
    [Fact]
    public async Task CancelBooking_EnforcesOwnershipPhone_And_TwoHourCutoffForApproved()
    {
        using var ctx = CreateInMemoryContext();
        var bookingSvc = new BookingService(ctx, new PricingService(), CreateQrService());
        var now = BookingService.GetVietnamNow();
        string tomorrowIso = now.AddDays(1).ToString("yyyy-MM-dd");

        var created = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 1,
            CustomerName = "Khach Hang Chinh Chu",
            CustomerPhone = "0909999888",
            Date = tomorrowIso,
            Time = "17:00",
            Duration = 60,
            AutoApprove = true
        }, currentAccountId: 10, currentRoleId: 2, isStaffOrAdmin: true);

        string code = created.Data!.BookingCode;

        // 1. Attacker with wrong phone and different account tries to cancel -> 403 Forbidden
        var attackerRes = await bookingSvc.CancelBookingAsync(
            code, callerAccountId: 99, callerPhone: "0900000111", isStaffOrAdmin: false, "Hủy hộ", "attacker");
        Assert.False(attackerRes.Success);
        Assert.Equal(403, attackerRes.StatusCode);

        // 2. Owner tries to cancel when start time is > 2 hours away -> Succeeds
        var ownerRes = await bookingSvc.CancelBookingAsync(
            code, callerAccountId: 10, callerPhone: "0909999888", isStaffOrAdmin: false, "Bận việc đột xuất", "owner");
        Assert.True(ownerRes.Success);
        Assert.Equal("cancelled", ownerRes.Data!.Status);
    }

    // =========================================================================
    // 8. P0-10, P1-14, P1-15: Server-Authoritative POS Stock & Payment Ledger
    // =========================================================================
    [Fact]
    public async Task PosStockAndPayment_EnforcesStockLimits_And_RecordsPaymentTransactions()
    {
        using var ctx = CreateInMemoryContext();
        var bookingSvc = new BookingService(ctx, new PricingService(), CreateQrService());
        string todayIso = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");

        var created = await bookingSvc.CreateBookingAsync(new CreateBookingRequest
        {
            CourtId = 3, // Standard court 120k/h normal
            CustomerName = "Khach POS",
            CustomerPhone = "0908888777",
            Date = todayIso,
            Time = "08:00",
            Duration = 60,
            AutoApprove = true
        }, currentAccountId: 3, currentRoleId: 3, isStaffOrAdmin: true);

        string code = created.Data!.BookingCode;
        Assert.Equal(120000, created.Data.CourtFee);

        // 1. Attempt to add 999 items (exceeds stock of 15) -> Fails
        var overStock = await bookingSvc.AddPosItemToBookingAsync(code, productId: 10, deltaQty: 999, "admin");
        Assert.False(overStock.Success);

        // 2. Add 3 items (@ 20,000đ = 60,000đ) -> Succeeds & decrements stock to 12
        var addPos = await bookingSvc.AddPosItemToBookingAsync(code, productId: 10, deltaQty: 3, "admin");
        Assert.True(addPos.Success);
        Assert.Equal(60000, addPos.Data!.PosTotal);
        Assert.Equal(180000, addPos.Data.AmountDue);
        Assert.Equal(0, addPos.Data.PaidAmount);

        var prodAfter = await ctx.TbProducts.FindAsync(10);
        Assert.Equal(12, prodAfter!.UnitInStock);

        // 3. Record checkout payment -> PaidAmount becomes 180,000đ, status = completed
        var payRes = await bookingSvc.RecordBookingPaymentAsync(code, 0, "transfer", markCompleted: true, "Tất toán cuối ca", "admin");
        Assert.True(payRes.Success);
        Assert.Equal(180000, payRes.Data!.PaidAmount);
        Assert.Equal("paid", payRes.Data.PaymentStatus);
        Assert.Equal("completed", payRes.Data.Status);

        int txCount = await ctx.TbPaymentTransactions.CountAsync(t => t.BookingCode == code);
        Assert.Equal(1, txCount);
    }

    // =========================================================================
    // 9. P0-12 & P1-16: Icon Sanitization & Paid Spend Tier Verification
    // =========================================================================
    [Fact]
    public async Task IconSanitizer_BlocksXssPayload_And_MemberSpendCountsOnlyPaidAmounts()
    {
        // P0-12: Malicious icon string is replaced with safe fallback
        string maliciousIcon = "\"><img src=x onerror=alert(1)>";
        string sanitized = ApiDbController.FormatIconHtml(maliciousIcon);
        Assert.DoesNotContain("onerror", sanitized);
        Assert.Equal("<iconify-icon icon=\"mdi:cup-water\" width=\"26\"></iconify-icon>", sanitized);
        Assert.Equal("<iconify-icon icon=\"lucide:trophy\" width=\"26\"></iconify-icon>", ApiDbController.FormatIconHtml("lucide:trophy"));

        // P1-16: Unpaid pending bookings do NOT count toward VIP tier spend
        using var ctx = CreateInMemoryContext();
        var pricing = new PricingService();

        ctx.TbBookings.Add(new TbBooking
        {
            BookingCode = "PB-UNPAID",
            CourtId = 1,
            AccountId = 50,
            CustomerName = "Spammer",
            Phone = "0907777666",
            BookingDate = "2026-11-08",
            StartTime = "17:00",
            Duration = 180,
            CourtFee = 10_000_000,
            AmountDue = 10_000_000,
            PaidAmount = 0,
            TotalPaid = 0,
            Status = "pending"
        });
        await ctx.SaveChangesAsync();

        int verifiedSpend = await pricing.GetVerifiedCustomerPaidSpendAsync(ctx, 50, "0907777666");
        Assert.Equal(0, verifiedSpend);
    }
}
