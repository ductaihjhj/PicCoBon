using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using PickleballClub.Models;
using PickleballClub.Services;
using Xunit;

namespace PickleballClub.Tests;

public class SafeRemediationRegressionTests : IClassFixture<SpikeWebAppFactory>
{
    private readonly SpikeWebAppFactory _factory;

    public SafeRemediationRegressionTests(SpikeWebAppFactory factory)
    {
        _factory = factory;
    }

    private HttpClient CreateCookieClient()
    {
        return _factory.CreateClient(new WebApplicationFactoryClientOptions
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

    private static async Task<HttpResponseMessage> PutJsonWithCsrfAsync(HttpClient client, string url, object body, string csrf)
    {
        using var req = new HttpRequestMessage(HttpMethod.Put, url)
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Add("RequestVerificationToken", csrf);
        return await client.SendAsync(req);
    }

    [Fact]
    public async Task Fix08_18_20_ZeroPeakSurcharge_UpdatesPricingVersion_QuoteApi_And_SsrHtml()
    {
        using var admin = CreateCookieClient();
        string adminCsrf = await LoginAndGetCsrfAsync(admin, "admin@spike.test", "AdminPass!2026");

        var initialQuoteRes = await admin.GetAsync($"/api/bookings/quote?courtId={_factory.Court2Id}&time=17:00&duration=60");
        initialQuoteRes.EnsureSuccessStatusCode();
        string v1Version;
        using (var d1 = JsonDocument.Parse(await initialQuoteRes.Content.ReadAsStringAsync()))
        {
            var q1 = d1.RootElement.GetProperty("quote");
            v1Version = q1.GetProperty("pricingVersion").GetString()!;
            Assert.Equal(150_000, q1.GetProperty("peakHourlyRate").GetInt32());
        }

        // Update peakSurcharge to 0 via Admin CMS
        var putRes = await PutJsonWithCsrfAsync(admin, "/api/admin/cms", new
        {
            courtPrice = 120000,
            peakSurcharge = 0,
            peakStart = "17:00",
            openTime = "06:00",
            closeTime = "22:00"
        }, adminCsrf);
        Assert.Equal(HttpStatusCode.OK, putRes.StatusCode);

        try
        {
            using var anon = CreateCookieClient();
            var q2Res = await anon.GetAsync($"/api/bookings/quote?courtId={_factory.Court2Id}&time=17:00&duration=60");
            q2Res.EnsureSuccessStatusCode();
            using var d2 = JsonDocument.Parse(await q2Res.Content.ReadAsStringAsync());
            var q2 = d2.RootElement.GetProperty("quote");
            string v2Version = q2.GetProperty("pricingVersion").GetString()!;
            Assert.NotEqual(v1Version, v2Version);
            Assert.Equal(120_000, q2.GetProperty("peakHourlyRate").GetInt32());
            Assert.Equal(120_000, q2.GetProperty("amountDue").GetInt32());

            // Verify SSR HTML reflects DB truth without hardcoded "150.000 đ" or "4 sân"
            var homeHtml = await (await anon.GetAsync("/")).Content.ReadAsStringAsync();
            Assert.Contains("Tổ hợp <span data-cms=\"courtCount\">2</span> sân", homeHtml);
            Assert.Contains("id=\"pricePeakCard\">120.000 đ", homeHtml);
            Assert.DoesNotContain("150.000 đ", homeHtml);

            var loginHtml = await (await anon.GetAsync("/Account/Login")).Content.ReadAsStringAsync();
            Assert.Contains("2 sân trực tiếp", loginHtml);
            Assert.DoesNotContain("4 sân trực tiếp", loginHtml);
        }
        finally
        {
            // Restore default CMS peakSurcharge cleanly
            await PutJsonWithCsrfAsync(admin, "/api/admin/cms", new
            {
                courtPrice = 120000,
                peakSurcharge = 30000,
                peakStart = "17:00",
                openTime = "06:00",
                closeTime = "22:00"
            }, adminCsrf);
        }
    }

    [Fact]
    public async Task Fix03_04_ProShopAttachAndFulfill_RespectSameDayBounds_Rbac_And_CancelledConflict()
    {
        string todayIso = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        string tomorrowIso = BookingService.GetVietnamNow().AddDays(1).ToString("yyyy-MM-dd");
        int initialStock;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var prod = await db.TbProducts.FirstAsync(p => p.ProductId == _factory.Product1Id);
            initialStock = prod.UnitInStock ?? 0;

            db.TbBookings.AddRange(
                new TbBooking
                {
                    BookingCode = "PB-FUT991",
                    CourtId = _factory.Court1Id,
                    AccountId = _factory.CustomerAAccountId,
                    CustomerName = "Hội Viên Alpha",
                    Phone = "0902000001",
                    BookingDate = tomorrowIso,
                    StartTime = "10:00",
                    Duration = 60,
                    CourtFee = 120_000,
                    AmountDue = 120_000,
                    AmountDueDec = 120_000m,
                    Status = "approved",
                    PaymentStatus = "unpaid",
                    CreatedDate = DateTime.UtcNow
                },
                new TbBooking
                {
                    BookingCode = "PB-TOD992",
                    CourtId = _factory.Court2Id,
                    AccountId = _factory.CustomerAAccountId,
                    CustomerName = "Hội Viên Alpha",
                    Phone = "0902000001",
                    BookingDate = todayIso,
                    StartTime = "06:00",
                    Duration = 1020, // covers until 23:00 VN so test works any time of day
                    CourtFee = 240_000,
                    AmountDue = 240_000,
                    AmountDueDec = 240_000m,
                    Status = "playing",
                    PaymentStatus = "unpaid",
                    CreatedDate = DateTime.UtcNow
                });
            await db.SaveChangesAsync();
        }

        using var member = CreateCookieClient();
        string memberCsrf = await LoginAndGetCsrfAsync(member, "alpha@spike.test", "AlphaPass!2026");

        // 1. Tomorrow booking attach MUST fail with 409 Conflict and leave stock intact
        var badAttach = await PostJsonWithCsrfAsync(member, "/api/orders/create", new
        {
            customerName = "Hội Viên Alpha",
            phone = "0902000001",
            bookingCode = "PB-FUT991",
            items = new[] { new { productId = _factory.Product1Id, qty = 2 } }
        }, memberCsrf);
        Assert.Equal(HttpStatusCode.Conflict, badAttach.StatusCode);

        // 2. Today active booking attach MUST succeed
        var okAttach = await PostJsonWithCsrfAsync(member, "/api/orders/create", new
        {
            customerName = "Hội Viên Alpha",
            phone = "0902000001",
            bookingCode = "PB-TOD992",
            items = new[] { new { productId = _factory.Product1Id, qty = 2 } }
        }, memberCsrf);
        Assert.Equal(HttpStatusCode.OK, okAttach.StatusCode);
        string linkedOrderCode;
        using (var d = JsonDocument.Parse(await okAttach.Content.ReadAsStringAsync()))
        {
            linkedOrderCode = d.RootElement.GetProperty("orderCode").GetString()!;
        }

        // Customer must be blocked from staff fulfill API (403)
        var forbiddenFulfill = await PostJsonWithCsrfAsync(member, $"/api/admin/orders/{linkedOrderCode}/fulfill", new { }, memberCsrf);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenFulfill.StatusCode);

        // Staff lists all orders (?standaloneOnly=false) and fulfills linked order
        using var staff = CreateCookieClient();
        string staffCsrf = await LoginAndGetCsrfAsync(staff, "staff@spike.test", "StaffPass!2026");

        var listRes = await staff.GetAsync("/api/admin/orders?standaloneOnly=false");
        Assert.Equal(HttpStatusCode.OK, listRes.StatusCode);
        var listJson = await listRes.Content.ReadAsStringAsync();
        Assert.Contains(linkedOrderCode, listJson);
        Assert.Contains("PB-TOD992", listJson);

        var fulfillRes = await PostJsonWithCsrfAsync(staff, $"/api/admin/orders/{linkedOrderCode}/fulfill", new { }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, fulfillRes.StatusCode);
        using (var fDoc = JsonDocument.Parse(await fulfillRes.Content.ReadAsStringAsync()))
        {
            Assert.Equal("delivered", fDoc.RootElement.GetProperty("order").GetProperty("fulfillmentStatus").GetString());
        }

        // Cancelled standalone order cannot be fulfilled -> 409 Conflict
        var soloRes = await PostJsonWithCsrfAsync(member, "/api/orders/create", new
        {
            customerName = "Hội Viên Alpha",
            phone = "0902000001",
            address = "Tại quầy",
            items = new[] { new { productId = _factory.Product1Id, qty = 1 } }
        }, memberCsrf);
        Assert.Equal(HttpStatusCode.OK, soloRes.StatusCode);
        string soloCode;
        using (var sDoc = JsonDocument.Parse(await soloRes.Content.ReadAsStringAsync()))
        {
            soloCode = sDoc.RootElement.GetProperty("orderCode").GetString()!;
        }

        var cancelSolo = await PostJsonWithCsrfAsync(staff, $"/api/admin/orders/{soloCode}/cancel", new { reason = "Hủy test" }, staffCsrf);
        Assert.Equal(HttpStatusCode.OK, cancelSolo.StatusCode);

        var cancelledFulfill = await PostJsonWithCsrfAsync(staff, $"/api/admin/orders/{soloCode}/fulfill", new { }, staffCsrf);
        Assert.Equal(HttpStatusCode.Conflict, cancelledFulfill.StatusCode);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var prod = await db.TbProducts.AsNoTracking().FirstAsync(p => p.ProductId == _factory.Product1Id);
            // 2 used by linked order, 1 cancelled & restocked
            Assert.Equal(initialStock - 2, prod.UnitInStock);
        }
    }

    [Fact]
    public async Task Fix07_ReportsSummary_PreservesNegativeCourtCollectedRevenue_WhenRefundsExceedGross()
    {
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var bk = new TbBooking
            {
                BookingCode = "PB-NEG701",
                CourtId = _factory.Court1Id,
                AccountId = _factory.CustomerBAccountId,
                CustomerName = "Hội Viên Beta",
                Phone = "0902000002",
                BookingDate = "2026-09-01",
                StartTime = "09:00",
                Duration = 60,
                CourtFee = 140_000,
                AmountDue = 140_000,
                AmountDueDec = 140_000m,
                PaidAmount = 40_000,
                PaidAmountDec = 40_000m,
                PaymentStatus = "partial",
                Status = "cancelled",
                CreatedDate = new DateTime(2026, 9, 1, 8, 0, 0)
            };
            db.TbBookings.Add(bk);
            await db.SaveChangesAsync();

            db.TbPaymentTransactions.AddRange(
                new TbPaymentTransaction
                {
                    BookingId = bk.BookingId,
                    BookingCode = bk.BookingCode,
                    Amount = 140_000,
                    AmountDec = 140_000m,
                    TransactionType = "payment",
                    PaymentMethod = "cash",
                    Status = "completed",
                    RecordedBy = "staff_test",
                    CreatedAt = new DateTime(2026, 9, 1, 9, 0, 0)
                },
                new TbPaymentTransaction
                {
                    BookingId = bk.BookingId,
                    BookingCode = bk.BookingCode,
                    Amount = 100_000,
                    AmountDec = 100_000m,
                    TransactionType = "refund",
                    PaymentMethod = "cash",
                    Status = "completed",
                    Note = "Hoàn tiền hủy sân",
                    RecordedBy = "staff_test",
                    CreatedAt = new DateTime(2026, 9, 2, 10, 0, 0)
                });
            await db.SaveChangesAsync();
        }

        using var staff = CreateCookieClient();
        await LoginAndGetCsrfAsync(staff, "staff@spike.test", "StaffPass!2026");

        // On 2026-09-02 alone: 0 payments + 100,000 refund => net collected = -100,000 on Court1!
        var res = await staff.GetAsync("/api/admin/reports/summary?dateFrom=2026-09-02&dateTo=2026-09-02");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
        Assert.Equal(-100_000, doc.RootElement.GetProperty("totalCollectedRevenue").GetInt32());

        var court1 = doc.RootElement.GetProperty("courtBreakdown")
            .EnumerateArray()
            .First(c => c.GetProperty("courtId").GetInt32() == _factory.Court1Id);
        Assert.Equal(-100_000, court1.GetProperty("collectedRevenue").GetInt32());
    }
}
