using System;
using System.Text;

namespace PickleballClub.Configuration;

public sealed class RegistrationOtpOptions
{
    public const string SectionName = "RegistrationOtp";

    public string HmacKey { get; set; } = string.Empty;
    public int OtpExpiryMinutes { get; set; } = 10;
    public int RequestLifetimeMinutes { get; set; } = 30;
    public int MaxVerifyAttempts { get; set; } = 5;
    public int ResendCooldownSeconds { get; set; } = 60;
    public int MaxSendsPerRequest { get; set; } = 5;
    public int MaxSendsPerSessionPerHour { get; set; } = 8;
    public int MaxSendsPerEmailPerHour { get; set; } = 6;
    public int MaxSendsPerIpPerHour { get; set; } = 12;
    public int MaxGlobalEmailsPerHour { get; set; } = 100;
    public int RetentionHoursAfterExpiry { get; set; } = 24;

    public (bool IsValid, byte[] KeyBytes, string ErrorCode, string ErrorMessage) ValidateAndResolveKey(string? otherSigningKey = null)
    {
        string raw = (HmacKey ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(raw) ||
            raw.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
            raw.Contains("DevOnly", StringComparison.OrdinalIgnoreCase) ||
            (raw.StartsWith('<') && raw.EndsWith('>')))
        {
            return (false, Array.Empty<byte>(), "OTP_HMAC_KEY_MISSING",
                "Chưa cấu hình khóa xác thực RegistrationOtp:HmacKey hợp lệ.");
        }

        if (!string.IsNullOrWhiteSpace(otherSigningKey) &&
            string.Equals(raw, otherSigningKey.Trim(), StringComparison.Ordinal))
        {
            return (false, Array.Empty<byte>(), "OTP_HMAC_KEY_REUSED",
                "Khóa RegistrationOtp:HmacKey phải là khóa bí mật riêng biệt, không dùng chung với khóa khác.");
        }

        byte[] keyBytes;
        byte[] decoded = new byte[raw.Length];
        if (Convert.TryFromBase64String(raw, decoded, out int bytesWritten) && bytesWritten >= 32)
        {
            keyBytes = decoded[..bytesWritten];
        }
        else
        {
            keyBytes = Encoding.UTF8.GetBytes(raw);
        }

        if (keyBytes.Length < 32)
        {
            return (false, Array.Empty<byte>(), "OTP_HMAC_KEY_TOO_SHORT",
                "Khóa RegistrationOtp:HmacKey phải có độ dài tối thiểu 32 byte.");
        }

        return (true, keyBytes, string.Empty, string.Empty);
    }
}
