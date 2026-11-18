using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using PickleballClub.Models;
using PickleballClub.Services;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using Xunit;

namespace PickleballClub.Tests;

public class V4ReleaseBlockersAndQrTests : IClassFixture<SpikeWebAppFactory>
{
    private readonly SpikeWebAppFactory _factory;

    public V4ReleaseBlockersAndQrTests(SpikeWebAppFactory factory)
    {
        _factory = factory;
    }

    private sealed class TestHostEnv : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "PickleballClub";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private HttpClient CreateCookieClient()
    {
        return _factory.CreateClient(new Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactoryClientOptions
        {
            AllowAutoRedirect = false,
            HandleCookies = true
        });
    }

    private static async Task<string> FetchCsrfTokenAsync(HttpClient client)
    {
        var res = await client.GetAsync("/api/security/csrf");
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<string> LoginAndGetCsrfAsync(HttpClient client, string identifier, string password)
    {
        string preCsrf = await FetchCsrfTokenAsync(client);
        using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/login")
        {
            Content = JsonContent.Create(new { identifier, password })
        };
        req.Headers.Add("RequestVerificationToken", preCsrf);
        var res = await client.SendAsync(req);
        res.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("csrfToken").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(HttpClient client, string url, object body, string csrf)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("RequestVerificationToken", csrf);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task V4_A1_PendingHoldTtl_ReleasesExpiredSlotImmediately_RejectsExpiredApproval_And_EnforcesPerActorCap()
    {
        using var guest1 = CreateCookieClient();
        string csrf1 = await FetchCsrfTokenAsync(guest1);
        string futureDate = BookingService.GetVietnamNow().AddDays(4).ToString("yyyy-MM-dd");

        // 1. Create a pending booking on Court 1 at 09:00
        var createRes1 = await PostJsonWithCsrfAsync(guest1, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Khách Giữ Chỗ 1",
            customerPhone = "0911222333",
            date = futureDate,
            time = "09:00",
            duration = 60,
            idempotencyKey = "V4A1-HOLD-" + Guid.NewGuid().ToString("N")[..8]
        }, csrf1);

        Assert.Equal(HttpStatusCode.OK, createRes1.StatusCode);
        using var doc1 = JsonDocument.Parse(await createRes1.Content.ReadAsStringAsync());
        string bookingCode1 = doc1.RootElement.GetProperty("booking").GetProperty("id").GetString()!;
        string? holdExpStr = doc1.RootElement.GetProperty("booking").GetProperty("holdExpiresAtUtc").GetString();
        Assert.False(string.IsNullOrWhiteSpace(holdExpStr));

        // 2. Expire the pending hold in SQL Server by setting HoldExpiresAtUtc to 2 minutes ago
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var row = await db.TbBookings.FirstAsync(b => b.BookingCode == bookingCode1);
            Assert.NotNull(row.HoldExpiresAtUtc);
            row.HoldExpiresAtUtc = DateTime.UtcNow.AddMinutes(-2);
            await db.SaveChangesAsync();
        }

        // 3. Staff tries to approve the expired pending booking -> MUST fail with 409 PENDING_HOLD_EXPIRED
        using var staffClient = CreateCookieClient();
        string staffCsrf = await LoginAndGetCsrfAsync(staffClient, "staff_test", "StaffPass!2026");
        var approveExpiredRes = await PostJsonWithCsrfAsync(staffClient, $"/api/admin/bookings/{bookingCode1}/approve", new { }, staffCsrf);
        Assert.Equal(HttpStatusCode.Conflict, approveExpiredRes.StatusCode);
        using var approveDoc = JsonDocument.Parse(await approveExpiredRes.Content.ReadAsStringAsync());
        Assert.Equal("PENDING_HOLD_EXPIRED", approveDoc.RootElement.GetProperty("code").GetString());

        // 4. Another guest books the exact same slot immediately (without waiting for background cron) -> MUST succeed!
        using var guest2 = CreateCookieClient();
        string csrf2 = await FetchCsrfTokenAsync(guest2);
        var createRes2 = await PostJsonWithCsrfAsync(guest2, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Khách Đặt Mới Sau Khi Hết Giữ Chỗ",
            customerPhone = "0922333444",
            date = futureDate,
            time = "09:00",
            duration = 60,
            idempotencyKey = "V4A1-NEW-" + Guid.NewGuid().ToString("N")[..8]
        }, csrf2);
        Assert.Equal(HttpStatusCode.OK, createRes2.StatusCode);

        // 5. Anti-abuse cap: Member A creates up to MaxActivePendingPerActor (3) active pending bookings; 4th is rejected with 429
        using var memberClient = CreateCookieClient();
        string memberCsrf = await LoginAndGetCsrfAsync(memberClient, "member_a", "AlphaPass!2026");
        string capDate = BookingService.GetVietnamNow().AddDays(5).ToString("yyyy-MM-dd");
        string[] times = { "07:00", "08:00", "09:00", "10:00" };

        for (int i = 0; i < 3; i++)
        {
            var okRes = await PostJsonWithCsrfAsync(memberClient, "/api/bookings", new
            {
                courtId = _factory.Court2Id,
                customerName = "Hội Viên Alpha",
                customerPhone = "0902000001",
                date = capDate,
                time = times[i],
                duration = 60,
                idempotencyKey = $"V4A1-CAP-{i}-{Guid.NewGuid():N}"
            }, memberCsrf);
            Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        }

        var fourthRes = await PostJsonWithCsrfAsync(memberClient, "/api/bookings", new
        {
            courtId = _factory.Court2Id,
            customerName = "Hội Viên Alpha",
            customerPhone = "0902000001",
            date = capDate,
            time = times[3],
            duration = 60,
            idempotencyKey = $"V4A1-CAP-4-{Guid.NewGuid():N}"
        }, memberCsrf);

        Assert.Equal(HttpStatusCode.TooManyRequests, fourthRes.StatusCode);
        using var fourthDoc = JsonDocument.Parse(await fourthRes.Content.ReadAsStringAsync());
        Assert.Equal("PENDING_HOLD_LIMIT_EXCEEDED", fourthDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task V4_A2_BookingLifecycle_RemainsPlayingWhenOverdue_UntilStaffExplicitlyFinishesSession()
    {
        using var staffClient = CreateCookieClient();
        string staffCsrf = await LoginAndGetCsrfAsync(staffClient, "staff_test", "StaffPass!2026");

        string bookingCode;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var qr = scope.ServiceProvider.GetRequiredService<QrTokenService>();
            var bookingSvc = scope.ServiceProvider.GetRequiredService<BookingService>();
            bookingCode = "PB-OVD" + Random.Shared.Next(100, 999);
            string yesterday = BookingService.GetVietnamNow().AddDays(-1).ToString("yyyy-MM-dd");

            db.TbBookings.Add(new TbBooking
            {
                BookingCode = bookingCode,
                CourtId = _factory.Court1Id,
                CustomerName = "Khách Đang Thi Đấu Quá Giờ",
                Phone = "0933444555",
                BookingDate = yesterday,
                StartTime = "18:00",
                Duration = 60,
                Players = 4,
                Status = "playing",
                CheckedInAt = BookingService.GetVietnamNow().AddDays(-1).AddHours(-2),
                CourtFee = 140_000,
                AmountDue = 140_000,
                PaidAmount = 0,
                PaymentStatus = "unpaid",
                CheckInToken = qr.GenerateCheckInToken(bookingCode, yesterday, DateTime.UtcNow.AddDays(5)),
                CreatedDate = BookingService.GetVietnamNow().AddDays(-1)
            });
            await db.SaveChangesAsync();

            // Run server reconciliation worker -> playing booking MUST NOT auto-complete!
            await bookingSvc.ReconcileBookingLifecycleAsync(db);
            var afterReconcile = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode);
            Assert.Equal("playing", afterReconcile.Status);
        }

        // Verify /api/db/state returns status == "playing" for the overdue session
        var stateRes = await staffClient.GetAsync("/api/db/state");
        stateRes.EnsureSuccessStatusCode();
        using var stateDoc = JsonDocument.Parse(await stateRes.Content.ReadAsStringAsync());
        var playingRow = stateDoc.RootElement.GetProperty("bookings").EnumerateArray()
            .First(b => b.GetProperty("id").GetString() == bookingCode);
        Assert.Equal("playing", playingRow.GetProperty("status").GetString());

        // Verify shared.js never assigns b.status = 'completed' on the client
        string sharedJsPath = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "wwwroot", "js", "shared.js"));
        string sharedJs = await File.ReadAllTextAsync(sharedJsPath);
        Assert.DoesNotContain("b.status = 'completed'", sharedJs);
        Assert.Contains("Quá giờ — chờ kết ca", sharedJs);

        // Staff explicitly finishes the session -> now status becomes "completed" while PaidAmount stays 0
        var finishRes = await PostJsonWithCsrfAsync(staffClient, $"/api/admin/bookings/{bookingCode}/finish", new { }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, finishRes.StatusCode);
        using var finishDoc = JsonDocument.Parse(await finishRes.Content.ReadAsStringAsync());
        Assert.Equal("completed", finishDoc.RootElement.GetProperty("booking").GetProperty("status").GetString());
        Assert.Equal(0, finishDoc.RootElement.GetProperty("booking").GetProperty("paidAmount").GetInt32());
    }

    [Fact]
    public async Task V4_A3_StrictIdempotency_ReplaysIdenticalRequest_And_RejectsPayloadOrTargetMismatchWith409()
    {
        using var staffClient = CreateCookieClient();
        string staffCsrf = await LoginAndGetCsrfAsync(staffClient, "staff_test", "StaffPass!2026");

        string futureDate = BookingService.GetVietnamNow().AddDays(6).ToString("yyyy-MM-dd");
        string createKey = "IDEM-BK-" + Guid.NewGuid().ToString("N")[..8];

        // 1. Create booking with createKey
        var r1 = await PostJsonWithCsrfAsync(staffClient, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Khách Idempotency",
            customerPhone = "0944555666",
            date = futureDate,
            time = "14:00",
            duration = 60,
            autoApprove = true,
            idempotencyKey = createKey
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        using var d1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync());
        string bookingCode1 = d1.RootElement.GetProperty("booking").GetProperty("id").GetString()!;
        Assert.False(d1.RootElement.GetProperty("deduplicated").GetBoolean());

        // 2. Replay exact same CreateBooking request -> 200 OK, deduplicated = true, same booking code
        var r1Replay = await PostJsonWithCsrfAsync(staffClient, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Khách Idempotency",
            customerPhone = "0944555666",
            date = futureDate,
            time = "14:00",
            duration = 60,
            autoApprove = true,
            idempotencyKey = createKey
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, r1Replay.StatusCode);
        using var d1Replay = JsonDocument.Parse(await r1Replay.Content.ReadAsStringAsync());
        Assert.True(d1Replay.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(bookingCode1, d1Replay.RootElement.GetProperty("booking").GetProperty("id").GetString());

        // 3. Reuse createKey with DIFFERENT slot time -> MUST return 409 IDEMPOTENCY_KEY_CONFLICT
        var r1Conflict = await PostJsonWithCsrfAsync(staffClient, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Khách Idempotency",
            customerPhone = "0944555666",
            date = futureDate,
            time = "15:00",
            duration = 60,
            autoApprove = true,
            idempotencyKey = createKey
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.Conflict, r1Conflict.StatusCode);
        using var d1Conflict = JsonDocument.Parse(await r1Conflict.Content.ReadAsStringAsync());
        Assert.Equal("IDEMPOTENCY_KEY_CONFLICT", d1Conflict.RootElement.GetProperty("code").GetString());

        // 4. Create a second booking for cross-entity payment idempotency test
        var r2 = await PostJsonWithCsrfAsync(staffClient, "/api/bookings", new
        {
            courtId = _factory.Court2Id,
            customerName = "Khách Idempotency 2",
            customerPhone = "0944555667",
            date = futureDate,
            time = "14:00",
            duration = 60,
            autoApprove = true,
            idempotencyKey = "IDEM-BK2-" + Guid.NewGuid().ToString("N")[..8]
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, r2.StatusCode);
        using var d2 = JsonDocument.Parse(await r2.Content.ReadAsStringAsync());
        string bookingCode2 = d2.RootElement.GetProperty("booking").GetProperty("id").GetString()!;

        // 5. Record payment on bookingCode1 with payKey
        string payKey = "IDEM-PAY-" + Guid.NewGuid().ToString("N")[..8];
        var pay1 = await PostJsonWithCsrfAsync(staffClient, $"/api/admin/bookings/{bookingCode1}/payments", new
        {
            amount = 50_000,
            paymentMethod = "cash",
            idempotencyKey = payKey
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, pay1.StatusCode);

        // 6. Reuse payKey with DIFFERENT amount on same booking -> MUST return 409 IDEMPOTENCY_KEY_CONFLICT
        var payDiffAmount = await PostJsonWithCsrfAsync(staffClient, $"/api/admin/bookings/{bookingCode1}/payments", new
        {
            amount = 80_000,
            paymentMethod = "cash",
            idempotencyKey = payKey
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.Conflict, payDiffAmount.StatusCode);
        using var payDiffDoc = JsonDocument.Parse(await payDiffAmount.Content.ReadAsStringAsync());
        Assert.Equal("IDEMPOTENCY_KEY_CONFLICT", payDiffDoc.RootElement.GetProperty("code").GetString());

        // 7. Reuse payKey on DIFFERENT booking (bookingCode2) -> MUST return 409 IDEMPOTENCY_KEY_CONFLICT
        var payDiffBooking = await PostJsonWithCsrfAsync(staffClient, $"/api/admin/bookings/{bookingCode2}/payments", new
        {
            amount = 50_000,
            paymentMethod = "cash",
            idempotencyKey = payKey
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.Conflict, payDiffBooking.StatusCode);
        using var payDiffBookingDoc = JsonDocument.Parse(await payDiffBooking.Content.ReadAsStringAsync());
        Assert.Equal("IDEMPOTENCY_KEY_CONFLICT", payDiffBookingDoc.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task V4_A4_CancelBookingLinkedWithProShop_AtomicallyCancelsOrders_RestocksExactlyOnce_And_BlocksDeliveredOrPaid()
    {
        using var staffClient = CreateCookieClient();
        string staffCsrf = await LoginAndGetCsrfAsync(staffClient, "staff_test", "StaffPass!2026");

        int initialStock;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var prod = await db.TbProducts.FirstAsync(p => p.ProductId == _factory.Product1Id);
            prod.Quantity = 25;
            prod.UnitInStock = 25;
            await db.SaveChangesAsync();
            initialStock = prod.Quantity ?? 25;
        }

        // Case 1: Unpaid & Undelivered linked Pro Shop order (3 units) + direct POS item (2 units) -> Cancel restores exact 5 units (back to 25)
        string date1 = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        var bkRes1 = await PostJsonWithCsrfAsync(staffClient, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Khách Gộp Đơn Pro Shop",
            customerPhone = "0955666777",
            date = date1,
            time = "20:00",
            duration = 60,
            autoApprove = true
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, bkRes1.StatusCode);
        using var bkDoc1 = JsonDocument.Parse(await bkRes1.Content.ReadAsStringAsync());
        string bookingCode1 = bkDoc1.RootElement.GetProperty("booking").GetProperty("id").GetString()!;

        // Create linked Pro Shop order for 3 units
        var ordRes1 = await PostJsonWithCsrfAsync(staffClient, "/api/orders/create", new
        {
            customerName = "Khách Gộp Đơn Pro Shop",
            phone = "0955666777",
            bookingCode = bookingCode1,
            items = new[] { new { productId = _factory.Product1Id, qty = 3 } }
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, ordRes1.StatusCode);
        using var ordDoc1 = JsonDocument.Parse(await ordRes1.Content.ReadAsStringAsync());
        string orderCode1 = ordDoc1.RootElement.GetProperty("orderCode").GetString()!;

        // Add 2 direct POS units on the booking
        var posRes1 = await PostJsonWithCsrfAsync(staffClient, $"/api/admin/bookings/{bookingCode1}/pos-item", new
        {
            productId = _factory.Product1Id,
            deltaQty = 2
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, posRes1.StatusCode);

        // Verify stock is now 25 - 3 - 2 = 20
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var prod = await db.TbProducts.AsNoTracking().FirstAsync(p => p.ProductId == _factory.Product1Id);
            Assert.Equal(initialStock - 5, prod.Quantity);
        }

        // Cancel bookingCode1 -> must cancel orderCode1 and restock 3 + 2 = 5 units (to 25, NOT 28!)
        var cancelRes1 = await PostJsonWithCsrfAsync(staffClient, $"/api/bookings/{bookingCode1}/cancel", new
        {
            reason = "Khách hủy ca kèm đơn Pro Shop chưa giao"
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, cancelRes1.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var prod = await db.TbProducts.AsNoTracking().FirstAsync(p => p.ProductId == _factory.Product1Id);
            var ord = await db.TbOrders.AsNoTracking().FirstAsync(o => o.Code == orderCode1);
            var bk = await db.TbBookings.AsNoTracking().FirstAsync(b => b.BookingCode == bookingCode1);

            Assert.Equal(initialStock, prod.Quantity);
            Assert.Equal("cancelled", ord.Status);
            Assert.Equal("cancelled", ord.FulfillmentStatus);
            Assert.Equal("cancelled", bk.Status);
            Assert.Equal(0, bk.PosTotal);
            Assert.Equal(0, bk.AmountDue);
        }

        // Case 2: Linked Pro Shop order already delivered -> CancelBooking MUST reject with 409 LINKED_ORDER_DELIVERED
        var bkRes2 = await PostJsonWithCsrfAsync(staffClient, "/api/bookings", new
        {
            courtId = _factory.Court1Id,
            customerName = "Khách Đã Nhận Hàng",
            customerPhone = "0955666888",
            date = date1,
            time = "21:00",
            duration = 60,
            autoApprove = true
        }, staffCsrf);
        using var bkDoc2 = JsonDocument.Parse(await bkRes2.Content.ReadAsStringAsync());
        string bookingCode2 = bkDoc2.RootElement.GetProperty("booking").GetProperty("id").GetString()!;

        var ordRes2 = await PostJsonWithCsrfAsync(staffClient, "/api/orders/create", new
        {
            customerName = "Khách Đã Nhận Hàng",
            phone = "0955666888",
            bookingCode = bookingCode2,
            items = new[] { new { productId = _factory.Product1Id, qty = 1 } }
        }, staffCsrf);
        using var ordDoc2 = JsonDocument.Parse(await ordRes2.Content.ReadAsStringAsync());
        string orderCode2 = ordDoc2.RootElement.GetProperty("orderCode").GetString()!;

        // Mark orderCode2 as delivered
        var fulfillRes = await PostJsonWithCsrfAsync(staffClient, $"/api/admin/orders/{orderCode2}/fulfill", new
        {
            fulfillmentStatus = "delivered"
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, fulfillRes.StatusCode);

        // Attempt to cancel bookingCode2 -> MUST return 409 Conflict with LINKED_ORDER_DELIVERED
        var cancelRes2 = await PostJsonWithCsrfAsync(staffClient, $"/api/bookings/{bookingCode2}/cancel", new
        {
            reason = "Thử hủy khi đã giao hàng"
        }, staffCsrf);
        Assert.Equal(HttpStatusCode.Conflict, cancelRes2.StatusCode);
        using var cancelDoc2 = JsonDocument.Parse(await cancelRes2.Content.ReadAsStringAsync());
        Assert.Equal("LINKED_ORDER_DELIVERED", cancelDoc2.RootElement.GetProperty("code").GetString());
    }

    [Fact]
    public async Task V4_B1_B2_B3_B4_CsrfRefresh_GuestRecovery_MustChangePassword_And_ETagConsistency()
    {
        // --- V4-B1: /api/security/csrf returns both token and csrfToken, and invalid CSRF returns 400 CSRF_TOKEN_INVALID ---
        using var client = CreateCookieClient();
        var csrfRes = await client.GetAsync("/api/security/csrf");
        csrfRes.EnsureSuccessStatusCode();
        using var csrfDoc = JsonDocument.Parse(await csrfRes.Content.ReadAsStringAsync());
        string tok1 = csrfDoc.RootElement.GetProperty("token").GetString()!;
        string tok2 = csrfDoc.RootElement.GetProperty("csrfToken").GetString()!;
        Assert.False(string.IsNullOrWhiteSpace(tok1));
        Assert.Equal(tok1, tok2);

        var badCsrfRes = await PostJsonWithCsrfAsync(client, "/api/bookings", new { courtId = 1 }, "INVALID_CSRF_TOKEN");
        Assert.Equal(HttpStatusCode.BadRequest, badCsrfRes.StatusCode);
        Assert.Equal("CSRF_TOKEN_INVALID", badCsrfRes.Headers.GetValues("X-Csrf-Error").FirstOrDefault());
        using var badCsrfDoc = JsonDocument.Parse(await badCsrfRes.Content.ReadAsStringAsync());
        Assert.Equal("CSRF_TOKEN_INVALID", badCsrfDoc.RootElement.GetProperty("code").GetString());

        // --- V4-B2: Guest booking retry in same session recovers guestAccessToken; different session does not ---
        string guestDate = BookingService.GetVietnamNow().AddDays(8).ToString("yyyy-MM-dd");
        string guestIdemKey = "GUEST-RECOVER-" + Guid.NewGuid().ToString("N")[..8];
        var guestPayload = new
        {
            courtId = _factory.Court2Id,
            customerName = "Khách Vãng Lai B2",
            customerPhone = "0966777888",
            date = guestDate,
            time = "15:00",
            duration = 60,
            idempotencyKey = guestIdemKey
        };

        var gCreate1 = await PostJsonWithCsrfAsync(client, "/api/bookings", guestPayload, tok1);
        Assert.Equal(HttpStatusCode.OK, gCreate1.StatusCode);
        using var gDoc1 = JsonDocument.Parse(await gCreate1.Content.ReadAsStringAsync());
        string issuedGuestToken = gDoc1.RootElement.GetProperty("guestAccessToken").GetString()!;
        Assert.StartsWith("bk_", issuedGuestToken);

        // Retry with same idempotencyKey in same browser session -> recovers guestAccessToken!
        var gCreateRetrySame = await PostJsonWithCsrfAsync(client, "/api/bookings", guestPayload, tok1);
        Assert.Equal(HttpStatusCode.OK, gCreateRetrySame.StatusCode);
        using var gDocRetrySame = JsonDocument.Parse(await gCreateRetrySame.Content.ReadAsStringAsync());
        Assert.True(gDocRetrySame.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.Equal(issuedGuestToken, gDocRetrySame.RootElement.GetProperty("guestAccessToken").GetString());

        // Retry from a different browser session (no recovery cookie) -> does NOT expose guestAccessToken
        using var otherSession = CreateCookieClient();
        string otherCsrf = await FetchCsrfTokenAsync(otherSession);
        var gCreateOther = await PostJsonWithCsrfAsync(otherSession, "/api/bookings", guestPayload, otherCsrf);
        Assert.Equal(HttpStatusCode.OK, gCreateOther.StatusCode);
        using var gDocOther = JsonDocument.Parse(await gCreateOther.Content.ReadAsStringAsync());
        Assert.True(gDocOther.RootElement.GetProperty("deduplicated").GetBoolean());
        Assert.True(
            !gDocOther.RootElement.TryGetProperty("guestAccessToken", out var leakEl) ||
            leakEl.ValueKind == JsonValueKind.Null ||
            string.IsNullOrEmpty(leakEl.GetString()));

        // --- V4-B3: MustChangePassword enforcement middleware ---
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var acc = await db.TbAccounts.FirstAsync(a => a.Username == "member_b");
            acc.MustChangePassword = true;
            await db.SaveChangesAsync();
        }

        using var memberBClient = CreateCookieClient();
        string memberBCsrf = await LoginAndGetCsrfAsync(memberBClient, "member_b", "BetaPass!2026");

        // Protected page GET /Booking -> redirects to /Account/Profile?forceChange=1
        var pageRes = await memberBClient.GetAsync("/Booking");
        Assert.Equal(HttpStatusCode.Redirect, pageRes.StatusCode);
        Assert.Contains("/Account/Profile", pageRes.Headers.Location?.ToString() ?? string.Empty);

        // Mutating API POST /api/bookings -> blocked with 403 MUST_CHANGE_PASSWORD
        var apiBlockedRes = await PostJsonWithCsrfAsync(memberBClient, "/api/bookings", guestPayload, memberBCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, apiBlockedRes.StatusCode);
        using var apiBlockedDoc = JsonDocument.Parse(await apiBlockedRes.Content.ReadAsStringAsync());
        Assert.Equal("MUST_CHANGE_PASSWORD", apiBlockedDoc.RootElement.GetProperty("code").GetString());

        // Change password via /api/auth/change-password -> clears MustChangePassword and refreshes cookie
        var changePwRes = await PostJsonWithCsrfAsync(memberBClient, "/api/auth/change-password", new
        {
            currentPassword = "BetaPass!2026",
            newPassword = "BetaNewStrongPass!2026"
        }, memberBCsrf);
        Assert.Equal(HttpStatusCode.OK, changePwRes.StatusCode);

        // Now GET /Booking succeeds (200 OK)
        var pageAfterRes = await memberBClient.GetAsync("/Booking");
        Assert.Equal(HttpStatusCode.OK, pageAfterRes.StatusCode);

        // Restore BetaPass!2026 for any other tests
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var pwd = scope.ServiceProvider.GetRequiredService<PasswordService>();
            var acc = await db.TbAccounts.FirstAsync(a => a.Username == "member_b");
            acc.Password = pwd.HashPassword(acc, "BetaPass!2026");
            acc.MustChangePassword = false;
            await db.SaveChangesAsync();
        }

        // --- V4-B4: ETag changes when court name or order state changes ---
        var state1 = await client.GetAsync("/api/db/state");
        string etag1 = state1.Headers.ETag!.Tag;

        using var check304Req = new HttpRequestMessage(HttpMethod.Get, "/api/db/state");
        check304Req.Headers.TryAddWithoutValidation("If-None-Match", etag1);
        var state304 = await client.SendAsync(check304Req);
        Assert.Equal(HttpStatusCode.NotModified, state304.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var court = await db.TbCourts.FirstAsync(c => c.CourtId == _factory.Court1Id);
            string origSurface = court.Surface ?? "Cushion 8 Lớp";
            court.Surface = origSurface + " Pro";
            await db.SaveChangesAsync();

            using var check200Req = new HttpRequestMessage(HttpMethod.Get, "/api/db/state");
            check200Req.Headers.TryAddWithoutValidation("If-None-Match", etag1);
            var state200 = await client.SendAsync(check200Req);
            Assert.Equal(HttpStatusCode.OK, state200.StatusCode);
            Assert.NotEqual(etag1, state200.Headers.ETag!.Tag);

            court.Surface = origSurface;
            await db.SaveChangesAsync();
        }
    }

    [Fact]
    public async Task V4_QR_100PlusBenchmark_AllGeneratedQrVariantsDecode100PercentWithZXing_And_SavesQaArtifacts()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Security:QrSigningKey"] = "V4QrBenchmark-HMAC-SecretKey-2026-SpikePickleball-32BytesMin!"
            })
            .Build();
        var qrService = new QrTokenService(config, new TestHostEnv());

        int totalTested = 0;
        int totalPassed = 0;
        var resultsByVariant = new Dictionary<string, int>
        {
            ["standard_512_png"] = 0,
            ["logo_512_png"] = 0,
            ["mobile_220_downscaled"] = 0,
            ["jpeg_compressed_q80"] = 0,
            ["highres_1024_png"] = 0
        };

        string qaDir = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "docs", "qa", "qr"));
        Directory.CreateDirectory(qaDir);

        for (int i = 1; i <= 105; i++)
        {
            string code = $"PB-{i:D6}";
            string date = "2026-11-15";
            string token = qrService.GenerateCheckInToken(code, date, DateTime.UtcNow.AddDays(30));

            byte[] testImageBytes;
            string category;

            if (i <= 35)
            {
                category = "standard_512_png";
                testImageBytes = qrService.GenerateQrPngBytes(token, pixelsPerModule: 10, includeLogo: false, targetSizePx: 512);
                if (i == 1)
                {
                    string stdSvg = qrService.GenerateQrSvg(token, 10, includeLogo: false);
                    Assert.Contains("viewBox=\"0 0 ", stdSvg);
                    Assert.Contains("preserveAspectRatio=\"xMidYMid meet\"", stdSvg);
                    await File.WriteAllBytesAsync(Path.Combine(qaDir, "sample-standard-512.png"), testImageBytes);
                    await File.WriteAllTextAsync(Path.Combine(qaDir, "sample-standard.svg"), stdSvg);
                }
            }
            else if (i <= 65)
            {
                category = "logo_512_png";
                testImageBytes = qrService.GenerateQrPngBytes(token, pixelsPerModule: 10, includeLogo: true, targetSizePx: 512);
                if (i == 36)
                {
                    string logoSvg = qrService.GenerateQrSvg(token, 10, includeLogo: true);
                    Assert.Contains("viewBox=\"0 0 ", logoSvg);
                    Assert.Contains("preserveAspectRatio=\"xMidYMid meet\"", logoSvg);
                    await File.WriteAllBytesAsync(Path.Combine(qaDir, "sample-logo-512.png"), testImageBytes);
                    await File.WriteAllTextAsync(Path.Combine(qaDir, "sample-logo.svg"), logoSvg);
                }
            }
            else if (i <= 80)
            {
                category = "mobile_220_downscaled";
                byte[] src = qrService.GenerateQrPngBytes(token, pixelsPerModule: 8, includeLogo: (i % 2 == 0), targetSizePx: 220);
                testImageBytes = src;
            }
            else if (i <= 95)
            {
                category = "jpeg_compressed_q80";
                byte[] src = qrService.GenerateQrPngBytes(token, pixelsPerModule: 10, includeLogo: (i % 2 == 0), targetSizePx: 512);
                using var img = Image.Load<Rgba32>(src);
                using var msJpg = new MemoryStream();
                img.SaveAsJpeg(msJpg, new JpegEncoder { Quality = 80 });
                testImageBytes = msJpg.ToArray();
            }
            else
            {
                category = "highres_1024_png";
                testImageBytes = qrService.GenerateQrPngBytes(token, pixelsPerModule: 16, includeLogo: true, targetSizePx: 1024);
                if (i == 96)
                {
                    await File.WriteAllBytesAsync(Path.Combine(qaDir, "sample-logo-1024.png"), testImageBytes);
                }
            }

            totalTested++;
            bool decodedOk = QrTokenService.TryDecodeQrImage(testImageBytes, out string decodedText, out string err);
            Assert.True(decodedOk, $"QR decode failed on sample #{i} ({category}): {err}");
            Assert.Equal(token, decodedText);

            bool validSig = qrService.TryValidateCheckInToken(decodedText, out string outCode, out string outDate, out _);
            Assert.True(validSig);
            Assert.Equal(code, outCode);
            Assert.Equal(date, outDate);

            totalPassed++;
            resultsByVariant[category]++;
        }

        Assert.Equal(105, totalTested);
        Assert.Equal(105, totalPassed);

        var reportJson = JsonSerializer.Serialize(new
        {
            benchmarkTimestampUtc = DateTime.UtcNow.ToString("O"),
            totalSamplesTested = totalTested,
            totalSamplesDecoded = totalPassed,
            passRatePercent = 100.0,
            breakdown = resultsByVariant
        }, new JsonSerializerOptions { WriteIndented = true });

        await File.WriteAllTextAsync(Path.Combine(qaDir, "qr-benchmark-report.json"), reportJson);
    }
}
