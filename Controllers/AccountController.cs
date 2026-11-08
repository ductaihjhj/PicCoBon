using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers;

public class AccountController : Controller
{
    private static readonly HashSet<string> ValidGenders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Nam", "Nữ", "Khác", ""
    };

    private readonly PickleballContext _context;
    private readonly PasswordService _passwordService;
    private readonly PricingService _pricingService;
    private readonly ClubSettingsService _settingsService;

    public AccountController(
        PickleballContext context,
        PasswordService passwordService,
        PricingService pricingService,
        ClubSettingsService settingsService)
    {
        _context = context;
        _passwordService = passwordService;
        _pricingService = pricingService;
        _settingsService = settingsService;
    }

    public IActionResult Login()
    {
        if (Function.IsLogin())
        {
            return RedirectToAction("Profile", "Account");
        }
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrEmpty(password))
        {
            TempData["AuthError"] = "Vui lòng nhập đầy đủ tài khoản và mật khẩu!";
            return RedirectToAction("Login", "Account");
        }

        string u = username.Trim();
        string normPhone = BookingService.NormalizePhone(u);
        var check = await _context.TbAccounts
            .FirstOrDefaultAsync(m =>
                m.Username == u ||
                m.Phone == u ||
                (!string.IsNullOrEmpty(normPhone) && m.Phone == normPhone) ||
                m.Email == u);

        if (check == null)
        {
            TempData["AuthError"] = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Login", "Account");
        }

        var verifyResult = _passwordService.VerifyPassword(check, check.Password, password, out string? upgradedHash);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            TempData["AuthError"] = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Login", "Account");
        }

        if (!check.IsActive)
        {
            TempData["AuthError"] = "Tài khoản của bạn hiện đang bị khóa!";
            return RedirectToAction("Login", "Account");
        }

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded && !string.IsNullOrEmpty(upgradedHash))
        {
            check.Password = upgradedHash;
        }

        if (string.IsNullOrEmpty(check.SecurityStamp))
        {
            check.SecurityStamp = Function.GenerateSecurityStamp();
        }

        check.LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, check);

        if (check.MustChangePassword)
        {
            TempData["ProfileError"] = "Tài khoản đang sử dụng mật khẩu tạm thời. Vui lòng cập nhật mật khẩu mới ngay bên dưới để bảo mật!";
            return RedirectToAction("Profile", "Account");
        }

        if (check.RoleId == 1 || check.RoleId == 3)
        {
            return RedirectToAction("Index", "Home", new { area = "Admin" });
        }

        return RedirectToAction("Profile", "Account");
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> Register(TbAccount account, string? ConfirmPassword)
    {
        if (account == null || string.IsNullOrWhiteSpace(account.Username) || string.IsNullOrEmpty(account.Password))
        {
            TempData["AuthError"] = "Vui lòng điền đầy đủ tên đăng nhập và mật khẩu!";
            return RedirectToAction("Login", "Account");
        }

        string username = account.Username.Trim();
        if (username.Length < 3 || username.Length > 50)
        {
            TempData["AuthError"] = "Tên đăng nhập phải từ 3 đến 50 ký tự!";
            return RedirectToAction("Login", "Account");
        }

        if (account.Password.Length < 6)
        {
            TempData["AuthError"] = "Mật khẩu phải có tối thiểu 6 ký tự!";
            return RedirectToAction("Login", "Account");
        }

        if (!string.IsNullOrEmpty(ConfirmPassword) && account.Password != ConfirmPassword)
        {
            TempData["AuthError"] = "Mật khẩu xác nhận không khớp!";
            return RedirectToAction("Login", "Account");
        }

        if (!BookingService.IsValidPhone(account.Phone))
        {
            TempData["AuthError"] = "Số điện thoại không hợp lệ (gồm 10 chữ số, bắt đầu bằng 0 hoặc +84)!";
            return RedirectToAction("Login", "Account");
        }

        string normalizedPhone = BookingService.NormalizePhone(account.Phone);
        string? normalizedEmail = string.IsNullOrWhiteSpace(account.Email) ? null : account.Email.Trim().ToLowerInvariant();

        var existing = await _context.TbAccounts.FirstOrDefaultAsync(m =>
            m.Username == username ||
            m.Phone == normalizedPhone ||
            (normalizedEmail != null && m.Email == normalizedEmail));

        if (existing != null)
        {
            TempData["AuthError"] = "Tên đăng nhập, Email hoặc Số điện thoại đã được đăng ký!";
            return RedirectToAction("Login", "Account");
        }

        var newAccount = new TbAccount
        {
            Username = username,
            FullName = string.IsNullOrWhiteSpace(account.FullName) ? username : account.FullName.Trim(),
            Phone = normalizedPhone,
            Email = normalizedEmail,
            SkillLevel = "Giao lưu (DUPR 3.0)",
            MemberTier = "Hội viên Tiêu chuẩn",
            DominantHand = "Tay phải",
            RoleId = 2, // Strictly Customer role (P0-03)
            IsActive = true,
            MustChangePassword = false,
            SecurityStamp = Function.GenerateSecurityStamp(),
            LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd")
        };

        newAccount.Password = _passwordService.HashPassword(newAccount, account.Password);

        _context.TbAccounts.Add(newAccount);
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, newAccount);

        TempData["ProfileSuccess"] = "Chào mừng hội viên mới! Hãy hoàn thiện hồ sơ VĐV của bạn.";
        return RedirectToAction("Profile", "Account");
    }

    /// <summary>
    /// P1-02 (BLOCKED_EXTERNAL): Honest server endpoint when SMS/SMTP OTP provider is not configured.
    /// Never fakes OTP success!
    /// </summary>
    [HttpPost("/api/auth/forgot-password")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> ForgotPasswordRequest()
    {
        var settings = await _settingsService.GetSettingsAsync(_context);
        return StatusCode(503, new
        {
            ok = false,
            code = "OTP_PROVIDER_NOT_CONFIGURED",
            error = $"Tính năng khôi phục mật khẩu tự động qua SMS/Email OTP hiện chưa được kích hoạt cổng nhà mạng. Vui lòng liên hệ Quầy lễ tân CLB hoặc Hotline {settings.Hotline} để xác minh chính chủ và cấp mật khẩu tạm thời một lần."
        });
    }

    [Authorize]
    public async Task<IActionResult> Profile()
    {
        if (!Function.IsLogin())
        {
            return RedirectToAction("Login", "Account");
        }

        int currentAccountId = Function._AccountId;
        var account = await _context.TbAccounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a => a.AccountId == currentAccountId && a.IsActive);

        if (account == null)
        {
            await Function.SignOutAccountAsync(HttpContext);
            return RedirectToAction("Login", "Account");
        }

        // P0-07 & P1-16: Strictly query bookings owned by verified AccountId (NEVER by unverified phone match!)
        var myBookings = await _context.TbBookings
            .AsNoTracking()
            .Include(b => b.Court)
            .Where(b => b.AccountId == account.AccountId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();

        var validBookings = myBookings
            .Where(b => b.Status is not ("cancelled" or "rejected" or "expired" or "no_show"))
            .ToList();

        int totalMatches = validBookings.Count;
        double totalHours = Math.Round(validBookings.Sum(b => (b.Duration > 0 ? b.Duration : 60) / 60.0), 1);

        // P1-16: Calculate member tier strictly from net ledger PAID spend, never unpaid bookings
        int totalSpent = await _pricingService.GetVerifiedAccountNetPaidSpendAsync(_context, account.AccountId);
        var (resolvedTier, discountPercent) = PricingService.ResolveMemberTier(totalSpent, account.MemberTier);

        if (string.IsNullOrWhiteSpace(account.MemberTier) ||
            (totalSpent >= 5_000_000 && account.MemberTier != "Hội viên Vàng VIP") ||
            (totalSpent >= 2_000_000 && totalSpent < 5_000_000 && account.MemberTier == "Hội viên Tiêu chuẩn"))
        {
            account.MemberTier = resolvedTier;
            await _context.SaveChangesAsync();
        }

        ViewBag.MyBookings = myBookings;
        ViewBag.Bookings = myBookings;
        ViewBag.TotalMatches = totalMatches;
        ViewBag.TotalBookings = myBookings.Count;
        ViewBag.TotalHours = totalHours;
        ViewBag.TotalSpent = totalSpent;
        ViewBag.MemberTier = account.MemberTier ?? resolvedTier;
        ViewBag.TierDiscountPercent = discountPercent;

        return View(account);
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UpdateProfile(
        string fullName,
        string phone,
        string? email,
        string? birthDate,
        string? gender,
        string? emergencyContact,
        string? address,
        string? avatar,
        string? skillLevel,
        string? dominantHand,
        string? paddleBrand,
        string? playStyle,
        string? bio)
    {
        if (!Function.IsLogin())
        {
            return RedirectToAction("Login", "Account");
        }

        int currentAccountId = Function._AccountId;
        var account = await _context.TbAccounts.FindAsync(currentAccountId);
        if (account == null || !account.IsActive)
        {
            return RedirectToAction("Login", "Account");
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2 || fullName.Trim().Length > 100)
        {
            TempData["ProfileError"] = "Họ và tên phải từ 2 đến 100 ký tự!";
            return RedirectToAction("Profile");
        }

        if (!BookingService.IsValidPhone(phone))
        {
            TempData["ProfileError"] = "Số điện thoại không hợp lệ (10 chữ số, bắt đầu bằng 0 hoặc +84)!";
            return RedirectToAction("Profile");
        }

        string normalizedPhone = BookingService.NormalizePhone(phone);
        bool phoneExists = await _context.TbAccounts.AnyAsync(a =>
            a.AccountId != account.AccountId && a.Phone == normalizedPhone);

        if (phoneExists)
        {
            TempData["ProfileError"] = "Số điện thoại này đã được liên kết với một tài khoản khác!";
            return RedirectToAction("Profile");
        }

        string? normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (normalizedEmail != null)
        {
            if (!normalizedEmail.Contains('@') || normalizedEmail.Length > 150)
            {
                TempData["ProfileError"] = "Địa chỉ Email không hợp lệ!";
                return RedirectToAction("Profile");
            }

            bool emailExists = await _context.TbAccounts.AnyAsync(a =>
                a.AccountId != account.AccountId && a.Email == normalizedEmail);
            if (emailExists)
            {
                TempData["ProfileError"] = "Địa chỉ Email này đã được sử dụng bởi tài khoản khác!";
                return RedirectToAction("Profile");
            }
        }

        if (!string.IsNullOrWhiteSpace(birthDate) && DateOnly.TryParse(birthDate.Trim(), out var parsedDob))
        {
            var today = DateOnly.FromDateTime(BookingService.GetVietnamNow());
            if (parsedDob >= today || parsedDob.Year < 1920)
            {
                TempData["ProfileError"] = "Ngày sinh không hợp lệ!";
                return RedirectToAction("Profile");
            }
            account.BirthDate = parsedDob.ToString("yyyy-MM-dd");
        }

        string cleanGender = (gender ?? string.Empty).Trim();
        if (!ValidGenders.Contains(cleanGender))
        {
            cleanGender = "Nam";
        }

        account.FullName = fullName.Trim();
        account.Email = normalizedEmail;
        account.Phone = normalizedPhone;
        account.Gender = string.IsNullOrEmpty(cleanGender) ? null : cleanGender;
        account.EmergencyContact = string.IsNullOrWhiteSpace(emergencyContact) ? null : emergencyContact.Trim()[..Math.Min(emergencyContact.Trim().Length, 150)];
        account.Address = string.IsNullOrWhiteSpace(address) ? null : address.Trim()[..Math.Min(address.Trim().Length, 250)];
        account.SkillLevel = string.IsNullOrWhiteSpace(skillLevel) ? "Giao lưu (DUPR 3.0)" : skillLevel.Trim()[..Math.Min(skillLevel.Trim().Length, 100)];
        account.DominantHand = string.IsNullOrWhiteSpace(dominantHand) ? "Tay phải" : dominantHand.Trim()[..Math.Min(dominantHand.Trim().Length, 50)];
        account.PaddleBrand = string.IsNullOrWhiteSpace(paddleBrand) ? null : paddleBrand.Trim()[..Math.Min(paddleBrand.Trim().Length, 150)];
        account.PlayStyle = string.IsNullOrWhiteSpace(playStyle) ? null : playStyle.Trim()[..Math.Min(playStyle.Trim().Length, 150)];
        account.Bio = string.IsNullOrWhiteSpace(bio) ? null : bio.Trim()[..Math.Min(bio.Trim().Length, 500)];

        if (!string.IsNullOrWhiteSpace(avatar))
        {
            string trimmedAvatar = avatar.Trim();
            if (trimmedAvatar.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                trimmedAvatar.StartsWith("https://", StringComparison.OrdinalIgnoreCase) ||
                trimmedAvatar.StartsWith("/", StringComparison.OrdinalIgnoreCase))
            {
                account.Avatar = trimmedAvatar[..Math.Min(trimmedAvatar.Length, 500)];
            }
        }

        await _context.SaveChangesAsync();
        await Function.SignInAccountAsync(HttpContext, account);

        TempData["ProfileSuccess"] = "Đã cập nhật Hồ sơ Hội viên & Vận động viên thành công!";
        return RedirectToAction("Profile");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ChangePassword(string currentPassword, string newPassword, string confirmPassword)
    {
        if (!Function.IsLogin())
        {
            return RedirectToAction("Login", "Account");
        }

        var account = await _context.TbAccounts.FindAsync(Function._AccountId);
        if (account == null || !account.IsActive)
        {
            return RedirectToAction("Login", "Account");
        }

        var verify = _passwordService.VerifyPassword(account, account.Password, currentPassword, out _);
        if (verify == PasswordVerificationResult.Failed)
        {
            TempData["ProfileError"] = "Mật khẩu hiện tại không chính xác!";
            return RedirectToAction("Profile");
        }

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 6)
        {
            TempData["ProfileError"] = "Mật khẩu mới phải có ít nhất 6 ký tự!";
            return RedirectToAction("Profile");
        }

        if (newPassword != confirmPassword)
        {
            TempData["ProfileError"] = "Mật khẩu xác nhận không khớp!";
            return RedirectToAction("Profile");
        }

        account.Password = _passwordService.HashPassword(account, newPassword);
        account.MustChangePassword = false;
        account.SecurityStamp = Function.GenerateSecurityStamp();
        await _context.SaveChangesAsync();

        // Re-sign current browser session with rotated SecurityStamp while revoking other stale sessions
        await Function.SignInAccountAsync(HttpContext, account);

        TempData["ProfileSuccess"] = "Đổi mật khẩu bảo mật thành công!";
        return RedirectToAction("Profile");
    }

    public async Task<IActionResult> Logout()
    {
        await Function.SignOutAccountAsync(HttpContext);
        return RedirectToAction("Index", "Home");
    }
}
