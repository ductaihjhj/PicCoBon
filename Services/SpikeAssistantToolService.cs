using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PickleballClub.Configuration;
using PickleballClub.Logging;
using PickleballClub.Models;

namespace PickleballClub.Services;

public sealed class AssistantActorContext
{
    public bool IsAuthenticated { get; init; }
    public int? AccountId { get; init; }
    public string? FullName { get; init; }
    public string? GuestAccessToken { get; init; }
}

public class SpikeAssistantToolService
{
    private static readonly HashSet<string> AllowedToolNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "get_club_info",
        "check_court_availability",
        "get_pricing_quote",
        "search_proshop_products",
        "get_website_guide",
        "get_my_bookings"
    };

    private readonly PickleballContext _context;
    private readonly ClubSettingsService _settingsService;
    private readonly PricingService _pricingService;
    private readonly PayPalOptions _payPalOptions;
    private readonly ILogger<SpikeAssistantToolService> _logger;

    public SpikeAssistantToolService(
        PickleballContext context,
        ClubSettingsService settingsService,
        PricingService pricingService,
        IOptions<PayPalOptions>? payPalOptions = null,
        ILogger<SpikeAssistantToolService>? logger = null)
    {
        _context = context;
        _settingsService = settingsService;
        _pricingService = pricingService;
        _payPalOptions = payPalOptions?.Value ?? new PayPalOptions();
        _logger = logger ?? NullLogger<SpikeAssistantToolService>.Instance;
    }

    public static bool IsToolAllowlisted(string? toolName) =>
        !string.IsNullOrWhiteSpace(toolName) && AllowedToolNames.Contains(toolName.Trim());

    public async Task<object> ExecuteToolAsync(
        string toolName,
        JsonElement args,
        AssistantActorContext actor,
        CancellationToken cancellationToken = default)
    {
        if (!IsToolAllowlisted(toolName))
        {
            _logger.LogWarning("Assistant tool rejected (not in allowlist): Tool={Tool}", CorrelationContext.StripControlChars(toolName, 60));
            return new { ok = false, error = "Tool is not permitted." };
        }

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(8));
        var ct = cts.Token;

        string normalized = toolName.Trim().ToLowerInvariant();
        return normalized switch
        {
            "get_club_info" => await GetClubInfoAsync(ct),
            "check_court_availability" => await CheckCourtAvailabilityAsync(args, ct),
            "get_pricing_quote" => await GetPricingQuoteAsync(args, actor, ct),
            "search_proshop_products" => await SearchProShopProductsAsync(args, ct),
            "get_website_guide" => await GetWebsiteGuideAsync(args, ct),
            "get_my_bookings" => await GetMyBookingsAsync(args, actor, ct),
            _ => new { ok = false, error = "Unsupported tool." }
        };
    }

    private async Task<object> GetClubInfoAsync(CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(_context);
        var courts = await _context.TbCourts
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CourtId)
            .ToListAsync(ct);

        string address = settings.RawCms.TryGetValue("address", out var addrVal) && !string.IsNullOrWhiteSpace(addrVal)
            ? SanitizeUntrustedText(addrVal, 160)
            : "Khu Thảo Điền, TP. Thủ Đức, TP.HCM";

        var courtSummaries = courts.Select(c =>
        {
            int baseRate = (c.BasePrice == 120_000 || c.BasePrice <= 0) ? settings.BaseCourtPrice : c.BasePrice;
            int stdHourly = baseRate + Math.Max(0, c.Surcharge);
            int peakHourly = stdHourly + settings.PeakSurchargePerHour;
            return new
            {
                courtId = c.CourtId,
                courtName = SanitizeUntrustedText(c.CourtName, 80),
                courtType = SanitizeUntrustedText(c.CourtType, 40),
                typeLabel = SanitizeUntrustedText(c.TypeLabel, 60),
                surface = SanitizeUntrustedText(c.Surface, 80),
                standardHourlyVnd = stdHourly,
                peakHourlyVnd = peakHourly,
                vipSurchargeVnd = Math.Max(0, c.Surcharge)
            };
        }).ToList();

        return new
        {
            ok = true,
            clubName = "Spike Pickleball Club",
            platformName = "Spike OS",
            founderAndDeveloper = "Phùng Đức Tài",
            address,
            hotline = SanitizeUntrustedText(settings.Hotline, 40),
            email = "contact@spikepickleball.vn",
            openingHours = $"{settings.OpenTime} – {settings.CloseTime}",
            standardHours = $"{settings.OpenTime} – {settings.PeakStart}",
            peakHours = $"{settings.PeakStart} – {settings.CloseTime}",
            baseStandardPriceVndPerHour = settings.BaseCourtPrice,
            peakSurchargeVndPerHour = settings.PeakSurchargePerHour,
            policies = new
            {
                pendingHoldMinutes = settings.PendingHoldMinutes,
                cancelCutoffMinutes = settings.CancelCutoffMinutes,
                checkInEarlyMinutes = settings.CheckInEarlyMinutes,
                maxActivePendingPerActor = settings.MaxActivePendingPerActor
            },
            activeCourtCount = courtSummaries.Count,
            courts = courtSummaries,
            verifiedRoutes = new
            {
                home = "/",
                booking = "/Booking",
                proShop = "/Product",
                lookupQr = "/Lookup",
                profile = "/Account/Profile"
            }
        };
    }

    private async Task<object> CheckCourtAvailabilityAsync(JsonElement args, CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(_context);
        DateTime vnNow = BookingService.GetVietnamNow();
        DateTime utcNow = BookingService.ToUtcFromVietnamNow(vnNow);
        DateOnly today = DateOnly.FromDateTime(vnNow);

        string? rawDate = GetStringArg(args, "date");
        DateOnly targetDate = ResolveTargetDate(rawDate, today);
        string dateIso = targetDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

        string? rawStartTime = GetStringArg(args, "startTime") ?? GetStringArg(args, "time");
        int durationMinutes = GetIntArg(args, "durationMinutes", GetIntArg(args, "duration", 60));
        durationMinutes = Math.Clamp((durationMinutes / 30) * 30, 60, 240);
        string? courtFilter = GetStringArg(args, "courtType") ?? GetStringArg(args, "courtName");

        var courtsQuery = _context.TbCourts.AsNoTracking().Where(c => c.IsActive);
        var courts = await courtsQuery.OrderBy(c => c.CourtId).ToListAsync(ct);
        if (!string.IsNullOrWhiteSpace(courtFilter))
        {
            string cleanFilter = courtFilter.Trim();
            var filtered = courts.Where(c =>
                (c.CourtName != null && c.CourtName.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase)) ||
                (c.CourtType != null && c.CourtType.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase)) ||
                (c.TypeLabel != null && c.TypeLabel.Contains(cleanFilter, StringComparison.OrdinalIgnoreCase)))
                .ToList();
            if (filtered.Count > 0) courts = filtered;
        }

        var bookingsOnDate = await _context.TbBookings
            .AsNoTracking()
            .Where(b => b.BookingDate == dateIso &&
                        b.Status != "cancelled" &&
                        b.Status != "rejected" &&
                        b.Status != "expired" &&
                        b.Status != "no_show")
            .ToListAsync(ct);

        var activeBookings = bookingsOnDate
            .Where(b => !string.Equals(b.Status, "pending", StringComparison.OrdinalIgnoreCase) ||
                        BookingService.IsPendingHoldActive(b, utcNow))
            .ToList();

        var locksOnDate = await _context.TbCourtLocks
            .AsNoTracking()
            .Where(l => l.LockDate == dateIso)
            .ToListAsync(ct);

        bool hasSpecificTime = PricingService.TryParseTimeMinutes(rawStartTime, out int reqStartMins);
        if (hasSpecificTime)
        {
            int reqEndMins = reqStartMins + durationMinutes;
            if (reqStartMins < settings.OpenMinutes || reqEndMins > settings.CloseMinutes)
            {
                return new
                {
                    ok = true,
                    serverCurrentVietnamTime = vnNow.ToString("yyyy-MM-dd HH:mm"),
                    requestedDate = dateIso,
                    requestedStartTime = PricingService.MinutesToTime(reqStartMins),
                    durationMinutes,
                    withinOpeningHours = false,
                    openingHours = $"{settings.OpenTime} – {settings.CloseTime}",
                    message = $"Khung giờ {PricingService.MinutesToTime(reqStartMins)}–{PricingService.MinutesToTime(reqEndMins)} nằm ngoài giờ mở cửa CLB ({settings.OpenTime} – {settings.CloseTime}).",
                    bookingUrl = "/Booking"
                };
            }

            if (targetDate == today && reqStartMins < (vnNow.Hour * 60 + vnNow.Minute))
            {
                return new
                {
                    ok = true,
                    serverCurrentVietnamTime = vnNow.ToString("yyyy-MM-dd HH:mm"),
                    requestedDate = dateIso,
                    requestedStartTime = PricingService.MinutesToTime(reqStartMins),
                    durationMinutes,
                    isPastTime = true,
                    message = $"Khung giờ {PricingService.MinutesToTime(reqStartMins)} hôm nay ({dateIso}) đã qua (giờ hệ thống hiện tại: {vnNow:HH:mm}).",
                    bookingUrl = "/Booking"
                };
            }

            var availableCourts = new List<object>();
            var unavailableCourts = new List<object>();

            foreach (var court in courts)
            {
                bool blockedByBooking = activeBookings.Any(b =>
                    b.CourtId == court.CourtId &&
                    PricingService.TryParseTimeMinutes(b.StartTime, out int bStart) &&
                    BookingService.RangesOverlap(reqStartMins, reqEndMins, bStart, bStart + (b.Duration > 0 ? b.Duration : 60)));

                bool blockedByLock = locksOnDate.Any(l =>
                    l.CourtId == court.CourtId &&
                    PricingService.TryParseTimeMinutes(l.StartTime, out int lStart) &&
                    BookingService.RangesOverlap(reqStartMins, reqEndMins, lStart, lStart + (l.Duration > 0 ? l.Duration : 60)));

                var quote = _pricingService.CalculateQuote(court, PricingService.MinutesToTime(reqStartMins), durationMinutes, settings: settings);
                if (!blockedByBooking && !blockedByLock)
                {
                    availableCourts.Add(new
                    {
                        courtId = court.CourtId,
                        courtName = SanitizeUntrustedText(court.CourtName, 80),
                        typeLabel = SanitizeUntrustedText(court.TypeLabel ?? court.CourtType, 60),
                        indicativeFeeVnd = quote.CourtFeeBeforeDiscount
                    });
                }
                else
                {
                    unavailableCourts.Add(new
                    {
                        courtId = court.CourtId,
                        courtName = SanitizeUntrustedText(court.CourtName, 80),
                        reason = blockedByLock ? "Đang khóa bảo trì" : "Đã có lịch đặt / giữ chỗ"
                    });
                }
            }

            return new
            {
                ok = true,
                serverCurrentVietnamTime = vnNow.ToString("yyyy-MM-dd HH:mm"),
                requestedDate = dateIso,
                requestedStartTime = PricingService.MinutesToTime(reqStartMins),
                requestedEndTime = PricingService.MinutesToTime(reqEndMins),
                durationMinutes,
                availableCount = availableCourts.Count,
                availableCourts,
                unavailableCourts,
                bookingUrl = "/Booking",
                disclaimer = "Tình trạng sân trống được kiểm tra theo thời gian thực nhưng không tự động giữ chỗ. Vui lòng hoàn tất đặt sân tại /Booking."
            };
        }

        // General day overview (e.g. "Tối nay còn sân không?" or "Ngày mai còn sân trống không?")
        int scanStartMins = settings.OpenMinutes;
        if (targetDate == today)
        {
            int nowMins = vnNow.Hour * 60 + vnNow.Minute;
            int nextHalfHour = ((nowMins + 29) / 30) * 30;
            scanStartMins = Math.Max(settings.OpenMinutes, nextHalfHour);
        }

        var courtSlotSummaries = new List<object>();
        foreach (var court in courts)
        {
            var sampleFreeSlots = new List<string>();
            for (int slotStart = scanStartMins; slotStart + durationMinutes <= settings.CloseMinutes; slotStart += 60)
            {
                int slotEnd = slotStart + durationMinutes;
                bool busy = activeBookings.Any(b =>
                                b.CourtId == court.CourtId &&
                                PricingService.TryParseTimeMinutes(b.StartTime, out int bStart) &&
                                BookingService.RangesOverlap(slotStart, slotEnd, bStart, bStart + (b.Duration > 0 ? b.Duration : 60))) ||
                            locksOnDate.Any(l =>
                                l.CourtId == court.CourtId &&
                                PricingService.TryParseTimeMinutes(l.StartTime, out int lStart) &&
                                BookingService.RangesOverlap(slotStart, slotEnd, lStart, lStart + (l.Duration > 0 ? l.Duration : 60)));

                if (!busy)
                {
                    sampleFreeSlots.Add($"{PricingService.MinutesToTime(slotStart)}–{PricingService.MinutesToTime(slotEnd)}");
                }
            }

            courtSlotSummaries.Add(new
            {
                courtId = court.CourtId,
                courtName = SanitizeUntrustedText(court.CourtName, 80),
                typeLabel = SanitizeUntrustedText(court.TypeLabel ?? court.CourtType, 60),
                availableHourSlotsCount = sampleFreeSlots.Count,
                sampleFreeSlots = sampleFreeSlots.Take(8).ToList()
            });
        }

        return new
        {
            ok = true,
            serverCurrentVietnamTime = vnNow.ToString("yyyy-MM-dd HH:mm"),
            requestedDate = dateIso,
            durationMinutes,
            openingHours = $"{settings.OpenTime} – {settings.CloseTime}",
            courts = courtSlotSummaries,
            bookingUrl = "/Booking",
            disclaimer = "Danh sách khung giờ trống mang tính tham khảo tại thời điểm tra cứu. Vui lòng chọn sân tại /Booking để giữ chỗ chính thức."
        };
    }

    private async Task<object> GetPricingQuoteAsync(JsonElement args, AssistantActorContext actor, CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(_context);
        var courts = await _context.TbCourts.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CourtId).ToListAsync(ct);

        string? startTime = GetStringArg(args, "startTime") ?? GetStringArg(args, "time");
        int durationMinutes = GetIntArg(args, "durationMinutes", GetIntArg(args, "duration", 60));
        durationMinutes = Math.Clamp((durationMinutes / 30) * 30, 30, 240);
        int? courtId = args.ValueKind == JsonValueKind.Object && args.TryGetProperty("courtId", out var cidEl) && cidEl.TryGetInt32(out int cid)
            ? cid
            : null;
        string? courtName = GetStringArg(args, "courtName") ?? GetStringArg(args, "courtType");

        TbCourt? selectedCourt = null;
        if (courtId.HasValue)
        {
            selectedCourt = courts.FirstOrDefault(c => c.CourtId == courtId.Value);
        }
        if (selectedCourt == null && !string.IsNullOrWhiteSpace(courtName))
        {
            selectedCourt = courts.FirstOrDefault(c =>
                (c.CourtName != null && c.CourtName.Contains(courtName.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (c.CourtType != null && c.CourtType.Contains(courtName.Trim(), StringComparison.OrdinalIgnoreCase)) ||
                (c.TypeLabel != null && c.TypeLabel.Contains(courtName.Trim(), StringComparison.OrdinalIgnoreCase)));
        }
        selectedCourt ??= courts.FirstOrDefault() ?? new TbCourt
        {
            CourtId = 1,
            CourtName = "Sân tiêu chuẩn",
            BasePrice = settings.BaseCourtPrice,
            Surcharge = 0
        };

        int verifiedSpend = 0;
        string? explicitTier = null;
        if (actor.IsAuthenticated && actor.AccountId.HasValue)
        {
            verifiedSpend = await _pricingService.GetVerifiedAccountNetPaidSpendAsync(_context, actor.AccountId.Value);
            var acc = await _context.TbAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == actor.AccountId.Value, ct);
            explicitTier = acc?.MemberTier;
        }

        string effectiveStart = PricingService.TryParseTimeMinutes(startTime, out _) ? startTime!.Trim() : "09:00";
        var quote = _pricingService.CalculateQuote(
            selectedCourt,
            effectiveStart,
            durationMinutes,
            posTotal: 0,
            verifiedPaidSpend: verifiedSpend,
            explicitMemberTier: explicitTier,
            settings: settings,
            isAuthenticatedMember: actor.IsAuthenticated);

        int vipSurcharge = courts.Where(c => c.Surcharge > 0).Select(c => c.Surcharge).FirstOrDefault();

        return new
        {
            ok = true,
            quoteType = "indicative_estimate",
            pricingRules = new
            {
                standardHours = $"{settings.OpenTime} – {settings.PeakStart}",
                peakHours = $"{settings.PeakStart} – {settings.CloseTime}",
                baseStandardHourlyVnd = settings.BaseCourtPrice,
                peakSurchargeHourlyVnd = settings.PeakSurchargePerHour,
                peakStandardCourtHourlyVnd = settings.BaseCourtPrice + settings.PeakSurchargePerHour,
                vipCourtSurchargeHourlyVnd = vipSurcharge,
                memberTiers = new[]
                {
                    new { tier = "Hội viên Tiêu chuẩn", minPaidSpendVnd = 0, discountPercent = 0 },
                    new { tier = "Hội viên Bạc (Silver)", minPaidSpendVnd = 2_000_000, discountPercent = 5 },
                    new { tier = "Hội viên Vàng VIP (Gold)", minPaidSpendVnd = 5_000_000, discountPercent = 10 }
                }
            },
            calculatedEstimate = new
            {
                courtId = quote.CourtId,
                courtName = SanitizeUntrustedText(quote.CourtName, 80),
                startTime = quote.StartTime,
                endTime = quote.EndTime,
                durationMinutes = quote.DurationMinutes,
                standardMinutes = quote.StandardMinutes,
                peakMinutes = quote.PeakMinutes,
                standardHourlyRateVnd = quote.StandardHourlyRate,
                peakHourlyRateVnd = quote.PeakHourlyRate,
                courtFeeBeforeDiscountVnd = quote.CourtFeeBeforeDiscount,
                appliedMemberTier = quote.MemberTier,
                discountPercent = quote.DiscountPercent,
                discountAmountVnd = quote.DiscountAmount,
                estimatedAmountDueVnd = quote.AmountDue,
                payPalSandboxUsdEstimate = _payPalOptions.VndPerUsd > 0
                    ? PayPalOptions.FormatUsdInvariant(_payPalOptions.ConvertVndToUsd(quote.AmountDue))
                    : null,
                payPalSandboxExchangeRateVndPerUsd = _payPalOptions.VndPerUsd > 0 ? _payPalOptions.VndPerUsd : (decimal?)null,
                segments = quote.Segments
            },
            bookingUrl = "/Booking",
            disclaimer = "Đây là báo giá ước tính tham khảo từ hệ thống. Báo giá chính thức sẽ được chốt khi bạn chọn sân và xác nhận tại /Booking."
        };
    }

    private async Task<object> SearchProShopProductsAsync(JsonElement args, CancellationToken ct)
    {
        string? query = GetStringArg(args, "query") ?? GetStringArg(args, "keyword");
        string? category = GetStringArg(args, "category");
        int maxResults = Math.Clamp(GetIntArg(args, "maxResults", 8), 1, 12);

        var rawProducts = await _context.TbProducts
            .AsNoTracking()
            .Include(p => p.CategoryProduct)
            .Where(p => p.IsActive == true &&
                        p.Title != null &&
                        p.Title.Trim().ToLower() != "slot")
            .OrderBy(p => p.ProductId)
            .ToListAsync(ct);

        IEnumerable<TbProduct> filtered = rawProducts;

        if (!string.IsNullOrWhiteSpace(category))
        {
            string catClean = category.Trim();
            filtered = filtered.Where(p =>
                p.CategoryProduct?.Title != null &&
                p.CategoryProduct.Title.Contains(catClean, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(query))
        {
            string qClean = query.Trim();
            var matched = filtered.Where(p =>
                (p.Title != null && p.Title.Contains(qClean, StringComparison.OrdinalIgnoreCase)) ||
                (p.CategoryProduct?.Title != null && p.CategoryProduct.Title.Contains(qClean, StringComparison.OrdinalIgnoreCase)) ||
                (p.Description != null && p.Description.Contains(qClean, StringComparison.OrdinalIgnoreCase)))
                .ToList();

            // If a specific keyword did not match, return all available products with a note so the assistant can list actual inventory
            if (matched.Count > 0)
            {
                filtered = matched;
            }
        }

        var items = filtered.Take(maxResults).Select(p =>
        {
            int stock = Math.Max(0, p.UnitInStock ?? p.Quantity ?? 0);
            int priceVnd = p.PriceSale is > 0 ? p.PriceSale.Value : (p.Price ?? 0);
            return new
            {
                productId = p.ProductId,
                title = SanitizeUntrustedText(p.Title, 90),
                category = SanitizeUntrustedText(p.CategoryProduct?.Title ?? "Pro Shop", 60),
                priceVnd,
                inStockQty = stock,
                isAvailable = stock > 0,
                description = SanitizeUntrustedText(p.Description, 120),
                url = "/Product"
            };
        }).ToList();

        return new
        {
            ok = true,
            searchQuery = SanitizeUntrustedText(query, 60),
            totalCatalogCount = rawProducts.Count,
            returnedCount = items.Count,
            products = items,
            proShopUrl = "/Product",
            disclaimer = "Số lượng tồn kho phản ánh tại thời điểm tra cứu. Quý khách đặt mua hoặc gộp vào ca sân trực tiếp tại /Product."
        };
    }

    private async Task<object> GetWebsiteGuideAsync(JsonElement args, CancellationToken ct)
    {
        var settings = await _settingsService.GetSettingsAsync(_context);
        string topic = (GetStringArg(args, "topic") ?? "general").Trim().ToLowerInvariant();

        return new
        {
            ok = true,
            topic,
            guides = new
            {
                booking = new
                {
                    route = "/Booking",
                    steps = new[]
                    {
                        "Truy cập /Booking, chọn Ngày thi đấu, Sân thi đấu, Khung giờ bắt đầu và Thời lượng (60–240 phút).",
                        "Xem bảng bóc tách giá Giờ tiêu chuẩn / Giờ vàng và ưu đãi hạng Hội viên trực tiếp.",
                        "Nhập Họ tên, Số điện thoại và chọn phương thức thanh toán (Tiền mặt tại quầy, Chuyển khoản, hoặc PayPal Sandbox).",
                        $"Đơn chờ duyệt (pending) được giữ chỗ trong {settings.PendingHoldMinutes} phút (tối đa {settings.MaxActivePendingPerActor} đơn chờ duyệt cùng lúc)."
                    }
                },
                qrCheckInAndLookup = new
                {
                    route = "/Lookup",
                    steps = new[]
                    {
                        "Sau khi đặt sân, hệ thống cấp Mã đặt sân (VD: BK-XXXXXX) kèm Vé điện tử QR ký số HMAC-SHA256 (định dạng SPK1.*).",
                        "Truy cập /Lookup và nhập Mã vé hoặc Số điện thoại để xem trạng thái vé, tải ảnh QR PNG/SVG và xuất trình tại quầy.",
                        $"Quầy Check-in mở trước giờ bắt đầu ca {settings.CheckInEarlyMinutes} phút."
                    }
                },
                cancellationPolicy = new
                {
                    route = "/Lookup",
                    steps = new[]
                    {
                        $"Khách hàng có thể tự hủy đơn chờ duyệt hoặc đơn đã duyệt trước giờ thi đấu ít nhất {settings.CancelCutoffMinutes} phút.",
                        "Nếu đơn đã thanh toán qua PayPal Sandbox và đủ điều kiện hoàn tiền khi hủy/hết hạn, hệ thống sẽ xử lý hoàn tiền theo quy trình đối soát."
                    }
                },
                proShop = new
                {
                    route = "/Product",
                    steps = new[]
                    {
                        "Truy cập /Product để chọn nước uống, đồ ăn nhẹ, bóng hoặc phụ kiện Pickleball.",
                        "Có thể chọn 'Mua lẻ / Nhận trực tiếp tại quầy' hoặc gộp trực tiếp vào ca đặt sân đang hoạt động của bạn.",
                        "Hỗ trợ thanh toán tại quầy hoặc thanh toán trực tuyến qua PayPal Sandbox."
                    }
                },
                paypalSandbox = new
                {
                    route = "/Payment/PayPalReturn",
                    environment = "PayPal Sandbox (Môi trường thử nghiệm USD)",
                    exchangeRateVndPerUsd = _payPalOptions.VndPerUsd > 0 ? _payPalOptions.VndPerUsd : 26000m,
                    steps = new[]
                    {
                        "Cổng PayPal trên Spike OS là môi trường PayPal Sandbox (USD Test Mode) phục vụ kiểm thử thanh toán an toàn.",
                        $"Số tiền VNĐ được quy đổi sang USD theo tỷ giá cấu hình (1 USD = {(_payPalOptions.VndPerUsd > 0 ? _payPalOptions.VndPerUsd : 26000m):N0}đ).",
                        "Sau khi chấp thuận trên PayPal Sandbox, bạn được chuyển về trang biên lai /Payment/PayPalReturn để xác nhận Capture và cập nhật sổ cái."
                    }
                },
                profileAndAvatar = new
                {
                    route = "/Account/Profile",
                    steps = new[]
                    {
                        "Đăng nhập tại /Account/Login và mở /Account/Profile để xem Thẻ Hội Viên, hạng tích lũy và lịch sử đặt sân.",
                        "Tại mục Ảnh đại diện hồ sơ, bạn có thể tải ảnh từ thiết bị (JPG, PNG, WEBP tối đa 2 MB) hoặc chọn 1 trong 8 nhân vật thể thao Spike OS."
                    }
                }
            }
        };
    }

    private async Task<object> GetMyBookingsAsync(JsonElement args, AssistantActorContext actor, CancellationToken ct)
    {
        string? rawCode = GetStringArg(args, "bookingCode");
        string? cleanCode = string.IsNullOrWhiteSpace(rawCode) ? null : rawCode.Trim().ToUpperInvariant();

        // 1. Authenticated user: strictly query only bookings owned by actor.AccountId
        if (actor.IsAuthenticated && actor.AccountId is > 0)
        {
            int accountId = actor.AccountId.Value;
            var query = _context.TbBookings
                .AsNoTracking()
                .Include(b => b.Court)
                .Where(b => b.AccountId == accountId);

            if (!string.IsNullOrEmpty(cleanCode))
            {
                query = query.Where(b => b.BookingCode == cleanCode);
            }

            var bookings = await query
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.StartTime)
                .Take(6)
                .ToListAsync(ct);

            if (!string.IsNullOrEmpty(cleanCode) && bookings.Count == 0)
            {
                return new
                {
                    ok = true,
                    authorized = true,
                    found = false,
                    message = $"Không tìm thấy mã đặt sân {SanitizeUntrustedText(cleanCode, 30)} thuộc tài khoản đang đăng nhập của bạn. Nếu đây là vé đặt khách vãng lai, vui lòng tra cứu tại /Lookup.",
                    lookupUrl = "/Lookup",
                    profileUrl = "/Account/Profile"
                };
            }

            return new
            {
                ok = true,
                authorized = true,
                bookingCount = bookings.Count,
                bookings = bookings.Select(MapSafeBookingSummary).ToList(),
                profileUrl = "/Account/Profile",
                lookupUrl = "/Lookup"
            };
        }

        // 2. Guest user with a specific bookingCode + verified GuestAccessToken
        if (!string.IsNullOrEmpty(cleanCode) && !string.IsNullOrWhiteSpace(actor.GuestAccessToken))
        {
            var booking = await _context.TbBookings
                .AsNoTracking()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.BookingCode == cleanCode, ct);

            if (booking != null &&
                QrTokenService.VerifyGuestAccessToken(actor.GuestAccessToken, booking.AccessTokenHash))
            {
                return new
                {
                    ok = true,
                    authorized = true,
                    bookingCount = 1,
                    bookings = new[] { MapSafeBookingSummary(booking) },
                    lookupUrl = $"/Lookup?code={Uri.EscapeDataString(booking.BookingCode)}"
                };
            }
        }

        // 3. Unauthenticated or unverified ownership -> never expose booking details
        return new
        {
            ok = true,
            authorized = false,
            requiresAuthenticationOrLookup = true,
            loginUrl = "/Account/Login",
            lookupUrl = "/Lookup",
            message = "Để bảo vệ quyền riêng tư của khách hàng, Spike Assistant chỉ hiển thị thông tin đặt sân khi bạn đã đăng nhập tài khoản sở hữu vé (/Account/Login) hoặc bạn có thể tra cứu trực tiếp bằng Mã vé & SĐT tại /Lookup."
        };
    }

    private static object MapSafeBookingSummary(TbBooking b)
    {
        int totalDue = b.AmountDue > 0 ? b.AmountDue : Math.Max(0, b.CourtFee + b.PosTotal - b.DiscountAmount);
        int paid = Math.Max(0, b.PaidAmount);
        int remaining = Math.Max(0, totalDue - paid);

        return new
        {
            bookingCode = b.BookingCode,
            courtName = SanitizeUntrustedText(b.Court?.CourtName ?? $"Sân #{b.CourtId}", 80),
            bookingDate = b.BookingDate,
            startTime = b.StartTime,
            durationMinutes = b.Duration > 0 ? b.Duration : 60,
            status = b.Status,
            paymentStatus = b.PaymentStatus,
            paymentMethod = b.PaymentMethod,
            amountDueVnd = totalDue,
            paidAmountVnd = paid,
            remainingDueVnd = remaining,
            lookupUrl = $"/Lookup?code={Uri.EscapeDataString(b.BookingCode)}"
        };
    }

    private static DateOnly ResolveTargetDate(string? rawDate, DateOnly today)
    {
        if (string.IsNullOrWhiteSpace(rawDate))
        {
            return today;
        }

        string norm = rawDate.Trim().ToLowerInvariant();
        if (norm is "today" or "hom nay" or "hôm nay" or "tonight" or "toi nay" or "tối nay")
        {
            return today;
        }
        if (norm is "tomorrow" or "ngay mai" or "ngày mai" or "mai")
        {
            return today.AddDays(1);
        }
        if (norm is "day_after_tomorrow" or "ngay kia" or "ngày kia" or "mot" or "mốt")
        {
            return today.AddDays(2);
        }

        if (DateOnly.TryParseExact(norm, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed) ||
            DateOnly.TryParse(norm, CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
        {
            if (parsed < today.AddDays(-1)) return today;
            if (parsed > today.AddDays(30)) return today.AddDays(30);
            return parsed;
        }

        return today;
    }

    private static string? GetStringArg(JsonElement args, string propName)
    {
        if (args.ValueKind != JsonValueKind.Object) return null;
        foreach (var prop in args.EnumerateObject())
        {
            if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
            {
                return prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.ToString();
            }
        }
        return null;
    }

    private static int GetIntArg(JsonElement args, string propName, int defaultValue)
    {
        if (args.ValueKind != JsonValueKind.Object) return defaultValue;
        foreach (var prop in args.EnumerateObject())
        {
            if (string.Equals(prop.Name, propName, StringComparison.OrdinalIgnoreCase))
            {
                if (prop.Value.ValueKind == JsonValueKind.Number && prop.Value.TryGetInt32(out int val))
                    return val;
                if (prop.Value.ValueKind == JsonValueKind.String && int.TryParse(prop.Value.GetString(), out int parsed))
                    return parsed;
            }
        }
        return defaultValue;
    }

    public static string SanitizeUntrustedText(string? raw, int maxLen = 160)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        string noHtml = Regex.Replace(raw, "<.*?>", " ");
        return CorrelationContext.StripControlChars(noHtml, maxLen);
    }
}
