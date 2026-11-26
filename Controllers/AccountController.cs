using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PickleballClub.Configuration;
using PickleballClub.Logging;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PickleballClub.Controllers;

public class AccountController : Controller
{
    private static readonly HashSet<string> ValidGenders = new(StringComparer.OrdinalIgnoreCase)
    {
        "Nam", "Nữ", "Khác", ""
    };

    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AllowedAvatarContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/pjpeg", "image/png", "image/webp"
    };

    private const long MaxAvatarUploadBytes = 2 * 1024 * 1024;
    private const int AvatarOutputSizePx = 360;
    private const int MaxAvatarSourceDimension = 6000;
    private const string AvatarUploadRelativeDir = "uploads/avatars";

    private readonly PickleballContext _context;
    private readonly PasswordService _passwordService;
    private readonly PricingService _pricingService;
    private readonly ClubSettingsService _settingsService;
    private readonly IWebHostEnvironment? _environment;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        PickleballContext context,
        PasswordService passwordService,
        PricingService pricingService,
        ClubSettingsService settingsService,
        IWebHostEnvironment? environment = null,
        ILogger<AccountController>? logger = null)
    {
        _context = context;
        _passwordService = passwordService;
        _pricingService = pricingService;
        _settingsService = settingsService;
        _environment = environment;
        _logger = logger ?? NullLogger<AccountController>.Instance;
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
        string lowerU = u.ToLowerInvariant();
        string normPhone = BookingService.NormalizePhone(u);
        var check = await _context.TbAccounts
            .FirstOrDefaultAsync(m =>
                (m.Username != null && m.Username.ToLower() == lowerU) ||
                m.Phone == u ||
                (!string.IsNullOrEmpty(normPhone) && m.Phone == normPhone) ||
                (m.Email != null && m.Email.ToLower() == lowerU));

        if (check == null)
        {
            _logger.LogWarning("Login failed for Identifier={Identifier}: Reason=AccountNotFound", CorrelationContext.MaskIdentifier(u));
            TempData["AuthError"] = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Login", "Account");
        }

        var verifyResult = _passwordService.VerifyPassword(check, check.Password, password, out string? upgradedHash);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            _logger.LogWarning("Login failed for AccountId={AccountId}: Reason=InvalidPassword", check.AccountId);
            TempData["AuthError"] = "Tài khoản hoặc mật khẩu không chính xác!";
            return RedirectToAction("Login", "Account");
        }

        if (!check.IsActive)
        {
            _logger.LogWarning("Login blocked for AccountId={AccountId}: Reason=AccountLocked", check.AccountId);
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
        _logger.LogInformation("User signed in via MVC (AccountId={AccountId}, RoleId={RoleId})", check.AccountId, check.RoleId);

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
    public async Task<IActionResult> Register(
        TbAccount account,
        string? ConfirmPassword,
        [FromServices] RegistrationOtpService registrationOtpService)
    {
        if (account == null)
        {
            TempData["RegisterError"] = "Vui lòng điền đầy đủ thông tin đăng ký.";
            return RedirectToAction("Login", "Account", new { mode = "register" });
        }

        string effectiveFullName = !string.IsNullOrWhiteSpace(account.FullName)
            ? account.FullName.Trim()
            : (account.Username ?? string.Empty).Trim();

        var result = await registrationOtpService.StartRegistrationAsync(
            _context,
            HttpContext,
            new RegistrationStartInput
            {
                FullName = effectiveFullName,
                Username = string.IsNullOrWhiteSpace(account.Username) ? null : account.Username.Trim(),
                Phone = account.Phone ?? string.Empty,
                Email = account.Email ?? string.Empty,
                Password = account.Password ?? string.Empty,
                ConfirmPassword = ConfirmPassword,
                ClientIp = registrationOtpService.ResolveClientIp(HttpContext)
            },
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            TempData["RegisterError"] = result.ErrorMessage ?? "Không thể gửi mã xác minh đăng ký.";
            return RedirectToAction("Login", "Account", new { mode = "register" });
        }

        TempData["RegisterInfo"] = result.Message;
        return RedirectToAction("Login", "Account", new { mode = "register" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> VerifyRegisterOtp(
        string? verificationToken,
        string? otp,
        [FromServices] RegistrationOtpService registrationOtpService)
    {
        var result = await registrationOtpService.VerifyOtpAndCreateAccountAsync(
            _context,
            HttpContext,
            verificationToken,
            otp,
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            TempData["RegisterError"] = result.ErrorMessage ?? "Không thể xác minh mã OTP.";
            return RedirectToAction("Login", "Account", new { mode = "register" });
        }

        TempData["RegisterVerified"] = "true";
        TempData["RegisterSuccessMessage"] = result.Message ?? "Đăng ký thành công! Bạn có thể đăng nhập ngay.";
        return RedirectToAction("Login", "Account", new { mode = "register" });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> ResendRegisterOtp(
        string? verificationToken,
        [FromServices] RegistrationOtpService registrationOtpService)
    {
        var result = await registrationOtpService.ResendOtpAsync(
            _context,
            HttpContext,
            verificationToken,
            registrationOtpService.ResolveClientIp(HttpContext),
            HttpContext.RequestAborted);

        if (!result.Success)
        {
            TempData["RegisterError"] = result.ErrorMessage ?? "Chưa thể gửi lại mã xác minh.";
        }
        else
        {
            TempData["RegisterInfo"] = result.Message;
        }

        return RedirectToAction("Login", "Account", new { mode = "register" });
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

    private IActionResult ProfileValidationError(string errorMessage, bool isAjax)
    {
        if (isAjax)
        {
            return BadRequest(new { ok = false, error = errorMessage });
        }
        TempData["ProfileError"] = errorMessage;
        return RedirectToAction("Profile");
    }

    [Authorize]
    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestFormLimits(MultipartBodyLengthLimit = 5 * 1024 * 1024)]
    public async Task<IActionResult> UpdateProfile(
        string fullName,
        string phone,
        string? email,
        string? birthDate,
        string? gender,
        string? emergencyContact,
        string? address,
        string? avatar,
        IFormFile? avatarFile,
        string? skillLevel,
        string? dominantHand,
        string? paddleBrand,
        string? playStyle,
        string? bio)
    {
        bool isAjax = string.Equals(Request.Headers["X-Requested-With"], "XMLHttpRequest", StringComparison.OrdinalIgnoreCase);

        if (!Function.IsLogin())
        {
            return isAjax ? Unauthorized(new { ok = false, error = "Phiên đăng nhập đã hết hạn." }) : RedirectToAction("Login", "Account");
        }

        int currentAccountId = Function._AccountId;
        var account = await _context.TbAccounts.FindAsync(currentAccountId);
        if (account == null || !account.IsActive)
        {
            return isAjax ? Unauthorized(new { ok = false, error = "Tài khoản không hợp lệ." }) : RedirectToAction("Login", "Account");
        }

        if (string.IsNullOrWhiteSpace(fullName) || fullName.Trim().Length < 2 || fullName.Trim().Length > 100)
        {
            return ProfileValidationError("Họ và tên phải từ 2 đến 100 ký tự!", isAjax);
        }

        if (!BookingService.IsValidPhone(phone))
        {
            return ProfileValidationError("Số điện thoại không hợp lệ (10 chữ số, bắt đầu bằng 0 hoặc +84)!", isAjax);
        }

        string normalizedPhone = BookingService.NormalizePhone(phone);
        bool phoneExists = await _context.TbAccounts.AnyAsync(a =>
            a.AccountId != account.AccountId && a.Phone == normalizedPhone);

        if (phoneExists)
        {
            return ProfileValidationError("Số điện thoại này đã được liên kết với một tài khoản khác!", isAjax);
        }

        string? previousNormalizedEmail = string.IsNullOrWhiteSpace(account.Email)
            ? null
            : account.Email.Trim().ToLowerInvariant();
        string? normalizedEmail = string.IsNullOrWhiteSpace(email) ? null : email.Trim().ToLowerInvariant();
        if (normalizedEmail != null)
        {
            if (!EmailSmtpOptions.IsValidEmailFormat(normalizedEmail))
            {
                return ProfileValidationError("Địa chỉ Email không hợp lệ!", isAjax);
            }

            bool emailExists = await _context.TbAccounts.AnyAsync(a =>
                a.AccountId != account.AccountId && a.Email != null && a.Email.ToLower() == normalizedEmail);
            if (emailExists)
            {
                return ProfileValidationError("Địa chỉ Email này đã được sử dụng bởi tài khoản khác!", isAjax);
            }
        }

        if (!string.IsNullOrWhiteSpace(birthDate) && DateOnly.TryParse(birthDate.Trim(), out var parsedDob))
        {
            var today = DateOnly.FromDateTime(BookingService.GetVietnamNow());
            if (parsedDob >= today || parsedDob.Year < 1920)
            {
                return ProfileValidationError("Ngày sinh không hợp lệ!", isAjax);
            }
            account.BirthDate = parsedDob.ToString("yyyy-MM-dd");
        }

        string cleanGender = (gender ?? string.Empty).Trim();
        if (!ValidGenders.Contains(cleanGender))
        {
            cleanGender = "Nam";
        }

        if (!string.Equals(previousNormalizedEmail, normalizedEmail, StringComparison.OrdinalIgnoreCase))
        {
            account.EmailVerifiedAtUtc = null;
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

        string? previousUploadedAvatar = null;
        if (avatarFile != null && avatarFile.Length > 0)
        {
            var (ok, savedRelativeUrl, uploadError) = await SaveUploadedAvatarAsync(account.AccountId, avatarFile);
            if (!ok || string.IsNullOrEmpty(savedRelativeUrl))
            {
                return ProfileValidationError(uploadError ?? "Không thể xử lý ảnh đại diện tải lên.", isAjax);
            }

            if (!string.IsNullOrWhiteSpace(account.Avatar) &&
                account.Avatar.StartsWith($"/{AvatarUploadRelativeDir}/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(account.Avatar, savedRelativeUrl, StringComparison.OrdinalIgnoreCase))
            {
                previousUploadedAvatar = account.Avatar;
            }

            account.Avatar = savedRelativeUrl;
        }
        else if (!string.IsNullOrWhiteSpace(avatar))
        {
            string trimmedAvatar = avatar.Trim();
            if (trimmedAvatar.Length > 500)
            {
                return ProfileValidationError("Đường dẫn ảnh đại diện quá dài (tối đa 500 ký tự).", isAjax);
            }

            string safeAvatar = Function.GetSafeAvatarUrl(trimmedAvatar);
            if (string.IsNullOrEmpty(safeAvatar))
            {
                return ProfileValidationError("Ảnh đại diện được chọn không hợp lệ.", isAjax);
            }

            if (!string.IsNullOrWhiteSpace(account.Avatar) &&
                account.Avatar.StartsWith($"/{AvatarUploadRelativeDir}/", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(account.Avatar, safeAvatar, StringComparison.OrdinalIgnoreCase))
            {
                previousUploadedAvatar = account.Avatar;
            }

            account.Avatar = safeAvatar;
        }

        await _context.SaveChangesAsync();
        if (!string.IsNullOrEmpty(previousUploadedAvatar))
        {
            TryDeletePreviousUploadedAvatar(account.AccountId, previousUploadedAvatar);
        }

        await Function.SignInAccountAsync(HttpContext, account);
        _logger.LogInformation("Profile updated for AccountId={AccountId}, HasUploadedAvatar={HasUpload}",
            account.AccountId, avatarFile != null && avatarFile.Length > 0);

        const string successMessage = "Đã cập nhật Hồ sơ Hội viên & Vận động viên thành công!";
        if (isAjax)
        {
            var antiforgery = HttpContext.RequestServices.GetService(typeof(Microsoft.AspNetCore.Antiforgery.IAntiforgery)) as Microsoft.AspNetCore.Antiforgery.IAntiforgery;
            string? nextCsrf = antiforgery?.GetAndStoreTokens(HttpContext).RequestToken;
            return Ok(new
            {
                ok = true,
                message = successMessage,
                avatar = Function.GetSafeAvatarUrl(account.Avatar),
                fullName = account.FullName,
                initials = Function.GetAvatarInitials(account.FullName, account.Username),
                csrfToken = nextCsrf
            });
        }

        TempData["ProfileSuccess"] = successMessage;
        return RedirectToAction("Profile");
    }

    private async Task<(bool Ok, string? RelativeUrl, string? ErrorMessage)> SaveUploadedAvatarAsync(int accountId, IFormFile file)
    {
        if (file.Length <= 0)
        {
            return (false, null, "Tệp ảnh đại diện rỗng.");
        }

        if (file.Length > MaxAvatarUploadBytes)
        {
            return (false, null, "Ảnh đại diện vượt quá dung lượng tối đa 2 MB.");
        }

        string ext = Path.GetExtension(file.FileName ?? string.Empty);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedAvatarExtensions.Contains(ext))
        {
            return (false, null, "Định dạng ảnh không được hỗ trợ. Vui lòng chọn tệp JPG, JPEG, PNG hoặc WEBP.");
        }

        if (!string.IsNullOrWhiteSpace(file.ContentType) && !AllowedAvatarContentTypes.Contains(file.ContentType))
        {
            return (false, null, "Loại tệp MIME ảnh không hợp lệ (chỉ chấp nhận JPG, PNG, WEBP).");
        }

        try
        {
            await using var stream = file.OpenReadStream();
            var info = await Image.IdentifyAsync(stream);
            if (info == null || info.Width < 16 || info.Height < 16 ||
                info.Width > MaxAvatarSourceDimension || info.Height > MaxAvatarSourceDimension)
            {
                return (false, null, "Kích thước ảnh không hợp lệ hoặc vượt quá 6000x6000 px.");
            }

            stream.Position = 0;
            using var image = await Image.LoadAsync(stream);
            image.Mutate(ctx => ctx
                .AutoOrient()
                .Resize(new ResizeOptions
                {
                    Size = new Size(AvatarOutputSizePx, AvatarOutputSizePx),
                    Mode = ResizeMode.Crop,
                    Position = AnchorPositionMode.Center
                }));

            // Strip EXIF/metadata privacy fields before saving sanitized WebP asset
            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;

            string uploadDir = GetAvatarUploadDirectory();
            Directory.CreateDirectory(uploadDir);

            string token = Guid.NewGuid().ToString("N")[..10];
            string safeFileName = $"spk-avatar-{accountId}-{token}.webp";
            string physicalPath = Path.Combine(uploadDir, safeFileName);
            await image.SaveAsWebpAsync(physicalPath);

            return (true, $"/{AvatarUploadRelativeDir}/{safeFileName}", null);
        }
        catch (UnknownImageFormatException)
        {
            return (false, null, "Nội dung tệp không phải là ảnh hợp lệ (JPG, PNG, WEBP).");
        }
        catch (InvalidImageContentException)
        {
            return (false, null, "Tệp ảnh bị hỏng hoặc không thể giải mã.");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Avatar upload decode failed for AccountId={AccountId}", accountId);
            return (false, null, "Không thể xử lý ảnh tải lên. Vui lòng thử một ảnh khác.");
        }
    }

    private string GetAvatarUploadDirectory()
    {
        string? webRoot = _environment?.WebRootPath;
        if (string.IsNullOrWhiteSpace(webRoot))
        {
            webRoot = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot");
        }

        return Path.Combine(webRoot, "uploads", "avatars");
    }

    private void TryDeletePreviousUploadedAvatar(int accountId, string relativeUrl)
    {
        try
        {
            string fileName = Path.GetFileName(relativeUrl);
            if (string.IsNullOrWhiteSpace(fileName) ||
                !fileName.StartsWith($"spk-avatar-{accountId}-", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            string avatarsRoot = Path.GetFullPath(GetAvatarUploadDirectory());
            string candidate = Path.GetFullPath(Path.Combine(avatarsRoot, fileName));
            if (candidate.StartsWith(avatarsRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) &&
                System.IO.File.Exists(candidate))
            {
                System.IO.File.Delete(candidate);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed cleaning up replaced avatar file");
        }
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

        if (string.IsNullOrWhiteSpace(newPassword) || newPassword.Length < 8)
        {
            TempData["ProfileError"] = "Mật khẩu mới phải có ít nhất 8 ký tự!";
            return RedirectToAction("Profile");
        }

        if (string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            TempData["ProfileError"] = "Mật khẩu mới không được trùng với mật khẩu tạm thời hiện tại!";
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
