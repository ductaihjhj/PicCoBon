using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PickleballClub.Configuration;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using Xunit;

namespace PickleballClub.Tests;

public sealed class RegistrationOtpWebAppFactory : WebApplicationFactory<Program>, IAsyncLifetime
{
    private const string MasterConnectionString =
        "Server=.\\SQLEXPRESS;Database=master;Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public string DatabaseName { get; } = "PickleballClub_RegOtpTest_" + Guid.NewGuid().ToString("N")[..8];
    public string ConnectionString =>
        $"Server=.\\SQLEXPRESS;Database={DatabaseName};Trusted_Connection=True;TrustServerCertificate=True;MultipleActiveResultSets=true";

    public FakeEmailSender FakeEmails { get; } = new();
    public AdjustableTimeProvider Clock { get; } = new();

    public RegistrationOtpWebAppFactory()
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
                ["Security:QrSigningKey"] = "RegOtpTest-QrSigningKey-2026-SpikePickleball-32CharsMinimum!",
                ["RegistrationOtp:HmacKey"] = "RegOtpTest-DedicatedHmacKey-2026-SpikePickleball-AtLeast32Bytes!",
                ["RegistrationOtp:OtpExpiryMinutes"] = "10",
                ["RegistrationOtp:RequestLifetimeMinutes"] = "30",
                ["RegistrationOtp:MaxVerifyAttempts"] = "5",
                ["RegistrationOtp:ResendCooldownSeconds"] = "60",
                ["RegistrationOtp:MaxSendsPerRequest"] = "4",
                ["RegistrationOtp:MaxSendsPerSessionPerHour"] = "6",
                ["RegistrationOtp:MaxSendsPerEmailPerHour"] = "4",
                ["RegistrationOtp:MaxSendsPerIpPerHour"] = "6",
                ["RegistrationOtp:MaxGlobalEmailsPerHour"] = "50",
                ["BackgroundWorkers:Enabled"] = "false",
                ["RateLimiting:AuthPermitLimit"] = "500"
            });
        });

        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<PickleballContext>>();
            services.AddDbContext<PickleballContext>(opts => opts.UseSqlServer(ConnectionString));

            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(Clock);

            services.RemoveAll<IEmailSender>();
            services.AddSingleton(FakeEmails);
            services.AddSingleton<IEmailSender>(FakeEmails);
        });
    }

    public async Task InitializeAsync()
    {
        using var scope = Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
        var pwd = scope.ServiceProvider.GetRequiredService<PasswordService>();

        await db.Database.MigrateAsync();

        var admin = new TbAccount
        {
            Username = "admin_otp",
            FullName = "Quản Trị Viên CLB",
            Phone = "0900000001",
            Email = "admin@spikepickleball.club",
            RoleId = 1,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        admin.Password = pwd.HashPassword(admin, "AdminPass!2026");

        var staff = new TbAccount
        {
            Username = "staff_otp",
            FullName = "Nhân Viên Sân",
            Phone = "0900000002",
            Email = "staff@spikepickleball.club",
            RoleId = 3,
            IsActive = true,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        staff.Password = pwd.HashPassword(staff, "StaffPass!2026");

        var legacyCustomer = new TbAccount
        {
            Username = "legacy_cust",
            FullName = "Hội Viên Cũ Trước OTP",
            Phone = "0900000003",
            Email = "legacy@spike.test",
            RoleId = 2,
            IsActive = true,
            EmailVerifiedAtUtc = null,
            SecurityStamp = Function.GenerateSecurityStamp()
        };
        legacyCustomer.Password = pwd.HashPassword(legacyCustomer, "LegacyPass!2026");

        db.TbAccounts.AddRange(admin, staff, legacyCustomer);
        db.TbCourts.Add(new TbCourt
        {
            CourtName = "Sân Trung Tâm 01",
            CourtType = "Indoor",
            BasePrice = 120_000,
            Surcharge = 30_000,
            IsActive = true
        });
        await db.SaveChangesAsync();
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

public class RegistrationEmailOtpIntegrationTests : IClassFixture<RegistrationOtpWebAppFactory>
{
    private readonly RegistrationOtpWebAppFactory _factory;

    public RegistrationEmailOtpIntegrationTests(RegistrationOtpWebAppFactory factory)
    {
        _factory = factory;
        _factory.FakeEmails.IsConfigured = true;
        _factory.FakeEmails.SetInterceptor(null);
    }

    private HttpClient CreateCookieClient() =>
        _factory.CreateClient(new WebApplicationFactoryClientOptions
        {
            BaseAddress = new Uri("https://localhost"),
            AllowAutoRedirect = false,
            HandleCookies = true
        });

    private static async Task<string> FetchCsrfAsync(HttpClient client)
    {
        var resp = await client.GetAsync("/api/security/csrf");
        resp.EnsureSuccessStatusCode();
        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        return doc.RootElement.GetProperty("token").GetString()!;
    }

    private static async Task<HttpResponseMessage> PostJsonWithCsrfAsync(
        HttpClient client,
        string url,
        object payload,
        string? testClientIp = null)
    {
        string csrf = await FetchCsrfAsync(client);
        using var req = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };
        req.Headers.Add("RequestVerificationToken", csrf);
        if (!string.IsNullOrEmpty(testClientIp))
        {
            req.Headers.Add("X-Test-Client-Ip", testClientIp);
        }
        return await client.SendAsync(req);
    }

    private static async Task<(string FormToken, string Html)> LoadLoginFormAndExtractTokenAsync(HttpClient client, string url = "/Account/Login?mode=register")
    {
        var getResp = await client.GetAsync(url);
        Assert.Equal(HttpStatusCode.OK, getResp.StatusCode);
        string html = await getResp.Content.ReadAsStringAsync();
        var match = Regex.Match(html, @"name=""__RequestVerificationToken""\s+type=""hidden""\s+value=""([^""]+)""");
        Assert.True(match.Success, "Expected hidden anti-forgery input in Login.cshtml");
        return (match.Groups[1].Value, html);
    }

    // =========================================================================
    // 1. Valid API & MVC Registration + Reload Persistence + Email Verification + Profile Email Reset
    // =========================================================================
    [Fact]
    public async Task ApiAndMvcRegister_SendSingleEmail_DoNotCreateAccountBeforeOtp_VerifyCreatesCustomerWithVerifiedTimestamp_And_ProfileEmailChangeClearsTimestamp()
    {
        _factory.FakeEmails.Clear();
        using var client = CreateCookieClient();

        int beforeAccountCount;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            beforeAccountCount = await db.TbAccounts.CountAsync();
        }

        // A. Missing CSRF token rejected
        using (var noCsrfReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = new StringContent("{}", Encoding.UTF8, "application/json")
        })
        {
            var noCsrfResp = await client.SendAsync(noCsrfReq);
            Assert.Equal(HttpStatusCode.BadRequest, noCsrfResp.StatusCode);
        }

        // B. Submit valid registration via JSON API -> FakeEmailSender receives 1 OTP email, 0 new TbAccount rows
        var startResp = await PostJsonWithCsrfAsync(client, "/api/auth/register", new
        {
            name = "Nguyễn Minh Khoa",
            phone = "+84 912 300 101",
            email = "  Khoa.Nguyen@Member.Test  ",
            password = "KhoaPassword!2026",
            confirmPassword = "KhoaPassword!2026"
        }, testClientIp: "10.10.1.1");

        Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        using var startDoc = JsonDocument.Parse(await startResp.Content.ReadAsStringAsync());
        Assert.Equal("pending_otp", startDoc.RootElement.GetProperty("status").GetString());
        string verificationToken = startDoc.RootElement.GetProperty("verificationToken").GetString()!;
        string maskedEmail = startDoc.RootElement.GetProperty("maskedEmail").GetString()!;
        Assert.Contains("***", maskedEmail);
        Assert.EndsWith("@member.test", maskedEmail);

        var captured = Assert.Single(_factory.FakeEmails.SentMessages);
        Assert.Equal("khoa.nguyen@member.test", captured.ToEmail);
        Assert.Equal("Mã xác minh đăng ký Spike Pickleball Club", captured.Subject);
        string otpCode = captured.ExtractOtpCode();
        Assert.Equal(6, otpCode.Length);
        Assert.DoesNotContain(otpCode, captured.Subject);
        Assert.Contains($"Mã xác minh của bạn là {otpCode}. Mã có hiệu lực 10 phút. Nếu bạn không yêu cầu đăng ký, bạn có thể bỏ qua email này.", captured.TextBody);

        // Verify SQL has NO new TbAccount yet, and temporary row stores ONLY hashed password + HMAC (never plaintext!)
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            Assert.Equal(beforeAccountCount, await db.TbAccounts.CountAsync());
            var pending = await db.TbRegistrationVerifications.AsNoTracking().SingleAsync(x => x.RequestToken == verificationToken);
            Assert.Equal("Pending", pending.Status);
            Assert.Equal("0912300101", pending.Phone);
            Assert.Equal("khoa.nguyen@member.test", pending.Email);
            Assert.NotNull(pending.PasswordHash);
            Assert.NotEqual("KhoaPassword!2026", pending.PasswordHash);
            Assert.NotNull(pending.OtpHmac);
            Assert.NotEqual(otpCode, pending.OtpHmac);
        }

        // C. Reload persistence check: GET /api/auth/register/session & SSR /Account/Login preserve pending session
        var sessionResp = await client.GetAsync("/api/auth/register/session");
        Assert.Equal(HttpStatusCode.OK, sessionResp.StatusCode);
        using (var sessDoc = JsonDocument.Parse(await sessionResp.Content.ReadAsStringAsync()))
        {
            Assert.True(sessDoc.RootElement.GetProperty("hasPending").GetBoolean());
            Assert.Equal(verificationToken, sessDoc.RootElement.GetProperty("verificationToken").GetString());
            Assert.Equal(maskedEmail, sessDoc.RootElement.GetProperty("maskedEmail").GetString());
        }

        var (_, reloadHtml) = await LoadLoginFormAndExtractTokenAsync(client, "/Account/Login");
        Assert.Contains(maskedEmail, reloadHtml);
        Assert.DoesNotContain(otpCode, reloadHtml);

        // D. Verify valid OTP -> creates exactly 1 Customer with EmailVerifiedAtUtc, wipes PasswordHash & OtpHmac, does NOT auto-login
        var verifyResp = await PostJsonWithCsrfAsync(client, "/api/auth/register/verify-otp", new
        {
            verificationToken,
            otp = otpCode
        });
        Assert.Equal(HttpStatusCode.OK, verifyResp.StatusCode);
        using (var verifyDoc = JsonDocument.Parse(await verifyResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("registered", verifyDoc.RootElement.GetProperty("status").GetString());
        }

        // Confirm not signed in automatically yet
        var stateBeforeLogin = await client.GetAsync("/api/db/state");
        using (var stateDoc = JsonDocument.Parse(await stateBeforeLogin.Content.ReadAsStringAsync()))
        {
            Assert.Equal(JsonValueKind.Null, stateDoc.RootElement.GetProperty("currentUser").ValueKind);
        }

        DateTime verifiedTimestamp;
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            Assert.Equal(beforeAccountCount + 1, await db.TbAccounts.CountAsync());

            var createdAcc = await db.TbAccounts.AsNoTracking().SingleAsync(a => a.Email == "khoa.nguyen@member.test");
            Assert.Equal(2, createdAcc.RoleId);
            Assert.True(createdAcc.IsActive);
            Assert.False(createdAcc.MustChangePassword);
            Assert.False(string.IsNullOrWhiteSpace(createdAcc.SecurityStamp));
            Assert.NotNull(createdAcc.EmailVerifiedAtUtc);
            verifiedTimestamp = createdAcc.EmailVerifiedAtUtc!.Value;

            var consumed = await db.TbRegistrationVerifications.AsNoTracking().SingleAsync(x => x.RequestToken == verificationToken);
            Assert.Equal("Verified", consumed.Status);
            Assert.Equal(createdAcc.AccountId, consumed.CreatedAccountId);
            Assert.Null(consumed.PasswordHash);
            Assert.Null(consumed.OtpHmac);
        }

        // E. Customer logs in with newly verified email, updates profile keeping email (preserves EmailVerifiedAtUtc), then changes email (clears EmailVerifiedAtUtc)
        var loginResp = await PostJsonWithCsrfAsync(client, "/api/auth/login", new
        {
            identifier = "KHOA.NGUYEN@MEMBER.TEST",
            password = "KhoaPassword!2026"
        });
        Assert.Equal(HttpStatusCode.OK, loginResp.StatusCode);

        string profileCsrf = await FetchCsrfAsync(client);
        using (var sameEmailReq = new HttpRequestMessage(HttpMethod.Post, "/Account/UpdateProfile")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["fullName"] = "Nguyễn Minh Khoa Updated",
                ["phone"] = "0912300101",
                ["email"] = "Khoa.Nguyen@Member.Test",
                ["skillLevel"] = "Nâng cao (DUPR 4.0)"
            })
        })
        {
            sameEmailReq.Headers.Add("RequestVerificationToken", profileCsrf);
            sameEmailReq.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var sameResp = await client.SendAsync(sameEmailReq);
            Assert.Equal(HttpStatusCode.OK, sameResp.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var acc = await db.TbAccounts.AsNoTracking().SingleAsync(a => a.Phone == "0912300101");
            Assert.Equal(verifiedTimestamp, acc.EmailVerifiedAtUtc);
        }

        profileCsrf = await FetchCsrfAsync(client);
        using (var changedEmailReq = new HttpRequestMessage(HttpMethod.Post, "/Account/UpdateProfile")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["fullName"] = "Nguyễn Minh Khoa Updated",
                ["phone"] = "0912300101",
                ["email"] = "khoa.new.address@member.test",
                ["skillLevel"] = "Nâng cao (DUPR 4.0)"
            })
        })
        {
            changedEmailReq.Headers.Add("RequestVerificationToken", profileCsrf);
            changedEmailReq.Headers.Add("X-Requested-With", "XMLHttpRequest");
            var changeResp = await client.SendAsync(changedEmailReq);
            Assert.Equal(HttpStatusCode.OK, changeResp.StatusCode);
        }

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var acc = await db.TbAccounts.AsNoTracking().SingleAsync(a => a.Phone == "0912300101");
            Assert.Equal("khoa.new.address@member.test", acc.Email);
            Assert.Null(acc.EmailVerifiedAtUtc);
        }

        // F. MVC endpoint (/Account/Register -> /Account/VerifyRegisterOtp) uses the exact same OTP service
        _factory.FakeEmails.Clear();
        using var mvcClient = CreateCookieClient();
        var (mvcCsrf, _) = await LoadLoginFormAndExtractTokenAsync(mvcClient);

        var mvcRegResp = await mvcClient.PostAsync("/Account/Register", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = mvcCsrf,
            ["Username"] = "mvc_lan_anh",
            ["FullName"] = "Trần Lan Anh",
            ["Phone"] = "0912300102",
            ["Email"] = "lananh.mvc@member.test",
            ["Password"] = "MvcSecret!2026",
            ["ConfirmPassword"] = "MvcSecret!2026"
        }));
        Assert.Equal(HttpStatusCode.Redirect, mvcRegResp.StatusCode);
        Assert.Contains("/Account/Login", mvcRegResp.Headers.Location?.ToString() ?? string.Empty);

        var mvcMail = Assert.Single(_factory.FakeEmails.SentMessages);
        string mvcOtp = mvcMail.ExtractOtpCode();

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            Assert.False(await db.TbAccounts.AnyAsync(a => a.Email == "lananh.mvc@member.test"));
        }

        var (mvcVerifyCsrf, pendingMvcHtml) = await LoadLoginFormAndExtractTokenAsync(mvcClient);
        Assert.Contains("la***c@member.test", pendingMvcHtml);

        var mvcVerifyResp = await mvcClient.PostAsync("/Account/VerifyRegisterOtp", new FormUrlEncodedContent(new Dictionary<string, string>
        {
            ["__RequestVerificationToken"] = mvcVerifyCsrf,
            ["otp"] = mvcOtp
        }));
        Assert.Equal(HttpStatusCode.Redirect, mvcVerifyResp.StatusCode);

        var (_, verifiedMvcHtml) = await LoadLoginFormAndExtractTokenAsync(mvcClient);
        Assert.Contains("Đăng ký thành công", verifiedMvcHtml);

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var mvcAcc = await db.TbAccounts.AsNoTracking().SingleAsync(a => a.Email == "lananh.mvc@member.test");
            Assert.Equal(2, mvcAcc.RoleId);
            Assert.NotNull(mvcAcc.EmailVerifiedAtUtc);
        }
    }

    // =========================================================================
    // 2. Rejections: Wrong OTP (5-attempt lockout), Expired OTP, Replay, Changed Email, Cross-Session OTP
    // =========================================================================
    [Fact]
    public async Task Rejections_WrongOtp5Times_ExpiredOtp_ReusedOtp_ChangedEmail_And_CrossSessionOtp_NeverIncreaseAccountCount()
    {
        using var scopeCount = _factory.Services.CreateScope();
        var countDb = scopeCount.ServiceProvider.GetRequiredService<PickleballContext>();
        int baselineAccounts = await countDb.TbAccounts.CountAsync();

        // A. Wrong OTP 5 times -> locks current OTP version so even right code on 6th try fails
        _factory.FakeEmails.Clear();
        using var clientWrong = CreateCookieClient();
        var r1 = await PostJsonWithCsrfAsync(clientWrong, "/api/auth/register", new
        {
            name = "Thử Sai Mã",
            phone = "0913400201",
            email = "wrong5@member.test",
            password = "Password!123"
        }, testClientIp: "10.10.2.1");
        Assert.Equal(HttpStatusCode.OK, r1.StatusCode);
        string tok1 = JsonDocument.Parse(await r1.Content.ReadAsStringAsync()).RootElement.GetProperty("verificationToken").GetString()!;
        string realOtp1 = Assert.Single(_factory.FakeEmails.SentMessages).ExtractOtpCode();
        string wrongOtp = realOtp1 == "000000" ? "999999" : "000000";

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            var badResp = await PostJsonWithCsrfAsync(clientWrong, "/api/auth/register/verify-otp", new
            {
                verificationToken = tok1,
                otp = wrongOtp
            });
            Assert.Equal(HttpStatusCode.BadRequest, badResp.StatusCode);
        }

        // 6th try with the REAL OTP must still be rejected because max 5 attempts invalidated it!
        var sixthResp = await PostJsonWithCsrfAsync(clientWrong, "/api/auth/register/verify-otp", new
        {
            verificationToken = tok1,
            otp = realOtp1
        });
        Assert.Equal(HttpStatusCode.BadRequest, sixthResp.StatusCode);
        using (var sixthDoc = JsonDocument.Parse(await sixthResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("OTP_MAX_ATTEMPTS", sixthDoc.RootElement.GetProperty("code").GetString());
        }

        // B. Expired OTP (> 10 minutes) rejected
        _factory.FakeEmails.Clear();
        using var clientExp = CreateCookieClient();
        var rExp = await PostJsonWithCsrfAsync(clientExp, "/api/auth/register", new
        {
            name = "Hết Hạn Mã",
            phone = "0913400202",
            email = "expired@member.test",
            password = "Password!123"
        }, testClientIp: "10.10.2.2");
        Assert.Equal(HttpStatusCode.OK, rExp.StatusCode);
        string tokExp = JsonDocument.Parse(await rExp.Content.ReadAsStringAsync()).RootElement.GetProperty("verificationToken").GetString()!;
        string otpExp = Assert.Single(_factory.FakeEmails.SentMessages).ExtractOtpCode();

        _factory.Clock.Advance(TimeSpan.FromMinutes(10).Add(TimeSpan.FromSeconds(5)));

        var expVerify = await PostJsonWithCsrfAsync(clientExp, "/api/auth/register/verify-otp", new
        {
            verificationToken = tokExp,
            otp = otpExp
        });
        Assert.Equal(HttpStatusCode.BadRequest, expVerify.StatusCode);
        using (var expDoc = JsonDocument.Parse(await expVerify.Content.ReadAsStringAsync()))
        {
            Assert.Equal("OTP_EXPIRED", expDoc.RootElement.GetProperty("code").GetString());
        }

        // C. Cross-session attack: Client B tries to submit Client A's valid token + OTP
        _factory.FakeEmails.Clear();
        using var clientSessionA = CreateCookieClient();
        using var clientSessionB = CreateCookieClient();

        var rSessA = await PostJsonWithCsrfAsync(clientSessionA, "/api/auth/register", new
        {
            name = "Chủ Phiên A",
            phone = "0913400203",
            email = "session.a@member.test",
            password = "Password!123"
        }, testClientIp: "10.10.2.3");
        Assert.Equal(HttpStatusCode.OK, rSessA.StatusCode);
        string tokSessA = JsonDocument.Parse(await rSessA.Content.ReadAsStringAsync()).RootElement.GetProperty("verificationToken").GetString()!;
        string otpSessA = Assert.Single(_factory.FakeEmails.SentMessages).ExtractOtpCode();

        var crossResp = await PostJsonWithCsrfAsync(clientSessionB, "/api/auth/register/verify-otp", new
        {
            verificationToken = tokSessA,
            otp = otpSessA
        });
        Assert.Equal(HttpStatusCode.Forbidden, crossResp.StatusCode);
        using (var crossDoc = JsonDocument.Parse(await crossResp.Content.ReadAsStringAsync()))
        {
            Assert.Equal("OTP_SESSION_MISMATCH", crossDoc.RootElement.GetProperty("code").GetString());
        }

        // Verify total accounts STILL equals baselineAccounts!
        Assert.Equal(baselineAccounts, await countDb.TbAccounts.CountAsync());

        // D. Changing email in ClientSessionA invalidates old request (session.a@member.test) and starts new request
        _factory.FakeEmails.Clear();
        var rNewEmail = await PostJsonWithCsrfAsync(clientSessionA, "/api/auth/register", new
        {
            name = "Chủ Phiên A",
            phone = "0913400203",
            email = "session.a.updated@member.test",
            password = "Password!123"
        }, testClientIp: "10.10.2.3");
        Assert.Equal(HttpStatusCode.OK, rNewEmail.StatusCode);
        string tokNewEmail = JsonDocument.Parse(await rNewEmail.Content.ReadAsStringAsync()).RootElement.GetProperty("verificationToken").GetString()!;
        string otpNewEmail = Assert.Single(_factory.FakeEmails.SentMessages).ExtractOtpCode();

        // Trying old OTP on old token -> Superseded
        var oldTokResp = await PostJsonWithCsrfAsync(clientSessionA, "/api/auth/register/verify-otp", new
        {
            verificationToken = tokSessA,
            otp = otpSessA
        });
        Assert.Equal(HttpStatusCode.BadRequest, oldTokResp.StatusCode);
        Assert.Equal("OTP_REQUEST_SUPERSEDED", JsonDocument.Parse(await oldTokResp.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());

        // Trying old OTP on new token -> Invalid HMAC (unless 1-in-1,000,000 identical digits, guard check)
        if (otpSessA != otpNewEmail)
        {
            var mixResp = await PostJsonWithCsrfAsync(clientSessionA, "/api/auth/register/verify-otp", new
            {
                verificationToken = tokNewEmail,
                otp = otpSessA
            });
            Assert.Equal(HttpStatusCode.BadRequest, mixResp.StatusCode);
        }

        Assert.Equal(baselineAccounts, await countDb.TbAccounts.CountAsync());

        // Verify new OTP succeeds -> creates 1 account, then replaying same OTP ("dùng lại") is rejected with 409!
        var okVerify = await PostJsonWithCsrfAsync(clientSessionA, "/api/auth/register/verify-otp", new
        {
            verificationToken = tokNewEmail,
            otp = otpNewEmail
        });
        Assert.Equal(HttpStatusCode.OK, okVerify.StatusCode);
        Assert.Equal(baselineAccounts + 1, await countDb.TbAccounts.CountAsync());

        var replayResp = await PostJsonWithCsrfAsync(clientSessionA, "/api/auth/register/verify-otp", new
        {
            verificationToken = tokNewEmail,
            otp = otpNewEmail
        });
        Assert.Equal(HttpStatusCode.Conflict, replayResp.StatusCode);
        Assert.Equal("OTP_ALREADY_USED", JsonDocument.Parse(await replayResp.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        Assert.Equal(baselineAccounts + 1, await countDb.TbAccounts.CountAsync());
    }

    // =========================================================================
    // 3. Resend Cooldown, Version Rotation, Concurrent Verification & SMTP / Config Failures
    // =========================================================================
    [Fact]
    public async Task ResendReplacesOldOtp_CooldownEnforced_ConcurrentVerifyCreatesSingleCustomer_And_SmtpFailuresHandledHonestly()
    {
        // A. Resend cooldown (60s) + new OTP replaces old OTP
        _factory.FakeEmails.Clear();
        using var client = CreateCookieClient();
        var startResp = await PostJsonWithCsrfAsync(client, "/api/auth/register", new
        {
            name = "Lê Hoàng Nam",
            phone = "0915600301",
            email = "nam.resend@member.test",
            password = "NamPassword!2026"
        }, testClientIp: "10.10.3.1");
        Assert.Equal(HttpStatusCode.OK, startResp.StatusCode);
        string token = JsonDocument.Parse(await startResp.Content.ReadAsStringAsync()).RootElement.GetProperty("verificationToken").GetString()!;
        string firstOtp = Assert.Single(_factory.FakeEmails.SentMessages).ExtractOtpCode();

        // Resend before 60s -> 429 TooManyRequests, no second email sent
        _factory.Clock.Advance(TimeSpan.FromSeconds(25));
        var earlyResend = await PostJsonWithCsrfAsync(client, "/api/auth/register/resend-otp", new { verificationToken = token }, testClientIp: "10.10.3.1");
        Assert.Equal(HttpStatusCode.TooManyRequests, earlyResend.StatusCode);
        Assert.Single(_factory.FakeEmails.SentMessages);

        // Advance past 60s -> Resend succeeds with a strictly different OTP and invalidates firstOtp
        _factory.Clock.Advance(TimeSpan.FromSeconds(40));
        var okResend = await PostJsonWithCsrfAsync(client, "/api/auth/register/resend-otp", new { verificationToken = token }, testClientIp: "10.10.3.1");
        Assert.Equal(HttpStatusCode.OK, okResend.StatusCode);
        Assert.Equal(2, _factory.FakeEmails.SentMessages.Count);
        string secondOtp = _factory.FakeEmails.SentMessages.Last().ExtractOtpCode();
        Assert.NotEqual(firstOtp, secondOtp);

        // Old OTP rejected
        var oldVerify = await PostJsonWithCsrfAsync(client, "/api/auth/register/verify-otp", new
        {
            verificationToken = token,
            otp = firstOtp
        });
        Assert.Equal(HttpStatusCode.BadRequest, oldVerify.StatusCode);

        // B. Concurrent parallel verification with valid secondOtp -> ONLY 1 HTTP 200 OK & ONLY 1 TbAccount created!
        string csrf = await FetchCsrfAsync(client);
        using var gate = new ManualResetEventSlim(false);
        var parallelTasks = Enumerable.Range(0, 6).Select(_ => Task.Run(async () =>
        {
            gate.Wait();
            using var req = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register/verify-otp")
            {
                Content = new StringContent(JsonSerializer.Serialize(new
                {
                    verificationToken = token,
                    otp = secondOtp
                }), Encoding.UTF8, "application/json")
            };
            req.Headers.Add("RequestVerificationToken", csrf);
            return await client.SendAsync(req);
        })).ToArray();

        gate.Set();
        var responses = await Task.WhenAll(parallelTasks);

        Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
        Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            Assert.Equal(1, await db.TbAccounts.CountAsync(a => a.Email == "nam.resend@member.test"));
        }

        // C. Transient SMTP error returns honest 503, creates 0 accounts, and allows retry after recovery
        _factory.FakeEmails.Clear();
        _factory.FakeEmails.SetInterceptor(_ => EmailSendResult.Fail("SMTP_TIMEOUT", "Máy chủ gửi email phản hồi quá thời gian chờ."));

        using var smtpFailClient = CreateCookieClient();
        var smtpFailResp = await PostJsonWithCsrfAsync(smtpFailClient, "/api/auth/register", new
        {
            name = "Phạm Thu Hà",
            phone = "0915600302",
            email = "ha.smtpfail@member.test",
            password = "HaPassword!2026"
        }, testClientIp: "10.10.3.2");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, smtpFailResp.StatusCode);
        using var failDoc = JsonDocument.Parse(await smtpFailResp.Content.ReadAsStringAsync());
        Assert.False(failDoc.RootElement.GetProperty("ok").GetBoolean());
        Assert.Equal("SMTP_TIMEOUT", failDoc.RootElement.GetProperty("code").GetString());
        string retryToken = failDoc.RootElement.GetProperty("verificationToken").GetString()!;

        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            Assert.False(await db.TbAccounts.AnyAsync(a => a.Email == "ha.smtpfail@member.test"));
        }

        // SMTP recovers -> Resend after short fail cooldown succeeds and account can be verified
        _factory.FakeEmails.SetInterceptor(null);
        _factory.Clock.Advance(TimeSpan.FromSeconds(20));

        var recoveredResend = await PostJsonWithCsrfAsync(smtpFailClient, "/api/auth/register/resend-otp", new
        {
            verificationToken = retryToken
        }, testClientIp: "10.10.3.2");
        Assert.Equal(HttpStatusCode.OK, recoveredResend.StatusCode);
        string recoveredOtp = Assert.Single(_factory.FakeEmails.SentMessages).ExtractOtpCode();

        var recoveredVerify = await PostJsonWithCsrfAsync(smtpFailClient, "/api/auth/register/verify-otp", new
        {
            verificationToken = retryToken,
            otp = recoveredOtp
        });
        Assert.Equal(HttpStatusCode.OK, recoveredVerify.StatusCode);

        // D. Unconfigured SMTP fails closed with 503 OTP_PROVIDER_NOT_CONFIGURED
        _factory.FakeEmails.IsConfigured = false;
        using var uncfgClient = CreateCookieClient();
        var uncfgResp = await PostJsonWithCsrfAsync(uncfgClient, "/api/auth/register", new
        {
            name = "Chưa Cấu Hình SMTP",
            phone = "0915600309",
            email = "uncfg@member.test",
            password = "Password!123"
        }, testClientIp: "10.10.3.9");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, uncfgResp.StatusCode);
        Assert.Equal("OTP_PROVIDER_NOT_CONFIGURED", JsonDocument.Parse(await uncfgResp.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        _factory.FakeEmails.IsConfigured = true;
    }

    // =========================================================================
    // 4. Cumulative Per-Email / Per-IP Quotas, Legacy Accounts & Staff/Admin Login, and Typed Options Validation
    // =========================================================================
    [Fact]
    public async Task CumulativeRateLimitsEnforced_LegacyAndStaffAdminLoginsUnaffected_And_PrivateEmailOptionsValidated()
    {
        // A. Cumulative per-email limit (MaxSendsPerEmailPerHour = 4) across separate sessions/IPs
        _factory.FakeEmails.Clear();
        for (int i = 1; i <= 4; i++)
        {
            using var c = CreateCookieClient();
            var resp = await PostJsonWithCsrfAsync(c, "/api/auth/register", new
            {
                name = $"Quota User {i}",
                phone = $"091770040{i}",
                email = "quota.target@member.test",
                password = "QuotaPassword!2026"
            }, testClientIp: $"10.20.4.{i}");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        using (var fifthClient = CreateCookieClient())
        {
            var fifthResp = await PostJsonWithCsrfAsync(fifthClient, "/api/auth/register", new
            {
                name = "Quota User 5",
                phone = "0917700405",
                email = "quota.target@member.test",
                password = "QuotaPassword!2026"
            }, testClientIp: "10.20.4.55");
            Assert.Equal(HttpStatusCode.TooManyRequests, fifthResp.StatusCode);
            Assert.Equal("OTP_EMAIL_QUOTA_EXCEEDED", JsonDocument.Parse(await fifthResp.Content.ReadAsStringAsync()).RootElement.GetProperty("code").GetString());
        }

        // B. Existing Legacy Customer (EmailVerifiedAtUtc == null), Staff, and Admin accounts still log in with full RBAC
        using (var scope = _factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
            var legacy = await db.TbAccounts.AsNoTracking().SingleAsync(a => a.Username == "legacy_cust");
            Assert.Null(legacy.EmailVerifiedAtUtc);
        }

        using var legacyClient = CreateCookieClient();
        var legLogin = await PostJsonWithCsrfAsync(legacyClient, "/api/auth/login", new
        {
            identifier = "legacy@spike.test",
            password = "LegacyPass!2026"
        });
        Assert.Equal(HttpStatusCode.OK, legLogin.StatusCode);

        using var staffClient = CreateCookieClient();
        var staffLogin = await PostJsonWithCsrfAsync(staffClient, "/api/auth/login", new
        {
            identifier = "staff_otp",
            password = "StaffPass!2026"
        });
        Assert.Equal(HttpStatusCode.OK, staffLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await staffClient.GetAsync("/api/admin/reports/summary")).StatusCode);

        using var adminClient = CreateCookieClient();
        var adminLogin = await PostJsonWithCsrfAsync(adminClient, "/api/auth/login", new
        {
            identifier = "admin_otp",
            password = "AdminPass!2026"
        });
        Assert.Equal(HttpStatusCode.OK, adminLogin.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await adminClient.GetAsync("/Admin/Accounts")).StatusCode);

        // C. Private Email SMTP & OTP HMAC strong-typed validation checks
        var validSmtp = new EmailSmtpOptions
        {
            Host = "mail.privateemail.com",
            Port = 587,
            Security = "StartTls",
            Username = "admin@spikepickleball.club",
            Password = "SampleAppPasswordNotReal123",
            FromAddress = "admin@spikepickleball.club",
            FromName = "Spike Pickleball Club"
        };
        Assert.True(validSmtp.Validate().IsValid);

        var mismatchedFrom = new EmailSmtpOptions
        {
            Host = "mail.privateemail.com",
            Port = 587,
            Security = "StartTls",
            Username = "admin@spikepickleball.club",
            Password = "SampleAppPasswordNotReal123",
            FromAddress = "other@spikepickleball.club"
        };
        Assert.Equal("SMTP_FROM_MISMATCH", mismatchedFrom.Validate().ErrorCode);

        var insecureTls = new EmailSmtpOptions
        {
            Host = "mail.privateemail.com",
            Port = 587,
            Security = "None",
            Username = "admin@spikepickleball.club",
            Password = "SampleAppPasswordNotReal123",
            FromAddress = "admin@spikepickleball.club"
        };
        Assert.Equal("SMTP_TLS_REQUIRED", insecureTls.Validate().ErrorCode);

        var reusedHmac = new RegistrationOtpOptions
        {
            HmacKey = "SameKeyAsQrSigningKey-MustBeRejected-32BytesMinimum!"
        };
        Assert.Equal("OTP_HMAC_KEY_REUSED", reusedHmac.ValidateAndResolveKey("SameKeyAsQrSigningKey-MustBeRejected-32BytesMinimum!").ErrorCode);
    }
}
