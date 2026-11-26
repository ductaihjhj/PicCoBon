 using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Core.Infrastructure;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
using PickleballClub.Configuration;
using PickleballClub.Logging;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

var builder = WebApplication.CreateBuilder(args);

bool isSqlDebugRequested =
    !builder.Environment.IsProduction() &&
    (builder.Environment.IsEnvironment("SqlDebug") ||
     args.Contains("--sql-debug", StringComparer.OrdinalIgnoreCase) ||
     string.Equals(Environment.GetEnvironmentVariable("SPIKE_SQL_DEBUG"), "true", StringComparison.OrdinalIgnoreCase));

if (isSqlDebugRequested && !builder.Environment.IsEnvironment("SqlDebug"))
{
    builder.Configuration.AddJsonFile("appsettings.SqlDebug.json", optional: true, reloadOnChange: true);
}

builder.Logging.AddConsoleFormatter<CleanSpikeConsoleFormatter, CleanSpikeConsoleFormatterOptions>();

bool isDevOrTesting = builder.Environment.IsDevelopment() ||
                      builder.Environment.IsEnvironment("SqlDebug") ||
                      builder.Environment.IsEnvironment("Testing");
var cookieSecurePolicy = isDevOrTesting ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

// Add services to the container.
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.Add<CsrfFailureResultFilter>();
});
builder.Services.AddHttpContextAccessor();
builder.Services.AddDataProtection();
builder.Services.AddMemoryCache();

builder.Services.Configure<PayPalOptions>(builder.Configuration.GetSection(PayPalOptions.SectionName));
builder.Services.Configure<GeminiOptions>(builder.Configuration.GetSection(GeminiOptions.SectionName));
builder.Services.Configure<EmailSmtpOptions>(builder.Configuration.GetSection(EmailSmtpOptions.SectionName));
builder.Services.Configure<RegistrationOtpOptions>(builder.Configuration.GetSection(RegistrationOtpOptions.SectionName));

// P1-17: Consistent Anti-Forgery protection across forms and JSON API endpoints (RequestVerificationToken header)
builder.Services.AddAntiforgery(options =>
{
    options.HeaderName = "RequestVerificationToken";
    options.Cookie.Name = "spike_csrf";
    options.Cookie.HttpOnly = true;
    options.Cookie.SameSite = SameSiteMode.Lax;
    options.Cookie.SecurePolicy = cookieSecurePolicy;
});

builder.Services.AddDbContext<PickleballContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register Domain & Security Services
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<QrTokenService>();
builder.Services.TryAddScoped<IEmailSender, MailKitEmailSender>();
builder.Services.AddScoped<RegistrationOtpService>();
builder.Services.AddScoped<ClubSettingsService>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<OrderService>();
builder.Services.AddScoped<ProductImageService>();
builder.Services.AddHttpClient<IPayPalClient, PayPalClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(25);
});
builder.Services.AddScoped<PayPalPaymentService>();
builder.Services.AddScoped<SocialCourtService>();
builder.Services.AddScoped<SpikeAssistantToolService>();
builder.Services.AddHttpClient<IGeminiClient, GeminiClient>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(30);
});
builder.Services.AddScoped<IAiAssistantService, GeminiAssistantService>();

if (builder.Configuration.GetValue("BackgroundWorkers:Enabled", true) && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<BookingLifecycleWorker>();
    builder.Services.AddHostedService<PayPalReconciliationWorker>();
}

// P0-01 & P0-02: Signed Cookie Authentication with HTTPS SecurePolicy=Always in Production & live revocation check
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.Cookie.Name = "spike_auth";
        options.Cookie.HttpOnly = true;
        options.Cookie.SameSite = SameSiteMode.Lax;
        options.Cookie.SecurePolicy = cookieSecurePolicy;
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/Login";

        options.Events.OnValidatePrincipal = async context =>
        {
            var idClaim = context.Principal?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            if (!int.TryParse(idClaim, out int accountId) || accountId <= 0)
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var db = context.HttpContext.RequestServices.GetRequiredService<PickleballContext>();
            var acc = await db.TbAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == accountId);
            if (acc == null || !acc.IsActive)
            {
                var authLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Auth");
                authLogger.LogWarning("Session revoked for AccountId={AccountId}: Reason={Reason}", accountId, acc == null ? "AccountMissing" : "AccountInactive");
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var tokenStamp = context.Principal?.FindFirst("SecurityStamp")?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(acc.SecurityStamp) &&
                !string.Equals(acc.SecurityStamp, tokenStamp, StringComparison.Ordinal))
            {
                var authLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Auth");
                authLogger.LogWarning("Session rejected due to rotated SecurityStamp for AccountId={AccountId}", accountId);
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            context.HttpContext.Items["MustChangePassword"] = acc.MustChangePassword;

            string liveAvatar = Function.GetSafeAvatarUrl(acc.Avatar);
            string liveFullName = string.IsNullOrWhiteSpace(acc.FullName)
                ? (acc.Username ?? string.Empty)
                : acc.FullName;

            context.HttpContext.Items["CurrentAvatar"] = liveAvatar;
            context.HttpContext.Items["CurrentFullName"] = liveFullName;

            var claimAvatar = context.Principal?.FindFirst("Avatar")?.Value ?? string.Empty;
            var claimFullName = context.Principal?.FindFirst("FullName")?.Value ?? string.Empty;
            if (!string.Equals(claimAvatar, liveAvatar, StringComparison.Ordinal) ||
                !string.Equals(claimFullName, liveFullName, StringComparison.Ordinal))
            {
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    var cloned = identity.Clone();
                    var oldAvatarClaim = cloned.FindFirst("Avatar");
                    if (oldAvatarClaim != null) cloned.RemoveClaim(oldAvatarClaim);
                    cloned.AddClaim(new Claim("Avatar", liveAvatar));

                    var oldNameClaim = cloned.FindFirst("FullName");
                    if (oldNameClaim != null) cloned.RemoveClaim(oldNameClaim);
                    cloned.AddClaim(new Claim("FullName", liveFullName));

                    context.ReplacePrincipal(new ClaimsPrincipal(cloned));
                    context.ShouldRenew = true;
                }
            }
        };

        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
                var authLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Auth");
                authLogger.LogWarning("Unauthenticated API request {Method} {Path} rejected (401)", context.Request.Method, context.Request.Path.Value);
                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                return Task.CompletedTask;
            }
            if (context.Request.Path.StartsWithSegments("/Admin"))
            {
                context.Response.Redirect("/Admin/Login");
                return Task.CompletedTask;
            }
            context.Response.Redirect(context.RedirectUri);
            return Task.CompletedTask;
        };

        options.Events.OnRedirectToAccessDenied = context =>
        {
            var authLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Auth");
            string actor = context.HttpContext.User?.Identity?.Name ?? "anonymous";
            authLogger.LogWarning("Authorization denied (403) for Actor={Actor} on {Method} {Path}",
                CorrelationContext.MaskIdentifier(actor), context.Request.Method, context.Request.Path.Value);

            if (context.Request.Path.StartsWithSegments("/api"))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return Task.CompletedTask;
            }
            if (context.Request.Path.StartsWithSegments("/Admin"))
            {
                context.Response.Redirect("/Admin/Login");
                return Task.CompletedTask;
            }
            context.Response.Redirect("/Account/Login");
            return Task.CompletedTask;
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = async (context, cancellationToken) =>
    {
        var rateLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Security");
        rateLogger.LogWarning("Rate limit exceeded (429) on {Method} {Path}", context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value);
        context.HttpContext.Response.StatusCode = StatusCodes.Status429TooManyRequests;
        context.HttpContext.Response.Headers.RetryAfter = "60";
        context.HttpContext.Response.ContentType = "application/json; charset=utf-8";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            ok = false,
            code = "RATE_LIMITED",
            retryAfterSeconds = 60,
            error = "Bạn đã gửi quá nhiều yêu cầu trong thời gian ngắn. Vui lòng thử lại sau 60 giây."
        }, cancellationToken);
    };

    static string ResolveTrustedClientPartition(HttpContext httpContext, bool allowTestHeader)
    {
        // V4-B5: Only trust RemoteIpAddress (never raw X-Forwarded-For without trusted proxy).
        // Allow X-Test-Client-Ip ONLY when running in-memory TestServer (RemoteIpAddress == null) or Testing env.
        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        if ((string.IsNullOrEmpty(ip) || allowTestHeader) &&
            httpContext.Request.Headers.TryGetValue("X-Test-Client-Ip", out var testIp) &&
            !string.IsNullOrWhiteSpace(testIp))
        {
            ip = testIp.ToString().Trim();
        }
        if (string.IsNullOrEmpty(ip)) ip = "loopback";

        string userPart = httpContext.User?.FindFirstValue(ClaimTypes.NameIdentifier) ?? "anon";
        string path = httpContext.Request.Path.Value?.ToLowerInvariant() ?? "/";
        string category = path switch
        {
            var p when p.Contains("/auth/login") || p.Contains("/account/login") => "auth.login",
            var p when p.Contains("/auth/register") || p.Contains("/account/register") || p.Contains("/account/verifyregisterotp") || p.Contains("/account/resendregisterotp") => "auth.register",
            var p when p.Contains("/reset-password") || p.Contains("/change-password") => "auth.password",
            var p when p.Contains("/lookup/") => "ticket.lookup",
            var p when p.Contains("/api/bookings") => "booking.create",
            var p when p.Contains("/api/payments/paypal") => "paypal.checkout",
            var p when p.Contains("/api/checkin") => "checkin.scan",
            var p when p.Contains("/api/assistant") => "assistant.chat",
            _ => "general"
        };

        return $"{category}|{ip}|{userPart}";
    }

    int authPermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 20);
    int checkInPermitLimit = builder.Configuration.GetValue("RateLimiting:CheckInPermitLimit", 60);
    int bookingPermitLimit = builder.Configuration.GetValue("RateLimiting:BookingPermitLimit", 15);

    options.AddPolicy("AuthAndLookup", httpContext =>
    {
        string key = ResolveTrustedClientPartition(httpContext, isDevOrTesting);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("CheckInLimiter", httpContext =>
    {
        string key = ResolveTrustedClientPartition(httpContext, isDevOrTesting);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = checkInPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("BookingCreateLimiter", httpContext =>
    {
        string key = ResolveTrustedClientPartition(httpContext, isDevOrTesting);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = bookingPermitLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("PayPalLimiter", httpContext =>
    {
        string key = ResolveTrustedClientPartition(httpContext, isDevOrTesting);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:PayPalPermitLimit", 30),
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });

    options.AddPolicy("AssistantLimiter", httpContext =>
    {
        string key = ResolveTrustedClientPartition(httpContext, isDevOrTesting);
        return RateLimitPartition.GetFixedWindowLimiter(key, _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = builder.Configuration.GetValue("RateLimiting:AssistantPermitLimit", 18),
            Window = TimeSpan.FromMinutes(1),
            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
            QueueLimit = 0,
            AutoReplenishment = true
        });
    });
});

builder.Services.AddSession(cfg =>
{
    cfg.Cookie.Name = "pickleball_session";
    cfg.Cookie.HttpOnly = true;
    cfg.Cookie.SameSite = SameSiteMode.Lax;
    cfg.Cookie.SecurePolicy = cookieSecurePolicy;
    cfg.IdleTimeout = TimeSpan.FromMinutes(60);
});

var app = builder.Build();

// Fail-fast check on QrTokenService signing secret at boot (P0-09)
_ = app.Services.GetRequiredService<QrTokenService>();

Function.Configure(app.Services.GetRequiredService<IHttpContextAccessor>());

// P2-01 & P2-02: Explicit --migrate CLI step for deployments; safe EF MigrateAsync in Development only; NEVER ad-hoc ALTER in Production boot!
if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    using var migrateScope = app.Services.CreateScope();
    var db = migrateScope.ServiceProvider.GetRequiredService<PickleballContext>();
    var logger = migrateScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    logger.LogInformation("Applying versioned EF Core database migrations...");
    await db.Database.MigrateAsync();
    logger.LogInformation("EF Core migrations applied successfully.");
    return;
}

if ((app.Environment.IsDevelopment() || app.Environment.IsEnvironment("SqlDebug")) &&
    app.Configuration.GetValue("Database:AutoMigrateInDevelopment", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

if (!app.Environment.IsDevelopment() && !app.Environment.IsEnvironment("SqlDebug"))
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Correlation ID & Unhandled API/MVC Diagnostics Middleware
app.Use(async (context, next) =>
{
    string? incomingCid = context.Request.Headers[CorrelationContext.HeaderName].FirstOrDefault();
    string correlationId = CorrelationContext.ResolveSafeId(incomingCid);
    context.TraceIdentifier = correlationId;
    context.Response.OnStarting(() =>
    {
        context.Response.Headers[CorrelationContext.HeaderName] = correlationId;
        return Task.CompletedTask;
    });

    var reqLogger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("SpikeOS.Request");
    using (CorrelationContext.Push(correlationId))
    using (reqLogger.BeginScope(new Dictionary<string, object?> { ["CorrelationId"] = correlationId }))
    {
        try
        {
            await next();
        }
        catch (Exception ex)
        {
            reqLogger.LogError(ex, "Unhandled exception processing {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            throw;
        }
    }
});

// Security Headers Middleware (P2-09), Legacy Cookie Cleanup (P0-02) & V4-B1 CSRF Error Formatting
app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
    context.Response.Headers["X-Permitted-Cross-Domain-Policies"] = "none";

    if (context.Request.Cookies.ContainsKey("pb_admin_uid"))
    {
        context.Response.Cookies.Delete("pb_admin_uid");
    }

    await next();

    var afFeature = context.Features.Get<Microsoft.AspNetCore.Antiforgery.IAntiforgeryValidationFeature>();
    bool isCsrfFail = (afFeature != null && !afFeature.IsValid) ||
                      (context.Response.StatusCode == StatusCodes.Status400BadRequest &&
                       !context.Response.HasStarted &&
                       string.IsNullOrEmpty(context.Response.ContentType) &&
                       (HttpMethods.IsPost(context.Request.Method) ||
                        HttpMethods.IsPut(context.Request.Method) ||
                        HttpMethods.IsPatch(context.Request.Method) ||
                        HttpMethods.IsDelete(context.Request.Method)));

    if (isCsrfFail)
    {
        context.Response.StatusCode = StatusCodes.Status400BadRequest;
        context.Response.Headers["X-Csrf-Error"] = "CSRF_TOKEN_INVALID";
        if (!context.Response.HasStarted)
        {
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                ok = false,
                code = "CSRF_TOKEN_INVALID",
                error = "Phiên bảo mật CSRF đã hết hạn hoặc không hợp lệ. Vui lòng thử lại."
            });
        }
    }
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseAuthentication();

// V4-B1: Deterministic CSRF validation before MVC / ApiController ProblemDetails formatting
app.Use(async (context, next) =>
{
    var endpoint = context.GetEndpoint();
    bool requiresCsrf = endpoint?.Metadata.GetMetadata<Microsoft.AspNetCore.Antiforgery.IAntiforgeryMetadata>()?.RequiresValidation == true ||
                        endpoint?.Metadata.GetMetadata<ValidateAntiForgeryTokenAttribute>() != null;

    if (requiresCsrf &&
        !HttpMethods.IsGet(context.Request.Method) &&
        !HttpMethods.IsHead(context.Request.Method) &&
        !HttpMethods.IsOptions(context.Request.Method) &&
        !HttpMethods.IsTrace(context.Request.Method))
    {
        var antiforgery = context.RequestServices.GetRequiredService<Microsoft.AspNetCore.Antiforgery.IAntiforgery>();
        try
        {
            await antiforgery.ValidateRequestAsync(context);
        }
        catch (Microsoft.AspNetCore.Antiforgery.AntiforgeryValidationException)
        {
            var secLogger = context.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Security");
            secLogger.LogWarning("CSRF token validation failed on {Method} {Path}", context.Request.Method, context.Request.Path.Value);
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            context.Response.Headers["X-Csrf-Error"] = "CSRF_TOKEN_INVALID";
            context.Response.ContentType = "application/json; charset=utf-8";
            await context.Response.WriteAsJsonAsync(new
            {
                ok = false,
                code = "CSRF_TOKEN_INVALID",
                error = "Phiên bảo mật CSRF đã hết hạn hoặc không hợp lệ. Vui lòng thử lại."
            });
            return;
        }
    }

    await next();
});

app.UseAuthorization();
app.UseRateLimiter();

// V4-B3: Server-side MustChangePassword enforcement middleware
app.Use(async (context, next) =>
{
    bool mustChange = (context.Items.TryGetValue("MustChangePassword", out var mcpVal) && mcpVal is true) ||
                      string.Equals(context.User?.FindFirstValue("MustChangePassword"), "true", StringComparison.OrdinalIgnoreCase);

    if (context.User?.Identity?.IsAuthenticated == true && mustChange)
    {
        var path = context.Request.Path.Value ?? "/";
        bool isAllowed =
            path.StartsWith("/Account/Profile", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Account/ChangePassword", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Account/Logout", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/Account/Login", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/change-password", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/auth/logout", StringComparison.OrdinalIgnoreCase) ||
            path.StartsWith("/api/security/csrf", StringComparison.OrdinalIgnoreCase) ||
            (path.StartsWith("/api/db/state", StringComparison.OrdinalIgnoreCase) && HttpMethods.IsGet(context.Request.Method)) ||
            path.StartsWith("/health", StringComparison.OrdinalIgnoreCase);

        if (!isAllowed)
        {
            if (path.StartsWith("/api/", StringComparison.OrdinalIgnoreCase))
            {
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                context.Response.ContentType = "application/json; charset=utf-8";
                await context.Response.WriteAsJsonAsync(new
                {
                    ok = false,
                    code = "MUST_CHANGE_PASSWORD",
                    redirectTo = "/Account/Profile",
                    error = "Tài khoản của bạn đang sử dụng mật khẩu tạm thời. Vui lòng đổi mật khẩu mới trước khi thực hiện thao tác này."
                });
                return;
            }

            context.Response.Redirect("/Account/Profile?forceChange=1");
            return;
        }
    }

    await next();
});

// P2-08: Operational Health Endpoint (zero PII or connection string disclosure)
app.MapGet("/health", async (PickleballContext db) =>
{
    try
    {
        bool canConnect = await db.Database.CanConnectAsync();
        if (!canConnect)
        {
            return Results.Json(new
            {
                status = "Unhealthy",
                database = "Unreachable",
                timestampUtc = DateTime.UtcNow
            }, statusCode: StatusCodes.Status503ServiceUnavailable);
        }

        return Results.Ok(new
        {
            status = "Healthy",
            database = "Online",
            environment = app.Environment.EnvironmentName,
            timestampUtc = DateTime.UtcNow
        });
    }
    catch
    {
        return Results.Json(new
        {
            status = "Unhealthy",
            database = "Error",
            timestampUtc = DateTime.UtcNow
        }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

if (!app.Environment.IsEnvironment("Testing"))
{
    using var bootScope = app.Services.CreateScope();
    var bootLogger = bootScope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var dbLogger = bootScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("Microsoft.EntityFrameworkCore.SpikeStartup");
    var payPalLogger = bootScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Services.PayPalPaymentService");

    bootLogger.LogInformation("Spike OS initialized (Environment={Environment}, SqlDebug={SqlDebug})",
        app.Environment.EnvironmentName, isSqlDebugRequested);

    try
    {
        var db = bootScope.ServiceProvider.GetRequiredService<PickleballContext>();
        if (db.Database.IsRelational())
        {
            if (await db.Database.CanConnectAsync())
            {
                dbLogger.LogInformation("Database connection verified");
            }
            else
            {
                dbLogger.LogWarning("Database connection unreachable at startup");
            }
        }

        var payPalService = bootScope.ServiceProvider.GetRequiredService<PayPalPaymentService>();
        var readiness = await payPalService.ValidateCheckoutReadinessAsync(db);
        if (readiness.IsReady)
        {
            var ppOpts = bootScope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<PayPalOptions>>().Value;
            payPalLogger.LogInformation("PayPal gateway ready (Env={PayPalEnv}, Currency={Currency}, Rate={Rate})",
                ppOpts.Environment, ppOpts.Currency, ppOpts.VndPerUsd);
        }
        else
        {
            payPalLogger.LogWarning("PayPal gateway inactive at startup: Code={ErrorCode}", readiness.ErrorCode);
        }

        var otpLogger = bootScope.ServiceProvider.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Services.RegistrationOtpService");
        var regOtpService = bootScope.ServiceProvider.GetRequiredService<RegistrationOtpService>();
        var otpReadiness = regOtpService.ValidateReadiness();
        if (otpReadiness.IsReady)
        {
            var smtpOpts = bootScope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<EmailSmtpOptions>>().Value;
            otpLogger.LogInformation("Registration Email OTP ready (Host={Host}, Port={Port}, Security={Security})",
                smtpOpts.Host, smtpOpts.Port, smtpOpts.Security);
        }
        else
        {
            otpLogger.LogWarning("Registration Email OTP inactive at startup: Code={ErrorCode}", otpReadiness.ErrorCode);
        }
    }
    catch (Exception ex)
    {
        bootLogger.LogError(ex, "Startup readiness probe encountered an error");
    }
}

app.Run();

public sealed class CsrfFailureResultFilter : IAsyncAlwaysRunResultFilter, IOrderedFilter
{
    public int Order => -3000;

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        if (context.Result is IAntiforgeryValidationFailedResult)
        {
            var secLogger = context.HttpContext.RequestServices.GetRequiredService<ILoggerFactory>().CreateLogger("PickleballClub.Security");
            secLogger.LogWarning("Antiforgery filter blocked {Method} {Path}", context.HttpContext.Request.Method, context.HttpContext.Request.Path.Value);
            context.HttpContext.Response.Headers["X-Csrf-Error"] = "CSRF_TOKEN_INVALID";
            context.Result = new JsonResult(new
            {
                ok = false,
                code = "CSRF_TOKEN_INVALID",
                error = "Phiên bảo mật CSRF đã hết hạn hoặc không hợp lệ. Vui lòng thử lại."
            })
            {
                StatusCode = StatusCodes.Status400BadRequest
            };
        }

        return next();
    }
}

public partial class Program { }
