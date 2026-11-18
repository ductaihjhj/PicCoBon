using System;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using QRCoder;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
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
    /// Supports optional center Spike emblem (ECC Level H, <= 5% QR area with pure white quiet pad and ZXing self-verification fallback).
    /// Always embeds an explicit viewBox="0 0 W H" and preserveAspectRatio="xMidYMid meet" so inline <svg> and <img> scale and center identically.
    /// </summary>
    public string GenerateQrSvg(string content, int pixelsPerModule = 6, bool includeLogo = false)
    {
        string safeContent = content ?? string.Empty;
        int ppm = Math.Clamp(pixelsPerModule, 4, 24);
        var ecc = includeLogo ? QRCodeGenerator.ECCLevel.H : QRCodeGenerator.ECCLevel.M;

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(safeContent, ecc, forceUtf8: true);
        int moduleCount = qrData.ModuleMatrix.Count;
        int defaultTotalSize = moduleCount * ppm;

        var svgQr = new SvgQRCode(qrData);
        string baseSvg = EnsureSvgViewBox(
            svgQr.GetGraphic(ppm, "#0F172A", "#FFFFFF", drawQuietZones: true),
            defaultTotalSize,
            out int actualSvgSize);

        if (!includeLogo)
        {
            return baseSvg;
        }

        // Verify that the corresponding PNG with center logo decodes 100% cleanly via ZXing.Net; if not, fall back to clean no-logo SVG
        byte[] verifyPng = GenerateQrPngBytes(safeContent, Math.Max(ppm, 8), includeLogo: true);
        if (!TryDecodeQrImage(verifyPng, out string decoded, out _) ||
            !string.Equals(decoded, safeContent.Trim(), StringComparison.Ordinal))
        {
            using var fallbackData = qrGenerator.CreateQrCode(safeContent, QRCodeGenerator.ECCLevel.M, forceUtf8: true);
            int fallbackTotal = fallbackData.ModuleMatrix.Count * ppm;
            return EnsureSvgViewBox(
                new SvgQRCode(fallbackData).GetGraphic(ppm, "#0F172A", "#FFFFFF", drawQuietZones: true),
                fallbackTotal,
                out _);
        }

        double totalSize = actualSvgSize > 0 ? actualSvgSize : defaultTotalSize;
        // Center emblem occupies 20% of width (4.0% of total QR area, well below the <= 10% limit)
        double boxSize = Math.Round(totalSize * 0.20, 2);
        double boxPos = Math.Round((totalSize - boxSize) / 2.0, 2);
        double innerSize = Math.Round(boxSize * 0.76, 2);
        double innerPos = Math.Round((totalSize - innerSize) / 2.0, 2);
        double rOuter = Math.Round(boxSize * 0.22, 2);
        double rInner = Math.Round(innerSize * 0.20, 2);
        double cx = Math.Round(totalSize / 2.0, 2);
        double cy = Math.Round(totalSize / 2.0 + innerSize * 0.13, 2);
        double fontSize = Math.Round(innerSize * 0.38, 2);

        string logoOverlay =
            $"<g class=\"spk-qr-emblem\" aria-hidden=\"true\">" +
            $"<rect x=\"{boxPos.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" y=\"{boxPos.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" width=\"{boxSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" height=\"{boxSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" rx=\"{rOuter.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" fill=\"#FFFFFF\"/>" +
            $"<rect x=\"{innerPos.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" y=\"{innerPos.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" width=\"{innerSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" height=\"{innerSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" rx=\"{rInner.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" fill=\"#0F172A\" stroke=\"#F97316\" stroke-width=\"1.8\"/>" +
            $"<text x=\"{cx.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" y=\"{cy.ToString(System.Globalization.CultureInfo.InvariantCulture)}\" text-anchor=\"middle\" fill=\"#F97316\" font-family=\"system-ui,-apple-system,sans-serif\" font-weight=\"900\" font-size=\"{fontSize.ToString(System.Globalization.CultureInfo.InvariantCulture)}\">SPK</text>" +
            $"</g></svg>";

        int closeIdx = baseSvg.LastIndexOf("</svg>", StringComparison.OrdinalIgnoreCase);
        if (closeIdx > 0)
        {
            return baseSvg[..closeIdx] + logoOverlay;
        }

        return baseSvg;
    }

    private static string EnsureSvgViewBox(string svg, int fallbackSize, out int resolvedSize)
    {
        resolvedSize = fallbackSize;
        if (string.IsNullOrWhiteSpace(svg))
        {
            return svg;
        }

        var widthMatch = System.Text.RegularExpressions.Regex.Match(
            svg,
            @"\bwidth=""(\d+)""",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (widthMatch.Success && int.TryParse(widthMatch.Groups[1].Value, out int parsedWidth) && parsedWidth > 0)
        {
            resolvedSize = parsedWidth;
        }

        if (svg.Contains("viewBox=", StringComparison.OrdinalIgnoreCase))
        {
            return svg;
        }

        int svgOpenIdx = svg.IndexOf("<svg ", StringComparison.OrdinalIgnoreCase);
        if (svgOpenIdx >= 0)
        {
            return svg.Insert(
                svgOpenIdx + 5,
                $"viewBox=\"0 0 {resolvedSize} {resolvedSize}\" preserveAspectRatio=\"xMidYMid meet\" ");
        }

        return svg;
    }

    /// <summary>
    /// Generates a real ISO/IEC 18004 QR Code PNG byte array using QRCoder.
    /// Supports optional center Spike emblem (ECC Level H, <= 5% QR area with pure white quiet pad) and target pixel sizing,
    /// with automatic ZXing self-verification fallback to no-logo if decoding fails.
    /// </summary>
    public byte[] GenerateQrPngBytes(string content, int pixelsPerModule = 8, bool includeLogo = false, int targetSizePx = 0)
    {
        string safeContent = content ?? string.Empty;
        int ppm = Math.Clamp(pixelsPerModule, 4, 24);
        var ecc = includeLogo ? QRCodeGenerator.ECCLevel.H : QRCodeGenerator.ECCLevel.M;

        using var qrGenerator = new QRCodeGenerator();
        using var qrData = qrGenerator.CreateQrCode(safeContent, ecc, forceUtf8: true);
        var pngQr = new PngByteQRCode(qrData);
        byte[] baseBytes = pngQr.GetGraphic(ppm, new byte[] { 15, 23, 42 }, new byte[] { 255, 255, 255 }, drawQuietZones: true);

        if (!includeLogo && targetSizePx <= 0)
        {
            return baseBytes;
        }

        using Image<Rgba32> img = Image.Load<Rgba32>(baseBytes);
        if (targetSizePx >= 180)
        {
            int clampedTarget = Math.Clamp(targetSizePx, 180, 1400);
            if (img.Width != clampedTarget)
            {
                img.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(clampedTarget, clampedTarget),
                    Sampler = KnownResamplers.NearestNeighbor
                }));
            }
        }

        byte[] cleanResizedBytes;
        using (var msClean = new MemoryStream())
        {
            img.SaveAsPng(msClean);
            cleanResizedBytes = msClean.ToArray();
        }

        if (!includeLogo)
        {
            return cleanResizedBytes;
        }

        // Draw centered emblem covering 18% of width (~3.24% of total QR area, well below <= 10% limit)
        int w = img.Width;
        int h = img.Height;
        int padBox = Math.Max(18, (int)Math.Round(w * 0.18));
        if (padBox % 2 != w % 2) padBox++;
        int startX = (w - padBox) / 2;
        int startY = (h - padBox) / 2;
        int innerMargin = Math.Max(3, (int)Math.Round(padBox * 0.16));
        int borderThickness = Math.Max(2, (int)Math.Round(padBox * 0.08));
        int dotRadius = Math.Max(2, (int)Math.Round(padBox * 0.14));
        int centerX = w / 2;
        int centerY = h / 2;

        var white = new Rgba32(255, 255, 255, 255);
        var navy = new Rgba32(15, 23, 42, 255);
        var orange = new Rgba32(249, 115, 22, 255);

        img.ProcessPixelRows(accessor =>
        {
            for (int y = startY; y < startY + padBox && y < h; y++)
            {
                if (y < 0) continue;
                var row = accessor.GetRowSpan(y);
                for (int x = startX; x < startX + padBox && x < w; x++)
                {
                    if (x < 0) continue;
                    int relX = x - startX;
                    int relY = y - startY;

                    bool inInner = relX >= innerMargin && relX < padBox - innerMargin &&
                                   relY >= innerMargin && relY < padBox - innerMargin;
                    if (!inInner)
                    {
                        row[x] = white;
                        continue;
                    }

                    bool isBorder = relX < innerMargin + borderThickness ||
                                    relX >= padBox - innerMargin - borderThickness ||
                                    relY < innerMargin + borderThickness ||
                                    relY >= padBox - innerMargin - borderThickness;
                    if (isBorder)
                    {
                        row[x] = orange;
                        continue;
                    }

                    int dx = x - centerX;
                    int dy = y - centerY;
                    if (dx * dx + dy * dy <= dotRadius * dotRadius)
                    {
                        row[x] = orange;
                    }
                    else
                    {
                        row[x] = navy;
                    }
                }
            }
        });

        using var msLogo = new MemoryStream();
        img.SaveAsPng(msLogo);
        byte[] logoBytes = msLogo.ToArray();

        // Mandatory V4-QR safety rule: verify logo QR with ZXing; if decode fails, fall back to clean no-logo QR
        if (TryDecodeQrImage(logoBytes, out string decodedText, out _) &&
            string.Equals(decodedText, safeContent.Trim(), StringComparison.Ordinal))
        {
            return logoBytes;
        }

        return cleanResizedBytes;
    }

    /// <summary>
    /// Decodes a QR Code from PNG/JPEG/WebP image bytes using ZXing.Net + ImageSharp (completely independent from QRCoder).
    /// Supports both standalone QR images and full Digital Ticket screenshots.
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
            if (TryDecodeFromImageInstance(image, out decodedText))
            {
                return true;
            }

            // If full-frame decode didn't match (e.g., high-resolution full ticket screenshot or rotated photo),
            // try upscaled/downscaled passes and sub-regions where the QR well resides on a ticket card.
            double[] scales = { 1.5, 0.75, 0.5 };
            foreach (double scale in scales)
            {
                int sw = Math.Max(64, (int)Math.Round(image.Width * scale));
                int sh = Math.Max(64, (int)Math.Round(image.Height * scale));
                if (sw > 2400 || sh > 2400) continue;

                using var scaled = image.Clone(ctx => ctx.Resize(sw, sh, KnownResamplers.Bicubic));
                if (TryDecodeFromImageInstance(scaled, out decodedText))
                {
                    return true;
                }
            }

            error = "Không tìm thấy mã QR hợp lệ trong ảnh tải lên. Vui lòng chụp rõ nét toàn bộ vùng mã QR.";
            return false;
        }
        catch
        {
            error = "Tệp tải lên không phải định dạng ảnh hợp lệ (PNG, JPG, WebP).";
            return false;
        }
    }

    private static bool TryDecodeFromImageInstance(Image<Rgba32> image, out string decodedText)
    {
        decodedText = string.Empty;
        int width = image.Width;
        int height = image.Height;
        byte[] rgbRaw = new byte[width * height * 4];
        image.CopyPixelDataTo(rgbRaw);

        var luminance = new RGBLuminanceSource(rgbRaw, width, height, RGBLuminanceSource.BitmapFormat.RGBA32);
        var options = new DecodingOptions
        {
            TryHarder = true,
            TryInverted = true,
            PossibleFormats = new[] { BarcodeFormat.QR_CODE }
        };

        var readerHybrid = new BarcodeReaderGeneric
        {
            AutoRotate = true,
            Options = options
        };

        var result = readerHybrid.Decode(luminance);
        if (result != null && !string.IsNullOrWhiteSpace(result.Text))
        {
            decodedText = result.Text.Trim();
            return true;
        }

        var readerGlobal = new BarcodeReaderGeneric(null, src => new GlobalHistogramBinarizer(src), null)
        {
            AutoRotate = true,
            Options = options
        };

        result = readerGlobal.Decode(luminance);
        if (result != null && !string.IsNullOrWhiteSpace(result.Text))
        {
            decodedText = result.Text.Trim();
            return true;
        }

        return false;
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
