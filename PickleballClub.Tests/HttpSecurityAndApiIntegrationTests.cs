using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using Xunit;

namespace PickleballClub.Tests;

public sealed class SpikeWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public string DatabaseName { get; } = "PickleballClub_HttpTest_" + Guid.NewGuid().ToString("N")[..8];
    public string ConnectionString =>
        $"Server=.\\SQLEXPRESS;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public int AdminAccountId { get; private set; }
    public int StaffAccountId { get; private set; }
    public int CustomerAAccountId { get; private set; }
    public int CustomerBAccountId { get; private set; }
    public int LockTargetAccountId { get; private set; }
    public int Court1Id { get; private set; }
    public int Court2Id { get; private set; }
    public int Product1Id { get; private set; }

    public SpikeWebAppFactory()
    {
        Environment.SetEnvironmentVariable("ASPNETCORE_ENVIRONMENT", "Testing");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.ConfigureAppConfiguration((_, cfg) =>
        {
            cfg.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = ConnectionString,
                ["Security:QrSigningKey"] = "HttpIntegration-HMAC-SecretKey-2026-SpikePickleball-32CharsPlus!",
                ["BackgroundWorkers:Enabled"] = "false",
                ["RateLimiting:AuthPermitLimit"] = "500",
                ["RateLimiting:CheckInPermitLimit"] = "500",
                ["RateLimiting:BookingCreatePermitLimit"] = "500"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PickleballContext>>();
            services.AddDbContext<PickleballContext>(opts => opts.UseSqlServer(ConnectionString));
        });
    }

    public async Task InitializeAsync()
    {
        // Trigger host build & migrate isolated SQL database
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pwd = scope.ServiceProvider.GetRequiredService<PasswordService>();

        await db.Database.MigrateAsync();

        var admin = new TbAccount
        {
            Username = "admin_test",
            FullName = "Admin Kiểm Thử",
            Phone = "0901000001",
            Email = "admin@spike.test",
            RoleId = 1,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        admin.Password = pwd.HashPassword(admin, "AdminPass!2026");

        var staff = new TbAccount
        {
            Username = "staff_test",
            FullName = "Thu Ngân Kiểm Thử",
            Phone = "0901000003",
            Email = "staff@spike.test",
            RoleId = 3,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        staff.Password = pwd.HashPassword(staff, "StaffPass!2026");

        var userA = new TbAccount
        {
            Username = "member_a",
            FullName = "Hội Viên Alpha",
            Phone = "0902000001",
            Email = "alpha@spike.test",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        userA.Password = pwd.HashPassword(userA, "AlphaPass!2026");

        var userB = new TbAccount
        {
            Username = "member_b",
            FullName = "Hội Viên Beta",
            Phone = "0902000002",
            Email = "beta@spike.test",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        userB.Password = pwd.HashPassword(userB, "BetaPass!2026");

        var lockTarget = new TbAccount
        {
            Username = "member_lock",
            FullName = "Hội Viên Thu Hồi",
            Phone = "0902000099",
            Email = "lock@spike.test",
            RoleId = 2,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        lockTarget.Password = pwd.HashPassword(lockTarget, "LockPass!2026");

        db.TbAccounts.AddRange(admin, staff, userA, userB, lockTarget);

        var c1 = new TbCourt
        {
            CourtName = "Sân VIP 01 (Arena A)",
            CourtType = "Indoor",
            TypeLabel = "Trong nhà VIP",
            Surface = "Cushion 8 Lớp",
            BasePrice = 120_000,
            Surcharge = 20_000,
            IsActive = true
        };
        var c2 = new TbCourt
        {
            CourtName = "Sân Ngoài Trời 02",
            CourtType = "Outdoor",
            TypeLabel = "Ngoài trời",
            Surface = "Hard Court",
            BasePrice = 120_000,
            Surcharge = 0,
            IsActive = true
        };
        db.TbCourts.AddRange(c1, c2);

        var cat = new TbProductCategory { Title = "Đồ Uống Thể Thao", Alias = "do-uong", IsActive = true };
        db.TbProductCategories.Add(cat);
        await db.SaveChangesAsync();

        var prod1 = new TbProduct
        {
            Title = "Nước Điện Giải Pocari",
            CategoryProductId = cat.CategoryProductId,
            Price = 25_000,
            UnitInStock = 20,
            Quantity = 20,
            IsActive = true,
            Icon = "lucide:cup-soda"
        };
        // Seed a polluted "Slot" placeholder inactive/filtered test row to ensure /api/db/state strips it (P2-04)
        var pollutedSlot = new TbProduct
        {
            Title = "Slot",
            CategoryProductId = cat.CategoryProductId,
            Price = 1000,
            UnitInStock = 1,
            Quantity = 1,
            IsActive = true,
            Icon = "<img src=x onerror=alert(1)>"
        };
        db.TbProducts.AddRange(prod1, pollutedSlot);
        await db.SaveChangesAsync();

        AdminAccountId = admin.AccountId;
        StaffAccountId = staff.AccountId;
        CustomerAAccountId = userA.AccountId;
        CustomerBAccountId = userB.AccountId;
        LockTargetAccountId = lockTarget.AccountId;
        Court1Id = c1.CourtId;
        Court2Id = c2.CourtId;
        Product1Id = prod1.ProductId;
    }

    public new async Task DisposeAsync()
    {
        await base.DisposeAsync();
        SqlConnection.ClearAllPools();
        await using var conn = new SqlConnection(MasterConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = $@"
IF DB_ID(N'{DatabaseName}') IS NOT NULL
BEGIN
    ALTER DATABASE [{DatabaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE;
    DROP DATABASE [{DatabaseName}];
END";
        await cmd.ExecuteNonQueryAsync();
    }
}

public class HttpSecurityAndApiIntegrationTests : IClassFixture<SpikeWebAppFactory>
{
    private readonly SpikeWebAppFactory _factory;

    public HttpSecurityAndApiIntegrationTests(SpikeWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateCookieClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/security/csrf");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(HttpClient client, string url, object payload, string? explicitCsrf = null)
    {
        string csrf = explicitCsrf ?? await FetchCsrfTokenAsync(client);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("RequestVerificationToken", csrf);
        return await client.SendAsync(req);
    }

    private static async Task<HttpResponseMessage> PutJsonWithCsrfAsync(HttpClient client, string url, object payload)
    {
        string csrf = await FetchCsrfTokenAsync(client);
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("RequestVerificationToken", csrf);
        return await client.SendAsync(req);
    }

    private async Task<HttpClient> LoginSessionAsync(string identifier, string password)
    {
        var client = CreateCookieClient();
        var resp = await PostJsonWithCsrfAsync(client, "/api/auth/login", new { identifier, password });
        Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        return client;
    }

    // =========================================================================
    // 1. P0-05, P2-08, P2-09: /api/db/sync Removed, /health Healthy & Security Headers
    // =========================================================================
    [Fact]
    public async Task RemovedDbSyncReturns404_HealthReturns200_And_SecurityHeadersPresent()
    {
        using var client = CreateCookieClient();

        // /api/db/sync MUST NOT exist
        var syncResp = await PostJsonWithCsrfAsync(client, "/api/db/sync", new { bookings = Array.Empty<object>() });
        Assert.True(syncResp.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.MethodNotAllowed);

        // /health returns 200 without leaking connection strings
        var healthResp = await client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, healthResp.StatusCode);
        string healthJson = await healthResp.Content.ReadAsStringAsync();
        Assert.Contains("Healthy", healthJson);
        Assert.DoesNotContain("SQLEXPRESS", healthJson, StringComparison.OrdinalIgnoreCase);

        // Security headers
        Assert.Equal("nosniff", healthResp.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("SAMEORIGIN", healthResp.Headers.GetValues("X-Frame-Options").Single());
    }

    // =========================================================================
    // 2. P1-17: Anti-CSRF Enforcement Across JSON API Endpoints
    // =========================================================================
    [Fact]
    public async Task MutatingApi_WithoutOrForgedCsrfReturns400_WithValidCsrfSucceeds()
    {
        using var client = CreateCookieClient();
        string tomorrow = BookingService.GetVietnamNow().AddDays(2).ToString("yyyy-MM-dd");

        // Missing CSRF token -> 400 Bad Request
        var noCsrfResp = await client.PostAsJsonAsync("/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "CSRF Test",
            phone = "0911223344",
            bookingDate = tomorrow,
            startTime = "09:00",
            duration = 60
        });
        Assert.Equal(HttpStatusCode.BadRequest, noCsrfResp.StatusCode);

        // Forged CSRF header -> 400 Bad Request
        var forgedResp = await PostJsonWithCsrfAsync(client, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "CSRF Forged",
            phone = "0911223344",
            bookingDate = tomorrow,
            startTime = "09:00",
            duration = 60
        }, explicitCsrf: "forged_invalid_csrf_token_12345");
        Assert.Equal(HttpStatusCode.BadRequest, forgedResp.StatusCode);

        // Valid CSRF token -> 200 OK
        var validResp = await PostJsonWithCsrfAsync(client, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "CSRF Valid Guest",
            phone = "0911223344",
            bookingDate = tomorrow,
            startTime = "09:00",
            duration = 60
        });
        Assert.Equal(HttpStatusCode.OK, validResp.StatusCode);
    }

    // =========================================================================
    // 3. P0-06 & P2-04: Anonymous /api/db/state Strips All PII, Filters "Slot" & Supports ETag 304
    // =========================================================================
    [Fact]
    public async Task AnonymousState_StripsAllPii_FiltersSlotPlaceholder_And_SupportsETag304()
    {
        using var userAClient = await LoginSessionAsync("member_a", "AlphaPass!2026");
        string targetDate = BookingService.GetVietnamNow().AddDays(3).ToString("yyyy-MM-dd");

        var createResp = await PostJsonWithCsrfAsync(userAClient, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Bí Mật Alpha PII",
            phone = "0902000001",
            bookingDate = targetDate,
            startTime = "10:00",
            duration = 60
        });
        Assert.Equal(HttpStatusCode.OK, createResp.StatusCode);

        // Anonymous caller inspects /api/db/state
        using var anonClient = CreateCookieClient();
        var stateResp = await anonClient.GetAsync("/api/db/state");
        Assert.Equal(HttpStatusCode.OK, stateResp.StatusCode);
        Assert.NotNull(stateResp.Headers.ETag);

        string rawBody = await stateResp.Content.ReadAsStringAsync();
        // Zero PII leaked to anonymous user!
        Assert.DoesNotContain("Bí Mật Alpha PII", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("0902000001", rawBody, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SPK1.", rawBody, StringComparison.Ordinal);

        using var doc = JsonDocument.Parse(rawBody);
        var bookings = doc.RootElement.GetProperty("bookings").EnumerateArray().ToList();
        var busyItem = bookings.FirstOrDefault(b =>
            b.GetProperty("courtId").GetInt32() == _factory.Court1Id &&
            b.GetProperty("date").GetString() == targetDate &&
            b.GetProperty("time").GetString() == "10:00");
        Assert.Equal(JsonValueKind.Object, busyItem.ValueKind);
        Assert.Equal("occupied", busyItem.GetProperty("status").GetString());
        Assert.True(busyItem.GetProperty("isBusySlot").GetBoolean());
        Assert.False(busyItem.TryGetProperty("phone", out _));
        Assert.False(busyItem.TryGetProperty("name", out _));

        // Polluted "Slot" product row must be stripped from public menu (P2-04)
        var menuItems = doc.RootElement.GetProperty("menu").EnumerateArray().ToList();
        Assert.DoesNotContain(menuItems, m => string.Equals(m.GetProperty("name").GetString(), "Slot", StringComparison.OrdinalIgnoreCase));

        // Conditional request with If-None-Match -> 304 Not Modified!
        using var condReq = new HttpRequestMessage(HttpMethod.Get, "/api/db/state");
        condReq.Headers.IfNoneMatch.Add(stateResp.Headers.ETag!);
        var notModifiedResp = await anonClient.SendAsync(condReq);
        Assert.Equal(HttpStatusCode.NotModified, notModifiedResp.StatusCode);
    }

    // =========================================================================
    // 4. P0-02, P0-03, P0-04, P1-08, P1-09, P2-05: RBAC, Forced Customer Registration, CMS Pricing & Live Session Revocation
    // =========================================================================
    [Fact]
    public async Task Rbac_RegisterForcesCustomer_StaffBlockedFromAdminEndpoints_AdminUpdatesCmsPricing_And_LiveLockRevokesCookie()
    {
        // A. Public registration attempting privilege escalation -> strictly Customer ("user")
        using var regClient = CreateCookieClient();
        var regResp = await PostJsonWithCsrfAsync(regClient, "/api/auth/register", new
        {
            name = "Tự Đăng Ký Escalate",
            phone = "0944556677",
            email = "escalate@spike.test",
            password = "StrongUser!123",
            roleId = 1,
            role = "admin"
        });
        Assert.Equal(HttpStatusCode.OK, regResp.StatusCode);
        using (var regDoc = JsonDocument.Parse(await regResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("user", regDoc.RootElement.GetProperty("user").GetProperty("role").GetString());
        }

        // Registered Customer blocked from /api/admin/reports/summary (403)
        var custReportResp = await regClient.GetAsync("/api/admin/reports/summary");
        Assert.Equal(HttpStatusCode.Forbidden, custReportResp.StatusCode);

        // B. Staff can read operational reports (200), but CANNOT mutate Admin-only CMS (403) or view /Admin/Accounts
        using var staffClient = await LoginSessionAsync("staff_test", "StaffPass!2026");
        var staffReportResp = await staffClient.GetAsync("/api/admin/reports/summary");
        Assert.Equal(HttpStatusCode.OK, staffReportResp.StatusCode);

        var staffCmsResp = await PutJsonWithCsrfAsync(staffClient, "/api/admin/cms", new { courtPrice = "150000" });
        Assert.Equal(HttpStatusCode.Forbidden, staffCmsResp.StatusCode);

        var staffAccPage = await staffClient.GetAsync("/Admin/Accounts");
        Assert.Equal(HttpStatusCode.Redirect, staffAccPage.StatusCode); // Redirected to AccessDenied / Login

        // C. Admin updates CMS peakSurcharge -> live /api/bookings/quote updates PricingVersion & AmountDue!
        using var adminClient = await LoginSessionAsync("admin_test", "AdminPass!2026");
        var beforeQuoteResp = await adminClient.GetAsync($"/api/bookings/quote?courtId={_factory.Court2Id}&startTime=18:00&duration=60");
        beforeQuoteResp.EnsureSuccessStatusCode();
        using var beforeDoc = JsonDocument.Parse(await beforeQuoteResp.Content.ReadAsStringAsync());
        string oldVersion = beforeDoc.RootElement.GetProperty("quote").GetProperty("pricingVersion").GetString()!;
        int oldAmount = beforeDoc.RootElement.GetProperty("quote").GetProperty("amountDue").GetInt32();

        var cmsResp = await PutJsonWithCsrfAsync(adminClient, "/api/admin/cms", new
        {
            peakSurcharge = "45000",
            openTime = "06:00",
            closeTime = "22:00"
        });
        Assert.Equal(HttpStatusCode.OK, cmsResp.StatusCode);

        var afterQuoteResp = await adminClient.GetAsync($"/api/bookings/quote?courtId={_factory.Court2Id}&startTime=18:00&duration=60");
        afterQuoteResp.EnsureSuccessStatusCode();
        using var afterDoc = JsonDocument.Parse(await afterQuoteResp.Content.ReadAsStringAsync());
        string newVersion = afterDoc.RootElement.GetProperty("quote").GetProperty("pricingVersion").GetString()!;
        int newAmount = afterDoc.RootElement.GetProperty("quote").GetProperty("amountDue").GetInt32();

        Assert.NotEqual(oldVersion, newVersion);
        Assert.Equal(oldAmount + 15_000, newAmount); // Surcharge increased from 30k -> 45k (+15k)

        // Restore peakSurcharge back to 30000 for deterministic subsequent tests
        await PutJsonWithCsrfAsync(adminClient, "/api/admin/cms", new { peakSurcharge = "30000" });

        // D. Live session revocation when Admin locks an active user (P0-02 & P2-05)
        using var targetClient = await LoginSessionAsync("member_lock", "LockPass!2026");
        Assert.Equal(HttpStatusCode.OK, (await targetClient.GetAsync("/api/bookings/me")).StatusCode);

        // Fetch form anti-forgery token from /Admin/Accounts HTML or API CSRF and lock the user via DB/Controller
        string adminCsrf = await FetchCsrfTokenAsync(adminClient);
        using var lockReq = new HttpRequestMessage(HttpMethod.Post, $"/Admin/Accounts/ToggleActive/{_factory.LockTargetAccountId}")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>())
        };
        lockReq.Headers.Add("RequestVerificationToken", adminCsrf);
        var lockResp = await adminClient.SendAsync(lockReq);
        Assert.Equal(HttpStatusCode.Redirect, lockResp.StatusCode);

        // Target user's previously valid cookie MUST immediately fail with 401 Unauthorized!
        var afterLockMe = await targetClient.GetAsync("/api/bookings/me");
        Assert.Equal(HttpStatusCode.Unauthorized, afterLockMe.StatusCode);
    }

    // =========================================================================
    // 5. P0-07, P1-12, P1-14: Anti-IDOR Across Lookup, QR Image, Cancel & Court-Side Order Attachment
    // =========================================================================
    [Fact]
    public async Task Idor_UserBCannotLookupQrCancelOrAttachOrderToUserABooking_GuestSecretTokenWorks_PhoneAloneRejected()
    {
        using var userAClient = await LoginSessionAsync("member_a", "AlphaPass!2026");
        using var userBClient = await LoginSessionAsync("member_b", "BetaPass!2026");
        using var guestClient = CreateCookieClient();

        string futureDate = BookingService.GetVietnamNow().AddDays(4).ToString("yyyy-MM-dd");

        // User A creates a member booking
        var userABkResp = await PostJsonWithCsrfAsync(userAClient, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Hội Viên Alpha",
            phone = "0902000001",
            bookingDate = futureDate,
            startTime = "14:00",
            duration = 60
        });
        Assert.Equal(HttpStatusCode.OK, userABkResp.StatusCode);
        using var userADoc = JsonDocument.Parse(await userABkResp.Content.ReadAsStringAsync());
        string userABookingCode = userADoc.RootElement.GetProperty("booking").GetProperty("id").GetString()!;

        // User B CANNOT view User A's QR PNG (403), CANNOT cancel User A's booking (403), CANNOT attach Pro Shop order (403)
        Assert.Equal(HttpStatusCode.Forbidden, (await userBClient.GetAsync($"/api/bookings/{userABookingCode}/qr.png")).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await PostJsonWithCsrfAsync(userBClient, $"/api/bookings/{userABookingCode}/cancel", new { reason = "Hack" })).StatusCode);

        var hijackOrderResp = await PostJsonWithCsrfAsync(userBClient, "/api/orders/create", new
        {
            customerName = "Beta Hijack",
            phone = "0902000002",
            bookingCode = userABookingCode,
            items = new[] { new { productId = _factory.Product1Id, qty = 1 } }
        });
        Assert.Equal(HttpStatusCode.Forbidden, hijackOrderResp.StatusCode);

        // User A CAN view own QR PNG (200) and attach order when booking is approved
        Assert.Equal(HttpStatusCode.OK, (await userAClient.GetAsync($"/api/bookings/{userABookingCode}/qr.png")).StatusCode);

        // Guest creates a booking -> receives one-time guestAccessToken (bk_...)
        var guestBkResp = await PostJsonWithCsrfAsync(guestClient, "/api/bookings", new
        {
            courtId = _factory.Court2Id,
            customerName = "Khách Vãng Lai VIP",
            phone = "0988123987",
            bookingDate = futureDate,
            startTime = "15:00",
            duration = 60
        });
        Assert.Equal(HttpStatusCode.OK, guestBkResp.StatusCode);
        using var guestDoc = JsonDocument.Parse(await guestBkResp.Content.ReadAsStringAsync());
        string guestCode = guestDoc.RootElement.GetProperty("booking").GetProperty("id").GetString()!;
        string guestSecret = guestDoc.RootElement.GetProperty("guestAccessToken").GetString()!;
        Assert.StartsWith("bk_", guestSecret);

        // Anonymous phone-only lookup MUST fail (never leak bookings by phone!)
        var phoneOnlyLookup = await guestClient.GetAsync("/api/bookings/lookup?phone=0988123987");
        Assert.NotEqual(HttpStatusCode.OK, phoneOnlyLookup.StatusCode);

        // Code without accessToken MUST fail (403/400)
        var codeOnlyLookup = await guestClient.GetAsync($"/api/bookings/lookup?code={guestCode}");
        Assert.NotEqual(HttpStatusCode.OK, codeOnlyLookup.StatusCode);

        // Code + valid guestAccessToken -> 200 OK!
        var validGuestLookup = await guestClient.GetAsync($"/api/bookings/lookup?code={guestCode}&accessToken={Uri.EscapeDataString(guestSecret)}");
        Assert.Equal(HttpStatusCode.OK, validGuestLookup.StatusCode);

        // Seed an active TODAY guest booking to verify court-side Pro Shop attachment requires valid bookingAccessToken
        string todayGuestCode = "PB-GSTORD1";
        var (todayGuestSecret, todayGuestHash) = QrTokenService.GenerateGuestAccessToken();
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var vnNow = BookingService.GetVietnamNow();
            int nowMins = vnNow.Hour * 60 + vnNow.Minute;
            int slotStartMins = (nowMins / 30) * 30;
            db.TbBookings.Add(new TbBooking
            {
                BookingCode = todayGuestCode,
                CourtId = _factory.Court2Id,
                CustomerName = "Khách Đang Chơi Hôm Nay",
                Phone = "0988123987",
                BookingDate = vnNow.ToString("yyyy-MM-dd"),
                StartTime = PricingService.MinutesToTime(slotStartMins),
                Duration = 60,
                CourtFee = 120_000,
                AmountDue = 120_000,
                PaidAmount = 0,
                PaymentStatus = "unpaid",
                Status = "approved",
                AccessTokenHash = todayGuestHash,
                CreatedDate = vnNow
            });
            await db.SaveChangesAsync();
        }

        // Guest WITHOUT bookingAccessToken trying to attach order by self-declared phone -> 403 Forbidden!
        var guestOrderWithoutToken = await PostJsonWithCsrfAsync(guestClient, "/api/orders/create", new
        {
            customerName = "Khách Đang Chơi Hôm Nay",
            phone = "0988123987",
            bookingCode = todayGuestCode,
            items = new[] { new { productId = _factory.Product1Id, qty = 1 } }
        });
        Assert.Equal(HttpStatusCode.Forbidden, guestOrderWithoutToken.StatusCode);

        // Guest WITH valid bookingAccessToken -> 200 OK!
        var guestOrderWithToken = await PostJsonWithCsrfAsync(guestClient, "/api/orders/create", new
        {
            customerName = "Khách Đang Chơi Hôm Nay",
            phone = "0988123987",
            bookingCode = todayGuestCode,
            bookingAccessToken = todayGuestSecret,
            items = new[] { new { productId = _factory.Product1Id, qty = 2 } }
        });
        Assert.Equal(HttpStatusCode.OK, guestOrderWithToken.StatusCode);
    }

    // =========================================================================
    // 6. P1-01 & P1-07: End-to-End Real QR PNG Download -> Multipart Upload -> ZXing Decode Check-In
    // =========================================================================
    [Fact]
    public async Task EndToEndQrImageUploadCheckIn_PendingRejected409_ApprovedDecodedByZXing200_ReplayRejected409()
    {
        string code = "PB-E2EQR1";
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var qr = scope.ServiceProvider.GetRequiredService<QrTokenService>();

            var vnNow = BookingService.GetVietnamNow();
            string todayIso = vnNow.ToString("yyyy-MM-dd");
            int nowMins = vnNow.Hour * 60 + vnNow.Minute;
            int slotStartMins = (nowMins / 30) * 30;
            string slotStart = PricingService.MinutesToTime(slotStartMins);

            string token = qr.GenerateCheckInToken(code, todayIso, DateTime.UtcNow.AddHours(6));
            db.TbBookings.Add(new TbBooking
            {
                BookingCode = code,
                CourtId = _factory.Court1Id,
                AccountId = _factory.CustomerAAccountId,
                CustomerName = "VĐV Quét Ảnh QR",
                Phone = "0902000001",
                BookingDate = todayIso,
                BookingDateValue = DateOnly.FromDateTime(vnNow),
                StartTime = slotStart,
                StartTimeValue = new TimeOnly(slotStartMins / 60, slotStartMins % 60),
                Duration = 60,
                Players = 4,
                CourtFee = 140_000,
                AmountDue = 140_000,
                AmountDueDec = 140_000m,
                PaidAmount = 0,
                PaidAmountDec = 0m,
                PaymentStatus = "unpaid",
                Status = "pending",
                CheckInToken = token,
                CreatedDate = vnNow
            });
            await db.SaveChangesAsync();
        }

        using var staffClient = await LoginSessionAsync("staff_test", "StaffPass!2026");

        // 1. Download real QR PNG generated by QRCoder from GET /api/bookings/{code}/qr.png
        var pngResp = await staffClient.GetAsync($"/api/bookings/{code}/qr.png");
        Assert.Equal(HttpStatusCode.OK, pngResp.StatusCode);
        Assert.Equal("image/png", pngResp.Content.Headers.ContentType?.MediaType);
        byte[] pngBytes = await pngResp.Content.ReadAsByteArrayAsync();
        Assert.True(pngBytes.Length > 200);

        async Task<HttpResponseMessage> UploadQrPngAsync()
        {
            string csrf = await FetchCsrfTokenAsync(staffClient);
            using var form = new MultipartFormDataContent();
            var imgContent = new ByteArrayContent(pngBytes);
            imgContent.Headers.ContentType = new MediaTypeHeaderValue("image/png");
            form.Add(imgContent, "file", $"{code}.png");
            form.Add(new StringContent("true"), "autoVerify");

            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/checkin/decode-qr") { Content = form };
            req.Headers.Add("RequestVerificationToken", csrf);
            return await staffClient.SendAsync(req);
        }

        // 2. Pending booking -> ZXing decodes QR PNG, server rejects check-in with 409 Conflict!
        var pendingScan = await UploadQrPngAsync();
        Assert.Equal(HttpStatusCode.Conflict, pendingScan.StatusCode);

        // 3. Approve booking -> 200 OK
        Assert.Equal(HttpStatusCode.OK, (await PostJsonWithCsrfAsync(staffClient, $"/api/admin/bookings/{code}/approve", new { })).StatusCode);

        // 4. Upload QR PNG again -> ZXing decodes QR PNG, validates HMAC, marks booking "playing" -> 200 OK!
        var approvedScan = await UploadQrPngAsync();
        Assert.Equal(HttpStatusCode.OK, approvedScan.StatusCode);
        using (var scanDoc = JsonDocument.Parse(await approvedScan.Content.ReadAsStringAsync()))
        {
            Assert.Equal("playing", scanDoc.RootElement.GetProperty("booking").GetProperty("status").GetString());
        }

        // 5. Replay same QR PNG -> rejected with 409 Conflict (already checked in / playing)!
        var replayScan = await UploadQrPngAsync();
        Assert.Equal(HttpStatusCode.Conflict, replayScan.StatusCode);
    }

    // =========================================================================
    // 7. P1-02 (BLOCKED_EXTERNAL) & P1-06: Honest 503 Forgot Password & Date-Accurate Ledger Reporting
    // =========================================================================
    [Fact]
    public async Task ForgotPasswordReturnsHonest503_And_ReportsSummaryFiltersStrictlyByPaymentCreatedAt()
    {
        using var anonClient = CreateCookieClient();
        var forgotResp = await PostJsonWithCsrfAsync(anonClient, "/api/auth/forgot-password", new { phone = "0902000001" });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, forgotResp.StatusCode);
        string forgotBody = await forgotResp.Content.ReadAsStringAsync();
        Assert.Contains("OTP_PROVIDER_NOT_CONFIGURED", forgotBody);

        // Seed a booking whose BookingDate is 2026-10-01, paid on 2026-10-05, plus a standalone order paid on 2026-10-05
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var bk = new TbBooking
            {
                BookingCode = "PB-RPT100",
                CourtId = _factory.Court2Id,
                AccountId = _factory.CustomerAAccountId,
                CustomerName = "Report Alpha",
                Phone = "0902000001",
                BookingDate = "2026-10-01",
                StartTime = "09:00",
                Duration = 60,
                CourtFee = 120_000,
                AmountDue = 120_000,
                AmountDueDec = 120_000m,
                PaidAmount = 120_000,
                PaidAmountDec = 120_000m,
                PaymentStatus = "paid",
                Status = "completed",
                CreatedDate = new DateTime(2026, 10, 1, 8, 0, 0)
            };
            var standaloneOrder = new TbOrder
            {
                Code = "ORD-RPT200",
                CustomerName = "Khách Lẻ ProShop",
                Phone = "0977888999",
                TotalAmount = 50_000,
                TotalAmountDec = 50_000m,
                PaidAmount = 50_000,
                PaidAmountDec = 50_000m,
                PaymentStatus = "paid",
                Status = "paid",
                OrderStatusId = 4,
                CreatedDate = new DateTime(2026, 10, 5, 14, 0, 0)
            };
            db.TbBookings.Add(bk);
            db.TbOrders.Add(standaloneOrder);
            await db.SaveChangesAsync();

            db.TbPaymentTransactions.AddRange(
                new TbPaymentTransaction
                {
                    BookingId = bk.BookingId,
                    BookingCode = bk.BookingCode,
                    Amount = 120_000,
                    AmountDec = 120_000m,
                    TransactionType = "payment",
                    PaymentMethod = "cash",
                    Status = "completed",
                    RecordedBy = "staff_test",
                    CreatedAt = new DateTime(2026, 10, 5, 15, 0, 0)
                },
                new TbPaymentTransaction
                {
                    OrderId = standaloneOrder.OrderId,
                    Amount = 50_000,
                    AmountDec = 50_000m,
                    TransactionType = "payment",
                    PaymentMethod = "transfer",
                    ReferenceCode = "FT261005111",
                    Status = "completed",
                    RecordedBy = "staff_test",
                    CreatedAt = new DateTime(2026, 10, 5, 15, 30, 0)
                });
            await db.SaveChangesAsync();
        }

        using var staffClient = await LoginSessionAsync("staff_test", "StaffPass!2026");

        // Querying BookingDate [2026-10-01..2026-10-02] -> 0 collected revenue because payment occurred on 2026-10-05!
        var oct1Resp = await staffClient.GetAsync("/api/admin/reports/summary?dateFrom=2026-10-01&dateTo=2026-10-02");
        Assert.Equal(HttpStatusCode.OK, oct1Resp.StatusCode);
        using (var oct1Doc = JsonDocument.Parse(await oct1Resp.Content.ReadAsStringAsync()))
        {
            Assert.Equal(0, oct1Doc.RootElement.GetProperty("totalCollectedRevenue").GetInt32());
        }

        // Querying Payment CreatedAt [2026-10-05..2026-10-05] -> 120,000 booking + 50,000 standalone shop = 170,000!
        var oct5Resp = await staffClient.GetAsync("/api/admin/reports/summary?dateFrom=2026-10-05&dateTo=2026-10-05");
        Assert.Equal(HttpStatusCode.OK, oct5Resp.StatusCode);
        using (var oct5Doc = JsonDocument.Parse(await oct5Resp.Content.ReadAsStringAsync()))
        {
            Assert.Equal(120_000, oct5Doc.RootElement.GetProperty("collectedBookingRevenue").GetInt32());
            Assert.Equal(50_000, oct5Doc.RootElement.GetProperty("standaloneRetailRevenue").GetInt32());
            Assert.Equal(170_000, oct5Doc.RootElement.GetProperty("totalCollectedRevenue").GetInt32());
        }
    }
}
