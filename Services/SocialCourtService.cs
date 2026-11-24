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

/// <summary>
/// Dịch vụ nghiệp vụ trung tâm cho Sân giao lưu (TbSocialProfile &amp; TbSocialInvite).
/// Chịu trách nhiệm:
/// - Ánh xạ đồng bộ ảnh đại diện (/Account/Profile) sang 8 nhân vật pixel chuẩn (sunny, ace, mochi, coco, maya, leo, kiko, kai).
/// - Xuất snapshot sân giao lưu đã lọc sạch thông tin nhạy cảm (PII) như Email, SĐT, PasswordHash.
/// - Gợi ý ghép cặp bạn tập (Matchmaking) theo trình độ DUPR, lối đánh, khung giờ rảnh và khu vực sân.
/// - Quản lý vòng đời lời mời giao lưu (gửi, chấp nhận, từ chối, hủy) kèm chống trùng lặp.
/// </summary>
public class SocialCourtService
{
    private readonly PickleballContext _db;

    public static readonly string[] ValidSpriteIds =
    {
        "sunny", "ace", "mochi", "coco", "maya", "leo", "kiko", "kai"
    };

    private static readonly Dictionary<string, string> PresetAvatarToSprite = new(StringComparer.OrdinalIgnoreCase)
    {
        ["/images/avatars/sunny.png"] = "sunny",
        ["/images/avatars/ace.png"] = "ace",
        ["/images/avatars/mochi.png"] = "mochi",
        ["/images/avatars/coco.png"] = "coco",
        ["/images/avatars/maya.png"] = "maya",
        ["/images/avatars/leo.png"] = "leo",
        ["/images/avatars/kiko.png"] = "kiko",
        ["/images/avatars/kai.png"] = "kai",
        ["/images/avatars/avatar-01.svg"] = "sunny",
        ["/images/avatars/avatar-02.svg"] = "ace",
        ["/images/avatars/avatar-03.svg"] = "mochi",
        ["/images/avatars/avatar-04.svg"] = "coco",
        ["/images/avatars/avatar-05.svg"] = "maya",
        ["/images/avatars/avatar-06.svg"] = "leo",
        ["/images/avatars/avatar-07.svg"] = "kiko",
        ["/images/avatars/avatar-08.svg"] = "kai"
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

    private static readonly Regex EmailLikeRegex = new(@"[A-Z0-9._%+-]+@[A-Z0-9.-]+\.[A-Z]{2,}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex PhoneLikeRegex = new(@"(?:\+?\d[\d\s.\-()]{7,}\d)", RegexOptions.Compiled);

    public SocialCourtService(PickleballContext db)
    {
        _db = db;
    }

    public static bool IsCustomerAccount(TbAccount? acc) =>
        acc != null && acc.IsActive && acc.RoleId != 1 && acc.RoleId != 3;

    public async Task<bool> IsActiveCustomerAsync(int accountId)
    {
        if (accountId <= 0) return false;
        return await _db.TbAccounts
            .AsNoTracking()
            .AnyAsync(a => a.AccountId == accountId && a.IsActive && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3)));
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

    public static (string SpriteId, string AvatarMode) ResolveSpriteSelection(string? rawAvatar, string? storedSpriteId, int accountId)
    {
        string safeAvatar = Function.GetSafeAvatarUrl(rawAvatar);
        if (!string.IsNullOrEmpty(safeAvatar))
        {
            int qIdx = safeAvatar.IndexOf('?');
            string pathOnly = qIdx >= 0 ? safeAvatar[..qIdx] : safeAvatar;
            if (PresetAvatarToSprite.TryGetValue(pathOnly, out var mappedSprite))
            {
                return (mappedSprite, "preset");
            }
            return (NormalizeSpriteId(storedSpriteId, accountId), "custom-upload");
        }

        return (NormalizeSpriteId(storedSpriteId, accountId), "fallback");
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

    private static bool ContainsContactLikeData(string value) =>
        EmailLikeRegex.IsMatch(value) || PhoneLikeRegex.IsMatch(value);

    public static string ResolveDisplayName(TbAccount acc)
    {
        if (!string.IsNullOrWhiteSpace(acc.FullName))
        {
            var clean = StripHtml(acc.FullName, 80);
            if (!string.IsNullOrWhiteSpace(clean) && !ContainsContactLikeData(clean))
            {
                return clean;
            }
        }
        return "Hội viên Spike";
    }

    private static string SanitizePublicField(string? input, string fallback, int maxLength = 100)
    {
        var clean = StripHtml(input, maxLength);
        if (string.IsNullOrWhiteSpace(clean) || ContainsContactLikeData(clean))
        {
            return fallback;
        }
        return clean;
    }

    public static string StripHtml(string? input, int maxLength)
    {
        if (string.IsNullOrWhiteSpace(input)) return string.Empty;
        var stripped = Regex.Replace(input, "<.*?>", string.Empty).Trim();
        return stripped.Length > maxLength ? stripped[..maxLength] : stripped;
    }

    public async Task<SocialCourtSnapshotDto?> GetCourtSnapshotAsync(int viewerAccountId, int page = 1, int pageSize = 60, string? search = null)
    {
        var viewer = await _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == viewerAccountId && a.IsActive && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3)));

        if (viewer == null)
        {
            return null;
        }

        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 120);

        var query = _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .Where(a => a.IsActive
                && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3))
                && a.SocialProfile != null
                && a.SocialProfile.IsVisibleOnCourt);

        var allVisible = await query
            .OrderByDescending(a => a.AccountId == viewerAccountId)
            .ThenByDescending(a => a.SocialProfile!.IsReadyToPlay)
            .ThenByDescending(a => a.SocialProfile!.UpdatedAtUtc)
            .ToListAsync();

        var mapped = allVisible.Select(a => MapPublicPlayer(a, viewerAccountId)).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            string q = search.Trim().ToLowerInvariant();
            mapped = mapped.Where(p =>
                p.DisplayName.ToLowerInvariant().Contains(q) ||
                p.SkillLabel.ToLowerInvariant().Contains(q) ||
                p.PlayStyle.ToLowerInvariant().Contains(q) ||
                p.PaddleBrand.ToLowerInvariant().Contains(q) ||
                p.FreeSlots.Any(s => s.ToLowerInvariant().Contains(q))).ToList();
        }

        int totalCount = mapped.Count;
        var pagedPlayers = mapped
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();

        var mySp = viewer.SocialProfile;
        var (mySpriteId, myAvatarMode) = ResolveSpriteSelection(viewer.Avatar, mySp?.SpriteId, viewer.AccountId);

        var myProfile = new MySocialProfileDto
        {
            AccountId = viewer.AccountId,
            DisplayName = ResolveDisplayName(viewer),
            Avatar = Function.GetSafeAvatarUrl(viewer.Avatar),
            AvatarMode = myAvatarMode,
            IsVisibleOnCourt = mySp?.IsVisibleOnCourt ?? false,
            IsReadyToPlay = mySp?.IsReadyToPlay ?? false,
            AllowMatchSuggestion = mySp?.AllowMatchSuggestion ?? false,
            SpriteId = mySpriteId,
            SkillConfirmed = mySp?.SkillConfirmed ?? false,
            ConfirmedSkillLevel = mySp?.ConfirmedSkillLevel ?? (ValidSkillLevels.Contains(viewer.SkillLevel ?? "") ? viewer.SkillLevel! : "Giao lưu (DUPR 3.0)"),
            PlayStyle = SanitizePublicField(viewer.PlayStyle, "Đánh đôi phối hợp (All-Court)", 120),
            DominantHand = SanitizePublicField(viewer.DominantHand, "Chưa cập nhật", 50),
            PaddleBrand = SanitizePublicField(viewer.PaddleBrand, "Chưa cập nhật", 100),
            FreeSlots = ParseFreeSlots(mySp?.PreferredFreeSlotsJson)
        };

        return new SocialCourtSnapshotDto
        {
            MyProfile = myProfile,
            Players = pagedPlayers,
            TotalCount = totalCount,
            Page = page,
            PageSize = pageSize
        };
    }

    public async Task<(bool Ok, string? Error, MySocialProfileDto? Profile)> UpdateMyProfileAsync(int accountId, UpdateSocialProfileRequest req)
    {
        var account = await _db.TbAccounts
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == accountId && a.IsActive && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3)));

        if (account == null)
        {
            return (false, "Chỉ hội viên Customer đang hoạt động mới được cập nhật hồ sơ Sân giao lưu.", null);
        }

        var sp = account.SocialProfile;
        var (resolvedSpriteId, avatarMode) = ResolveSpriteSelection(account.Avatar, req.SpriteId ?? sp?.SpriteId, accountId);

        // If user has a custom uploaded/fallback avatar and provided a valid fallback spriteId, keep it for compatibility
        string spriteToPersist = resolvedSpriteId;
        if (avatarMode != "preset" && !string.IsNullOrWhiteSpace(req.SpriteId))
        {
            var candidateSprite = req.SpriteId.Trim().ToLowerInvariant();
            if (!ValidSpriteIds.Contains(candidateSprite))
            {
                return (false, "Nhân vật pixel không hợp lệ.", null);
            }
            spriteToPersist = candidateSprite;
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

        if (sp == null)
        {
            sp = new TbSocialProfile { AccountId = accountId };
            _db.TbSocialProfiles.Add(sp);
        }

        sp.IsVisibleOnCourt = req.IsVisibleOnCourt;
        sp.IsReadyToPlay = req.IsVisibleOnCourt && req.IsReadyToPlay;
        sp.AllowMatchSuggestion = req.IsVisibleOnCourt && req.AllowMatchSuggestion;
        sp.SpriteId = spriteToPersist;
        sp.SkillConfirmed = req.SkillConfirmed;
        sp.ConfirmedSkillLevel = confirmedSkill;
        sp.PreferredFreeSlotsJson = JsonSerializer.Serialize(cleanSlots);
        sp.UpdatedAtUtc = DateTime.UtcNow;

        await _db.SaveChangesAsync();

        var (effectiveSpriteId, effectiveAvatarMode) = ResolveSpriteSelection(account.Avatar, sp.SpriteId, account.AccountId);

        return (true, null, new MySocialProfileDto
        {
            AccountId = account.AccountId,
            DisplayName = ResolveDisplayName(account),
            Avatar = Function.GetSafeAvatarUrl(account.Avatar),
            AvatarMode = effectiveAvatarMode,
            IsVisibleOnCourt = sp.IsVisibleOnCourt,
            IsReadyToPlay = sp.IsReadyToPlay,
            AllowMatchSuggestion = sp.AllowMatchSuggestion,
            SpriteId = effectiveSpriteId,
            SkillConfirmed = sp.SkillConfirmed,
            ConfirmedSkillLevel = sp.ConfirmedSkillLevel ?? "Giao lưu (DUPR 3.0)",
            PlayStyle = SanitizePublicField(account.PlayStyle, "Đánh đôi phối hợp (All-Court)", 120),
            DominantHand = SanitizePublicField(account.DominantHand, "Chưa cập nhật", 50),
            PaddleBrand = SanitizePublicField(account.PaddleBrand, "Chưa cập nhật", 100),
            FreeSlots = cleanSlots
        });
    }

    public async Task<List<SocialMatchSuggestionDto>> GetMatchSuggestionsAsync(int viewerAccountId)
    {
        var viewer = await _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == viewerAccountId && a.IsActive && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3)));

        if (viewer == null)
        {
            return new List<SocialMatchSuggestionDto>();
        }

        var candidates = await _db.TbAccounts
            .AsNoTracking()
            .Include(a => a.SocialProfile)
            .Where(a => a.AccountId != viewerAccountId
                && a.IsActive
                && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3))
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

            var (spriteId, avatarMode) = ResolveSpriteSelection(cand.Avatar, sp.SpriteId, cand.AccountId);

            scored.Add((score, new SocialMatchSuggestionDto
            {
                AccountId = cand.AccountId,
                DisplayName = ResolveDisplayName(cand),
                Avatar = Function.GetSafeAvatarUrl(cand.Avatar),
                AvatarMode = avatarMode,
                SpriteId = spriteId,
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
            var (partnerSpriteId, _) = ResolveSpriteSelection(partner.Avatar, partnerSp?.SpriteId, partner.AccountId);

            var dto = new SocialInviteDto
            {
                InviteId = inv.InviteId,
                Direction = isSender ? "sent" : "received",
                PartnerAccountId = partner.AccountId,
                PartnerName = ResolveDisplayName(partner),
                PartnerAvatar = Function.GetSafeAvatarUrl(partner.Avatar),
                PartnerSpriteId = partnerSpriteId,
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
            .FirstOrDefaultAsync(a => a.AccountId == senderAccountId && a.IsActive && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3)));
        if (sender == null)
        {
            return (false, "Chỉ hội viên Customer đang hoạt động mới có thể gửi lời mời.", null);
        }

        var receiver = await _db.TbAccounts.AsNoTracking()
            .Include(a => a.SocialProfile)
            .FirstOrDefaultAsync(a => a.AccountId == req.ReceiverAccountId && a.IsActive && (a.RoleId == null || (a.RoleId != 1 && a.RoleId != 3)));
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

        var (receiverSpriteId, _) = ResolveSpriteSelection(receiver.Avatar, receiver.SocialProfile.SpriteId, receiver.AccountId);

        var dto = new SocialInviteDto
        {
            InviteId = invite.InviteId,
            Direction = "sent",
            PartnerAccountId = receiver.AccountId,
            PartnerName = ResolveDisplayName(receiver),
            PartnerAvatar = Function.GetSafeAvatarUrl(receiver.Avatar),
            PartnerSpriteId = receiverSpriteId,
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
        if (!await IsActiveCustomerAsync(actorAccountId))
        {
            return (false, "Chỉ hội viên Customer đang hoạt động mới được phản hồi lời mời.", null);
        }

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
        var (spriteId, avatarMode) = ResolveSpriteSelection(acc.Avatar, sp.SpriteId, acc.AccountId);

        return new SocialCourtPlayerDto
        {
            AccountId = acc.AccountId,
            DisplayName = ResolveDisplayName(acc),
            Avatar = Function.GetSafeAvatarUrl(acc.Avatar),
            AvatarMode = avatarMode,
            SpriteId = spriteId,
            SkillConfirmed = sp.SkillConfirmed,
            SkillLabel = ResolvePublicSkillLabel(acc, sp),
            DominantHand = SanitizePublicField(acc.DominantHand, "Chưa cập nhật", 50),
            PaddleBrand = SanitizePublicField(acc.PaddleBrand, "Chưa cập nhật", 100),
            PlayStyle = SanitizePublicField(acc.PlayStyle, "Chưa cập nhật", 120),
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
    public int TotalCount { get; set; }
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 60;
}

public class MySocialProfileDto
{
    public int AccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string AvatarMode { get; set; } = "fallback";
    public bool IsVisibleOnCourt { get; set; }
    public bool IsReadyToPlay { get; set; }
    public bool AllowMatchSuggestion { get; set; }
    public string SpriteId { get; set; } = "maya";
    public bool SkillConfirmed { get; set; }
    public string ConfirmedSkillLevel { get; set; } = "Giao lưu (DUPR 3.0)";
    public string DominantHand { get; set; } = "Chưa cập nhật";
    public string PaddleBrand { get; set; } = "Chưa cập nhật";
    public string PlayStyle { get; set; } = string.Empty;
    public List<string> FreeSlots { get; set; } = new();
}

public class SocialCourtPlayerDto
{
    public int AccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string AvatarMode { get; set; } = "fallback";
    public string SpriteId { get; set; } = "maya";
    public bool SkillConfirmed { get; set; }
    public string SkillLabel { get; set; } = "Chưa xác nhận trình độ";
    public string DominantHand { get; set; } = "Chưa cập nhật";
    public string PaddleBrand { get; set; } = "Chưa cập nhật";
    public string PlayStyle { get; set; } = "Chưa cập nhật";
    public List<string> FreeSlots { get; set; } = new();
    public bool ReadyToPlay { get; set; }
    public bool IsSelf { get; set; }
}

public class UpdateSocialProfileRequest
{
    public bool IsVisibleOnCourt { get; set; }
    public bool IsReadyToPlay { get; set; }
    public bool AllowMatchSuggestion { get; set; }
    public string? SpriteId { get; set; }
    public bool SkillConfirmed { get; set; }
    public string? ConfirmedSkillLevel { get; set; }
    public List<string>? FreeSlots { get; set; }
}

public class SocialMatchSuggestionDto
{
    public int AccountId { get; set; }
    public string DisplayName { get; set; } = string.Empty;
    public string Avatar { get; set; } = string.Empty;
    public string AvatarMode { get; set; } = "fallback";
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
