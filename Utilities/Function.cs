using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Claims;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using PickleballClub.Models;

namespace PickleballClub.Utilities;

public static class Function
{
    private static IHttpContextAccessor? _httpContextAccessor;

    public static void Configure(IHttpContextAccessor accessor)
    {
        _httpContextAccessor = accessor;
    }

    private static ClaimsPrincipal? CurrentUser => _httpContextAccessor?.HttpContext?.User;

    public static int _AccountId
    {
        get
        {
            var claim = CurrentUser?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
            return int.TryParse(claim, out int id) ? id : 0;
        }
    }

    public static string _Username =>
        CurrentUser?.FindFirst(ClaimTypes.Name)?.Value ?? string.Empty;

    public static string _FullName =>
        CurrentUser?.FindFirst("FullName")?.Value ?? string.Empty;

    public static string _Email =>
        CurrentUser?.FindFirst(ClaimTypes.Email)?.Value ?? string.Empty;

    public static string _Phone =>
        CurrentUser?.FindFirst(ClaimTypes.MobilePhone)?.Value ?? string.Empty;

    public static int _RoleId
    {
        get
        {
            var claim = CurrentUser?.FindFirst("RoleId")?.Value;
            return int.TryParse(claim, out int roleId) ? roleId : 0;
        }
    }

    public static string _Avatar =>
        CurrentUser?.FindFirst("Avatar")?.Value ?? string.Empty;

    public static string _SkillLevel =>
        CurrentUser?.FindFirst("SkillLevel")?.Value ?? string.Empty;

    public static bool IsLogin() =>
        CurrentUser?.Identity?.IsAuthenticated == true && _AccountId > 0;

    public static bool IsAdmin() =>
        IsLogin() && _RoleId == 1;

    public static bool IsStaffOrAdmin() =>
        IsLogin() && (_RoleId == 1 || _RoleId == 3);

    public static string GenerateSecurityStamp() =>
        Guid.NewGuid().ToString("N");

    public static string GetRoleName(int? roleId)
    {
        return roleId switch
        {
            1 => "Admin",
            3 => "Staff",
            _ => "Customer"
        };
    }

    public static async Task SignInAccountAsync(HttpContext httpContext, TbAccount account, bool isPersistent = true)
    {
        int roleId = account.RoleId ?? 2;
        string roleName = GetRoleName(roleId);

        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, account.AccountId.ToString()),
            new(ClaimTypes.Name, account.Username ?? string.Empty),
            new("FullName", account.FullName ?? account.Username ?? string.Empty),
            new(ClaimTypes.Email, account.Email ?? string.Empty),
            new(ClaimTypes.MobilePhone, account.Phone ?? string.Empty),
            new("RoleId", roleId.ToString()),
            new(ClaimTypes.Role, roleName),
            new("Avatar", account.Avatar ?? string.Empty),
            new("SkillLevel", account.SkillLevel ?? string.Empty),
            new("SecurityStamp", account.SecurityStamp ?? string.Empty)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(isPersistent ? 7 : 1),
                AllowRefresh = true
            });

        httpContext.User = principal;
        httpContext.Response.Cookies.Delete("pb_admin_uid");
    }

    public static async Task SignOutAccountAsync(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete("pb_admin_uid");
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public static string TitleSlugGenerationAlias(string title)
    {
        if (string.IsNullOrEmpty(title)) return "";
        string str = title.ToLower();
        str = RemoveVietnameseSigns(str);
        str = Regex.Replace(str, @"[^a-z0-9\s-]", "");
        str = Regex.Replace(str, @"\s+", " ").Trim();
        str = str.Substring(0, str.Length <= 100 ? str.Length : 100).Trim();
        str = Regex.Replace(str, @"\s", "-");
        return str;
    }

    private static string RemoveVietnameseSigns(string str)
    {
        string[] vietnameseSigns =
        {
            "aAeEoOuUiIdDyY",
            "áàạảãâấầậẩẫăắằặẳẵ",
            "ÁÀẠẢÃÂẤẦẬẨẪĂẮẰẶẲẴ",
            "éèẹẻẽêếềệểễ",
            "ÉÈẸẺẼÊẾỀỆỂỄ",
            "óòọỏõôốồộổỗơớờợởỡ",
            "ÓÒỌỎÕÔỐỒỘỔỖƠỚỜỢỞỠ",
            "úùụủũưứừựửữ",
            "ÚÙỤỦŨƯỨỪỰỬỮ",
            "íìịỉĩ",
            "ÍÌỊỈĨ",
            "đ",
            "Đ",
            "ýỳỵỷỹ",
            "ÝỲỴỶỸ"
        };

        for (int i = 1; i < vietnameseSigns.Length; i++)
        {
            for (int j = 0; j < vietnameseSigns[i].Length; j++)
            {
                str = str.Replace(vietnameseSigns[i][j], vietnameseSigns[0][i - 1]);
            }
        }
        return str;
    }
}
