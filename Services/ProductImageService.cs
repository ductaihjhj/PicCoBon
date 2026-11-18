using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Webp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;

namespace PickleballClub.Services;

public sealed record ProductImageUploadResult(
    bool Success,
    string? RelativePath = null,
    string? ErrorMessage = null,
    int Width = 0,
    int Height = 0);

public class ProductImageService
{
    public const long MaxFileSizeBytes = 5 * 1024 * 1024; // 5 MB
    public const int MinDimensionPx = 16;
    public const int MaxSourceDimensionPx = 6000;
    public const int MaxOutputDimensionPx = 1200;
    public const string UploadUrlPrefix = "/uploads/products/";

    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".webp"
    };

    private static readonly HashSet<string> AllowedContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/jpg", "image/webp"
    };

    private static readonly HashSet<string> LegacyNonProductImages = new(StringComparer.OrdinalIgnoreCase)
    {
        "/assets/court-vip-arena.png",
        "/assets/hero-spike-pickleball.png",
        "/assets/hero-pickleball-club.png",
        "/assets/auth-athlete-banner.png"
    };

    private readonly IWebHostEnvironment _env;

    public ProductImageService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public static string GetCategoryPlaceholderUrl(string? categoryAlias)
    {
        var normalized = (categoryAlias ?? string.Empty).Trim().ToLowerInvariant();
        return normalized switch
        {
            "drink" => "/assets/products/placeholder-drink.svg",
            "food" => "/assets/products/placeholder-food.svg",
            "other" => "/assets/products/placeholder-other.svg",
            _ => "/assets/products/placeholder-default.svg"
        };
    }

    public static bool HasCustomProductImage(string? rawImage)
    {
        if (string.IsNullOrWhiteSpace(rawImage))
            return false;

        var trimmed = rawImage.Trim();
        if (!trimmed.StartsWith('/') || trimmed.StartsWith("//") || trimmed.Contains("..") || trimmed.Contains('\\'))
            return false;

        if (LegacyNonProductImages.Contains(trimmed))
            return false;

        if (trimmed.StartsWith("/assets/products/placeholder-", StringComparison.OrdinalIgnoreCase))
            return false;

        return trimmed.StartsWith(UploadUrlPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("/assets/products/", StringComparison.OrdinalIgnoreCase);
    }

    public static string NormalizeProductImageUrl(string? rawImage, string? categoryAlias)
    {
        if (HasCustomProductImage(rawImage))
            return rawImage!.Trim();

        return GetCategoryPlaceholderUrl(categoryAlias);
    }

    public async Task<ProductImageUploadResult> SaveValidatedProductImageAsync(
        IFormFile? file,
        CancellationToken cancellationToken = default)
    {
        if (file == null || file.Length <= 0)
        {
            return new ProductImageUploadResult(false, ErrorMessage: "Vui lòng chọn file ảnh hợp lệ.");
        }

        if (file.Length > MaxFileSizeBytes)
        {
            return new ProductImageUploadResult(
                false,
                ErrorMessage: "Dung lượng ảnh vượt quá giới hạn tối đa 5 MB.");
        }

        var originalFileName = file.FileName ?? string.Empty;
        if (string.IsNullOrWhiteSpace(originalFileName)
            || originalFileName.Contains("..")
            || originalFileName.IndexOfAny(new[] { '\0' }) >= 0)
        {
            return new ProductImageUploadResult(false, ErrorMessage: "Tên file ảnh không hợp lệ.");
        }

        var ext = Path.GetExtension(originalFileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExtensions.Contains(ext))
        {
            return new ProductImageUploadResult(
                false,
                ErrorMessage: "Định dạng ảnh không được hỗ trợ. Chỉ chấp nhận PNG, JPG/JPEG hoặc WebP.");
        }

        var contentType = (file.ContentType ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(contentType) || !AllowedContentTypes.Contains(contentType))
        {
            return new ProductImageUploadResult(
                false,
                ErrorMessage: "Content-Type của file không hợp lệ. Chỉ chấp nhận PNG, JPG/JPEG hoặc WebP.");
        }

        await using var memoryStream = new MemoryStream((int)Math.Min(file.Length, MaxFileSizeBytes));
        await file.CopyToAsync(memoryStream, cancellationToken);
        var bytes = memoryStream.ToArray();

        if (!HasValidImageMagicBytes(bytes))
        {
            return new ProductImageUploadResult(
                false,
                ErrorMessage: "Nội dung file không phải định dạng ảnh PNG, JPEG hoặc WebP hợp lệ.");
        }

        try
        {
            memoryStream.Position = 0;
            var imageInfo = await Image.IdentifyAsync(memoryStream, cancellationToken);
            if (imageInfo == null)
            {
                return new ProductImageUploadResult(
                    false,
                    ErrorMessage: "Không thể nhận diện cấu trúc ảnh tải lên.");
            }

            if (imageInfo.Width < MinDimensionPx || imageInfo.Height < MinDimensionPx)
            {
                return new ProductImageUploadResult(
                    false,
                    ErrorMessage: $"Kích thước ảnh quá nhỏ ({imageInfo.Width}×{imageInfo.Height}px). Tối thiểu {MinDimensionPx}×{MinDimensionPx}px.");
            }

            if (imageInfo.Width > MaxSourceDimensionPx || imageInfo.Height > MaxSourceDimensionPx)
            {
                return new ProductImageUploadResult(
                    false,
                    ErrorMessage: $"Kích thước ảnh quá lớn ({imageInfo.Width}×{imageInfo.Height}px). Tối đa {MaxSourceDimensionPx}×{MaxSourceDimensionPx}px.");
            }

            memoryStream.Position = 0;
            using var image = await Image.LoadAsync<Rgba32>(memoryStream, cancellationToken);

            // Strip all metadata profiles for security and smaller payload
            image.Metadata.ExifProfile = null;
            image.Metadata.IccProfile = null;
            image.Metadata.XmpProfile = null;

            if (image.Width > MaxOutputDimensionPx || image.Height > MaxOutputDimensionPx)
            {
                image.Mutate(ctx => ctx.Resize(new ResizeOptions
                {
                    Size = new Size(MaxOutputDimensionPx, MaxOutputDimensionPx),
                    Mode = ResizeMode.Max,
                    Sampler = KnownResamplers.Lanczos3
                }));
            }

            var webRoot = GetWebRootPath();
            var uploadDir = Path.Combine(webRoot, "uploads", "products");
            Directory.CreateDirectory(uploadDir);

            var safeFileName = $"{Guid.NewGuid():N}.webp";
            var fullOutputPath = Path.GetFullPath(Path.Combine(uploadDir, safeFileName));
            var fullUploadDir = Path.GetFullPath(uploadDir);

            if (!fullOutputPath.StartsWith(fullUploadDir, StringComparison.OrdinalIgnoreCase))
            {
                return new ProductImageUploadResult(false, ErrorMessage: "Đường dẫn lưu ảnh không an toàn.");
            }

            var encoder = new WebpEncoder
            {
                Quality = 86,
                FileFormat = WebpFileFormatType.Lossy,
                TransparentColorMode = WebpTransparentColorMode.Preserve
            };

            await using (var outStream = new FileStream(fullOutputPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                await image.SaveAsWebpAsync(outStream, encoder, cancellationToken);
            }

            var relativeUrl = $"{UploadUrlPrefix}{safeFileName}";
            return new ProductImageUploadResult(true, relativeUrl, null, image.Width, image.Height);
        }
        catch (Exception)
        {
            return new ProductImageUploadResult(
                false,
                ErrorMessage: "File ảnh bị hỏng hoặc không thể giải mã an toàn.");
        }
    }

    public static bool HasValidImageMagicBytes(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < 12)
            return false;

        // PNG: 89 50 4E 47 0D 0A 1A 0A
        var isPng = bytes[0] == 0x89 &&
                    bytes[1] == 0x50 &&
                    bytes[2] == 0x4E &&
                    bytes[3] == 0x47 &&
                    bytes[4] == 0x0D &&
                    bytes[5] == 0x0A &&
                    bytes[6] == 0x1A &&
                    bytes[7] == 0x0A;
        if (isPng)
            return true;

        // JPEG: FF D8 FF
        var isJpeg = bytes[0] == 0xFF &&
                     bytes[1] == 0xD8 &&
                     bytes[2] == 0xFF;
        if (isJpeg)
            return true;

        // WebP: RIFF....WEBP
        var isWebp = bytes[0] == 0x52 && // 'R'
                     bytes[1] == 0x49 && // 'I'
                     bytes[2] == 0x46 && // 'F'
                     bytes[3] == 0x46 && // 'F'
                     bytes[8] == 0x57 && // 'W'
                     bytes[9] == 0x45 && // 'E'
                     bytes[10] == 0x42 && // 'B'
                     bytes[11] == 0x50;  // 'P'
        return isWebp;
    }

    public bool DeletePhysicalUploadedFile(string? relativeUrl)
    {
        if (string.IsNullOrWhiteSpace(relativeUrl))
            return false;

        var trimmed = relativeUrl.Trim();
        if (!trimmed.StartsWith(UploadUrlPrefix, StringComparison.OrdinalIgnoreCase)
            || trimmed.Contains("..")
            || trimmed.Contains('\\'))
        {
            return false;
        }

        var fileName = Path.GetFileName(trimmed);
        if (string.IsNullOrWhiteSpace(fileName) || ! string.Equals($"{UploadUrlPrefix}{fileName}", trimmed, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var webRoot = GetWebRootPath();
        var uploadDir = Path.GetFullPath(Path.Combine(webRoot, "uploads", "products"));
        var fullFilePath = Path.GetFullPath(Path.Combine(uploadDir, fileName));

        if (!fullFilePath.StartsWith(uploadDir, StringComparison.OrdinalIgnoreCase))
            return false;

        try
        {
            if (File.Exists(fullFilePath))
            {
                File.Delete(fullFilePath);
                return true;
            }
        }
        catch
        {
            // Ignore IO cleanup errors so user request does not crash after DB commit
        }

        return false;
    }

    public async Task<bool> DeleteOldUploadIfUnreferencedAsync(
        PickleballContext db,
        string? oldRelativeUrl,
        int? excludeProductId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(oldRelativeUrl))
            return false;

        var trimmed = oldRelativeUrl.Trim();
        if (!trimmed.StartsWith(UploadUrlPrefix, StringComparison.OrdinalIgnoreCase))
            return false;

        var stillReferenced = await db.TbProducts
            .AsNoTracking()
            .AnyAsync(
                p => p.Image == trimmed && (!excludeProductId.HasValue || p.ProductId != excludeProductId.Value),
                cancellationToken);

        if (stillReferenced)
            return false;

        return DeletePhysicalUploadedFile(trimmed);
    }

    private string GetWebRootPath()
    {
        if (!string.IsNullOrWhiteSpace(_env.WebRootPath))
            return _env.WebRootPath;

        return Path.Combine(_env.ContentRootPath ?? Directory.GetCurrentDirectory(), "wwwroot");
    }
}
