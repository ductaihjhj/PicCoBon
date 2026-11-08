using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using ZXing;
using ZXing.Common;

namespace PickleballClub.Services;

public class QrTokenService
{
    public const string DevOnlyFallbackKey = "SpikePickleballClub-DevOnly-HMAC-Key-2026-ChangeInProd-32Bytes!";
    private readonly byte[] _secretKey;

    public QrTokenService(IConfiguration configuration, IHostEnvironment? environment = null)
    {
        string? configuredKey = configuration["Security:QrSigningKey"];
        if (string.IsNullOrWhiteSpace(configuredKey))
        {
            configuredKey = Environment.GetEnvironmentVariable("SPIKE_QR_SIGNING_KEY");
        }

        bool isDevOrTest = environment == null ||
                           environment.IsDevelopment() ||
                           environment.IsEnvironment("Testing");

        bool isPlaceholderKey = !string.IsNullOrWhiteSpace(configuredKey) &&
                                (configuredKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) ||
                                 configuredKey.Contains("DevOnly", StringComparison.OrdinalIgnoreCase) ||
                                 string.Equals(configuredKey.Trim(), DevOnlyFallbackKey, StringComparison.OrdinalIgnoreCase));

        if (string.IsNullOrWhiteSpace(configuredKey) || configuredKey.Trim().Length < 32 || (!isDevOrTest && isPlaceholderKey))
        {
            if (!isDevOrTest)
            {
                throw new InvalidOperationException(
                    "CRITICAL SECURITY CONFIGURATION ERROR: 'Security:QrSigningKey' (or environment variable 'SPIKE_QR_SIGNING_KEY') " +
                    "must be configured with a non-placeholder secret of at least 32 characters in Production/Staging environments.");
            }

            configuredKey = DevOnlyFallbackKey;
        }

        _secretKey = Encoding.UTF8.GetBytes(configuredKey.Trim());
    }

    /// <summary>
    /// Generates an HMAC-SHA256 signed check-in token containing ONLY booking reference, date, nonce, and expiry (zero PII).
    /// Format: SPK1.{base64UrlPayload}.{base64UrlSignature}
    /// </summary>
    public string GenerateCheckInToken(string bookingCode, string bookingDate, DateTime expiresUtc)
    {
        string cleanCode = (bookingCode ?? string.Empty).Trim().ToUpperInvariant();
        string cleanDate = (bookingDate ?? string.Empty).Trim();
        long expUnix = new DateTimeOffset(expiresUtc.Kind == DateTimeKind.Utc ? expiresUtc : expiresUtc.ToUniversalTime()).ToUnixTimeSeconds();
        string nonce = Convert.ToHexString(RandomNumberGenerator.GetBytes(6)).ToLowerInvariant();
        string payload = $"{cleanCode}|{cleanDate}|{expUnix}|{nonce}";
        string payloadB64 = Base64UrlEncode(Encoding.UTF8.GetBytes(payload));
        string signatureB64 = ComputeSignature(payloadB64);
        return $"SPK1.{payloadB64}.{signatureB64}";
    }

    public bool TryValidateCheckInToken(
        string token,
        out string bookingCode,
        out string bookingDate,
        out string errorReason)
    {
        bookingCode = string.Empty;
        bookingDate = string.Empty;
        errorReason = string.Empty;

        if (string.IsNullOrWhiteSpace(token))
        {
            errorReason = "Mã QR trống.";
            return false;
        }

        string trimmed = token.Trim();
        if (!trimmed.StartsWith("SPK1.", StringComparison.Ordinal))
        {
            errorReason = "Định dạng mã QR không phải vé điện tử hợp lệ của CLB.";
            return false;
        }

        var parts = trimmed.Split('.');
        if (parts.Length != 3)
        {
            errorReason = "Cấu trúc chữ ký mã QR không hợp lệ.";
            return false;
        }

        string payloadB64 = parts[1];
        string providedSig = parts[2];
        string expectedSig = ComputeSignature(payloadB64);

        if (!CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(providedSig),
                Encoding.UTF8.GetBytes(expectedSig)))
        {
            errorReason = "Chữ ký bảo mật của mã QR không hợp lệ hoặc đã bị chỉnh sửa!";
            return false;
        }

        try
        {
            string payload = Encoding.UTF8.GetString(Base64UrlDecode(payloadB64));
            var fields = payload.Split('|');
            if (fields.Length < 3)
            {
                errorReason = "Dữ liệu vé bên trong mã QR không đầy đủ.";
                return false;
            }

            bookingCode = fields[0];
            bookingDate = fields[1];
            if (!long.TryParse(fields[2], out long expUnix))
            {
                errorReason = "Thời hạn mã QR không hợp lệ.";
                return false;
            }

            long nowUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
            if (nowUnix > expUnix)
            {
                errorReason = "Mã QR này đã hết thời hạn hiệu lực.";
                return false;
            }

            return true;
        }
        catch
        {
            errorReason = "Không thể giải mã nội dung vé QR.";
            return false;
        }
    }

    /// <summary>
    /// Generates a cryptographically random opaque guest BookingAccessToken and its SHA-256 hex hash for DB storage.
    /// </summary>
    public static (string RawToken, string TokenHash) GenerateGuestAccessToken()
    {
        string raw = "bk_" + Base64UrlEncode(RandomNumberGenerator.GetBytes(24));
        string hash = HashAccessToken(raw);
        return (raw, hash);
    }

    public static string HashAccessToken(string rawToken)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes((rawToken ?? string.Empty).Trim()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static bool VerifyGuestAccessToken(string? providedRawToken, string? storedHash)
    {
        if (string.IsNullOrWhiteSpace(providedRawToken) || string.IsNullOrWhiteSpace(storedHash))
        {
            return false;
        }

        string computedHash = HashAccessToken(providedRawToken);
        return CryptographicOperations.FixedTimeEquals(
            Encoding.UTF8.GetBytes(computedHash),
            Encoding.UTF8.GetBytes(storedHash.Trim().ToLowerInvariant()));
    }

    /// <summary>
    /// Generates a real ISO/IEC 18004 QR Code SVG using QRCoder.
    /// </summary>
    public string GenerateQrSvg(string content, int pixelsPerModule = 6)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(content ?? string.Empty, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        var svgQr = new SvgQRCode(qrData);
        return svgQr.GetGraphic(Math.Clamp(pixelsPerModule, 3, 20), "#0F172A", "#FFFFFF", drawQuietZones: true);
    }

    /// <summary>
    /// Generates a real ISO/IEC 18004 QR Code PNG byte array using QRCoder.
    /// </summary>
    public byte[] GenerateQrPngBytes(string content, int pixelsPerModule = 8)
    {
        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(content ?? string.Empty, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
        var pngQr = new PngByteQRCode(qrData);
        return pngQr.GetGraphic(Math.Clamp(pixelsPerModule, 4, 20));
    }

    /// <summary>
    /// Decodes a QR Code from PNG/JPEG/WebP image bytes using ZXing.Net + ImageSharp (completely independent from QRCoder).
    /// </summary>
    public static bool TryDecodeQrImage(byte[] imageBytes, out string decodedText, out string error)
    {
        decodedText = string.Empty;
        error = string.Empty;

        if (imageBytes == null || imageBytes.Length == 0)
        {
            error = "Tệp ảnh tải lên trống.";
            return false;
        }

        if (imageBytes.Length > 8 * 1024 * 1024)
        {
            error = "Kích thước ảnh vượt quá giới hạn 8MB.";
            return false;
        }

        try
        {
            using Image<Rgba32> image = Image.Load<Rgba32>(imageBytes);
            int width = image.Width;
            int height = image.Height;
            byte[] rgbRaw = new byte[width * height * 4];
            image.CopyPixelDataTo(rgbRaw);

            var luminance = new RGBLuminanceSource(rgbRaw, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
            var reader = new BarcodeReaderGeneric
            {
                AutoRotate = true,
                Options = new DecodingOptions
                {
                    TryHarder = true,
                    TryInverted = true,
                    PossibleFormats = new[] { BarcodeFormat.QR_CODE }
                }
            };

            var result = reader.Decode(luminance);
            if (result == null || string.IsNullOrWhiteSpace(result.Text))
            {
                error = "Không tìm thấy mã QR hợp lệ trong ảnh tải lên. Vui lòng chụp rõ nét toàn bộ vùng mã QR.";
                return false;
            }

            decodedText = result.Text.Trim();
            return true;
        }
        catch
        {
            error = "Tệp tải lên không phải định dạng ảnh hợp lệ (PNG, JPG, WebP).";
            return false;
        }
    }

    private string ComputeSignature(string payloadB64)
    {
        using var hmac = new HMACSHA256(_secretKey);
        byte[] sig = hmac.ComputeHash(Encoding.UTF8.GetBytes(payloadB64));
        return Base64UrlEncode(sig)[..24];
    }

    private static string Base64UrlEncode(byte[] input)
    {
        return Convert.ToBase64String(input)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }

    private static byte[] Base64UrlDecode(string input)
    {
        string padded = input.Replace('-', '+').Replace('_', '/');
        switch (padded.Length % 4)
        {
            case 2: padded += "=="; break;
            case 3: padded += "="; break;
        }
        return Convert.FromBase64String(padded);
    }
}
