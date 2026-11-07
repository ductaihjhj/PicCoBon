using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<PickleballContext>(options =>
{
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection"));
});

builder.Services.AddControllersWithViews().AddRazorRuntimeCompilation();

builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseSession();

// Tự động khôi phục phiên đăng nhập từ Cookie nếu Server vừa khởi động lại
app.Use(async (context, next) =>
{
    if (context.Request.Cookies.TryGetValue("pb_admin_uid", out string? uidStr) &&
        int.TryParse(uidStr, out int uid) &&
        uid > 0 &&
        (!Function.IsLogin() || Function._AccountId != uid))
    {
        var db = context.RequestServices.GetService<PickleballContext>();
        if (db != null)
        {
            var acc = await db.TbAccounts.FirstOrDefaultAsync(a => a.AccountId == uid && a.IsActive);
            if (acc != null)
            {
                Function._AccountId = acc.AccountId;
                Function._Username = acc.Username ?? acc.Email ?? acc.Phone ?? string.Empty;
                Function._FullName = acc.FullName ?? "Hội viên CLB";
                Function._Email = acc.Email ?? string.Empty;
                Function._Phone = acc.Phone ?? string.Empty;
                Function._Avatar = acc.Avatar ?? string.Empty;
                Function._SkillLevel = acc.SkillLevel ?? string.Empty;
                Function._RoleId = acc.RoleId ?? 2;
            }
        }
    }
    await next();
});

app.UseAuthorization();

app.MapControllerRoute(
    name: "legacy_index",
    pattern: "index.html",
    defaults: new { controller = "Home", action = "Index" });

app.MapControllerRoute(
    name: "legacy_booking",
    pattern: "booking.html",
    defaults: new { controller = "Booking", action = "Index" });

app.MapControllerRoute(
    name: "legacy_menu",
    pattern: "menu.html",
    defaults: new { controller = "Product", action = "Index" });

app.MapControllerRoute(
    name: "legacy_lookup",
    pattern: "lookup.html",
    defaults: new { controller = "Lookup", action = "Index" });

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Home}/{action=Index}/{id?}"
);

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"
);

app.Run();
