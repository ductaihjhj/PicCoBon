using System;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Services;

public sealed class RegistrationStartInput
{
    public string FullName { get; init; } = string.Empty;
    public string? Username { get; init; }
    public string Phone { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string Password { get; init; } = string.Empty;
    public string? ConfirmPassword { get; init; }
    public string? ClientIp { get; init; }
}

public sealed class RegistrationOtpResult
{
    public bool Success { get; init; }
    public int StatusCode { get; init; } = StatusCodes.Status200OK;
    public string Status { get; init; } = string.Empty;
    public string? ErrorCode { get; init; }
    public string? ErrorMessage { get; init; }
    public string? Message { get; init; }
    public string? VerificationToken { get; init; }
    public string? MaskedEmail { get; init; }
    public int ExpiresInSeconds { get; init; }
    public int ResendCooldownSeconds { get; init; }
    public int RemainingAttempts { get; init; }
    public int MaxAttempts { get; init; }
    public int? RetryAfterSeconds { get; init; }
    public bool CanResend { get; init; }
    public int? CreatedAccountId { get; init; }

    public static RegistrationOtpResult Fail(
        int statusCode,
        string errorCode,
        string errorMessage,
        int? retryAfterSeconds = null,
        string? verificationToken = null,
        string? maskedEmail = null,
        int resendCooldownSeconds = 0,
        bool canResend = false,
        int? remainingAttempts = null) => new()
    {
        Success = false,
        StatusCode = statusCode,
        ErrorCode = errorCode,
        ErrorMessage = errorMessage,
        RetryAfterSeconds = retryAfterSeconds,
        VerificationToken = verificationToken,
        MaskedEmail = maskedEmail,
        ResendCooldownSeconds = resendCooldownSeconds,
        CanResend = canResend,
        RemainingAttempts = remainingAttempts ?? 0
    };
}

public sealed class RegistrationPendingSessionState
{
    public bool HasPending { get; init; }
    public string? VerificationToken { get; init; }
    public string? MaskedEmail { get; init; }
    public int ExpiresInSeconds { get; init; }
    public int ResendCooldownSeconds { get; init; }
    public int RemainingAttempts { get; init; }
    public int MaxAttempts { get; init; }
    public bool DeliveryFailed { get; init; }
}

public class RegistrationOtpService
{
    public const string SessionBindingCookieName = "spike_reg_sid";
    public const string RequestTokenCookieName = "spike_reg_tok";
    public const string EmailSubject = "Mã xác minh đăng ký Spike Pickleball Club";

    private const string GenericAccountConflictMessage =
        "Thông tin đăng ký chưa thể sử dụng. Nếu bạn đã có tài khoản hội viên, vui lòng chuyển sang Đăng nhập hoặc liên hệ CLB.";

    private static readonly Regex SafeUsernameCharsRegex = new(@"^[a-zA-Z0-9._-]+$", RegexOptions.Compiled);
    private static readonly Regex Hex64Regex = new(@"^[a-fA-F0-9]{64}$", RegexOptions.Compiled);

    private readonly PasswordService _passwordService;
    private readonly IEmailSender _emailSender;
    private readonly IOptionsMonitor<RegistrationOtpOptions> _otpOptions;
    private readonly IConfiguration _configuration;
    private readonly IHostEnvironment _environment;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<RegistrationOtpService> _logger;

    public RegistrationOtpService(
        PasswordService passwordService,
        IEmailSender emailSender,
        IOptionsMonitor<RegistrationOtpOptions> otpOptions,
        IConfiguration configuration,
        IHostEnvironment environment,
        TimeProvider? timeProvider = null,
        ILogger<RegistrationOtpService>? logger = null)
    {
        _passwordService = passwordService;
        _emailSender = emailSender;
        _otpOptions = otpOptions;
        _configuration = configuration;
        _environment = environment;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _logger = logger ?? NullLogger<RegistrationOtpService>.Instance;
    }

    private DateTime UtcNow => _timeProvider.GetUtcNow().UtcDateTime;

    public (bool IsReady, byte[] KeyBytes, string ErrorCode, string ErrorMessage) ValidateReadiness()
    {
        string? qrKey = _configuration["SPIKE_QR_SIGNING_KEY"] ?? _configuration["Security:QrSigningKey"];
        var (hmacOk, keyBytes, hmacCode, hmacMsg) = _otpOptions.CurrentValue.ValidateAndResolveKey(qrKey);
        if (!hmacOk)
        {
            return (false, Array.Empty<byte>(), hmacCode, hmacMsg);
        }

        var (smtpOk, smtpCode, smtpMsg) = _emailSender.ValidateConfiguration();
        if (!smtpOk)
        {
            return (false, Array.Empty<byte>(), smtpCode, smtpMsg);
        }

        return (true, keyBytes, string.Empty, string.Empty);
    }

    public static string NormalizeEmail(string? rawEmail) =>
        string.IsNullOrWhiteSpace(rawEmail) ? string.Empty : rawEmail.Trim().ToLowerInvariant();

    public static string MaskEmail(string normalizedEmail)
    {
        if (string.IsNullOrWhiteSpace(normalizedEmail) || !normalizedEmail.Contains('@'))
        {
            return "***";
        }

        int atIndex = normalizedEmail.IndexOf('@');
        string local = normalizedEmail[..atIndex];
        string domain = normalizedEmail[(atIndex + 1)..];

        string maskedLocal = local.Length switch
        {
            <= 1 => $"{local}***",
            2 => $"{local[0]}***",
            3 => $"{local[0]}***{local[^1]}",
            _ => $"{local[..2]}***{local[^1]}"
        };

        return $"{maskedLocal}@{domain}";
    }

    public static string GenerateSixDigitOtp()
    {
        int value = RandomNumberGenerator.GetInt32(0, 1_000_000);
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    public static string ComputeOtpHmacHex(
        byte[] hmacKeyBytes,
        string requestToken,
        int otpVersion,
        string normalizedEmail,
        string sessionBindingHash,
        string otpCode)
    {
        string contextPayload =
            $"purpose=register|req={requestToken}|ver={otpVersion}|email={normalizedEmail}|session={sessionBindingHash}|code={otpCode}";
        byte[] mac = HMACSHA256.HashData(hmacKeyBytes, Encoding.UTF8.GetBytes(contextPayload));
        return Convert.ToHexString(mac);
    }

    public static bool VerifyOtpHmacConstantTime(
        byte[] hmacKeyBytes,
        string? storedOtpHmacHex,
        string requestToken,
        int otpVersion,
        string normalizedEmail,
        string sessionBindingHash,
        string providedOtpCode)
    {
        if (string.IsNullOrWhiteSpace(storedOtpHmacHex) || storedOtpHmacHex.Length != 64)
        {
            return false;
        }

        byte[] expectedBytes;
        try
        {
            expectedBytes = Convert.FromHexString(storedOtpHmacHex);
        }
        catch
        {
            return false;
        }

        string contextPayload =
            $"purpose=register|req={requestToken}|ver={otpVersion}|email={normalizedEmail}|session={sessionBindingHash}|code={providedOtpCode}";
        byte[] actualBytes = HMACSHA256.HashData(hmacKeyBytes, Encoding.UTF8.GetBytes(contextPayload));

        return CryptographicOperations.FixedTimeEquals(expectedBytes, actualBytes);
    }

    private CookieOptions BuildCookieOptions(HttpContext httpContext, TimeSpan maxAge)
    {
        bool isDevOrTest = _environment.IsDevelopment() ||
                           _environment.IsEnvironment("SqlDebug") ||
                           _environment.IsEnvironment("Testing");

        return new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = isDevOrTest ? httpContext.Request.IsHttps : true,
            MaxAge = maxAge,
            IsEssential = true,
            Path = "/"
        };
    }

    public string EnsureSessionBinding(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(SessionBindingCookieName, out var existing) &&
            !string.IsNullOrWhiteSpace(existing) &&
            Hex64Regex.IsMatch(existing.Trim()))
        {
            return ComputeSessionBindingHash(existing.Trim().ToLowerInvariant());
        }

        string rawSid = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        httpContext.Response.Cookies.Append(
            SessionBindingCookieName,
            rawSid,
            BuildCookieOptions(httpContext, TimeSpan.FromHours(2)));
        return ComputeSessionBindingHash(rawSid);
    }

    public string? ReadSessionBindingHash(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(SessionBindingCookieName, out var existing) &&
            !string.IsNullOrWhiteSpace(existing) &&
            Hex64Regex.IsMatch(existing.Trim()))
        {
            return ComputeSessionBindingHash(existing.Trim().ToLowerInvariant());
        }

        return null;
    }

    public void SavePendingTokenCookie(HttpContext httpContext, string requestToken)
    {
        int lifetimeMins = Math.Clamp(_otpOptions.CurrentValue.RequestLifetimeMinutes, 10, 120);
        httpContext.Response.Cookies.Append(
            RequestTokenCookieName,
            requestToken,
            BuildCookieOptions(httpContext, TimeSpan.FromMinutes(lifetimeMins)));
    }

    public void ClearPendingTokenCookie(HttpContext httpContext)
    {
        httpContext.Response.Cookies.Delete(RequestTokenCookieName, new CookieOptions { Path = "/" });
    }

    public string? ReadPendingTokenCookie(HttpContext httpContext)
    {
        if (httpContext.Request.Cookies.TryGetValue(RequestTokenCookieName, out var tok) &&
            !string.IsNullOrWhiteSpace(tok) &&
            Hex64Regex.IsMatch(tok.Trim()))
        {
            return tok.Trim().ToLowerInvariant();
        }

        return null;
    }

    private static string ComputeSessionBindingHash(string rawSessionId) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"spike:reg-bind:v1:{rawSessionId}"))).ToLowerInvariant();

    private static string ComputeEmailHash(string normalizedEmail) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"spike:reg-email:v1:{normalizedEmail}"))).ToLowerInvariant();

    private static string? ComputeIpHash(string? clientIp)
    {
        if (string.IsNullOrWhiteSpace(clientIp))
        {
            return null;
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"spike:reg-ip:v1:{clientIp.Trim()}"))).ToLowerInvariant();
    }

    public string? ResolveClientIp(HttpContext httpContext)
    {
        string ip = httpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        bool allowTestHeader = _environment.IsEnvironment("Testing") ||
                               _environment.IsDevelopment() ||
                               _environment.IsEnvironment("SqlDebug");

        if ((string.IsNullOrEmpty(ip) || allowTestHeader) &&
            httpContext.Request.Headers.TryGetValue("X-Test-Client-Ip", out var testIp) &&
            !string.IsNullOrWhiteSpace(testIp))
        {
            ip = testIp.ToString().Trim();
        }

        return string.IsNullOrWhiteSpace(ip) ? null : ip;
    }

    public async Task<int> CleanupExpiredVerificationsAsync(PickleballContext db, CancellationToken cancellationToken = default)
    {
        var now = UtcNow;
        var opts = _otpOptions.CurrentValue;
        int retentionHours = Math.Clamp(opts.RetentionHoursAfterExpiry, 1, 168);
        var purgeCutoff = now.AddHours(-retentionHours);

        var newlyExpired = await db.TbRegistrationVerifications
            .Where(x => x.Status == "Pending" && x.RequestExpiresAtUtc <= now)
            .ToListAsync(cancellationToken);

        foreach (var item in newlyExpired)
        {
            item.Status = "Expired";
            item.PasswordHash = null;
            item.OtpHmac = null;
            item.UpdatedAtUtc = now;
        }

        var oldRecords = await db.TbRegistrationVerifications
            .Where(x => x.RequestExpiresAtUtc <= purgeCutoff && x.Status != "Pending")
            .ToListAsync(cancellationToken);

        if (oldRecords.Count > 0)
        {
            db.TbRegistrationVerifications.RemoveRange(oldRecords);
        }

        var oldLogs = await db.TbRegistrationEmailLogs
            .Where(x => x.CreatedAtUtc <= purgeCutoff)
            .ToListAsync(cancellationToken);

        if (oldLogs.Count > 0)
        {
            db.TbRegistrationEmailLogs.RemoveRange(oldLogs);
        }

        if (newlyExpired.Count > 0 || oldRecords.Count > 0 || oldLogs.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return newlyExpired.Count + oldRecords.Count;
    }

    public async Task<RegistrationPendingSessionState> GetPendingSessionStateAsync(
        PickleballContext db,
        HttpContext httpContext,
        string? explicitToken = null,
        CancellationToken cancellationToken = default)
    {
        string? sessionHash = ReadSessionBindingHash(httpContext);
        string? token = !string.IsNullOrWhiteSpace(explicitToken) && Hex64Regex.IsMatch(explicitToken.Trim())
            ? explicitToken.Trim().ToLowerInvariant()
            : ReadPendingTokenCookie(httpContext);

        if (string.IsNullOrEmpty(sessionHash) || string.IsNullOrEmpty(token))
        {
            return new RegistrationPendingSessionState { HasPending = false };
        }

        var now = UtcNow;
        var opts = _otpOptions.CurrentValue;
        int maxAttempts = Math.Clamp(opts.MaxVerifyAttempts, 1, 10);
        int cooldownCfg = Math.Clamp(opts.ResendCooldownSeconds, 10, 600);

        var row = await db.TbRegistrationVerifications
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.RequestToken == token, cancellationToken);

        if (row == null ||
            !string.Equals(row.SessionBindingHash, sessionHash, StringComparison.Ordinal) ||
            !string.Equals(row.Status, "Pending", StringComparison.Ordinal) ||
            row.RequestExpiresAtUtc <= now)
        {
            ClearPendingTokenCookie(httpContext);
            return new RegistrationPendingSessionState { HasPending = false };
        }

        int expiresInSec = Math.Max(0, (int)Math.Ceiling((row.OtpExpiresAtUtc - now).TotalSeconds));
        int cooldownSec = 0;
        if (row.LastSentAtUtc.HasValue)
        {
            var nextAllowed = row.LastSentAtUtc.Value.AddSeconds(cooldownCfg);
            if (nextAllowed > now)
            {
                cooldownSec = Math.Max(1, (int)Math.Ceiling((nextAllowed - now).TotalSeconds));
            }
        }

        return new RegistrationPendingSessionState
        {
            HasPending = true,
            VerificationToken = row.RequestToken,
            MaskedEmail = MaskEmail(row.Email),
            ExpiresInSeconds = expiresInSec,
            ResendCooldownSeconds = cooldownSec,
            RemainingAttempts = Math.Max(0, maxAttempts - row.AttemptCount),
            MaxAttempts = maxAttempts,
            DeliveryFailed = string.Equals(row.LastDeliveryStatus, "SendFailed", StringComparison.OrdinalIgnoreCase)
        };
    }

    public async Task<RegistrationOtpResult> StartRegistrationAsync(
        PickleballContext db,
        HttpContext httpContext,
        RegistrationStartInput input,
        CancellationToken cancellationToken = default)
    {
        string fullName = (input.FullName ?? string.Empty).Trim();
        if (fullName.Length < 2 || fullName.Length > 100)
        {
            return RegistrationOtpResult.Fail(StatusCodes.Status400BadRequest, "INVALID_FULL_NAME",
                "Họ và tên phải từ 2 đến 100 ký tự.");
        }

        if (!BookingService.IsValidPhone(input.Phone))
        {
            return RegistrationOtpResult.Fail(StatusCodes.Status400BadRequest, "INVALID_PHONE",
                "Số điện thoại không hợp lệ (gồm 9–11 chữ số, bắt đầu bằng 0 hoặc +84).");
        }

        string normalizedEmail = NormalizeEmail(input.Email);
        if (string.IsNullOrEmpty(normalizedEmail) || !EmailSmtpOptions.IsValidEmailFormat(normalizedEmail))
        {
            return RegistrationOtpResult.Fail(StatusCodes.Status400BadRequest, "INVALID_EMAIL",
                "Vui lòng nhập địa chỉ Email hợp lệ để nhận mã xác minh.");
        }

        string password = input.Password ?? string.Empty;
        if (password.Length < 6 || password.Length > 128)
        {
            return RegistrationOtpResult.Fail(StatusCodes.Status400BadRequest, "INVALID_PASSWORD",
                "Mật khẩu phải có từ 6 đến 128 ký tự.");
        }

        if (input.ConfirmPassword != null && !string.Equals(password, input.ConfirmPassword, StringComparison.Ordinal))
        {
            return RegistrationOtpResult.Fail(StatusCodes.Status400BadRequest, "PASSWORD_MISMATCH",
                "Mật khẩu xác nhận không khớp.");
        }

        string normalizedPhone = BookingService.NormalizePhone(input.Phone);
        string resolvedUsername;
        bool explicitUsername = !string.IsNullOrWhiteSpace(input.Username);

        if (explicitUsername)
        {
            resolvedUsername = input.Username!.Trim();
            if (resolvedUsername.Length < 3 || resolvedUsername.Length > 50 || !SafeUsernameCharsRegex.IsMatch(resolvedUsername))
            {
                return RegistrationOtpResult.Fail(StatusCodes.Status400BadRequest, "INVALID_USERNAME",
                    "Tên đăng nhập phải từ 3 đến 50 ký tự và chỉ gồm chữ cái, chữ số, dấu chấm, gạch ngang hoặc gạch dưới.");
            }
        }
        else
        {
            resolvedUsername = DeriveCandidateUsername(normalizedEmail, normalizedPhone);
        }

        // Fail-closed check on SMTP + dedicated OTP HMAC key before accepting registration
        var (isReady, hmacKeyBytes, cfgCode, _) = ValidateReadiness();
        if (!isReady)
        {
            _logger.LogWarning("Registration OTP blocked because provider/key is not ready: Code={ErrorCode}", cfgCode);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "OTP_PROVIDER_NOT_CONFIGURED",
                "Hệ thống đăng ký tạm thời chưa gửi được mã xác minh qua email. Vui lòng thử lại sau hoặc liên hệ CLB.");
        }

        await CleanupExpiredVerificationsAsync(db, cancellationToken);

        string lowerUsername = resolvedUsername.ToLowerInvariant();
        bool accountConflict = await db.TbAccounts.AsNoTracking().AnyAsync(a =>
            (a.Phone != null && a.Phone == normalizedPhone) ||
            (a.Email != null && a.Email.ToLower() == normalizedEmail) ||
            (explicitUsername && a.Username != null && a.Username.ToLower() == lowerUsername),
            cancellationToken);

        if (accountConflict)
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status409Conflict,
                "REGISTRATION_UNAVAILABLE",
                GenericAccountConflictMessage);
        }

        if (!explicitUsername &&
            await db.TbAccounts.AsNoTracking().AnyAsync(a => a.Username != null && a.Username.ToLower() == lowerUsername, cancellationToken))
        {
            resolvedUsername = AppendRandomUsernameSuffix(resolvedUsername);
        }

        string sessionHash = EnsureSessionBinding(httpContext);
        string emailHash = ComputeEmailHash(normalizedEmail);
        string? ipHash = ComputeIpHash(input.ClientIp);
        var now = UtcNow;
        var opts = _otpOptions.CurrentValue;
        int cooldownSec = Math.Clamp(opts.ResendCooldownSeconds, 10, 600);

        // If the same session re-submits the SAME email within the 60s cooldown while pending and already delivered, enforce cooldown
        var activePendingForSession = await db.TbRegistrationVerifications
            .Where(x => x.SessionBindingHash == sessionHash && x.Status == "Pending")
            .ToListAsync(cancellationToken);

        var sameEmailActive = activePendingForSession.FirstOrDefault(x =>
            string.Equals(x.Email, normalizedEmail, StringComparison.OrdinalIgnoreCase) &&
            x.RequestExpiresAtUtc > now &&
            string.Equals(x.LastDeliveryStatus, "Sent", StringComparison.OrdinalIgnoreCase) &&
            x.LastSentAtUtc.HasValue &&
            x.LastSentAtUtc.Value.AddSeconds(cooldownSec) > now);

        if (sameEmailActive != null)
        {
            int waitSec = Math.Max(1, (int)Math.Ceiling((sameEmailActive.LastSentAtUtc!.Value.AddSeconds(cooldownSec) - now).TotalSeconds));
            return RegistrationOtpResult.Fail(
                StatusCodes.Status429TooManyRequests,
                "OTP_RESEND_COOLDOWN",
                $"Mã xác minh vừa được gửi. Vui lòng chờ {waitSec} giây trước khi yêu cầu mã mới.",
                retryAfterSeconds: waitSec,
                verificationToken: sameEmailActive.RequestToken,
                maskedEmail: MaskEmail(normalizedEmail),
                resendCooldownSeconds: waitSec,
                canResend: true);
        }

        var quotaCheck = await CheckCumulativeSendQuotasAsync(db, sessionHash, emailHash, ipHash, now, cancellationToken);
        if (!quotaCheck.Allowed)
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status429TooManyRequests,
                quotaCheck.ErrorCode,
                quotaCheck.ErrorMessage,
                retryAfterSeconds: quotaCheck.RetryAfterSeconds);
        }

        // Invalidate any previous pending requests in this browser session (including when changing email)
        foreach (var prev in activePendingForSession)
        {
            prev.Status = "Superseded";
            prev.PasswordHash = null;
            prev.OtpHmac = null;
            prev.UpdatedAtUtc = now;
        }

        var hashTemplate = new TbAccount
        {
            Username = resolvedUsername,
            Email = normalizedEmail,
            Phone = normalizedPhone,
            RoleId = 2
        };
        string passwordHash = _passwordService.HashPassword(hashTemplate, password);

        string requestToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        string otpCode = GenerateSixDigitOtp();
        int otpVersion = 1;
        string otpHmac = ComputeOtpHmacHex(hmacKeyBytes, requestToken, otpVersion, normalizedEmail, sessionHash, otpCode);

        int otpExpiryMins = Math.Clamp(opts.OtpExpiryMinutes, 1, 30);
        int reqLifetimeMins = Math.Clamp(opts.RequestLifetimeMinutes, otpExpiryMins, 120);
        int maxAttempts = Math.Clamp(opts.MaxVerifyAttempts, 1, 10);

        var verification = new TbRegistrationVerification
        {
            RequestToken = requestToken,
            SessionBindingHash = sessionHash,
            FullName = fullName,
            Username = resolvedUsername,
            Phone = normalizedPhone,
            Email = normalizedEmail,
            EmailHash = emailHash,
            PasswordHash = passwordHash,
            OtpHmac = otpHmac,
            OtpVersion = otpVersion,
            AttemptCount = 0,
            SendCount = 1,
            Status = "Pending",
            LastDeliveryStatus = "PendingSend",
            ClientIpHash = ipHash,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
            LastSentAtUtc = now,
            OtpExpiresAtUtc = now.AddMinutes(otpExpiryMins),
            RequestExpiresAtUtc = now.AddMinutes(reqLifetimeMins)
        };

        db.TbRegistrationVerifications.Add(verification);
        await db.SaveChangesAsync(cancellationToken);

        SavePendingTokenCookie(httpContext, requestToken);
        string maskedEmail = MaskEmail(normalizedEmail);

        var sendResult = await SendVerificationEmailInternalAsync(
            normalizedEmail,
            fullName,
            otpCode,
            otpExpiryMins,
            cancellationToken);

        db.TbRegistrationEmailLogs.Add(new TbRegistrationEmailLog
        {
            VerificationId = verification.VerificationId,
            SessionBindingHash = sessionHash,
            EmailHash = emailHash,
            ClientIpHash = ipHash,
            Outcome = sendResult.Sent ? "Sent" : "Failed",
            CreatedAtUtc = UtcNow
        });

        if (!sendResult.Sent)
        {
            verification.LastDeliveryStatus = "SendFailed";
            verification.OtpHmac = null;
            // Shorten retry cooldown on transient SMTP error so customer can retry reasonably while still rate-limited
            int failRetrySec = Math.Min(15, cooldownSec);
            verification.LastSentAtUtc = now.AddSeconds(-(cooldownSec - failRetrySec));
            verification.UpdatedAtUtc = UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return RegistrationOtpResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                sendResult.ErrorCode ?? "OTP_SEND_FAILED",
                sendResult.ErrorMessage ?? "Tạm thời chưa gửi được email xác minh. Bạn có thể thử gửi lại sau ít giây.",
                retryAfterSeconds: failRetrySec,
                verificationToken: requestToken,
                maskedEmail: maskedEmail,
                resendCooldownSeconds: failRetrySec,
                canResend: true);
        }

        verification.LastDeliveryStatus = "Sent";
        verification.UpdatedAtUtc = UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Registration OTP dispatched (VerificationId={VerificationId}, Email={MaskedEmail}, Version={OtpVersion})",
            verification.VerificationId, maskedEmail, otpVersion);

        return new RegistrationOtpResult
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Status = "pending_otp",
            Message = $"Đã gửi mã xác minh gồm 6 chữ số tới {maskedEmail}.",
            VerificationToken = requestToken,
            MaskedEmail = maskedEmail,
            ExpiresInSeconds = otpExpiryMins * 60,
            ResendCooldownSeconds = cooldownSec,
            RemainingAttempts = maxAttempts,
            MaxAttempts = maxAttempts,
            CanResend = true
        };
    }

    public async Task<RegistrationOtpResult> ResendOtpAsync(
        PickleballContext db,
        HttpContext httpContext,
        string? verificationToken,
        string? clientIp,
        CancellationToken cancellationToken = default)
    {
        var (isReady, hmacKeyBytes, cfgCode, _) = ValidateReadiness();
        if (!isReady)
        {
            _logger.LogWarning("Registration OTP resend blocked because provider/key is not ready: Code={ErrorCode}", cfgCode);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "OTP_PROVIDER_NOT_CONFIGURED",
                "Hệ thống đăng ký tạm thời chưa gửi được mã xác minh qua email. Vui lòng thử lại sau.");
        }

        string? sessionHash = ReadSessionBindingHash(httpContext);
        if (string.IsNullOrEmpty(sessionHash))
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status403Forbidden,
                "OTP_SESSION_MISMATCH",
                "Phiên xác minh không khớp với trình duyệt hiện tại. Vui lòng điền lại biểu mẫu đăng ký.");
        }

        string? token = !string.IsNullOrWhiteSpace(verificationToken) && Hex64Regex.IsMatch(verificationToken.Trim())
            ? verificationToken.Trim().ToLowerInvariant()
            : ReadPendingTokenCookie(httpContext);

        if (string.IsNullOrEmpty(token))
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_NOT_FOUND",
                "Không tìm thấy yêu cầu xác minh đang chờ. Vui lòng gửi lại biểu mẫu đăng ký.");
        }

        var verification = await db.TbRegistrationVerifications
            .FirstOrDefaultAsync(x => x.RequestToken == token, cancellationToken);

        if (verification == null)
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_NOT_FOUND",
                "Yêu cầu xác minh không tồn tại hoặc đã hết hạn.");
        }

        if (!string.Equals(verification.SessionBindingHash, sessionHash, StringComparison.Ordinal))
        {
            _logger.LogWarning("OTP resend blocked due to session mismatch (VerificationId={VerificationId})", verification.VerificationId);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status403Forbidden,
                "OTP_SESSION_MISMATCH",
                "Phiên xác minh không thuộc trình duyệt hiện tại.");
        }

        var now = UtcNow;
        var opts = _otpOptions.CurrentValue;
        int cooldownSec = Math.Clamp(opts.ResendCooldownSeconds, 10, 600);
        int maxSendsPerReq = Math.Clamp(opts.MaxSendsPerRequest, 1, 20);
        int otpExpiryMins = Math.Clamp(opts.OtpExpiryMinutes, 1, 30);
        int maxAttempts = Math.Clamp(opts.MaxVerifyAttempts, 1, 10);

        if (string.Equals(verification.Status, "Verified", StringComparison.Ordinal))
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status409Conflict,
                "OTP_ALREADY_USED",
                "Tài khoản đã được xác minh thành công. Vui lòng chuyển sang Đăng nhập.");
        }

        if (string.Equals(verification.Status, "Superseded", StringComparison.Ordinal))
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_SUPERSEDED",
                "Yêu cầu xác minh cũ đã bị thay thế khi bạn đổi thông tin đăng ký.");
        }

        if (!string.Equals(verification.Status, "Pending", StringComparison.Ordinal) ||
            verification.RequestExpiresAtUtc <= now ||
            string.IsNullOrEmpty(verification.PasswordHash))
        {
            verification.Status = "Expired";
            verification.PasswordHash = null;
            verification.OtpHmac = null;
            verification.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            ClearPendingTokenCookie(httpContext);

            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_EXPIRED",
                "Phiên đăng ký đã hết hạn. Vui lòng điền lại biểu mẫu đăng ký.");
        }

        if (verification.LastSentAtUtc.HasValue && verification.LastSentAtUtc.Value.AddSeconds(cooldownSec) > now)
        {
            int waitSec = Math.Max(1, (int)Math.Ceiling((verification.LastSentAtUtc.Value.AddSeconds(cooldownSec) - now).TotalSeconds));
            return RegistrationOtpResult.Fail(
                StatusCodes.Status429TooManyRequests,
                "OTP_RESEND_COOLDOWN",
                $"Vui lòng chờ {waitSec} giây trước khi yêu cầu gửi lại mã mới.",
                retryAfterSeconds: waitSec,
                verificationToken: verification.RequestToken,
                maskedEmail: MaskEmail(verification.Email),
                resendCooldownSeconds: waitSec,
                canResend: true);
        }

        if (verification.SendCount >= maxSendsPerReq)
        {
            verification.Status = "Locked";
            verification.PasswordHash = null;
            verification.OtpHmac = null;
            verification.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            ClearPendingTokenCookie(httpContext);

            return RegistrationOtpResult.Fail(
                StatusCodes.Status429TooManyRequests,
                "OTP_REQUEST_SEND_LIMIT",
                "Bạn đã vượt quá số lần gửi lại mã cho yêu cầu đăng ký này. Vui lòng thử lại sau.",
                retryAfterSeconds: 300);
        }

        string? ipHash = ComputeIpHash(clientIp) ?? verification.ClientIpHash;
        var quotaCheck = await CheckCumulativeSendQuotasAsync(
            db,
            sessionHash,
            verification.EmailHash,
            ipHash,
            now,
            cancellationToken);

        if (!quotaCheck.Allowed)
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status429TooManyRequests,
                quotaCheck.ErrorCode,
                quotaCheck.ErrorMessage,
                retryAfterSeconds: quotaCheck.RetryAfterSeconds,
                verificationToken: verification.RequestToken,
                maskedEmail: MaskEmail(verification.Email),
                canResend: false);
        }

        // Generate a new 6-digit OTP strictly different from the immediately previous OTP, and invalidate previous version
        string? previousHmac = verification.OtpHmac;
        int previousVersion = verification.OtpVersion;
        string nextOtpCode;
        int guard = 0;
        do
        {
            nextOtpCode = GenerateSixDigitOtp();
            guard++;
        } while (guard < 25 &&
                 !string.IsNullOrEmpty(previousHmac) &&
                 VerifyOtpHmacConstantTime(
                     hmacKeyBytes,
                     previousHmac,
                     verification.RequestToken,
                     previousVersion,
                     verification.Email,
                     sessionHash,
                     nextOtpCode));

        int nextVersion = previousVersion + 1;
        string nextHmac = ComputeOtpHmacHex(
            hmacKeyBytes,
            verification.RequestToken,
            nextVersion,
            verification.Email,
            sessionHash,
            nextOtpCode);

        verification.OtpVersion = nextVersion;
        verification.OtpHmac = nextHmac;
        verification.AttemptCount = 0;
        verification.SendCount += 1;
        verification.LastSentAtUtc = now;
        verification.OtpExpiresAtUtc = now.AddMinutes(otpExpiryMins);
        verification.LastDeliveryStatus = "PendingSend";
        verification.UpdatedAtUtc = now;
        await db.SaveChangesAsync(cancellationToken);

        string maskedEmail = MaskEmail(verification.Email);
        var sendResult = await SendVerificationEmailInternalAsync(
            verification.Email,
            verification.FullName,
            nextOtpCode,
            otpExpiryMins,
            cancellationToken);

        db.TbRegistrationEmailLogs.Add(new TbRegistrationEmailLog
        {
            VerificationId = verification.VerificationId,
            SessionBindingHash = sessionHash,
            EmailHash = verification.EmailHash,
            ClientIpHash = ipHash,
            Outcome = sendResult.Sent ? "Sent" : "Failed",
            CreatedAtUtc = UtcNow
        });

        if (!sendResult.Sent)
        {
            verification.LastDeliveryStatus = "SendFailed";
            verification.OtpHmac = null;
            int failRetrySec = Math.Min(15, cooldownSec);
            verification.LastSentAtUtc = now.AddSeconds(-(cooldownSec - failRetrySec));
            verification.UpdatedAtUtc = UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return RegistrationOtpResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                sendResult.ErrorCode ?? "OTP_SEND_FAILED",
                sendResult.ErrorMessage ?? "Tạm thời chưa gửi được mã xác minh mới. Vui lòng thử gửi lại sau ít giây.",
                retryAfterSeconds: failRetrySec,
                verificationToken: verification.RequestToken,
                maskedEmail: maskedEmail,
                resendCooldownSeconds: failRetrySec,
                canResend: true);
        }

        verification.LastDeliveryStatus = "Sent";
        verification.UpdatedAtUtc = UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Registration OTP resent (VerificationId={VerificationId}, Email={MaskedEmail}, Version={OtpVersion})",
            verification.VerificationId, maskedEmail, nextVersion);

        return new RegistrationOtpResult
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Status = "pending_otp",
            Message = $"Đã gửi mã xác minh mới tới {maskedEmail}. Mã cũ không còn hiệu lực.",
            VerificationToken = verification.RequestToken,
            MaskedEmail = maskedEmail,
            ExpiresInSeconds = otpExpiryMins * 60,
            ResendCooldownSeconds = cooldownSec,
            RemainingAttempts = maxAttempts,
            MaxAttempts = maxAttempts,
            CanResend = verification.SendCount < maxSendsPerReq
        };
    }

    public async Task<RegistrationOtpResult> VerifyOtpAndCreateAccountAsync(
        PickleballContext db,
        HttpContext httpContext,
        string? verificationToken,
        string? rawOtpCode,
        CancellationToken cancellationToken = default)
    {
        string? qrKey = _configuration["SPIKE_QR_SIGNING_KEY"] ?? _configuration["Security:QrSigningKey"];
        var (hmacOk, hmacKeyBytes, hmacCode, _) = _otpOptions.CurrentValue.ValidateAndResolveKey(qrKey);
        if (!hmacOk)
        {
            _logger.LogWarning("OTP verification blocked due to invalid HMAC key: Code={ErrorCode}", hmacCode);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status503ServiceUnavailable,
                "OTP_PROVIDER_NOT_CONFIGURED",
                "Hệ thống xác minh tạm thời chưa sẵn sàng. Vui lòng thử lại sau.");
        }

        string cleanOtp = Regex.Replace((rawOtpCode ?? string.Empty).Trim(), @"[\s-]", string.Empty);
        if (cleanOtp.Length != 6 || !cleanOtp.All(char.IsAsciiDigit))
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_FORMAT_INVALID",
                "Mã xác minh phải gồm đúng 6 chữ số.");
        }

        string? sessionHash = ReadSessionBindingHash(httpContext);
        if (string.IsNullOrEmpty(sessionHash))
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status403Forbidden,
                "OTP_SESSION_MISMATCH",
                "Phiên xác minh không khớp với trình duyệt hiện tại. Vui lòng thực hiện đăng ký lại trên trình duyệt này.");
        }

        string? token = !string.IsNullOrWhiteSpace(verificationToken) && Hex64Regex.IsMatch(verificationToken.Trim())
            ? verificationToken.Trim().ToLowerInvariant()
            : ReadPendingTokenCookie(httpContext);

        if (string.IsNullOrEmpty(token))
        {
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_NOT_FOUND",
                "Không tìm thấy phiên xác minh đăng ký. Vui lòng điền lại biểu mẫu đăng ký.");
        }

        var opts = _otpOptions.CurrentValue;
        int maxAttempts = Math.Clamp(opts.MaxVerifyAttempts, 1, 10);
        int maxSendsPerReq = Math.Clamp(opts.MaxSendsPerRequest, 1, 20);
        bool isRelational = db.Database.IsRelational();

        await using var tx = isRelational
            ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, cancellationToken)
            : null;

        if (isRelational)
        {
            await db.Database.ExecuteSqlRawAsync(
                "DECLARE @res INT; EXEC @res = sp_getapplock @Resource = N'Spike.PublicRegisterCommit', @LockMode = N'Exclusive', @LockOwner = N'Transaction', @LockTimeout = 10000;",
                cancellationToken);
        }

        TbRegistrationVerification? verification = isRelational
            ? await db.TbRegistrationVerifications
                .FromSqlInterpolated($"SELECT * FROM tb_RegistrationVerification WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE RequestToken = {token}")
                .FirstOrDefaultAsync(cancellationToken)
            : await db.TbRegistrationVerifications
                .FirstOrDefaultAsync(x => x.RequestToken == token, cancellationToken);

        if (verification == null)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_NOT_FOUND",
                "Yêu cầu xác minh không tồn tại hoặc đã hết hạn.");
        }

        if (!string.Equals(verification.SessionBindingHash, sessionHash, StringComparison.Ordinal))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            _logger.LogWarning("OTP verify rejected due to session mismatch (VerificationId={VerificationId})", verification.VerificationId);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status403Forbidden,
                "OTP_SESSION_MISMATCH",
                "Mã xác minh này không thuộc phiên trình duyệt hiện tại.");
        }

        var now = UtcNow;

        if (string.Equals(verification.Status, "Verified", StringComparison.Ordinal) || verification.ConsumedAtUtc.HasValue)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            ClearPendingTokenCookie(httpContext);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status409Conflict,
                "OTP_ALREADY_USED",
                "Mã xác minh này đã được sử dụng. Tài khoản của bạn đã sẵn sàng, vui lòng bấm Đăng nhập.");
        }

        if (string.Equals(verification.Status, "Superseded", StringComparison.Ordinal))
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_SUPERSEDED",
                "Yêu cầu xác minh cũ đã bị hủy do bạn đã tạo yêu cầu mới.");
        }

        if (!string.Equals(verification.Status, "Pending", StringComparison.Ordinal) ||
            verification.RequestExpiresAtUtc <= now ||
            string.IsNullOrEmpty(verification.PasswordHash))
        {
            verification.Status = "Expired";
            verification.PasswordHash = null;
            verification.OtpHmac = null;
            verification.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            ClearPendingTokenCookie(httpContext);

            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_REQUEST_EXPIRED",
                "Yêu cầu đăng ký đã hết hạn. Vui lòng thực hiện đăng ký lại.");
        }

        if (verification.AttemptCount >= maxAttempts)
        {
            if (tx != null) await tx.RollbackAsync(cancellationToken);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_MAX_ATTEMPTS",
                "Bạn đã nhập sai quá 5 lần cho mã này. Vui lòng bấm Gửi lại mã để nhận mã mới.",
                verificationToken: verification.RequestToken,
                maskedEmail: MaskEmail(verification.Email),
                canResend: verification.SendCount < maxSendsPerReq);
        }

        if (verification.OtpExpiresAtUtc <= now || string.IsNullOrEmpty(verification.OtpHmac))
        {
            verification.OtpHmac = null;
            verification.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);

            bool isSendFailed = string.Equals(verification.LastDeliveryStatus, "SendFailed", StringComparison.OrdinalIgnoreCase);
            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                isSendFailed ? "OTP_NOT_DELIVERED" : "OTP_EXPIRED",
                isSendFailed
                    ? "Mã xác minh chưa gửi thành công trước đó. Vui lòng bấm Gửi lại mã."
                    : "Mã xác minh đã hết hiệu lực (quá 10 phút). Vui lòng bấm Gửi lại mã để nhận mã mới.",
                verificationToken: verification.RequestToken,
                maskedEmail: MaskEmail(verification.Email),
                canResend: verification.SendCount < maxSendsPerReq);
        }

        bool isMatch = VerifyOtpHmacConstantTime(
            hmacKeyBytes,
            verification.OtpHmac,
            verification.RequestToken,
            verification.OtpVersion,
            verification.Email,
            sessionHash,
            cleanOtp);

        if (!isMatch)
        {
            verification.AttemptCount += 1;
            verification.UpdatedAtUtc = now;
            int remaining = Math.Max(0, maxAttempts - verification.AttemptCount);

            if (verification.AttemptCount >= maxAttempts)
            {
                verification.OtpHmac = null;
                if (verification.SendCount >= maxSendsPerReq)
                {
                    verification.Status = "Locked";
                    verification.PasswordHash = null;
                    ClearPendingTokenCookie(httpContext);
                }
            }

            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);

            if (remaining <= 0)
            {
                return RegistrationOtpResult.Fail(
                    StatusCodes.Status400BadRequest,
                    "OTP_MAX_ATTEMPTS",
                    verification.Status == "Locked"
                        ? "Yêu cầu xác minh đã bị khóa do nhập sai quá số lần cho phép. Vui lòng đăng ký lại sau."
                        : "Bạn đã nhập sai quá 5 lần cho mã này. Vui lòng bấm Gửi lại mã để nhận mã mới.",
                    verificationToken: verification.Status == "Locked" ? null : verification.RequestToken,
                    maskedEmail: MaskEmail(verification.Email),
                    canResend: verification.Status != "Locked",
                    remainingAttempts: 0);
            }

            return RegistrationOtpResult.Fail(
                StatusCodes.Status400BadRequest,
                "OTP_INVALID",
                $"Mã xác minh không chính xác. Bạn còn {remaining} lần thử.",
                verificationToken: verification.RequestToken,
                maskedEmail: MaskEmail(verification.Email),
                canResend: true,
                remainingAttempts: remaining);
        }

        // Re-check uniqueness of Email, Phone, and Username inside the serialized transaction
        string normalizedEmail = verification.Email;
        string normalizedPhone = verification.Phone;
        string finalUsername = verification.Username;
        string lowerUsername = finalUsername.ToLowerInvariant();

        bool duplicateAccountExists = await db.TbAccounts.AnyAsync(a =>
            (a.Email != null && a.Email.ToLower() == normalizedEmail) ||
            (a.Phone != null && a.Phone == normalizedPhone) ||
            (a.Username != null && a.Username.ToLower() == lowerUsername),
            cancellationToken);

        if (duplicateAccountExists)
        {
            verification.Status = "Cancelled";
            verification.PasswordHash = null;
            verification.OtpHmac = null;
            verification.UpdatedAtUtc = now;
            await db.SaveChangesAsync(cancellationToken);
            if (tx != null) await tx.CommitAsync(cancellationToken);
            ClearPendingTokenCookie(httpContext);

            return RegistrationOtpResult.Fail(
                StatusCodes.Status409Conflict,
                "REGISTRATION_UNAVAILABLE",
                GenericAccountConflictMessage);
        }

        var newAccount = new TbAccount
        {
            Username = finalUsername,
            Password = verification.PasswordHash,
            FullName = verification.FullName,
            Phone = normalizedPhone,
            Email = normalizedEmail,
            EmailVerifiedAtUtc = now,
            SkillLevel = "Giao lưu (DUPR 3.0)",
            MemberTier = "Hội viên Tiêu chuẩn",
            DominantHand = "Tay phải",
            RoleId = 2, // Strictly Customer role (P0-03)
            IsActive = true,
            MustChangePassword = false,
            SecurityStamp = Function.GenerateSecurityStamp(),
            LastLogin = null
        };

        db.TbAccounts.Add(newAccount);
        await db.SaveChangesAsync(cancellationToken);

        // Consume OTP and wipe hashed password + sensitive OTP digest from temporary verification row in the same transaction
        verification.Status = "Verified";
        verification.ConsumedAtUtc = now;
        verification.CreatedAccountId = newAccount.AccountId;
        verification.PasswordHash = null;
        verification.OtpHmac = null;
        verification.UpdatedAtUtc = now;

        await db.SaveChangesAsync(cancellationToken);
        if (tx != null)
        {
            await tx.CommitAsync(cancellationToken);
        }

        ClearPendingTokenCookie(httpContext);

        _logger.LogInformation(
            "Verified registration OTP and created Customer account (AccountId={AccountId}, VerificationId={VerificationId})",
            newAccount.AccountId, verification.VerificationId);

        return new RegistrationOtpResult
        {
            Success = true,
            StatusCode = StatusCodes.Status200OK,
            Status = "registered",
            CreatedAccountId = newAccount.AccountId,
            MaskedEmail = MaskEmail(normalizedEmail),
            Message = "Đăng ký thành công! Email của bạn đã được xác minh, bạn có thể đăng nhập ngay."
        };
    }

    public async Task CancelPendingAsync(
        PickleballContext db,
        HttpContext httpContext,
        string? verificationToken = null,
        CancellationToken cancellationToken = default)
    {
        string? sessionHash = ReadSessionBindingHash(httpContext);
        if (string.IsNullOrEmpty(sessionHash))
        {
            ClearPendingTokenCookie(httpContext);
            return;
        }

        var now = UtcNow;
        var query = db.TbRegistrationVerifications
            .Where(x => x.SessionBindingHash == sessionHash && x.Status == "Pending");

        if (!string.IsNullOrWhiteSpace(verificationToken) && Hex64Regex.IsMatch(verificationToken.Trim()))
        {
            string tok = verificationToken.Trim().ToLowerInvariant();
            query = query.Where(x => x.RequestToken == tok);
        }

        var rows = await query.ToListAsync(cancellationToken);
        foreach (var row in rows)
        {
            row.Status = "Superseded";
            row.PasswordHash = null;
            row.OtpHmac = null;
            row.UpdatedAtUtc = now;
        }

        if (rows.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        ClearPendingTokenCookie(httpContext);
    }

    private async Task<(bool Allowed, string ErrorCode, string ErrorMessage, int RetryAfterSeconds)> CheckCumulativeSendQuotasAsync(
        PickleballContext db,
        string sessionHash,
        string emailHash,
        string? ipHash,
        DateTime utcNow,
        CancellationToken cancellationToken)
    {
        var opts = _otpOptions.CurrentValue;
        var windowStart = utcNow.AddHours(-1);

        int maxGlobal = Math.Clamp(opts.MaxGlobalEmailsPerHour, 5, 5000);
        int globalCount = await db.TbRegistrationEmailLogs
            .AsNoTracking()
            .CountAsync(x => x.CreatedAtUtc >= windowStart, cancellationToken);

        if (globalCount >= maxGlobal)
        {
            _logger.LogWarning("Global registration OTP mailbox hourly quota reached (Count={Count}, Limit={Limit})", globalCount, maxGlobal);
            return (false, "OTP_MAILBOX_QUOTA_EXCEEDED",
                "Hệ thống gửi email xác minh đang đạt giới hạn tạm thời. Vui lòng thử lại sau ít phút.", 300);
        }

        int maxPerSession = Math.Clamp(opts.MaxSendsPerSessionPerHour, 2, 100);
        int sessionCount = await db.TbRegistrationEmailLogs
            .AsNoTracking()
            .CountAsync(x => x.SessionBindingHash == sessionHash && x.CreatedAtUtc >= windowStart, cancellationToken);

        if (sessionCount >= maxPerSession)
        {
            return (false, "OTP_SESSION_QUOTA_EXCEEDED",
                "Bạn đã yêu cầu mã xác minh quá nhiều lần trên phiên này. Vui lòng thử lại sau.", 300);
        }

        int maxPerEmail = Math.Clamp(opts.MaxSendsPerEmailPerHour, 2, 100);
        int emailCount = await db.TbRegistrationEmailLogs
            .AsNoTracking()
            .CountAsync(x => x.EmailHash == emailHash && x.CreatedAtUtc >= windowStart, cancellationToken);

        if (emailCount >= maxPerEmail)
        {
            return (false, "OTP_EMAIL_QUOTA_EXCEEDED",
                "Địa chỉ email này đã nhận tối đa lượt gửi mã trong giờ qua. Vui lòng kiểm tra hộp thư hoặc thử lại sau.", 300);
        }

        if (!string.IsNullOrEmpty(ipHash))
        {
            int maxPerIp = Math.Clamp(opts.MaxSendsPerIpPerHour, 2, 200);
            int ipCount = await db.TbRegistrationEmailLogs
                .AsNoTracking()
                .CountAsync(x => x.ClientIpHash == ipHash && x.CreatedAtUtc >= windowStart, cancellationToken);

            if (ipCount >= maxPerIp)
            {
                return (false, "OTP_IP_QUOTA_EXCEEDED",
                    "Mạng của bạn đã gửi quá nhiều yêu cầu mã xác minh trong thời gian ngắn. Vui lòng thử lại sau.", 300);
            }
        }

        return (true, string.Empty, string.Empty, 0);
    }

    private Task<EmailSendResult> SendVerificationEmailInternalAsync(
        string recipientEmail,
        string recipientName,
        string otpCode,
        int expiryMinutes,
        CancellationToken cancellationToken)
    {
        string textBody =
            $"Mã xác minh của bạn là {otpCode}. Mã có hiệu lực {expiryMinutes} phút. Nếu bạn không yêu cầu đăng ký, bạn có thể bỏ qua email này.";

        string encodedName = WebUtility.HtmlEncode(recipientName);
        string encodedOtp = WebUtility.HtmlEncode(otpCode);
        string htmlBody = $@"<!DOCTYPE html>
<html lang=""vi"">
<body style=""margin:0;padding:24px;font-family:-apple-system,BlinkMacSystemFont,'Segoe UI',Roboto,Arial,sans-serif;color:#111827;background-color:#F6F5F4;"">
  <div style=""max-width:480px;margin:0 auto;background:#FFFFFF;border:1px solid #E5E7EB;border-radius:12px;padding:28px;"">
    <div style=""font-size:15px;font-weight:700;color:#226B48;margin-bottom:12px;"">Spike Pickleball Club</div>
    <p style=""margin:0 0 14px;font-size:15px;line-height:1.55;"">Xin chào {encodedName},</p>
    <p style=""margin:0 0 16px;font-size:15px;line-height:1.55;"">Mã xác minh của bạn là <strong style=""font-size:20px;letter-spacing:3px;font-family:monospace;color:#111827;"">{encodedOtp}</strong>. Mã có hiệu lực {expiryMinutes} phút.</p>
    <p style=""margin:0;font-size:13px;line-height:1.5;color:#615D59;"">Nếu bạn không yêu cầu đăng ký, bạn có thể bỏ qua email này.</p>
  </div>
</body>
</html>";

        return _emailSender.SendSingleEmailAsync(new EmailSendMessage
        {
            ToEmail = recipientEmail,
            ToName = recipientName,
            Subject = EmailSubject,
            TextBody = textBody,
            HtmlBody = htmlBody
        }, cancellationToken);
    }

    private static string DeriveCandidateUsername(string normalizedEmail, string normalizedPhone)
    {
        string local = normalizedEmail.Split('@')[0];
        string cleaned = Regex.Replace(local, @"[^a-zA-Z0-9._-]", string.Empty);
        if (cleaned.Length < 3)
        {
            cleaned = normalizedPhone;
        }
        if (cleaned.Length > 44)
        {
            cleaned = cleaned[..44];
        }
        return cleaned;
    }

    private static string AppendRandomUsernameSuffix(string baseUsername)
    {
        string trimmed = baseUsername.Length > 44 ? baseUsername[..44] : baseUsername;
        return $"{trimmed}_{RandomNumberGenerator.GetInt32(1000, 9999)}";
    }
}
