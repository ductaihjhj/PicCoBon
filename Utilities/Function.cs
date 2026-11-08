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

    // ThreadStatic fallback only for unit tests where HttpContext is not present
    [ThreadStatic]
    private static int _fallbackAccountId;
    [ThreadStatic]
    private static string? _fallbackUsername;
    [ThreadStatic]
    private static string? _fallbackFullName;
    [ThreadStatic]
    private static string? _fallbackEmail;
    [ThreadStatic]
    private static string? _fallbackPhone;
    [ThreadStatic]
    private static int _fallbackRoleId;
    [ThreadStatic]
    private static string? _fallbackAvatar;

    public static string _Message = string.Empty;
    public static string _MessageEmail = string.Empty;

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
            if (int.TryParse(claim, out int id)) return id;
            return _fallbackAccountId;
        }
        set => _fallbackAccountId = value;
    }

    public static string _Username
    {
        get => CurrentUser?.FindFirst(ClaimTypes.Name)?.Value ?? _fallbackUsername ?? string.Empty;
        set => _fallbackUsername = value;
    }

    public static string _FullName
    {
        get => CurrentUser?.FindFirst("FullName")?.Value ?? _fallbackFullName ?? string.Empty;
        set => _fallbackFullName = value;
    }

    public static string _Email
    {
        get => CurrentUser?.FindFirst(ClaimTypes.Email)?.Value ?? _fallbackEmail ?? string.Empty;
        set => _fallbackEmail = value;
    }

    public static string _Phone
    {
        get => CurrentUser?.FindFirst(ClaimTypes.MobilePhone)?.Value ?? _fallbackPhone ?? string.Empty;
        set => _fallbackPhone = value;
    }

    public static int _RoleId
    {
        get
        {
            var claim = CurrentUser?.FindFirst("RoleId")?.Value;
            if (int.TryParse(claim, out int roleId)) return roleId;
            return _fallbackRoleId;
        }
        set => _fallbackRoleId = value;
    }

    public static string _Avatar
    {
        get => CurrentUser?.FindFirst("Avatar")?.Value ?? _fallbackAvatar ?? string.Empty;
        set => _fallbackAvatar = value;
    }

    [ThreadStatic]
    private static string? _fallbackSkillLevel;

    public static string _SkillLevel
    {
        get => CurrentUser?.FindFirst("SkillLevel")?.Value ?? _fallbackSkillLevel ?? string.Empty;
        set => _fallbackSkillLevel = value;
    }

    public static bool IsLogin()
    {
        if (CurrentUser?.Identity?.IsAuthenticated == true && _AccountId > 0)
        {
            return true;
        }
        return _fallbackAccountId > 0 && !string.IsNullOrEmpty(_fallbackUsername);
    }

    public static bool IsAdmin()
    {
        return IsLogin() && _RoleId == 1;
    }

    public static bool IsStaffOrAdmin()
    {
        return IsLogin() && (_RoleId == 1 || _RoleId == 3);
    }

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
            new("SkillLevel", account.SkillLevel ?? string.Empty)
        };

        var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
        var principal = new ClaimsPrincipal(identity);

        await httpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            principal,
            new AuthenticationProperties
            {
                IsPersistent = isPersistent,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7),
                AllowRefresh = true
            });

        // Also update current request user immediately
        httpContext.User = principal;

        // Remove legacy insecure cookie if present
        httpContext.Response.Cookies.Delete("pb_admin_uid");
    }

    public static async Task SignOutAccountAsync(HttpContext httpContext)
    {
        ClearFallbackState();
        httpContext.Response.Cookies.Delete("pb_admin_uid");
        await httpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
    }

    public static void ClearFallbackState()
    {
        _fallbackAccountId = 0;
        _fallbackUsername = string.Empty;
        _fallbackFullName = string.Empty;
        _fallbackEmail = string.Empty;
        _fallbackPhone = string.Empty;
        _fallbackRoleId = 0;
        _fallbackAvatar = string.Empty;
        _Message = string.Empty;
        _MessageEmail = string.Empty;
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
        string stFormD = str.Normalize(NormalizationForm.FormD);
        StringBuilder sb = new StringBuilder();
        for (int ich = 0; ich < stFormD.Length; ich++)
        {
            UnicodeCategory uc = CharUnicodeInfo.GetUnicodeCategory(stFormD[ich]);
            if (uc != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(stFormD[ich]);
            }
        }
        sb = sb.Replace('Đ', 'D');
        sb = sb.Replace('đ', 'd');
        return (sb.ToString().Normalize(NormalizationForm.FormC));
    }
}
