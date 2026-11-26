using System;
using System.Net.Mail;

namespace PickleballClub.Configuration;

public sealed class EmailSmtpOptions
{
    public const string SectionName = "EmailSmtp";

    public string Host { get; set; } = "mail.privateemail.com";
    public int Port { get; set; } = 587;
    public string Security { get; set; } = "StartTls";
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public string FromAddress { get; set; } = string.Empty;
    public string FromName { get; set; } = "Spike Pickleball Club";
    public int TimeoutSeconds { get; set; } = 15;

    public int GetEffectiveTimeoutSeconds() => Math.Clamp(TimeoutSeconds <= 0 ? 15 : TimeoutSeconds, 3, 60);

    public string GetEffectiveFromName() =>
        string.IsNullOrWhiteSpace(FromName) ? "Spike Pickleball Club" : FromName.Trim();

    public (bool IsValid, string ErrorCode, string ErrorMessage) Validate()
    {
        string host = (Host ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(host) || IsPlaceholder(host) || host.Contains(' '))
        {
            return (false, "SMTP_HOST_INVALID", "Chưa cấu hình máy chủ SMTP hợp lệ.");
        }

        if (Port <= 0 || Port > 65535)
        {
            return (false, "SMTP_PORT_INVALID", "Cổng kết nối SMTP không hợp lệ.");
        }

        string security = (Security ?? string.Empty).Trim();
        if (!string.Equals(security, "StartTls", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(security, "SslOnConnect", StringComparison.OrdinalIgnoreCase))
        {
            return (false, "SMTP_TLS_REQUIRED", "Chế độ bảo mật SMTP bắt buộc là StartTls (cổng 587) hoặc SslOnConnect (cổng 465).");
        }

        string username = (Username ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(username) || IsPlaceholder(username) || !IsValidEmailFormat(username))
        {
            return (false, "SMTP_USERNAME_INVALID", "Chưa cấu hình tài khoản hộp thư SMTP hợp lệ.");
        }

        string password = Password ?? string.Empty;
        if (string.IsNullOrWhiteSpace(password) || IsPlaceholder(password.Trim()))
        {
            return (false, "SMTP_PASSWORD_MISSING", "Chưa cấu hình Application Password cho hộp thư SMTP.");
        }

        string fromAddress = (FromAddress ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(fromAddress) || IsPlaceholder(fromAddress) || !IsValidEmailFormat(fromAddress))
        {
            return (false, "SMTP_FROM_INVALID", "Chưa cấu hình địa chỉ gửi (FromAddress) hợp lệ.");
        }

        if (!string.Equals(username, fromAddress, StringComparison.OrdinalIgnoreCase))
        {
            return (false, "SMTP_FROM_MISMATCH", "Địa chỉ gửi (FromAddress) phải trùng khớp với tài khoản hộp thư SMTP đã xác thực.");
        }

        return (true, string.Empty, string.Empty);
    }

    public static bool IsValidEmailFormat(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return false;
        }

        string trimmed = email.Trim();
        if (trimmed.Length > 150 || trimmed.Contains(' ') || trimmed.Contains('\r') || trimmed.Contains('\n'))
        {
            return false;
        }

        int atIdx = trimmed.IndexOf('@');
        if (atIdx <= 0 || atIdx != trimmed.LastIndexOf('@') || atIdx >= trimmed.Length - 3)
        {
            return false;
        }

        string domain = trimmed[(atIdx + 1)..];
        if (!domain.Contains('.') || domain.StartsWith('.') || domain.EndsWith('.'))
        {
            return false;
        }

        try
        {
            var addr = new MailAddress(trimmed);
            return string.Equals(addr.Address, trimmed, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static bool IsPlaceholder(string value)
    {
        return value.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("YOUR_", StringComparison.OrdinalIgnoreCase) ||
               value.Contains("PLACEHOLDER", StringComparison.OrdinalIgnoreCase) ||
               (value.StartsWith('<') && value.EndsWith('>'));
    }
}
