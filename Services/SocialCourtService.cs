using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Services;

public class SocialCourtService
{
    private readonly PickleballContext _db;

    public static readonly string[] ValidSpriteIds =
    {
        "sunny", "ace", "mochi", "coco", "maya", "leo", "kiko", "kai"
    };

    public static readonly string[] ValidSkillLevels =
    {
        "Newbie / Cơ bản",
        "Giao lưu (DUPR 3.0)",
        "Khá (DUPR 3.5+)",
        "Bán chuyên (DUPR 4.0+)",
        "Chuyên nghiệp (DUPR 4.5+)"
    };

    public static readonly string[] ValidFreeSlots =
    {
        "Sáng T2–T6",
        "Chiều T2–T6",
        "Tối T2–T6",
        "Sáng T7–CN",
        "Chiều T7–CN",
        "Tối T7–CN"
    };

    public SocialCourtService(PickleballContext db)
    {
        _db = db;
    }

    public static DateTime GetVietnamNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                var tzIana = TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
                return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tzIana);
            }
            catch
            {
                return DateTime.UtcNow.AddHours(7);
            }
        }
    }

    public static string NormalizeSpriteId(string? spriteId, int accountId)
    {
        if (!string.IsNullOrWhiteSpace(spriteId))
        {
            var clean = spriteId.Trim().ToLowerInvariant();
            if (ValidSpriteIds.Contains(clean))
            {
                return clean;
            }
        }
        int idx = Math.Abs(accountId) % ValidSpriteIds.Length;
        return ValidSpriteIds[idx];
    }

    public static List<string> ParseFreeSlots(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<string>();
        }
        try
        {
            var parsed = JsonSerializer.Deserialize<List<string>>(json);
            if (parsed == null) return new List<string>();
            return parsed
                .Where(s => !string.IsNullOrWhiteSpace(s) && ValidFreeSlots.Contains(s.Trim()))
                .Select(s => s.Trim())
                .Distinct()
                .Take(6)
                .ToList();
        }
        catch
        {
            return new List<string>();
        }
    }

    private static int GetSkillRank(string? skill)
    {
        if (string.IsNullOrWhiteSpace(skill)) return 0;
        for (int i = 0; i < ValidSkillLevels.Length; i++)
        {
            if (string.Equals(ValidSkillLevels[i], skill.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                return i + 1;
            }
        }
        return 0;
    }

    private static string ResolvePublicSkillLabel(TbAccount acc, TbSocialProfile? sp)
    {
        if (sp != null && sp.SkillConfirmed)
        {
            if (!string.IsNullOrWhiteSpace(sp.ConfirmedSkillLevel))
            {
                return sp.ConfirmedSkillLevel.Trim();
            }
            if (!string.IsNullOrWhiteSpace(acc.SkillLevel))
            {
                return acc.SkillLevel.Trim();
            }
        }
        return "Chưa xác nhận trình độ";
    }

    private static string ResolveDisplayName(TbAccount acc)
    {
        if (!string.IsNullOrWhiteSpace(acc.FullName)) return acc.FullName.Trim();
        if (!string.IsNullOrWhiteSpace(acc.Username)) return acc.Username.Trim();
        return $"Hội viên #{acc.AccountId}";
    }

    public static string StripHtml(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var stripped = Regex.Replace(input, "<.*?>", string.Empty).Trim();
        return stripped.Length > maxLength ? stripped[..maxLength] : stripped;
    }

    public async Task<SocialCourtSnapshotDto?> GetCourtSnapshotAsync(int viewerAccountId)
    {
        var viewer = await _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == viewerAccountId && a.IsActive);

        if (viewer == null)
        {
            return null;
        }

        var visibleAccounts = await _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .Where(a => a.IsActive && a.SocialProfile != null && a.SocialProfile.IsVisibleOnCourt)
            .OrderByDescending(a => a.AccountId == viewerAccountId)
            .ThenByDescending(a => a.SocialProfile!.IsReadyToPlay)
            .ThenByDescending(a => a.SocialProfile!.UpdatedAtUtc)
            .Take(60)
            .ToListAsync();

        var players = visibleAccounts.Select(a => MapPublicPlayer(a, viewerAccountId)).ToList();

        var mySp = viewer.SocialProfile;
        var myProfile = new MySocialProfileDto
        {
            AccountId = viewer.AccountId,
            DisplayName = ResolveDisplayName(viewer),
            Avatar = Function.GetSafeAvatarUrl(viewer.Avatar),
            IsVisibleOnCourt = mySp?.IsVisibleOnCourt ?? false,
            IsReadyToPlay = mySp?.IsReadyToPlay ?? false,
            AllowMatchSuggestion = mySp?.AllowMatchSuggestion ?? false,
            SpriteId = NormalizeSpriteId(mySp?.SpriteId, viewer.AccountId),
            SkillConfirmed = mySp?.SkillConfirmed ?? false,
            ConfirmedSkillLevel = mySp?.ConfirmedSkillLevel ?? (ValidSkillLevels.Contains(viewer.SkillLevel ?? "") ? viewer.SkillLevel! : "Giao lưu (DUPR 3.0)"),
            PlayStyle = string.IsNullOrWhiteSpace(viewer.PlayStyle) ? "Đánh đôi phối hợp (All-Court)" : viewer.PlayStyle!.Trim(),
            Bio = string.IsNullOrWhiteSpace(viewer.Bio) ? string.Empty : viewer.Bio!.Trim(),
            FreeSlots = ParseFreeSlots(mySp?.PreferredFreeSlotsJson)
        };

        return new SocialCourtSnapshotDto
        {
            MyProfile = myProfile,
            Players = players
        };
    }

    public async Task<(bool Ok, string? Error, MySocialProfileDto? Profile)> UpdateMyProfileAsync(int accountId, UpdateSocialProfileRequest req)
    {
        var account = await _db.TbAccounts
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.IsActive);

        if (account == null)
        {
            return (false, "Tài khoản không tồn tại hoặc đang bị tạm khóa.", null);
        }

        var cleanSprite = req.SpriteId?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(cleanSprite) || !ValidSpriteIds.Contains(cleanSprite))
        {
            return (false, "Nhân vật pixel không hợp lệ.", null);
        }

        string? confirmedSkill = null;
        if (req.SkillConfirmed)
        {
            var candidateSkill = req.ConfirmedSkillLevel?.Trim();
            if (string.IsNullOrWhiteSpace(candidateSkill) || !ValidSkillLevels.Contains(candidateSkill))
            {
                return (false, "Vui lòng chọn trình độ hợp lệ khi xác nhận.", null);
            }
            confirmedSkill = candidateSkill;
        }

        var cleanSlots = (req.FreeSlots ?? new List<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s) && ValidFreeSlots.Contains(s.Trim()))
            .Select(s => s.Trim())
            .Distinct()
            .Take(6)
            .ToList();

        var sp = account.SocialProfile;
        if (sp == null)
        {
            sp = new TbSocialProfile { AccountId = accountId };
            _db.TbSocialProfiles.Add(sp);
        }

        sp.IsVisibleOnCourt = req.IsVisibleOnCourt;
        sp.IsReadyToPlay = req.IsVisibleOnCourt && req.IsReadyToPlay;
        sp.AllowMatchSuggestion = req.IsVisibleOnCourt && req.AllowMatchSuggestion;
        sp.SpriteId = cleanSprite;
        sp.SkillConfirmed = req.SkillConfirmed;
        sp.ConfirmedSkillLevel = confirmedSkill;
        sp.PreferredFreeSlotsJson = JsonSerializer.Serialize(cleanSlots);
        sp.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        return (true, null, new MySocialProfileDto
        {
            AccountId = account.AccountId,
            DisplayName = ResolveDisplayName(account),
            Avatar = Function.GetSafeAvatarUrl(account.Avatar),
            IsVisibleOnCourt = sp.IsVisibleOnCourt,
            IsReadyToPlay = sp.IsReadyToPlay,
            AllowMatchSuggestion = sp.AllowMatchSuggestion,
            SpriteId = sp.SpriteId!,
            SkillConfirmed = sp.SkillConfirmed,
            ConfirmedSkillLevel = sp.ConfirmedSkillLevel ?? "Giao lưu (DUPR 3.0)",
            PlayStyle = string.IsNullOrWhiteSpace(account.PlayStyle) ? "Đánh đôi phối hợp (All-Court)" : account.PlayStyle!.Trim(),
            Bio = string.IsNullOrWhiteSpace(account.Bio) ? string.Empty : account.Bio!.Trim(),
            FreeSlots = cleanSlots
        });
    }

    public async Task<List<SocialMatchSuggestionDto>> GetMatchSuggestionsAsync(int viewerAccountId)
    {
        var viewer = await _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == viewerAccountId && a.IsActive);

        if (viewer == null)
        {
            return new List<SocialMatchSuggestionDto>();
        }

        var candidates = await _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .Where(a => a.AccountId != viewerAccountId
                && a.IsActive
                && a.SocialProfile != null
                && a.SocialProfile.IsVisibleOnCourt
                && a.SocialProfile.AllowMatchSuggestion)
            .ToListAsync();

        var viewerSp = viewer.SocialProfile;
        var viewerSkillRank = viewerSp?.SkillConfirmed == true ? GetSkillRank(viewerSp.ConfirmedSkillLevel) : 0;
        var viewerSlots = ParseFreeSlots(viewerSp?.PreferredFreeSlotsJson);
        var viewerPlayStyle = viewer.PlayStyle?.Trim() ?? string.Empty;

        var scored = new List<(int Score, SocialMatchSuggestionDto Dto)>();

        foreach (var cand in candidates)
        {
            var sp = cand.SocialProfile!;
            int score = 0;
            var reasons = new List<string>();

            if (sp.IsReadyToPlay)
            {
                score += 15;
                reasons.Add("Đang sẵn sàng giao lưu");
            }

            var candRank = sp.SkillConfirmed ? GetSkillRank(sp.ConfirmedSkillLevel) : 0;
            if (viewerSkillRank > 0 && candRank > 0)
            {
                int diff = Math.Abs(viewerSkillRank - candRank);
                if (diff == 0)
                {
                    score += 40;
                    reasons.Add($"Cùng trình độ {sp.ConfirmedSkillLevel}");
                }
                else if (diff == 1)
                {
                    score += 25;
                    reasons.Add($"Trình độ gần nhau ({sp.ConfirmedSkillLevel})");
                }
                else
                {
                    score += 5;
                }
            }
            else if (candRank > 0)
            {
                score += 10;
                reasons.Add($"Đã xác nhận {sp.ConfirmedSkillLevel}");
            }

            var candSlots = ParseFreeSlots(sp.PreferredFreeSlotsJson);
            var overlap = viewerSlots.Intersect(candSlots, StringComparer.OrdinalIgnoreCase).ToList();
            if (overlap.Count > 0)
            {
                score += Math.Min(30, overlap.Count * 15);
                reasons.Add($"Trùng lịch rảnh: {string.Join(", ", overlap.Take(2))}");
            }
            else if (candSlots.Count > 0)
            {
                score += 5;
                reasons.Add($"Thường rảnh: {string.Join(", ", candSlots.Take(2))}");
            }

            if (!string.IsNullOrWhiteSpace(viewerPlayStyle) &&
                !string.IsNullOrWhiteSpace(cand.PlayStyle) &&
                string.Equals(viewerPlayStyle, cand.PlayStyle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                score += 12;
                reasons.Add($"Cùng lối đánh {cand.PlayStyle.Trim()}");
            }

            if (reasons.Count == 0)
            {
                reasons.Add("Đang tham gia Sân giao lưu và mở gợi ý ghép cặp");
            }

            scored.Add((score, new SocialMatchSuggestionDto
            {
                AccountId = cand.AccountId,
                DisplayName = ResolveDisplayName(cand),
                Avatar = Function.GetSafeAvatarUrl(cand.Avatar),
                SpriteId = NormalizeSpriteId(sp.SpriteId, cand.AccountId),
                SkillLabel = ResolvePublicSkillLabel(cand, sp),
                SkillConfirmed = sp.SkillConfirmed,
                ReadyToPlay = sp.IsReadyToPlay,
                Reason = string.Join(" · ", reasons.Take(3)),
                Score = score
            }));
        }

        return scored
            .OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Dto.ReadyToPlay)
            .Take(3)
            .Select(x => x.Dto)
            .ToList();
    }

    public async Task<SocialInvitesListDto> GetInvitesAsync(int accountId)
    {
        var invites = await _db.TbSocialInvites
            .AsNoTracking()
            .Include(i => i.SenderAccount).ThenInclude(a => a.SocialProfile)
            .Include(i => i.ReceiverAccount).ThenInclude(a => a.SocialProfile)
            .Where(i => i.SenderAccountId == accountId || i.ReceiverAccountId == accountId)
            .OrderByDescending(i => i.CreatedAtUtc)
            .Take(50)
            .ToListAsync();

        var sent = new List<SocialInviteDto>();
        var received = new List<SocialInviteDto>();

        foreach (var inv in invites)
        {
            var isSender = inv.SenderAccountId == accountId;
            var partner = isSender ? inv.ReceiverAccount : inv.SenderAccount;
            var partnerSp = partner.SocialProfile;

            var dto = new SocialInviteDto
            {
                InviteId = inv.InviteId,
                Direction = isSender ? "sent" : "received",
                PartnerAccountId = partner.AccountId,
                PartnerName = ResolveDisplayName(partner),
                PartnerAvatar = Function.GetSafeAvatarUrl(partner.Avatar),
                PartnerSpriteId = NormalizeSpriteId(partnerSp?.SpriteId, partner.AccountId),
                PartnerSkillLabel = ResolvePublicSkillLabel(partner, partnerSp),
                ProposedDate = inv.ProposedDate.ToString("yyyy-MM-dd"),
                ProposedStartTime = inv.ProposedStartTime,
                DurationMinutes = inv.DurationMinutes,
                Note = inv.Note ?? string.Empty,
                Status = inv.Status,
                CounterDate = inv.CounterDate?.ToString("yyyy-MM-dd"),
                CounterStartTime = inv.CounterStartTime,
                CreatedAtUtc = inv.CreatedAtUtc,
                RespondedAtUtc = inv.RespondedAtUtc
            };

            if (isSender) sent.Add(dto);
            else received.Add(dto);
        }

        return new SocialInvitesListDto
        {
            Sent = sent,
            Received = received
        };
    }

    public async Task<(bool Ok, string? Error, SocialInviteDto? Invite)> CreateInviteAsync(int senderAccountId, CreateSocialInviteRequest req)
    {
        if (req.ReceiverAccountId <= 0 || req.ReceiverAccountId == senderAccountId)
        {
            return (false, "Không thể gửi lời mời cho chính mình hoặc tài khoản không hợp lệ.", null);
        }

        var sender = await _db.TbAccounts.AsNoTracking()
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == senderAccountId && a.IsActive);
        if (sender == null)
        {
            return (false, "Tài khoản của bạn chưa hợp lệ.", null);
        }

        var receiver = await _db.TbAccounts.AsNoTracking()
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == req.ReceiverAccountId && a.IsActive);
        if (receiver == null || receiver.SocialProfile == null || !receiver.SocialProfile.IsVisibleOnCourt)
        {
            return (false, "Hội viên này hiện không mở hiển thị trên Sân giao lưu.", null);
        }

        if (!TryParseVietnamFutureSlot(req.ProposedDate, req.ProposedStartTime, out var parsedDate, out var cleanTime, out var timeError))
        {
            return (false, timeError, null);
        }

        int duration = req.DurationMinutes;
        if (duration < 60 || duration > 180 || duration % 30 != 0)
        {
            return (false, "Thời lượng giao lưu phải từ 60 đến 180 phút (bội số 30 phút).", null);
        }

        bool duplicatePending = await _db.TbSocialInvites.AnyAsync(i =>
            i.SenderAccountId == senderAccountId &&
            i.ReceiverAccountId == req.ReceiverAccountId &&
            i.Status == "pending");

        if (duplicatePending)
        {
            return (false, "Bạn đã có một lời mời đang chờ phản hồi tới hội viên này.", null);
        }

        var invite = new TbSocialInvite
        {
            SenderAccountId = senderAccountId,
            ReceiverAccountId = req.ReceiverAccountId,
            ProposedDate = parsedDate,
            ProposedStartTime = cleanTime,
            DurationMinutes = duration,
            Note = StripHtml(req.Note, 200),
            Status = "pending",
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.TbSocialInvites.Add(invite);
        await _db.SaveChangesAsync();

        var dto = new SocialInviteDto
        {
            InviteId = invite.InviteId,
            Direction = "sent",
            PartnerAccountId = receiver.AccountId,
            PartnerName = ResolveDisplayName(receiver),
            PartnerAvatar = Function.GetSafeAvatarUrl(receiver.Avatar),
            PartnerSpriteId = NormalizeSpriteId(receiver.SocialProfile.SpriteId, receiver.AccountId),
            PartnerSkillLabel = ResolvePublicSkillLabel(receiver, receiver.SocialProfile),
            ProposedDate = invite.ProposedDate.ToString("yyyy-MM-dd"),
            ProposedStartTime = invite.ProposedStartTime,
            DurationMinutes = invite.DurationMinutes,
            Note = invite.Note ?? string.Empty,
            Status = invite.Status,
            CreatedAtUtc = invite.CreatedAtUtc
        };

        return (true, null, dto);
    }

    public async Task<(bool Ok, string? Error, string? Status)> RespondInviteAsync(int actorAccountId, int inviteId, RespondSocialInviteRequest req)
    {
        var invite = await _db.TbSocialInvites.FirstOrDefaultAsync(i => i.InviteId == inviteId);
        if (invite == null)
        {
            return (false, "Không tìm thấy lời mời giao lưu.", null);
        }

        var action = req.Action?.Trim().ToLowerInvariant();
        if (string.IsNullOrWhiteSpace(action))
        {
            return (false, "Hành động phản hồi không hợp lệ.", null);
        }

        bool isSender = invite.SenderAccountId == actorAccountId;
        bool isReceiver = invite.ReceiverAccountId == actorAccountId;
        if (!isSender && !isReceiver)
        {
            return (false, "Bạn không có quyền thao tác trên lời mời này.", null);
        }

        switch (action)
        {
            case "cancel":
                if (!isSender || (invite.Status != "pending" && invite.Status != "countered"))
                {
                    return (false, "Chỉ người gửi mới có thể hủy lời mời đang chờ.", null);
                }
                invite.Status = "cancelled";
                break;

            case "accept":
                if (invite.Status == "pending" && isReceiver)
                {
                    invite.Status = "accepted";
                }
                else if (invite.Status == "countered" && isSender)
                {
                    if (invite.CounterDate.HasValue) invite.ProposedDate = invite.CounterDate.Value;
                    if (!string.IsNullOrWhiteSpace(invite.CounterStartTime)) invite.ProposedStartTime = invite.CounterStartTime;
                    invite.Status = "accepted";
                }
                else
                {
                    return (false, "Trạng thái lời mời hiện tại không cho phép đồng ý.", null);
                }
                break;

            case "decline":
                if ((invite.Status == "pending" && isReceiver) || (invite.Status == "countered" && isSender))
                {
                    invite.Status = "declined";
                }
                else
                {
                    return (false, "Không thể từ chối lời mời ở trạng thái hiện tại.", null);
                }
                break;

            case "counter":
                if (!isReceiver || invite.Status != "pending")
                {
                    return (false, "Chỉ người nhận mới có thể đề xuất giờ khác cho lời mời đang chờ.", null);
                }
                if (!TryParseVietnamFutureSlot(req.CounterDate, req.CounterStartTime, out var cDate, out var cTime, out var cErr))
                {
                    return (false, cErr, null);
                }
                invite.CounterDate = cDate;
                invite.CounterStartTime = cTime;
                invite.Status = "countered";
                break;

            default:
                return (false, "Hành động không được hỗ trợ.", null);
        }

        invite.RespondedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync();
        return (true, null, invite.Status);
    }

    private static bool TryParseVietnamFutureSlot(string? dateStr, string? timeStr, out DateOnly parsedDate, out string cleanTime, out string error)
    {
        parsedDate = default;
        cleanTime = string.Empty;
        error = string.Empty;

        if (!DateOnly.TryParseExact(dateStr?.Trim(), "yyyy-MM-dd", out parsedDate))
        {
            error = "Ngày giao lưu không đúng định dạng (yyyy-MM-dd).";
            return false;
        }

        if (!TimeOnly.TryParseExact(timeStr?.Trim(), "HH:mm", out var parsedTime))
        {
            error = "Giờ bắt đầu không đúng định dạng (HH:mm).";
            return false;
        }

        if (parsedTime.Hour < 5 || parsedTime.Hour > 22 || (parsedTime.Minute != 0 && parsedTime.Minute != 30))
        {
            error = "Giờ giao lưu hợp lệ từ 05:00 đến 22:30 (mốc 30 phút).";
            return false;
        }

        var vnNow = GetVietnamNow();
        var proposedLocal = parsedDate.ToDateTime(parsedTime);
        if (proposedLocal <= vnNow.AddMinutes(15))
        {
            error = "Không thể đặt lời mời giao lưu vào thời điểm đã qua.";
            return false;
        }

        cleanTime = parsedTime.ToString("HH:mm");
        return true;
    }

    private static SocialCourtPlayerDto MapPublicPlayer(TbAccount acc, int viewerAccountId)
    {
        var sp = acc.SocialProfile!;
        return new SocialCourtPlayerDto
        {
            AccountId = acc.AccountId,
            DisplayName = ResolveDisplayName(acc),
            Avatar = Function.GetSafeAvatarUrl(acc.Avatar),
            SpriteId = NormalizeSpriteId(sp.SpriteId, acc.AccountId),
            Bio = string.IsNullOrWhiteSpace(acc.Bio) ? "Hội viên đang tham gia Sân giao lưu Spike OS." : acc.Bio!.Trim(),
            SkillConfirmed = sp.SkillConfirmed,
            SkillLabel = ResolvePublicSkillLabel(acc, sp),
            PlayStyle = string.IsNullOrWhiteSpace(acc.PlayStyle) ? "Đánh đôi phối hợp (All-Court)" : acc.PlayStyle!.Trim(),
            FreeSlots = ParseFreeSlots(sp.PreferredFreeSlotsJson),
            ReadyToPlay = sp.IsReadyToPlay,
            IsSelf = acc.AccountId == viewerAccountId
        };
    }
}

public class SocialCourtSnapshotDto
{
    public MySocialProfileDto MyProfile { get; set; } = new();
    public List<SocialCourtPlayerDto> Players { get; set; } = new();
}

public class MySocialProfileDto
{
    public int AccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public bool IsVisibleOnCourt { get; set; }
    public bool IsReadyToPlay { get; set; }
    public bool AllowMatchSuggestion { get; set; }
    public string SpriteId { get; set; } = "maya";
    public bool SkillConfirmed { get; set; }
    public string ConfirmedSkillLevel { get; set; } = "Giao lưu (DUPR 3.0)";
    public string PlayStyle { get; set; } = string.Empty;
    public string Bio { get; set; } = string.Empty;
    public List<string> FreeSlots { get; set; } = new();
}

public class SocialCourtPlayerDto
{
    public int AccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string SpriteId { get; set; } = "maya";
    public string Bio { get; set; } = string.Empty;
    public bool SkillConfirmed { get; set; }
    public string SkillLabel { get; set; } = "Chưa xác nhận trình độ";
    public string PlayStyle { get; set; } = string.Empty;
    public List<string> FreeSlots { get; set; } = new();
    public bool ReadyToPlay { get; set; }
    public bool IsSelf { get; set; }
}

public class UpdateSocialProfileRequest
{
    public bool IsVisibleOnCourt { get; set; }
    public bool IsReadyToPlay { get; set; }
    public bool AllowMatchSuggestion { get; set; }
    public string SpriteId { get; set; } = "maya";
    public bool SkillConfirmed { get; set; }
    public string? ConfirmedSkillLevel { get; set; }
    public List<string>? FreeSlots { get; set; }
}

public class SocialMatchSuggestionDto
{
    public int AccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string SpriteId { get; set; } = "maya";
    public string SkillLabel { get; set; } = string.Empty;
    public bool SkillConfirmed { get; set; }
    public bool ReadyToPlay { get; set; }
    public string Reason { get; set; } = string.Empty;
    public int Score { get; set; }
}

public class SocialInvitesListDto
{
    public List<SocialInviteDto> Sent { get; set; } = new();
    public List<SocialInviteDto> Received { get; set; } = new();
}

public class SocialInviteDto
{
    public int InviteId { get; set; }
    public string Direction { get; set; } = "sent";
    public int PartnerAccountId { get; set; }
    public string PartnerName { get; set; } = string.Empty;
    public string PartnerAvatar { get; set; } = string.Empty;
    public string PartnerSpriteId { get; set; } = "maya";
    public string PartnerSkillLabel { get; set; } = string.Empty;
    public string ProposedDate { get; set; } = string.Empty;
    public string ProposedStartTime { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public string Note { get; set; } = string.Empty;
    public string Status { get; set; } = "pending";
    public string? CounterDate { get; set; }
    public string? CounterStartTime { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? RespondedAtUtc { get; set; }
}

public class CreateSocialInviteRequest
{
    public int ReceiverAccountId { get; set; }
    public string ProposedDate { get; set; } = string.Empty;
    public string ProposedStartTime { get; set; } = string.Empty;
    public int DurationMinutes { get; set; } = 60;
    public string? Note { get; set; }
}

public class RespondSocialInviteRequest
{
    public string Action { get; set; } = string.Empty;
    public string? CounterDate { get; set; }
    public string? CounterStartTime { get; set; }
}
