using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Services;

public sealed class ClubSettingsSnapshot
{
    public string OpenTime { get; init; } = "06:00";
    public string CloseTime { get; init; } = "22:00";
    public int OpenMinutes { get; init; } = 6 * 60;
    public int CloseMinutes { get; init; } = 22 * 60;
    public string PeakStart { get; init; } = "17:00";
    public int PeakStartMinutes { get; init; } = 17 * 60;
    public int BaseCourtPrice { get; init; } = 120_000;
    public int PeakSurchargePerHour { get; init; } = 30_000;
    public int CheckInEarlyMinutes { get; init; } = 30;
    public int CheckInLateGraceMinutes { get; init; } = 60;
    public int PendingHoldMinutes { get; init; } = 30;
    public int MaxActivePendingPerActor { get; init; } = 3;
    public int CancelCutoffMinutes { get; init; } = 120;
    public string Hotline { get; init; } = "0901 234 567";
    public Dictionary<string, string> RawCms { get; init; } = new(StringComparer.OrdinalIgnoreCase);
    public string PricingVersion { get; init; } = "v1";
}

public class ClubSettingsService
{
    public static readonly IReadOnlyDictionary<string, string> AllowedCmsKeys = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        ["heroTag"] = "Nhãn nổi bật trang chủ",
        ["heroSubtag"] = "Dòng phụ trang chủ",
        ["heroTitle"] = "Tiêu đề chính trang chủ",
        ["heroSubtitle"] = "Mô tả chi tiết trang chủ",
        ["heroDesc"] = "Mô tả phụ trang chủ",
        ["courtPrice"] = "Đơn giá thuê sân tiêu chuẩn (VNĐ/giờ)",
        ["peakStart"] = "Giờ bắt đầu khung giờ vàng (HH:mm)",
        ["peakSurcharge"] = "Phụ thu khung giờ vàng (VNĐ/giờ)",
        ["openTime"] = "Giờ mở cửa (HH:mm)",
        ["closeTime"] = "Giờ đóng cửa (HH:mm)",
        ["hotline"] = "Số điện thoại Hotline CLB",
        ["clubTitle"] = "Tiêu đề khối Hội viên",
        ["clubDesc"] = "Mô tả quyền lợi Hội viên",
        ["checkInEarlyMinutes"] = "Số phút cho phép Check-in sớm trước ca",
        ["cancelCutoffMinutes"] = "Số phút tối thiểu để khách tự hủy vé đã duyệt",
        ["pendingHoldMinutes"] = "Thời gian giữ chỗ tạm cho đơn chờ duyệt (phút)",
        ["maxActivePendingPerActor"] = "Số đơn chờ duyệt tối đa đồng thời trên mỗi khách/tài khoản"
    };

    public async Task<ClubSettingsSnapshot> GetSettingsAsync(PickleballContext context)
    {
        var rows = await context.TbCmsConfigs.AsNoTracking().ToListAsync();
        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var r in rows)
        {
            if (!string.IsNullOrWhiteSpace(r.ConfigKey))
            {
                dict[r.ConfigKey.Trim()] = r.ConfigValue ?? string.Empty;
            }
        }

        string openTime = NormalizeTimeOrDefault(GetVal(dict, "openTime"), "06:00", out int openMins);
        string closeTime = NormalizeTimeOrDefault(GetVal(dict, "closeTime"), "22:00", out int closeMins);
        if (closeMins <= openMins + 60)
        {
            openTime = "06:00";
            closeTime = "22:00";
            openMins = 360;
            closeMins = 1320;
        }

        string peakStart = NormalizeTimeOrDefault(GetVal(dict, "peakStart"), "17:00", out int peakMins);
        if (peakMins < openMins || peakMins >= closeMins)
        {
            peakStart = "17:00";
            peakMins = Math.Clamp(17 * 60, openMins, closeMins);
        }

        int basePrice = ParseIntInRange(GetVal(dict, "courtPrice"), 120_000, 20_000, 5_000_000);
        int peakSurcharge = ParseIntInRange(GetVal(dict, "peakSurcharge"), 30_000, 0, 2_000_000);
        int checkInEarly = ParseIntInRange(GetVal(dict, "checkInEarlyMinutes"), 30, 5, 180);
        int cancelCutoff = ParseIntInRange(GetVal(dict, "cancelCutoffMinutes"), 120, 0, 1440);
        int pendingHold = ParseIntInRange(GetVal(dict, "pendingHoldMinutes"), 30, 1, 180);
        int maxActivePending = ParseIntInRange(GetVal(dict, "maxActivePendingPerActor"), 3, 1, 20);
        string hotline = string.IsNullOrWhiteSpace(GetVal(dict, "hotline")) ? "0901 234 567" : GetVal(dict, "hotline")!.Trim();

        string versionSeed = $"{openTime}|{closeTime}|{peakStart}|{basePrice}|{peakSurcharge}|{pendingHold}|{maxActivePending}";
        string versionHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(versionSeed)))[..12].ToLowerInvariant();

        return new ClubSettingsSnapshot
        {
            OpenTime = openTime,
            CloseTime = closeTime,
            OpenMinutes = openMins,
            CloseMinutes = closeMins,
            PeakStart = peakStart,
            PeakStartMinutes = peakMins,
            BaseCourtPrice = basePrice,
            PeakSurchargePerHour = peakSurcharge,
            CheckInEarlyMinutes = checkInEarly,
            CheckInLateGraceMinutes = 60,
            PendingHoldMinutes = pendingHold,
            MaxActivePendingPerActor = maxActivePending,
            CancelCutoffMinutes = cancelCutoff,
            Hotline = hotline,
            RawCms = dict,
            PricingVersion = versionHash
        };
    }

    private static string? GetVal(Dictionary<string, string> dict, string key) =>
        dict.TryGetValue(key, out var v) ? v : null;

    private static string NormalizeTimeOrDefault(string? raw, string fallback, out int minutes)
    {
        if (!string.IsNullOrWhiteSpace(raw) && TimeOnly.TryParse(raw.Trim(), out var t))
        {
            minutes = t.Hour * 60 + t.Minute;
            return $"{t.Hour:D2}:{t.Minute:D2}";
        }

        var fb = TimeOnly.Parse(fallback);
        minutes = fb.Hour * 60 + fb.Minute;
        return fallback;
    }

    private static int ParseIntInRange(string? raw, int fallback, int min, int max)
    {
        if (!string.IsNullOrWhiteSpace(raw) && int.TryParse(raw.Trim(), out int val) && val >= min && val <= max)
        {
            return val;
        }
        return fallback;
    }
}
