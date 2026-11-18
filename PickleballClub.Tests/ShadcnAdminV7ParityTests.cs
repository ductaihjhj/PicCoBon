using System;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc.Testing;
using PickleballClub.Services;
using Xunit;

namespace PickleballClub.Tests;

public class ShadcnAdminV7ParityTests : IClassFixture<SpikeWebAppFactory>
{
    private readonly SpikeWebAppFactory _factory;

    public ShadcnAdminV7ParityTests(SpikeWebAppFactory factory)
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

    private static async Task<string> LoginAsync(HttpClient client, string identifier, string password)
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

    [Fact]
    public async Task AdminShell_RendersShadcnSidebarTopbarCommandAndThemeSwitch_AcrossAllAdminRoutes()
    {
        using var adminClient = CreateCookieClient();
        await LoginAsync(adminClient, "admin@spike.test", "AdminPass!2026");

        var adminRoutes = new[]
        {
            "/Admin",
            "/Management",
            "/Checkin",
            "/Reports",
            "/Cms",
            "/Admin/Bookings",
            "/Admin/Courts",
            "/Admin/Products",
            "/Admin/Accounts",
            "/Admin/Menus"
        };

        foreach (var route in adminRoutes)
        {
            var res = await adminClient.GetAsync(route);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var html = await res.Content.ReadAsStringAsync();

            Assert.Contains("id=\"shAdminShell\"", html);
            Assert.Contains("id=\"shAdminSidebar\"", html);
            Assert.Contains("id=\"shAdminTopbar\"", html);
            Assert.Contains("id=\"shSidebarToggleBtn\"", html);
            Assert.Contains("id=\"shCmdTriggerBtn\"", html);
            Assert.Contains("id=\"shThemeToggleBtn\"", html);
            Assert.Contains("id=\"shCommandModal\"", html);
            Assert.Contains("data-testid=\"sh-nav-group-admin-only\"", html);
            Assert.Contains("meta name=\"csrf-token\"", html);
        }
    }

    [Fact]
    public async Task StaffRole_OnlySeesOperationalSidebarLinks_AndIsBlockedFromAdminOnlyRoutes()
    {
        using var staffClient = CreateCookieClient();
        await LoginAsync(staffClient, "staff@spike.test", "StaffPass!2026");

        // Operational routes succeed and hide Admin-only nav group
        foreach (var opRoute in new[] { "/Admin", "/Management", "/Checkin", "/Reports", "/Admin/Bookings" })
        {
            var res = await staffClient.GetAsync(opRoute);
            Assert.Equal(HttpStatusCode.OK, res.StatusCode);
            var html = await res.Content.ReadAsStringAsync();

            Assert.Contains("id=\"shAdminSidebar\"", html);
            Assert.Contains("Staff Operator", html);
            Assert.DoesNotContain("data-testid=\"sh-nav-group-admin-only\"", html);
        }

        // Direct calls to Admin-only routes are blocked by server authorization
        foreach (var adminOnlyRoute in new[] { "/Cms", "/Admin/Courts", "/Admin/Products", "/Admin/Accounts", "/Admin/Menus" })
        {
            var res = await staffClient.GetAsync(adminOnlyRoute);
            Assert.True(
                res.StatusCode == HttpStatusCode.Forbidden || res.StatusCode == HttpStatusCode.Redirect,
                $"Expected Forbidden/Redirect for Staff accessing {adminOnlyRoute}, got {res.StatusCode}");
        }
    }

    [Fact]
    public async Task PublicWebsite_DoesNotRenderAdminSidebar_PreservesAllCriticalSelectorsAndThemeToggle()
    {
        using var publicClient = CreateCookieClient();

        // Homepage
        var homeRes = await publicClient.GetAsync("/");
        Assert.Equal(HttpStatusCode.OK, homeRes.StatusCode);
        var homeHtml = await homeRes.Content.ReadAsStringAsync();
        Assert.DoesNotContain("id=\"shAdminSidebar\"", homeHtml);
        Assert.Contains("id=\"siteThemeToggleBtn\"", homeHtml);
        Assert.Contains("id=\"dashMatrixTable\"", homeHtml);
        Assert.Contains("id=\"courtsGrid\"", homeHtml);

        // Booking page & QR modal host
        var bookRes = await publicClient.GetAsync("/Booking");
        Assert.Equal(HttpStatusCode.OK, bookRes.StatusCode);
        var bookHtml = await bookRes.Content.ReadAsStringAsync();
        Assert.Contains("id=\"modalQR\"", bookHtml);
        Assert.Contains("id=\"bookingDigitalTicketHost\"", bookHtml);
        Assert.Contains("id=\"btnSubmitBooking\"", bookHtml);
        Assert.DoesNotContain("id=\"shAdminSidebar\"", bookHtml);

        // Pro Shop page & packshot grid
        var shopRes = await publicClient.GetAsync("/Product");
        Assert.Equal(HttpStatusCode.OK, shopRes.StatusCode);
        var shopHtml = await shopRes.Content.ReadAsStringAsync();
        Assert.Contains("id=\"menuGrid\"", shopHtml);
        Assert.Contains("id=\"shopCartBadge\"", shopHtml);
        Assert.Contains("id=\"mobileShopCartBar\"", shopHtml);

        // Lookup page & QR modal host
        var lookupRes = await publicClient.GetAsync("/Lookup");
        Assert.Equal(HttpStatusCode.OK, lookupRes.StatusCode);
        var lookupHtml = await lookupRes.Content.ReadAsStringAsync();
        Assert.Contains("id=\"modalTicketQR\"", lookupHtml);
        Assert.Contains("id=\"lookupDigitalTicketHost\"", lookupHtml);
        Assert.Contains("id=\"lookupCode\"", lookupHtml);
        Assert.Contains("id=\"lookupAccessToken\"", lookupHtml);
    }

    [Fact]
    public async Task AdminLoginAndRegisterPages_RenderShadcnAuthCardAndCsrfToken()
    {
        using var anonClient = CreateCookieClient();
        var loginRes = await anonClient.GetAsync("/Admin/Login");
        Assert.Equal(HttpStatusCode.OK, loginRes.StatusCode);
        var loginHtml = await loginRes.Content.ReadAsStringAsync();
        Assert.Contains("sh-auth-card", loginHtml);
        Assert.Contains("__RequestVerificationToken", loginHtml);

        using var adminClient = CreateCookieClient();
        await LoginAsync(adminClient, "admin@spike.test", "AdminPass!2026");
        var regRes = await adminClient.GetAsync("/Admin/Register");
        Assert.Equal(HttpStatusCode.OK, regRes.StatusCode);
        var regHtml = await regRes.Content.ReadAsStringAsync();
        Assert.Contains("sh-auth-card", regHtml);
        Assert.Contains("__RequestVerificationToken", regHtml);
    }
}
