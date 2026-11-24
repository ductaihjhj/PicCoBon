using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace PickleballClub.Controllers;

[ApiController]
[Route("api/assistant")]
public class AssistantController : ControllerBase
{
    private const string AssistantSessionCookieName = "spike_ai_sid";
    private const long MaxAvatarUploadBytes = 2 * 1024 * 1024;
    private const int AvatarOutputSizePx = 360;
    private const int MaxAvatarSourceDimension = 6000;

    private static readonly HashSet<string> AllowedAvatarExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private static readonly HashSet<string> AllowedAvatarContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        "image/jpeg", "image/pjpeg", "image/png", "image/webp"
    };

    private readonly IAiAssistantService _assistantService;
    private readonly GeminiOptions _options;
    private readonly PickleballContext? _context;
    private readonly ClubSettingsService? _settingsService;
    private readonly IWebHostEnvironment? _environment;
    private readonly ILogger<AssistantController> _logger;

    public AssistantController(
        IAiAssistantService assistantService,
        IOptions<GeminiOptions> options,
        PickleballContext? context = null,
        ClubSettingsService? settingsService = null,
        IWebHostEnvironment? environment = null,
        ILogger<AssistantController>? logger = null)
    {
        _assistantService = assistantService;
        _options = options.Value;
        _context = context;
        _settingsService = settingsService;
        _environment = environment;
        _logger = logger ?? NullLogger<AssistantController>.Instance;
    }

    [HttpGet("status")]
    public async Task<IActionResult> GetStatus()
    {
        var (isValid, errorCode, _) = _options.Validate();
        string avatarUrl = ClubSettingsService.DefaultAssistantAvatar;
        if (_context != null && _settingsService != null)
        {
            var settings = await _settingsService.GetSettingsAsync(_context);
            avatarUrl = settings.AssistantAvatar;
        }

        return Ok(new
        {
            ok = true,
            enabled = _options.Enabled,
            ready = isValid,
            statusCode = isValid ? "READY" : errorCode,
            avatarUrl,
            welcomeMessage = "Xin chào! 🏓 Mình là Spike Assistant. Mình có thể giúp bạn tìm sân, xem giá, tìm sản phẩm và hướng dẫn sử dụng Spike OS.",
            privacyNotice = "Trợ lý AI dùng dữ liệu Spike OS. Không nhập mật khẩu hoặc mã OTP.",
            suggestedQuestions = GeminiAssistantService.GetDefaultSuggestions(false)
        });
    }

    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [HttpPost("avatar")]
    public async Task<IActionResult> UpdateAssistantAvatar([FromForm] string? avatarUrl, [FromForm] IFormFile? avatarFile)
    {
        if (_context == null || _settingsService == null)
        {
            return StatusCode(StatusCodes.Status503ServiceUnavailable, new
            {
                ok = false,
                error = "Dịch vụ cấu hình hiện không khả dụng."
            });
        }

        var currentSettings = await _settingsService.GetSettingsAsync(_context);
        string previousAvatar = currentSettings.AssistantAvatar;
        string resolvedAvatarUrl;

        if (avatarFile != null && avatarFile.Length > 0)
        {
            var (uploadOk, uploadedUrl, uploadErr) = await SaveUploadedAssistantAvatarAsync(avatarFile);
            if (!uploadOk || string.IsNullOrWhiteSpace(uploadedUrl))
            {
                return BadRequest(new
                {
                    ok = false,
                    error = uploadErr ?? "Không thể tải lên ảnh đại diện Trợ lý AI."
                });
            }
            resolvedAvatarUrl = uploadedUrl;
        }
        else
        {
            resolvedAvatarUrl = ClubSettingsService.ResolveSafeAssistantAvatarUrl(avatarUrl);
        }

        var vnNow = BookingService.GetVietnamNow();
        string actor = Function._Username;
        var existing = await _context.TbCmsConfigs.FirstOrDefaultAsync(c => c.ConfigKey == "assistantAvatar");
        if (existing != null)
        {
            existing.ConfigValue = resolvedAvatarUrl;
            existing.UpdatedAt = vnNow;
            existing.UpdatedBy = actor;
        }
        else
        {
            _context.TbCmsConfigs.Add(new TbCmsConfig
            {
                ConfigKey = "assistantAvatar",
                ConfigValue = resolvedAvatarUrl,
                Description = ClubSettingsService.AllowedCmsKeys["assistantAvatar"],
                UpdatedAt = vnNow,
                UpdatedBy = actor
            });
        }

        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "ASSISTANT_AVATAR_UPDATED",
            EntityType = "CmsConfig",
            EntityId = "assistantAvatar",
            ActorAccountId = Function._AccountId,
            ActorUsername = actor,
            Details = $"Avatar={resolvedAvatarUrl}",
            IpAddress = HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            CreatedAt = vnNow
        });

        await _context.SaveChangesAsync();

        if (!string.Equals(previousAvatar, resolvedAvatarUrl, StringComparison.OrdinalIgnoreCase))
        {
            TryDeletePreviousUploadedAssistantAvatar(previousAvatar);
        }

        _logger.LogInformation("Admin updated Spike Assistant avatar (Avatar={Avatar}, Actor={Actor})",
            resolvedAvatarUrl, actor);

        return Ok(new
        {
            ok = true,
            avatarUrl = resolvedAvatarUrl,
            message = "Đã cập nhật ảnh đại diện cho Trợ lý AI Spike Assistant!"
        });
    }

    [HttpPost("chat")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AssistantLimiter")]
    public async Task<IActionResult> Chat([FromBody] AssistantChatRequest? request)
    {
        if (request == null || string.IsNullOrWhiteSpace(request.Message))
        {
            return BadRequest(new
            {
                ok = false,
                errorCode = "EMPTY_MESSAGE",
                error = "Vui lòng nhập nội dung câu hỏi.",
                pose = "idle"
            });
        }

        var actor = BuildActorContext(request);
        string partitionKey = ResolveSessionPartitionKey(actor);

        var result = await _assistantService.ChatAsync(request, actor, partitionKey, HttpContext.RequestAborted);
        if (!result.Ok)
        {
            int statusCode = result.ErrorCode switch
            {
                "EMPTY_MESSAGE" or "MESSAGE_TOO_LONG" => StatusCodes.Status400BadRequest,
                "DUPLICATE_IN_FLIGHT" => StatusCodes.Status429TooManyRequests,
                "ASSISTANT_DISABLED" or "GEMINI_API_KEY_MISSING" or "GEMINI_INVALID_BASE_URL" or "GEMINI_MODEL_MISSING" => StatusCodes.Status503ServiceUnavailable,
                _ => StatusCodes.Status503ServiceUnavailable
            };

            return StatusCode(statusCode, new
            {
                ok = false,
                errorCode = result.ErrorCode,
                error = result.Error,
                pose = result.Pose,
                conversationId = result.ConversationId,
                suggestedQuestions = result.SuggestedQuestions,
                links = result.Links
            });
        }

        return Ok(new
        {
            ok = true,
            reply = result.Reply,
            pose = result.Pose,
            conversationId = result.ConversationId,
            suggestedQuestions = result.SuggestedQuestions,
            links = result.Links,
            toolsUsed = result.ToolsUsed
        });
    }

    [HttpPost("reset")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("AssistantLimiter")]
    public IActionResult Reset([FromBody] AssistantChatRequest? request)
    {
        var actor = BuildActorContext(request);
        string partitionKey = ResolveSessionPartitionKey(actor);
        _assistantService.ResetConversation(partitionKey, request?.ConversationId);

        return Ok(new
        {
            ok = true,
            pose = "wave",
            conversationId = "conv-" + Guid.NewGuid().ToString("N")[..12],
            suggestedQuestions = GeminiAssistantService.GetDefaultSuggestions(false)
        });
    }

    private async Task<(bool Ok, string? RelativeUrl, string? ErrorMessage)> SaveUploadedAssistantAvatarAsync(IFormFile file)
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

            image.Metadata.ExifProfile = null;
            image.Metadata.XmpProfile = null;

            string uploadDir = GetAvatarUploadDirectory();
            Directory.CreateDirectory(uploadDir);

            string token = Guid.NewGuid().ToString("N")[..10];
            string safeFileName = $"spk-ai-avatar-{token}.webp";
            string physicalPath = Path.Combine(uploadDir, safeFileName);
            await image.SaveAsWebpAsync(physicalPath);

            return (true, $"/uploads/avatars/{safeFileName}", null);
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
            _logger.LogWarning(ex, "Assistant avatar upload decode failed");
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

    private void TryDeletePreviousUploadedAssistantAvatar(string relativeUrl)
    {
        try
        {
            string fileName = Path.GetFileName(relativeUrl);
            if (string.IsNullOrWhiteSpace(fileName) ||
                !fileName.StartsWith("spk-ai-avatar-", StringComparison.OrdinalIgnoreCase))
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
            _logger.LogWarning(ex, "Failed cleaning up replaced assistant avatar file");
        }
    }

    private AssistantActorContext BuildActorContext(AssistantChatRequest? request)
    {
        bool isLoggedIn = Function.IsLogin();
        string? headerGuestToken = Request.Headers["X-Booking-Access-Token"].FirstOrDefault();
        string? guestToken = !string.IsNullOrWhiteSpace(request?.GuestAccessToken)
            ? request!.GuestAccessToken!.Trim()
            : headerGuestToken?.Trim();

        return new AssistantActorContext
        {
            IsAuthenticated = isLoggedIn,
            AccountId = isLoggedIn ? Function._AccountId : null,
            FullName = isLoggedIn ? Function._FullName : null,
            GuestAccessToken = guestToken
        };
    }

    private string ResolveSessionPartitionKey(AssistantActorContext actor)
    {
        if (actor.IsAuthenticated && actor.AccountId is > 0)
        {
            return $"acc:{actor.AccountId.Value}";
        }

        if (Request.Cookies.TryGetValue(AssistantSessionCookieName, out var existingSid) &&
            !string.IsNullOrWhiteSpace(existingSid) &&
            Regex.IsMatch(existingSid, @"^[A-Za-z0-9_-]{16,48}$"))
        {
            return $"guest:{existingSid}";
        }

        string newSid = Guid.NewGuid().ToString("N");
        Response.Cookies.Append(AssistantSessionCookieName, newSid, new CookieOptions
        {
            HttpOnly = true,
            SameSite = SameSiteMode.Lax,
            Secure = Request.IsHttps,
            Expires = DateTimeOffset.UtcNow.AddHours(4)
        });

        return $"guest:{newSid}";
    }
}
