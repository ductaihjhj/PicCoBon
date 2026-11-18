using System;
using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace PickleballClub.Tests;

public class UnitAndQrVerificationTests
{
    private sealed class FakeHostEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = Environments.Production;
        public string ApplicationName { get; set; } = "PickleballClub";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static QrTokenService CreateTestQrService(string? secret = "UnitTest-HMAC-SecretKey-2026-SpikePickleball-32PlusChars!", string envName = "Testing")
    {
        var dict = new Dictionary<string, string?>();
        if (secret != null)
        {
            dict["Security:QrSigningKey"] = secret;
        }
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
        return new QrTokenService(config, new FakeHostEnvironment { EnvironmentName = envName });
    }

    // =========================================================================
    // 1. P0-01 & P0-02: Per-Request Auth Isolation & Forged Cookie Rejection
    // =========================================================================
    [Fact]
    public void AuthIsolation_ForgedCookieIsIgnored_And_RequestsAreIsolated()
    {
        var accessor = new HttpContextAccessor();
        Function.Configure(accessor);

        var anonContext = new DefaultHttpContext();
        anonContext.Request.Headers["Cookie"] = "pb_admin_uid=1";
        accessor.HttpContext = anonContext;

        Assert.False(Function.IsLogin());
        Assert.False(Function.IsAdmin());
        Assert.Equal(0, Function._AccountId);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, "1"),
            new Claim(ClaimTypes.Name, "admin"),
            new Claim("FullName", "Ban Quản Trị"),
            new Claim(ClaimTypes.Role, "Admin"),
            new Claim("RoleId", "1"),
            new Claim("SecurityStamp", "stamp_abc")
        };
        var authContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(claims, "SpikeCookieAuth"))
        };
        accessor.HttpContext = authContext;

        Assert.True(Function.IsLogin());
        Assert.True(Function.IsAdmin());
        Assert.True(Function.IsStaffOrAdmin());
        Assert.Equal(1, Function._AccountId);

        accessor.HttpContext = anonContext;
        Assert.False(Function.IsLogin());
        Assert.False(Function.IsAdmin());
    }

    // =========================================================================
    // 2. P0-09: PBKDF2 Hashing, Legacy MD5 Upgrade & Production Secret Fail-Fast
    // =========================================================================
    [Fact]
    public void PasswordService_Pbkdf2AndLegacyMd5Upgrade_WorkCorrectly()
    {
        var pwdService = new PasswordService();
        var account = new TbAccount { AccountId = 99, Username = "athlete01" };

        string legacyMd5 = HashMD5.GetMD5("Pickle2026!");
        var legacyRes = pwdService.VerifyPassword(account, legacyMd5, "Pickle2026!", out string? upgradedHash);

        Assert.Equal(PasswordVerificationResult.SuccessRehashNeeded, legacyRes);
        Assert.False(string.IsNullOrEmpty(upgradedHash));
        Assert.True(upgradedHash!.Length > 50);
        Assert.NotEqual(legacyMd5, upgradedHash);

        var modernRes = pwdService.VerifyPassword(account, upgradedHash, "Pickle2026!", out _);
        Assert.Equal(PasswordVerificationResult.Success, modernRes);

        var wrongRes = pwdService.VerifyPassword(account, upgradedHash, "WrongPass", out _);
        Assert.Equal(PasswordVerificationResult.Failed, wrongRes);
    }

    [Fact]
    public void QrTokenService_ProductionMissingOrShortKey_FailsFast_DevAllowsEphemeral()
    {
        // Production without secret -> fail fast
        Assert.Throws<InvalidOperationException>(() =>
            CreateTestQrService(secret: null, envName: Environments.Production));

        // Production with < 32 char secret -> fail fast
        Assert.Throws<InvalidOperationException>(() =>
            CreateTestQrService(secret: "too-short-secret-123", envName: Environments.Production));

        // Production with known placeholder -> fail fast
        Assert.Throws<InvalidOperationException>(() =>
            CreateTestQrService(secret: "CHANGE_ME_IN_PRODUCTION_32_CHARS_MINIMUM", envName: Environments.Production));

        // Development without configured secret -> generates ephemeral 32-byte key safely
        var devQr = CreateTestQrService(secret: null, envName: Environments.Development);
        string tok = devQr.GenerateCheckInToken("PB-DEV001", "2026-11-15", DateTime.UtcNow.AddHours(2));
        Assert.True(devQr.TryValidateCheckInToken(tok, out string code, out _, out _));
        Assert.Equal("PB-DEV001", code);
    }

    // =========================================================================
    // 3. P1-01 & P0-07: Real ISO/IEC 18004 QR Generation + Independent ZXing Decode
    // =========================================================================
    [Fact]
    public void QrTokenService_GeneratePng_And_IndependentZXingDecode_ExactRoundtrip_TamperAndExpiryRejected()
    {
        var qrService = CreateTestQrService();
        string token = qrService.GenerateCheckInToken("PB-ZXING99", "2026-11-20", DateTime.UtcNow.AddHours(3));
        Assert.StartsWith("SPK1.", token);

        // Generate real PNG QR image using QRCoder
        byte[] pngBytes = qrService.GenerateQrPngBytes(token, pixelsPerModule: 8);
        Assert.NotNull(pngBytes);
        Assert.True(pngBytes.Length > 200);
        // PNG magic header: 89 50 4E 47
        Assert.Equal(0x89, pngBytes[0]);
        Assert.Equal(0x50, pngBytes[1]);
        Assert.Equal(0x4E, pngBytes[2]);
        Assert.Equal(0x47, pngBytes[3]);

        // Generate real SVG QR image using QRCoder
        string svg = qrService.GenerateQrSvg(token, pixelsPerModule: 6);
        Assert.Contains("<svg", svg, StringComparison.OrdinalIgnoreCase);

        // Independently decode PNG bytes via ZXing.Net + ImageSharp
        bool decodedOk = QrTokenService.TryDecodeQrImage(pngBytes, out string decodedText, out string decodeErr);
        Assert.True(decodedOk, $"ZXing failed to decode generated QR PNG: {decodeErr}");
        Assert.Equal(token, decodedText);

        // Validate HMAC signature on decoded payload
        Assert.True(qrService.TryValidateCheckInToken(decodedText, out string code, out string date, out _));
        Assert.Equal("PB-ZXING99", code);
        Assert.Equal("2026-11-20", date);

        // Tampered signature must fail
        string tampered = token[..^3] + (token.EndsWith("AAA", StringComparison.Ordinal) ? "BBB" : "AAA");
        Assert.False(qrService.TryValidateCheckInToken(tampered, out _, out _, out _));

        // Expired token must fail
        string expired = qrService.GenerateCheckInToken("PB-EXP001", "2026-11-01", DateTime.UtcNow.AddMinutes(-10));
        Assert.False(qrService.TryValidateCheckInToken(expired, out _, out _, out string expErr));
        Assert.Contains("hết thời hạn", expErr, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GuestAccessToken_Sha256ConstantTimeVerify_RejectsWrongOrEmpty()
    {
        var (rawToken, hash) = QrTokenService.GenerateGuestAccessToken();
        Assert.StartsWith("bk_", rawToken);
        Assert.Equal(64, hash.Length); // SHA-256 hex

        Assert.True(QrTokenService.VerifyGuestAccessToken(rawToken, hash));
        Assert.False(QrTokenService.VerifyGuestAccessToken(rawToken + "x", hash));
        Assert.False(QrTokenService.VerifyGuestAccessToken("", hash));
        Assert.False(QrTokenService.VerifyGuestAccessToken(null, hash));
    }

    // =========================================================================
    // 4. P0-12: Stored XSS Prevention in IconSanitizer
    // =========================================================================
    [Fact]
    public void IconSanitizer_BlocksXssPayloads_AllowsWhitelistedIcons()
    {
        string safe1 = IconSanitizer.FormatSafeIconHtmlString("lucide:trophy", 24);
        Assert.Equal("<iconify-icon icon='lucide:trophy' width='24'></iconify-icon>", safe1);

        string safe2 = IconSanitizer.FormatSafeIconHtmlString("<iconify-icon icon='mdi:tennis' width='20'></iconify-icon>", 22);
        Assert.Equal("<iconify-icon icon='mdi:tennis' width='22'></iconify-icon>", safe2);

        string xssImg = IconSanitizer.FormatSafeIconHtmlString("<img src=x onerror=alert(1)>", 24, "lucide:shield");
        Assert.DoesNotContain("<img", xssImg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("onerror", xssImg, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<iconify-icon icon='lucide:shield' width='24'></iconify-icon>", xssImg);

        string xssHandler = IconSanitizer.FormatSafeIconHtmlString("<iconify-icon icon='mdi:cup' onload='alert(1)'></iconify-icon>", 24, "lucide:shield");
        Assert.DoesNotContain("onload", xssHandler, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<iconify-icon icon='lucide:shield' width='24'></iconify-icon>", xssHandler);
    }

    // =========================================================================
    // 5. P1-08, P1-09, P1-16, P1-18: Segmented Peak Pricing & Verified Member Spend
    // =========================================================================
    [Fact]
    public async Task PricingService_SegmentedCrossBoundary_UnauthenticatedZeroDiscount_VerifiedAccountGetsTierDiscount()
    {
        var options = new DbContextOptionsBuilder<PickleballContext>()
            .UseInMemoryDatabase("PricingUnitDb_" + Guid.NewGuid().ToString("N"))
            .Options;
        await using var ctx = new PickleballContext(options);

        var vipCourt = new TbCourt
        {
            CourtId = 1,
            CourtName = "Sân VIP 01",
            BasePrice = 120_000,
            Surcharge = 20_000,
            IsActive = true
        };
        var settings = new ClubSettingsSnapshot
        {
            OpenTime = "06:00",
            CloseTime = "22:00",
            OpenMinutes = 360,
            CloseMinutes = 1320,
            PeakStart = "17:00",
            PeakStartMinutes = 1020,
            BaseCourtPrice = 120_000,
            PeakSurchargePerHour = 30_000,
            PricingVersion = "vtest1"
        };

        var pricing = new PricingService();

        // 16:30 -> 18:00 (90 mins):
        // - Standard [16:30..17:00] = 30 mins @ (120k + 20k = 140k/hr) = 70,000đ
        // - Peak     [17:00..18:00] = 60 mins @ (120k + 20k + 30k = 170k/hr) = 170,000đ
        // Total before discount = 240,000đ
        var guestEvenWithHighSpendQuote = pricing.CalculateQuote(
            vipCourt, "16:30", 90, posTotal: 0, verifiedPaidSpend: 10_000_000, explicitMemberTier: "VIP Kim Cương", settings: settings, isAuthenticatedMember: false);

        Assert.Equal(30, guestEvenWithHighSpendQuote.StandardMinutes);
        Assert.Equal(60, guestEvenWithHighSpendQuote.PeakMinutes);
        Assert.Equal(240_000, guestEvenWithHighSpendQuote.CourtFeeBeforeDiscount);
        // P1-18: Unauthenticated caller NEVER receives member discount!
        Assert.Equal(0, guestEvenWithHighSpendQuote.DiscountPercent);
        Assert.Equal(240_000, guestEvenWithHighSpendQuote.AmountDue);

        // Verified Account #42: unpaid booking gives 0 spend; paid ledger minus refund gives net spend
        ctx.TbAccounts.Add(new TbAccount { AccountId = 42, Username = "goldmember", Phone = "0912345678", IsActive = true });
        ctx.TbBookings.Add(new TbBooking
        {
            BookingId = 100,
            BookingCode = "PB-UNPAID1",
            CourtId = 1,
            AccountId = 42,
            BookingDate = "2026-11-05",
            StartTime = "18:00",
            Duration = 120,
            CourtFee = 5_000_000,
            AmountDue = 5_000_000,
            PaidAmount = 0,
            Status = "completed"
        });
        await ctx.SaveChangesAsync();

        // Unpaid booking must yield 0 verified spend!
        Assert.Equal(0, await pricing.GetVerifiedAccountNetPaidSpendAsync(ctx, 42));

        // Record 2,500,000 payment and 300,000 refund -> Net = 2,200,000 (>= 2,000,000 -> Hạng Vàng 10%)
        ctx.TbBookings.Add(new TbBooking
        {
            BookingId = 101,
            BookingCode = "PB-PAID101",
            CourtId = 1,
            AccountId = 42,
            BookingDate = "2026-11-06",
            StartTime = "18:00",
            Duration = 60,
            AmountDue = 2_500_000,
            PaidAmount = 2_200_000,
            Status = "completed"
        });
        ctx.TbPaymentTransactions.AddRange(
            new TbPaymentTransaction
            {
                BookingId = 101,
                BookingCode = "PB-PAID101",
                Amount = 2_500_000,
                TransactionType = "payment",
                PaymentMethod = "cash",
                Status = "completed",
                CreatedAt = DateTime.UtcNow
            },
            new TbPaymentTransaction
            {
                BookingId = 101,
                BookingCode = "PB-PAID101",
                Amount = 300_000,
                TransactionType = "refund",
                PaymentMethod = "cash",
                Status = "completed",
                CreatedAt = DateTime.UtcNow
            });
        await ctx.SaveChangesAsync();

        int verifiedNetSpend = await pricing.GetVerifiedAccountNetPaidSpendAsync(ctx, 42);
        Assert.Equal(2_200_000, verifiedNetSpend);

        // 2,200,000đ net spend -> Hạng Bạc (5% discount on 240,000đ court fee = 12,000đ)
        var authSilverQuote = pricing.CalculateQuote(
            vipCourt, "16:30", 90, posTotal: 40_000, verifiedPaidSpend: verifiedNetSpend, explicitMemberTier: null, settings: settings, isAuthenticatedMember: true);

        Assert.Equal(5, authSilverQuote.DiscountPercent);
        Assert.Equal(12_000, authSilverQuote.DiscountAmount); // 5% of 240k court fee (not POS)
        Assert.Equal(268_000, authSilverQuote.AmountDue);     // (240k - 12k) + 40k POS

        // 5,200,000đ net spend -> Hạng Vàng VIP (15% discount on 240,000đ court fee = 36,000đ)
        var authVipQuote = pricing.CalculateQuote(
            vipCourt, "16:30", 90, posTotal: 40_000, verifiedPaidSpend: 5_200_000, explicitMemberTier: null, settings: settings, isAuthenticatedMember: true);

        Assert.Equal(15, authVipQuote.DiscountPercent);
        Assert.Equal(36_000, authVipQuote.DiscountAmount);
        Assert.Equal(244_000, authVipQuote.AmountDue);
    }
}
