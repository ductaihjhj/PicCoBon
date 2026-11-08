using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;

namespace PickleballClub.Services;

public class QrTokenService
{
    private readonly byte[] _secretKey;

    public QrTokenService(IConfiguration configuration)
    {
        string? configuredKey = configuration["Security:QrSigningKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            configuredKey = Environment.GetEnvironmentVariable("SPIKE_QR_SIGNING_KEY");
        }
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            configuredKey = "SpikePickleballClub-Default-Dev-HMAC-Key-2026-DoNotShare";
        }
        _secretKey = Encoding.UTF8.GetBytes(configuredKey);
    }

    /// <summary>
    /// Generates an HMAC-SHA256 signed check-in token: SPK1.{bookingCode}.{courtId}.{date}.{expUnix}.{sigHex16}
    /// </summary>
    public string GenerateToken(string bookingCode, int courtId, string bookingDateStr, string startTimeStr, int durationMinutes)
    {
        if (!DateOnly.TryParse(bookingDateStr, out var date))
        {
            date = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(7));
        }
        if (!TimeOnly.TryParse(startTimeStr, out var startTime))
        {
            startTime = new TimeOnly(17, 0);
        }

        int mins = durationMinutes > 0 ? durationMinutes : 60;
        var endDateTime = date.ToDateTime(startTime).AddMinutes(mins + 240); // valid until 4 hours after match end
        long expUnix = new DateTimeOffset(endDateTime, TimeSpan.FromHours(7)).ToUnixTimeSeconds();
        string dateIso = date.ToString("yyyy-MM-dd");
        string cleanCode = bookingCode.Trim().ToUpperInvariant();
        string payload = $"SPK1.{cleanCode}.{courtId}.{dateIso}.{expUnix}";
        string sig = ComputeSignature(payload);
        return $"{payload}.{sig}";
    }

    public QrValidationResult ValidateToken(string rawInput)
    {
        if (string.IsNullOrWhiteSpace(rawInput))
        {
            return QrValidationResult.Invalid("Mã QR hoặc mã vé trống.");
        }

        string trimmed = rawInput.Trim();
        if (!trimmed.StartsWith("SPK1.", StringComparison.OrdinalIgnoreCase))
        {
            return new QrValidationResult
            {
                IsSignedToken = false,
                IsValid = false,
                BookingCode = trimmed.ToUpperInvariant(),
                ErrorMessage = "Mã nhập tay (không phải QR chữ ký số)."
            };
        }

        var parts = trimmed.Split('.');
        if (parts.Length != 6)
        {
            return QrValidationResult.Invalid("Định dạng mã QR không hợp lệ.");
        }

        string bookingCode = parts[1].ToUpperInvariant();
        if (!int.TryParse(parts[2], out int courtId))
        {
            return QrValidationResult.Invalid("Thông tin sân trong QR không hợp lệ.");
        }

        string dateStr = parts[3];
        if (!long.TryParse(parts[4], out long expUnix))
        {
            return QrValidationResult.Invalid("Thời hạn vé QR không hợp lệ.");
        }

        string providedSig = parts[5];
        string payload = $"SPK1.{bookingCode}.{courtId}.{dateStr}.{expUnix}";
        string expectedSig = ComputeSignature(payload);

        if (providedSig.Length != expectedSig.Length ||
            !CryptographicOperations.FixedTimeEquals(
                Encoding.ASCII.GetBytes(providedSig.ToUpperInvariant()),
                Encoding.ASCII.GetBytes(expectedSig.ToUpperInvariant())))
        {
            return QrValidationResult.Invalid("Chữ ký bảo mật QR không hợp lệ hoặc đã bị giả mạo.");
        }

        long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        if (nowUnix > expUnix)
        {
            return new QrValidationResult
            {
                IsSignedToken = true,
                IsValid = false,
                IsExpired = true,
                BookingCode = bookingCode,
                CourtId = courtId,
                BookingDate = dateStr,
                ErrorMessage = "Mã QR này đã hết hạn sử dụng."
            };
        }

        return new QrValidationResult
        {
            IsSignedToken = true,
            IsValid = true,
            BookingCode = bookingCode,
            CourtId = courtId,
            BookingDate = dateStr
        };
    }

    private string ComputeSignature(string payload)
    {
        using var hmac = new HMACSHA256(_secretKey);
        byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(hash)[..16];
    }
}

public class QrValidationResult
{
    public bool IsSignedToken { get; set; }
    public bool IsValid { get; set; }
    public bool IsExpired { get; set; }
    public string BookingCode { get; set; } = string.Empty;
    public int CourtId { get; set; }
    public string BookingDate { get; set; } = string.Empty;
    public string? ErrorMessage { get; set; }

    public static QrValidationResult Invalid(string message) => new()
    {
        IsSignedToken = true,
        IsValid = false,
        ErrorMessage = message
    };
}
