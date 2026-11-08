using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

var builder = WebApplication.CreateBuilder(args);

bool isDevOrTesting = builder.Environment.IsDevelopment() || builder.Environment.IsEnvironment("Testing");
var cookieSecurePolicy = isDevOrTesting ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;

// Add services to the container.
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

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
builder.Services.AddSingleton<PasswordService>();
builder.Services.AddSingleton<QrTokenService>();
builder.Services.AddScoped<ClubSettingsService>();
builder.Services.AddScoped<PricingService>();
builder.Services.AddScoped<BookingService>();
builder.Services.AddScoped<OrderService>();

if (builder.Configuration.GetValue("BackgroundWorkers:Enabled", true) && !builder.Environment.IsEnvironment("Testing"))
{
    builder.Services.AddHostedService<BookingLifecycleWorker>();
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
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
                return;
            }

            var tokenStamp = context.Principal?.FindFirst("SecurityStamp")?.Value ?? string.Empty;
            if (!string.IsNullOrEmpty(acc.SecurityStamp) &&
                !string.Equals(acc.SecurityStamp, tokenStamp, StringComparison.Ordinal))
            {
                context.RejectPrincipal();
                await context.HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            }
        };

        options.Events.OnRedirectToLogin = context =>
        {
            if (context.Request.Path.StartsWithSegments("/api"))
            {
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

    options.AddFixedWindowLimiter("AuthAndLookup", limiterOptions =>
    {
        limiterOptions.PermitLimit = builder.Configuration.GetValue("RateLimiting:AuthPermitLimit", 25);
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 2;
    });

    options.AddFixedWindowLimiter("CheckInLimiter", limiterOptions =>
    {
        limiterOptions.PermitLimit = builder.Configuration.GetValue("RateLimiting:CheckInPermitLimit", 60);
        limiterOptions.Window = TimeSpan.FromMinutes(1);
        limiterOptions.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        limiterOptions.QueueLimit = 2;
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

if (app.Environment.IsDevelopment() && app.Configuration.GetValue("Database:AutoMigrateInDevelopment", true))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<PickleballContext>();
    if (db.Database.IsRelational())
    {
        await db.Database.MigrateAsync();
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Security Headers Middleware (P2-09) & Legacy Cookie Cleanup (P0-02)
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
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseSession();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

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

app.Run();

public partial class Program { }
