using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers;

[ApiController]
public class ApiDbController : ControllerBase
{
    private const string GuestRecoveryCookieName = "spike_guest_bk";

    private readonly PickleballContext _context;
    private readonly BookingService _bookingService;
    private readonly OrderService _orderService;
    private readonly PricingService _pricingService;
    private readonly QrTokenService _qrTokenService;
    private readonly ClubSettingsService _settingsService;
    private readonly PasswordService _passwordService;
    private readonly IAntiforgery _antiforgery;
    private readonly IDataProtector _guestBookingProtector;

    public ApiDbController(
        PickleballContext context,
        BookingService bookingService,
        OrderService orderService,
        PricingService pricingService,
        QrTokenService qrTokenService,
        ClubSettingsService settingsService,
        PasswordService passwordService,
        IAntiforgery antiforgery,
        IDataProtectionProvider dataProtectionProvider)
    {
        _context = context;
        _bookingService = bookingService;
        _orderService = orderService;
        _pricingService = pricingService;
        _qrTokenService = qrTokenService;
        _settingsService = settingsService;
        _passwordService = passwordService;
        _antiforgery = antiforgery;
        _guestBookingProtector = dataProtectionProvider.CreateProtector("Spike.GuestBookingRecovery.v1");
    }

    public static string FormatIconHtml(string? rawIcon) =>
        IconSanitizer.FormatSafeIconHtmlString(rawIcon, width: 26, fallback: "mdi:cup-water");

    public static string NormalizeCourtImageUrl(string? rawImage, string? courtType)
    {
        if (string.IsNullOrWhiteSpace(rawImage) ||
            rawImage.Contains("hero-pickleball-club.png", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(courtType, "Indoor", StringComparison.OrdinalIgnoreCase)
                ? "/assets/court-vip-arena.png"
                : "/assets/hero-spike-pickleball.png";
        }

        return rawImage.Trim();
    }

    private string? ClientIp => HttpContext.Connection.RemoteIpAddress?.ToString();

    [HttpGet("/api/security/csrf")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult GetCsrfToken()
    {
        var tokens = _antiforgery.GetAndStoreTokens(HttpContext);
        return Ok(new
        {
            ok = true,
            token = tokens.RequestToken,
            csrfToken = tokens.RequestToken
        });
    }

    private sealed class GuestBookingRecoveryEntry
    {
        public string BookingCode { get; set; } = string.Empty;
        public string GuestAccessToken { get; set; } = string.Empty;
        public string? IdempotencyKey { get; set; }
        public DateTime ExpiresAtUtc { get; set; }
    }

    private void SaveGuestBookingRecoveryProof(string bookingCode, string rawGuestToken, string? idempotencyKey)
    {
        if (string.IsNullOrWhiteSpace(bookingCode) || string.IsNullOrWhiteSpace(rawGuestToken))
            return;

        try
        {
            var entries = ReadGuestBookingRecoveryEntries();
            entries.RemoveAll(e =>
                e.ExpiresAtUtc <= DateTime.UtcNow ||
                string.Equals(e.BookingCode, bookingCode, StringComparison.OrdinalIgnoreCase));

            entries.Insert(0, new GuestBookingRecoveryEntry
            {
                BookingCode = bookingCode.Trim().ToUpperInvariant(),
                GuestAccessToken = rawGuestToken,
                IdempotencyKey = IdempotencyService.NormalizeKey(idempotencyKey),
                ExpiresAtUtc = DateTime.UtcNow.AddHours(2)
            });

            if (entries.Count > 5)
            {
                entries.RemoveRange(5, entries.Count - 5);
            }

            string json = JsonSerializer.Serialize(entries);
            string protectedPayload = _guestBookingProtector.Protect(json);

            Response.Cookies.Append(GuestRecoveryCookieName, protectedPayload, new CookieOptions
            {
                HttpOnly = true,
                SameSite = SameSiteMode.Lax,
                Secure = Request.IsHttps,
                Expires = DateTimeOffset.UtcNow.AddHours(2),
                IsEssential = true
            });
        }
        catch
        {
            // Ignore non-fatal cookie serialization error
        }
    }

    private List<GuestBookingRecoveryEntry> ReadGuestBookingRecoveryEntries()
    {
        try
        {
            if (!Request.Cookies.TryGetValue(GuestRecoveryCookieName, out var rawCookie) ||
                string.IsNullOrWhiteSpace(rawCookie))
            {
                return new List<GuestBookingRecoveryEntry>();
            }

            string json = _guestBookingProtector.Unprotect(rawCookie);
            return JsonSerializer.Deserialize<List<GuestBookingRecoveryEntry>>(json) ?? new List<GuestBookingRecoveryEntry>();
        }
        catch
        {
            return new List<GuestBookingRecoveryEntry>();
        }
    }

    private string? TryRecoverGuestBookingToken(TbBooking booking, string? idempotencyKey)
    {
        string cleanIdem = IdempotencyService.NormalizeKey(idempotencyKey) ?? string.Empty;
        var entries = ReadGuestBookingRecoveryEntries();
        var match = entries.FirstOrDefault(e =>
            e.ExpiresAtUtc > DateTime.UtcNow &&
            string.Equals(e.BookingCode, booking.BookingCode, StringComparison.OrdinalIgnoreCase) &&
            (string.IsNullOrEmpty(cleanIdem) || string.Equals(e.IdempotencyKey, cleanIdem, StringComparison.Ordinal)));

        if (match != null && QrTokenService.VerifyGuestAccessToken(match.GuestAccessToken, booking.AccessTokenHash))
        {
            return match.GuestAccessToken;
        }

        return null;
    }

    private object MapOwnerOrStaffBookingDto(TbBooking b, string? accessTokenForUrl = null, bool embedQrSvg = false)
    {
        var utcNow = DateTime.UtcNow;
        var posItemsObj = BookingService.ParsePosItems(b.PosItemsJson)
            .Select(i => new
            {
                id = i.ProductId,
                productId = i.ProductId,
                name = i.Name,
                price = i.Price,
                qty = i.Qty,
                orderId = i.OrderId,
                orderCode = i.OrderCode
            })
            .ToList();

        object? pricingBreakdown = null;
        if (!string.IsNullOrWhiteSpace(b.PricingBreakdownJson))
        {
            try
            {
                pricingBreakdown = JsonSerializer.Deserialize<object>(b.PricingBreakdownJson);
            }
            catch
            {
                // Ignore malformed historical breakdown JSON
            }
        }

        int amountDue = b.AmountDue > 0 ? b.AmountDue : Math.Max(0, b.CourtFee + b.PosTotal - b.DiscountAmount);
        int paidAmount = b.PaidAmount;

        bool isExpiredPending = string.Equals(b.Status, "pending", StringComparison.OrdinalIgnoreCase) &&
                                !BookingService.IsPendingHoldActive(b, utcNow);
        string effectiveStatus = isExpiredPending ? "expired" : b.Status;

        DateTime? holdExpiresUtc = b.HoldExpiresAtUtc.HasValue
            ? (b.HoldExpiresAtUtc.Value.Kind == DateTimeKind.Utc
                ? b.HoldExpiresAtUtc.Value
                : DateTime.SpecifyKind(b.HoldExpiresAtUtc.Value, DateTimeKind.Utc))
            : null;
        int holdRemainingSeconds = (string.Equals(effectiveStatus, "pending", StringComparison.OrdinalIgnoreCase) && holdExpiresUtc.HasValue)
            ? Math.Max(0, (int)Math.Ceiling((holdExpiresUtc.Value - utcNow).TotalSeconds))
            : 0;

        string qrToken = !string.IsNullOrWhiteSpace(b.CheckInToken)
            ? b.CheckInToken
            : _qrTokenService.GenerateCheckInToken(b.BookingCode, b.BookingDate, DateTime.UtcNow.AddDays(30));

        string tokenQuery = !string.IsNullOrWhiteSpace(accessTokenForUrl)
            ? $"?accessToken={Uri.EscapeDataString(accessTokenForUrl)}"
            : string.Empty;

        string qrSvgUrl = $"/api/bookings/{Uri.EscapeDataString(b.BookingCode)}/qr.svg{tokenQuery}";
        string qrPngUrl = $"/api/bookings/{Uri.EscapeDataString(b.BookingCode)}/qr.png{tokenQuery}";

        return new
        {
            id = b.BookingCode,
            bookingId = b.BookingId,
            courtId = b.CourtId,
            courtName = b.Court?.CourtName ?? $"Sân #{b.CourtId}",
            userId = b.AccountId,
            date = b.BookingDate,
            name = b.CustomerName ?? "Khách hàng",
            phone = b.Phone ?? string.Empty,
            time = b.StartTime,
            duration = b.Duration,
            status = effectiveStatus,
            holdExpiresAtUtc = holdExpiresUtc?.ToString("O"),
            holdRemainingSeconds,
            players = b.Players,
            skillLevel = b.SkillLevel ?? "Giao lưu",
            courtFee = b.CourtFee,
            discountAmount = b.DiscountAmount,
            posTotal = b.PosTotal,
            amountDue,
            paidAmount,
            totalPaid = paidAmount,
            remainingDue = Math.Max(0, amountDue - paidAmount),
            paymentStatus = b.PaymentStatus ?? BookingService.ComputePaymentStatus(paidAmount, amountDue),
            paymentMethod = b.PaymentMethod ?? "cash",
            posItems = posItemsObj,
            pricingBreakdown,
            checkInToken = qrToken,
            qrSvgUrl,
            qrPngUrl,
            qrSvg = embedQrSvg ? _qrTokenService.GenerateQrSvg(qrToken, 6) : null,
            checkedInAt = b.CheckedInAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            completedAt = b.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            cancelledAt = b.CancelledAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            cancelReason = isExpiredPending && string.IsNullOrWhiteSpace(b.CancelReason)
                ? "Hết thời gian giữ chỗ chờ duyệt (TTL)"
                : b.CancelReason,
            isBusySlot = false
        };
    }

    private static object MapAnonymousBusyIntervalDto(TbBooking b) => new
    {
        courtId = b.CourtId,
        date = b.BookingDate,
        time = b.StartTime,
        duration = b.Duration,
        status = "occupied",
        isBusySlot = true
    };

    /// <summary>
    /// P0-06, P1-07, P2-04: Read-only state snapshot with zero DB writes, ETag/304 conditional caching,
    /// bounded public date window, and strict anonymous busy-interval projection (zero PII or booking metadata leaks).
    /// </summary>
    [HttpGet("/api/db/state")]
    public async Task<IActionResult> GetState()
    {
        bool isStaffOrAdmin = Function.IsStaffOrAdmin();
        int? currentAccountId = Function.IsLogin() ? Function._AccountId : null;
        int currentRoleId = Function.IsLogin() ? Function._RoleId : 0;

        var vnNow = BookingService.GetVietnamNow();
        var today = DateOnly.FromDateTime(vnNow);
        string publicMinDate = today.AddDays(-1).ToString("yyyy-MM-dd");
        string publicMaxDate = today.AddDays(30).ToString("yyyy-MM-dd");

        var settings = await _settingsService.GetSettingsAsync(_context);

        var courtsRaw = await _context.TbCourts
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CourtId)
            .ToListAsync();

        var rawProducts = await _context.TbProducts
            .AsNoTracking()
            .Include(p => p.CategoryProduct)
            .Where(p => p.IsActive && p.Title != null && p.Title.Trim().ToLower() != "slot")
            .OrderBy(p => p.ProductId)
            .ToListAsync();

        List<TbBooking> scopedBookings;
        if (isStaffOrAdmin)
        {
            string staffMinDate = today.AddDays(-60).ToString("yyyy-MM-dd");
            scopedBookings = await _context.TbBookings
                .AsNoTracking()
                .Include(b => b.Court)
                .Where(b => string.Compare(b.BookingDate, staffMinDate) >= 0)
                .OrderByDescending(b => b.BookingDate)
                .ThenBy(b => b.StartTime)
                .Take(600)
                .ToListAsync();
        }
        else
        {
            var blockingStatuses = new[] { "pending", "approved", "confirmed", "playing", "active" };
            scopedBookings = await _context.TbBookings
                .AsNoTracking()
                .Include(b => b.Court)
                .Where(b => (string.Compare(b.BookingDate, publicMinDate) >= 0 &&
                             string.Compare(b.BookingDate, publicMaxDate) <= 0 &&
                             blockingStatuses.Contains(b.Status)) ||
                            (currentAccountId.HasValue && b.AccountId == currentAccountId.Value))
                .OrderByDescending(b => b.BookingDate)
                .ThenBy(b => b.StartTime)
                .ToListAsync();
        }

        var locksQuery = _context.TbCourtLocks.AsNoTracking();
        if (!isStaffOrAdmin)
        {
            locksQuery = locksQuery.Where(l =>
                string.Compare(l.LockDate, publicMinDate) >= 0 &&
                string.Compare(l.LockDate, publicMaxDate) <= 0);
        }
        var rawLocks = await locksQuery.OrderByDescending(l => l.LockDate).ToListAsync();

        List<TbOrder> standaloneOrders = new();
        object? todayLedgerSummaryDto = null;
        string todayLedgerHashSeed = "no-ledger";
        if (isStaffOrAdmin)
        {
            standaloneOrders = await _context.TbOrders
                .AsNoTracking()
                .Include(o => o.TbOrderDetails)
                .Where(o => o.BookingId == null)
                .OrderByDescending(o => o.CreatedDate)
                .Take(100)
                .ToListAsync();

            string todayIso = today.ToString("yyyy-MM-dd");
            DateTime todayStart = today.ToDateTime(TimeOnly.MinValue);
            DateTime tomorrowStart = today.AddDays(1).ToDateTime(TimeOnly.MinValue);

            var todayLedgerRows = await _context.TbPaymentTransactions
                .AsNoTracking()
                .Where(t => t.Status == "completed" && t.CreatedAt >= todayStart && t.CreatedAt < tomorrowStart)
                .ToListAsync();

            int grossToday = todayLedgerRows
                .Where(t => !string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Max(0, t.Amount));
            int refundedToday = todayLedgerRows
                .Where(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
                .Sum(t => Math.Abs(t.Amount));
            int netCollectedToday = grossToday - refundedToday;

            int bookingNetToday = todayLedgerRows
                .Where(t => t.BookingId != null)
                .Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : Math.Max(0, t.Amount));
            int shopNetToday = todayLedgerRows
                .Where(t => t.OrderId != null && t.BookingId == null)
                .Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : Math.Max(0, t.Amount));

            var billableStatuses = new[] { "approved", "confirmed", "playing", "completed" };
            var todayBillableBookings = scopedBookings
                .Where(b => b.BookingDate == todayIso && billableStatuses.Contains(b.Status))
                .ToList();

            int todayBookingReceivableTotal = todayBillableBookings
                .Sum(b => b.AmountDue > 0 ? b.AmountDue : Math.Max(0, b.CourtFee + b.PosTotal - b.DiscountAmount));
            int todayBookingUnpaid = todayBillableBookings
                .Sum(b => Math.Max(0, (b.AmountDue > 0 ? b.AmountDue : Math.Max(0, b.CourtFee + b.PosTotal - b.DiscountAmount)) - b.PaidAmount));

            var todayStandaloneOrders = standaloneOrders
                .Where(o => o.Status != "cancelled" && o.CreatedDate.HasValue && o.CreatedDate.Value >= todayStart && o.CreatedDate.Value < tomorrowStart)
                .ToList();
            int todayShopReceivableTotal = todayStandaloneOrders.Sum(o => o.TotalAmount ?? 0);
            int todayShopUnpaid = todayStandaloneOrders.Sum(o => Math.Max(0, (o.TotalAmount ?? 0) - o.PaidAmount));

            int todayReceivableTotal = todayBookingReceivableTotal + todayShopReceivableTotal;
            int todayUnpaidReceivable = todayBookingUnpaid + todayShopUnpaid;

            todayLedgerHashSeed = $"{todayIso}:{grossToday}:{refundedToday}:{netCollectedToday}:{todayReceivableTotal}:{todayUnpaidReceivable}:{todayLedgerRows.Count}";
            todayLedgerSummaryDto = new
            {
                date = todayIso,
                grossCollectedToday = grossToday,
                refundedToday,
                netCollectedToday,
                bookingNetToday,
                shopNetToday,
                todayReceivableTotal,
                todayUnpaidReceivable,
                txCountToday = todayLedgerRows.Count
            };
        }

        var utcNow = DateTime.UtcNow;

        object? currentUserDto = null;
        string currentUserHashSeed = "anon";
        if (Function.IsLogin() && currentAccountId.HasValue)
        {
            var acc = await _context.TbAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == currentAccountId.Value && a.IsActive);
            if (acc != null)
            {
                int paidSpend = await _pricingService.GetVerifiedAccountNetPaidSpendAsync(_context, acc.AccountId);
                var (tierName, discountPct) = PricingService.ResolveMemberTier(paidSpend, acc.MemberTier);
                currentUserHashSeed = $"{acc.AccountId}:{acc.FullName}:{acc.Email}:{acc.Phone}:{acc.SkillLevel}:{tierName}:{discountPct}:{paidSpend}:{acc.MustChangePassword}:{acc.RoleId}:{acc.Avatar}";
                currentUserDto = new
                {
                    id = acc.AccountId,
                    name = acc.FullName ?? acc.Username ?? "Hội viên",
                    email = acc.Email ?? string.Empty,
                    phone = acc.Phone ?? string.Empty,
                    skillLevel = acc.SkillLevel ?? "Giao lưu (DUPR 3.0)",
                    memberTier = tierName,
                    discountPercent = discountPct,
                    paidSpend,
                    mustChangePassword = acc.MustChangePassword,
                    role = acc.RoleId == 1 ? "admin" : acc.RoleId == 3 ? "staff" : "user",
                    roleLabel = acc.RoleId == 1 ? "Ban Quản Trị CLB" : acc.RoleId == 3 ? "Nhân viên Vận hành" : "Hội viên CLB",
                    avatar = acc.Avatar ?? string.Empty
                };
            }
        }

        // V4-B4: Compute comprehensive ETag over ALL returned fields so any change to any displayed property invalidates 304 cache
        var etagSb = new StringBuilder(1024);
        etagSb.Append($"u:{currentUserHashSeed}|r:{currentRoleId}|pv:{settings.PricingVersion}|");
        etagSb.Append($"c:{courtsRaw.Count}:");
        foreach (var c in courtsRaw)
        {
            etagSb.Append($"{c.CourtId}:{c.CourtName}:{c.CourtType}:{c.TypeLabel}:{c.Surface}:{c.BasePrice}:{c.Surcharge}:{c.Image}:{c.Description}:{c.IsActive};");
        }
        etagSb.Append($"|p:{rawProducts.Count}:");
        foreach (var p in rawProducts)
        {
            etagSb.Append($"{p.ProductId}:{p.Title}:{p.Price ?? 0}:{p.UnitInStock ?? p.Quantity ?? 0}:{p.CategoryProductId}:{p.Icon}:{p.Image}:{p.Description}:{p.ModifiedDate:O}:{Convert.ToHexString(p.RowVersion ?? Array.Empty<byte>())};");
        }
        etagSb.Append($"|l:{rawLocks.Count}:");
        foreach (var l in rawLocks)
        {
            etagSb.Append($"{l.LockId}:{l.LockCode}:{l.CourtId}:{l.LockDate}:{l.StartTime}:{l.Duration}:{l.Reason}:{Convert.ToHexString(l.RowVersion ?? Array.Empty<byte>())};");
        }
        etagSb.Append($"|b:{scopedBookings.Count}:");
        foreach (var b in scopedBookings)
        {
            bool isExpiredHold = string.Equals(b.Status, "pending", StringComparison.OrdinalIgnoreCase) &&
                                 !BookingService.IsPendingHoldActive(b, utcNow);
            string effStatus = isExpiredHold ? "expired" : b.Status;
            etagSb.Append($"{b.BookingId}:{b.BookingCode}:{b.CourtId}:{b.BookingDate}:{b.StartTime}:{b.Duration}:{effStatus}:{b.HoldExpiresAtUtc:O}:{b.CustomerName}:{b.Phone}:{b.CourtFee}:{b.DiscountAmount}:{b.PosTotal}:{b.AmountDue}:{b.PaidAmount}:{b.PaymentStatus}:{b.PosItemsJson}:{Convert.ToHexString(b.RowVersion ?? Array.Empty<byte>())};");
        }
        if (isStaffOrAdmin)
        {
            etagSb.Append($"|tl:{todayLedgerHashSeed}|o:{standaloneOrders.Count}:");
            foreach (var o in standaloneOrders)
            {
                etagSb.Append($"{o.OrderId}:{o.Code}:{o.Status}:{o.FulfillmentStatus}:{o.PaidAmount}:{o.TotalAmount ?? 0}:{o.PaymentStatus}:{Convert.ToHexString(o.RowVersion ?? Array.Empty<byte>())};");
            }
        }
        foreach (var kv in settings.RawCms.OrderBy(k => k.Key))
        {
            etagSb.Append($"{kv.Key}={kv.Value};");
        }

        string etagHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(etagSb.ToString())))[..20].ToLowerInvariant();
        string etag = $"\"spk-{etagHash}\"";

        Response.Headers["Cache-Control"] = "private, no-cache";
        Response.Headers.ETag = etag;

        if (Request.Headers.IfNoneMatch.Any(v => v != null && v.Split(',').Select(s => s.Trim()).Contains(etag, StringComparer.Ordinal)))
        {
            return StatusCode(StatusCodes.Status304NotModified);
        }

        var courts = courtsRaw.Select(c => new
        {
            id = c.CourtId,
            name = c.CourtName,
            type = c.CourtType,
            typeLabel = c.TypeLabel,
            surface = c.Surface,
            surcharge = c.Surcharge,
            basePrice = c.BasePrice,
            imageUrl = NormalizeCourtImageUrl(c.Image, c.CourtType),
            description = c.Description,
            isActive = c.IsActive
        }).ToList();

        var bookings = new List<object>(scopedBookings.Count);
        foreach (var b in scopedBookings)
        {
            if (isStaffOrAdmin || (currentAccountId.HasValue && b.AccountId == currentAccountId.Value))
            {
                bookings.Add(MapOwnerOrStaffBookingDto(b));
            }
            else if (BookingService.IsBookingHoldingSlot(b, utcNow, includePendingConflicts: true))
            {
                bookings.Add(MapAnonymousBusyIntervalDto(b));
            }
        }

        var myBookings = currentAccountId.HasValue
            ? scopedBookings
                .Where(b => b.AccountId == currentAccountId.Value)
                .Select(b => MapOwnerOrStaffBookingDto(b))
                .ToList()
            : new List<object>();

        var courtLocks = rawLocks.Select(l => isStaffOrAdmin
            ? (object)new
            {
                id = l.LockCode,
                courtId = l.CourtId,
                date = l.LockDate,
                startTime = l.StartTime,
                duration = l.Duration,
                reason = l.Reason
            }
            : new
            {
                courtId = l.CourtId,
                date = l.LockDate,
                startTime = l.StartTime,
                duration = l.Duration,
                status = "locked"
            }).ToList();

        var menu = rawProducts.Select(p =>
        {
            var categoryAlias = p.CategoryProduct != null && !string.IsNullOrEmpty(p.CategoryProduct.Alias)
                ? p.CategoryProduct.Alias
                : "drink";
            return new
            {
                id = p.ProductId,
                name = p.Title,
                price = p.Price ?? 0,
                stock = p.UnitInStock ?? p.Quantity ?? 0,
                type = categoryAlias,
                description = p.Description ?? string.Empty,
                imageUrl = ProductImageService.NormalizeProductImageUrl(p.Image, categoryAlias),
                rawImage = ProductImageService.HasCustomProductImage(p.Image) ? p.Image : null,
                hasCustomImage = ProductImageService.HasCustomProductImage(p.Image),
                icon = FormatIconHtml(p.Icon)
            };
        }).ToList();

        var cmsConfig = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in settings.RawCms)
        {
            if (int.TryParse(kv.Value, out int numVal))
                cmsConfig[kv.Key] = numVal;
            else
                cmsConfig[kv.Key] = kv.Value;
        }
        cmsConfig["openTime"] = settings.OpenTime;
        cmsConfig["closeTime"] = settings.CloseTime;
        cmsConfig["openHours"] = $"{settings.OpenTime} – {settings.CloseTime}";
        cmsConfig["peakStart"] = settings.PeakStart;
        cmsConfig["courtPrice"] = settings.BaseCourtPrice;
        cmsConfig["peakSurcharge"] = settings.PeakSurchargePerHour;
        cmsConfig["checkInEarlyMinutes"] = settings.CheckInEarlyMinutes;
        cmsConfig["cancelCutoffMinutes"] = settings.CancelCutoffMinutes;
        cmsConfig["pendingHoldMinutes"] = settings.PendingHoldMinutes;
        cmsConfig["maxActivePendingPerActor"] = settings.MaxActivePendingPerActor;
        cmsConfig["hotline"] = settings.Hotline;
        cmsConfig["pricingVersion"] = settings.PricingVersion;

        var ordersDto = isStaffOrAdmin
            ? standaloneOrders.Select(MapOrderDto).ToList()
            : new List<object>();

        return Ok(new
        {
            ok = true,
            etag,
            pricingVersion = settings.PricingVersion,
            serverTime = vnNow.ToString("yyyy-MM-dd HH:mm:ss"),
            serverTimeUtc = utcNow.ToString("O"),
            isStaffOrAdmin,
            currentUser = currentUserDto,
            courts,
            bookings,
            myBookings,
            courtLocks,
            menu,
            orders = ordersDto,
            todayLedgerSummary = todayLedgerSummaryDto,
            cmsConfig
        });
    }

    private static object MapOrderDto(TbOrder o)
    {
        int total = o.TotalAmount ?? 0;
        int paid = o.PaidAmount;
        return new
        {
            orderId = o.OrderId,
            code = o.Code,
            bookingId = o.BookingId,
            bookingCode = o.BookingCode ?? string.Empty,
            customerName = o.CustomerName ?? "Khách lẻ",
            phone = o.Phone ?? string.Empty,
            address = o.Address ?? "Nhận tại quầy Pro Shop",
            totalAmount = total,
            paidAmount = paid,
            remainingDue = Math.Max(0, total - paid),
            paymentStatus = o.PaymentStatus ?? BookingService.ComputePaymentStatus(paid, total),
            paymentMethod = o.PaymentMethod ?? "cash",
            status = o.Status ?? "confirmed",
            fulfillmentStatus = o.FulfillmentStatus ?? "pending",
            quantity = o.Quanlity ?? 0,
            createdDate = o.CreatedDate?.ToString("yyyy-MM-dd HH:mm:ss"),
            items = o.TbOrderDetails?.Select(d => new
            {
                productId = d.ProductId,
                price = d.Price ?? 0,
                qty = d.Quantity ?? 0
            }).ToList()
        };
    }

    // ================================================================
    // ADMIN CMS CONFIGURATION ENDPOINT (P0-05, P2-05 — replaces /api/db/sync)
    // ================================================================

    [Authorize(Roles = "Admin")]
    [ValidateAntiForgeryToken]
    [HttpPut("/api/admin/cms")]
    [HttpPost("/api/admin/cms")]
    public async Task<IActionResult> UpdateCmsConfig([FromBody] JsonElement payload)
    {
        JsonElement cmsObj = payload;
        if (payload.ValueKind == JsonValueKind.Object &&
            payload.TryGetProperty("cmsConfig", out var nested) &&
            nested.ValueKind == JsonValueKind.Object)
        {
            cmsObj = nested;
        }

        if (cmsObj.ValueKind != JsonValueKind.Object)
        {
            return BadRequest(new { ok = false, error = "Dữ liệu cấu hình CMS không hợp lệ." });
        }

        var updates = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in cmsObj.EnumerateObject())
        {
            if (!ClubSettingsService.AllowedCmsKeys.ContainsKey(prop.Name))
            {
                return BadRequest(new { ok = false, error = $"Khóa cấu hình '{prop.Name}' không nằm trong danh sách cho phép." });
            }

            string rawVal = prop.Value.ValueKind == JsonValueKind.String
                ? (prop.Value.GetString() ?? string.Empty)
                : prop.Value.GetRawText().Trim('"');

            // Strip HTML tags to prevent stored XSS in CMS fields
            string sanitized = Regex.Replace(rawVal, "<.*?>", string.Empty).Trim();
            if (sanitized.Length > 500)
            {
                return BadRequest(new { ok = false, error = $"Nội dung trường '{prop.Name}' vượt quá tối đa 500 ký tự." });
            }

            updates[prop.Name] = sanitized;
        }

        // Validate time & pricing fields before saving
        if (updates.TryGetValue("openTime", out var openStr) && !PricingService.TryParseTimeMinutes(openStr, out _))
        {
            return BadRequest(new { ok = false, error = "Giờ mở cửa (openTime) phải theo định dạng HH:mm." });
        }
        if (updates.TryGetValue("closeTime", out var closeStr) && !PricingService.TryParseTimeMinutes(closeStr, out _))
        {
            return BadRequest(new { ok = false, error = "Giờ đóng cửa (closeTime) phải theo định dạng HH:mm." });
        }
        if (updates.TryGetValue("peakStart", out var peakStr) && !PricingService.TryParseTimeMinutes(peakStr, out _))
        {
            return BadRequest(new { ok = false, error = "Giờ bắt đầu khung giờ vàng (peakStart) phải theo định dạng HH:mm." });
        }

        var currentSettings = await _settingsService.GetSettingsAsync(_context);
        string effOpen = updates.TryGetValue("openTime", out var o1) ? o1 : currentSettings.OpenTime;
        string effClose = updates.TryGetValue("closeTime", out var c1) ? c1 : currentSettings.CloseTime;
        PricingService.TryParseTimeMinutes(effOpen, out int effOpenMins);
        PricingService.TryParseTimeMinutes(effClose, out int effCloseMins);

        if (effCloseMins <= effOpenMins + 60)
        {
            return BadRequest(new { ok = false, error = "Giờ đóng cửa phải lớn hơn giờ mở cửa ít nhất 60 phút." });
        }

        if (updates.TryGetValue("courtPrice", out var cpStr))
        {
            if (!int.TryParse(cpStr, out int cp) || cp < 20_000 || cp > 5_000_000)
            {
                return BadRequest(new { ok = false, error = "Đơn giá thuê sân tiêu chuẩn phải nằm trong khoảng 20.000đ – 5.000.000đ." });
            }
        }

        if (updates.TryGetValue("peakSurcharge", out var psStr))
        {
            if (!int.TryParse(psStr, out int ps) || ps < 0 || ps > 2_000_000)
            {
                return BadRequest(new { ok = false, error = "Phụ thu khung giờ vàng phải nằm trong khoảng 0đ – 2.000.000đ." });
            }
        }

        var vnNow = BookingService.GetVietnamNow();
        string actor = Function._Username;

        foreach (var kv in updates)
        {
            var existing = await _context.TbCmsConfigs.FirstOrDefaultAsync(c => c.ConfigKey == kv.Key);
            if (existing != null)
            {
                existing.ConfigValue = kv.Value;
                existing.UpdatedAt = vnNow;
                existing.UpdatedBy = actor;
            }
            else
            {
                _context.TbCmsConfigs.Add(new TbCmsConfig
                {
                    ConfigKey = kv.Key,
                    ConfigValue = kv.Value,
                    Description = ClubSettingsService.AllowedCmsKeys[kv.Key],
                    UpdatedAt = vnNow,
                    UpdatedBy = actor
                });
            }
        }

        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "CMS_CONFIG_UPDATED",
            EntityType = "CmsConfig",
            EntityId = "GLOBAL",
            ActorAccountId = Function._AccountId,
            ActorUsername = actor,
            Details = $"Keys={string.Join(',', updates.Keys)}",
            IpAddress = ClientIp,
            CreatedAt = vnNow
        });

        await _context.SaveChangesAsync();
        var refreshed = await _settingsService.GetSettingsAsync(_context);

        return Ok(new
        {
            ok = true,
            pricingVersion = refreshed.PricingVersion,
            openTime = refreshed.OpenTime,
            closeTime = refreshed.CloseTime,
            openHours = $"{refreshed.OpenTime} – {refreshed.CloseTime}",
            peakStart = refreshed.PeakStart,
            courtPrice = refreshed.BaseCourtPrice,
            peakSurcharge = refreshed.PeakSurchargePerHour
        });
    }

    // ================================================================
    // BOOKING QUOTE, CREATE, REAL QR & SECURE LOOKUP (P0-07, P0-08, P1-01, P1-08..12)
    // ================================================================

    [HttpGet("/api/bookings/quote")]
    public async Task<IActionResult> GetBookingQuoteGet(
        [FromQuery] int courtId = 1,
        [FromQuery] string time = "17:00",
        [FromQuery] double duration = 60,
        [FromQuery] string? date = null)
    {
        return await ComputeQuoteResponseAsync(courtId, time, duration, date);
    }

    [ValidateAntiForgeryToken]
    [HttpPost("/api/bookings/quote")]
    public async Task<IActionResult> GetBookingQuotePost([FromBody] JsonElement payload)
    {
        int courtId = payload.TryGetProperty("courtId", out var cEl) && cEl.TryGetInt32(out int cid) ? cid : 1;
        string time = payload.TryGetProperty("time", out var tEl) ? (tEl.GetString() ?? "17:00") : "17:00";
        double rawDur = payload.TryGetProperty("duration", out var dEl) && dEl.TryGetDouble(out double d) ? d : 60;
        string? date = payload.TryGetProperty("date", out var dtEl) ? dtEl.GetString() : null;
        return await ComputeQuoteResponseAsync(courtId, time, rawDur, date);
    }

    private async Task<IActionResult> ComputeQuoteResponseAsync(int courtId, string time, double rawDuration, string? date = null)
    {
        string resolvedDate = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        if (!string.IsNullOrWhiteSpace(date))
        {
            if (!DateOnly.TryParseExact(date.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedQuoteDate))
            {
                return BadRequest(new { ok = false, error = "Ngày báo giá không đúng định dạng YYYY-MM-DD." });
            }
            resolvedDate = parsedQuoteDate.ToString("yyyy-MM-dd");
        }

        int durationMinutes = rawDuration <= 8
            ? (int)Math.Round(rawDuration * 60, MidpointRounding.AwayFromZero)
            : (int)Math.Round(rawDuration, MidpointRounding.AwayFromZero);

        if (durationMinutes < 30 || durationMinutes > 480 || durationMinutes % 30 != 0)
        {
            return BadRequest(new { ok = false, error = "Thời lượng đặt sân phải từ 30 đến 480 phút (bội số 30 phút)." });
        }

        var court = await _context.TbCourts.AsNoTracking().FirstOrDefaultAsync(c => c.CourtId == courtId && c.IsActive);
        if (court == null)
        {
            return NotFound(new { ok = false, error = "Không tìm thấy sân thi đấu." });
        }

        var settings = await _settingsService.GetSettingsAsync(_context);
        bool isAuthenticatedMember = Function.IsLogin() && Function._AccountId > 0;
        int verifiedSpend = 0;
        string? explicitTier = null;

        if (isAuthenticatedMember)
        {
            var acc = await _context.TbAccounts.AsNoTracking()
                .FirstOrDefaultAsync(a => a.AccountId == Function._AccountId && a.IsActive);
            if (acc != null)
            {
                explicitTier = acc.MemberTier;
                verifiedSpend = await _pricingService.GetVerifiedAccountNetPaidSpendAsync(_context, acc.AccountId);
            }
            else
            {
                isAuthenticatedMember = false;
            }
        }

        var quote = _pricingService.CalculateQuote(
            court,
            time,
            durationMinutes,
            posTotal: 0,
            verifiedPaidSpend: verifiedSpend,
            explicitMemberTier: explicitTier,
            settings: settings,
            isAuthenticatedMember: isAuthenticatedMember);

        return Ok(new
        {
            ok = true,
            courtId,
            date = resolvedDate,
            time = time.Trim(),
            durationMinutes,
            pricingVersion = settings.PricingVersion,
            quote
        });
    }

    [ValidateAntiForgeryToken]
    [EnableRateLimiting("BookingCreateLimiter")]
    [HttpPost("/api/bookings")]
    public async Task<IActionResult> CreateBooking([FromBody] JsonElement payload)
    {
        int courtId = payload.TryGetProperty("courtId", out var cEl) && cEl.TryGetInt32(out int cid) ? cid : 0;
        string customerName = payload.TryGetProperty("customerName", out var cnEl)
            ? (cnEl.GetString() ?? string.Empty)
            : (payload.TryGetProperty("name", out var nEl) ? (nEl.GetString() ?? string.Empty) : string.Empty);
        string phone = payload.TryGetProperty("customerPhone", out var cpEl)
            ? (cpEl.GetString() ?? string.Empty)
            : (payload.TryGetProperty("phone", out var pEl) ? (pEl.GetString() ?? string.Empty) : string.Empty);
        string date = payload.TryGetProperty("bookingDate", out var bdEl)
            ? (bdEl.GetString() ?? string.Empty)
            : (payload.TryGetProperty("date", out var dEl) ? (dEl.GetString() ?? string.Empty) : string.Empty);
        string startTime = payload.TryGetProperty("startTime", out var stEl)
            ? (stEl.GetString() ?? string.Empty)
            : (payload.TryGetProperty("time", out var tEl) ? (tEl.GetString() ?? string.Empty) : string.Empty);

        double rawDur = payload.TryGetProperty("duration", out var durEl) && durEl.TryGetDouble(out double dVal) ? dVal : 60;
        int durationMins = rawDur <= 8
            ? (int)Math.Round(rawDur * 60, MidpointRounding.AwayFromZero)
            : (int)Math.Round(rawDur, MidpointRounding.AwayFromZero);

        int players = payload.TryGetProperty("players", out var plEl) && plEl.TryGetInt32(out int pl) ? pl : 4;
        string? skillLevel = payload.TryGetProperty("skillLevel", out var skEl) ? skEl.GetString() : null;
        string? paymentMethod = payload.TryGetProperty("paymentMethod", out var pmEl) ? pmEl.GetString() : "cash";
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikEl) ? ikEl.GetString() : null;
        string? expectedPricingVersion = payload.TryGetProperty("pricingVersion", out var pvEl) ? pvEl.GetString() : null;
        int? expectedAmountDue = payload.TryGetProperty("expectedAmountDue", out var eaEl) && eaEl.TryGetInt32(out int ea) ? ea : null;

        bool isStaffOrAdmin = Function.IsStaffOrAdmin();
        bool autoApproveWalkIn = isStaffOrAdmin &&
                                 payload.TryGetProperty("autoApprove", out var aaEl) &&
                                 aaEl.ValueKind == JsonValueKind.True;

        var input = new BookingCreateInput
        {
            CourtId = courtId,
            CustomerName = customerName,
            Phone = phone,
            BookingDate = date,
            StartTime = startTime,
            Duration = durationMins,
            Players = players,
            SkillLevel = skillLevel,
            PaymentMethod = paymentMethod,
            AccountId = Function.IsLogin() ? Function._AccountId : null,
            ActorRoleId = Function.IsLogin() ? Function._RoleId : null,
            AutoApproveWalkIn = autoApproveWalkIn,
            IdempotencyKey = idempotencyKey,
            ExpectedPricingVersion = expectedPricingVersion,
            ExpectedAmountDue = expectedAmountDue,
            IpAddress = ClientIp
        };

        var result = await _bookingService.CreateBookingAsync(_context, input);
        if (!result.Success || result.Booking == null)
        {
            return StatusCode(result.StatusCode, new
            {
                ok = false,
                code = result.ErrorCode,
                error = result.ErrorMessage,
                latestQuote = result.Quote
            });
        }

        if (result.Booking.Court == null)
        {
            result.Booking.Court = await _context.TbCourts.AsNoTracking()
                .FirstOrDefaultAsync(c => c.CourtId == result.Booking.CourtId);
        }

        string? effectiveGuestToken = result.GuestAccessToken;
        if (!result.IsIdempotentReplay && !string.IsNullOrEmpty(effectiveGuestToken))
        {
            SaveGuestBookingRecoveryProof(result.Booking.BookingCode, effectiveGuestToken, idempotencyKey);
        }
        else if (result.IsIdempotentReplay && string.IsNullOrEmpty(effectiveGuestToken))
        {
            effectiveGuestToken = TryRecoverGuestBookingToken(result.Booking, idempotencyKey);
        }

        return Ok(new
        {
            ok = true,
            deduplicated = result.IsIdempotentReplay,
            guestAccessToken = effectiveGuestToken,
            booking = MapOwnerOrStaffBookingDto(result.Booking, effectiveGuestToken, embedQrSvg: true)
        });
    }

    private bool CallerCanAccessBookingTicket(TbBooking booking, string? accessToken)
    {
        if (Function.IsStaffOrAdmin())
        {
            return true;
        }

        if (Function.IsLogin() && booking.AccountId.HasValue && booking.AccountId.Value == Function._AccountId)
        {
            return true;
        }

        string? providedToken = !string.IsNullOrWhiteSpace(accessToken)
            ? accessToken.Trim()
            : Request.Headers["X-Booking-Access-Token"].FirstOrDefault()?.Trim();

        if (!string.IsNullOrWhiteSpace(providedToken))
        {
            if (providedToken.StartsWith("SPK1.", StringComparison.Ordinal))
            {
                return string.Equals(booking.CheckInToken, providedToken, StringComparison.Ordinal) &&
                       _qrTokenService.TryValidateCheckInToken(providedToken, out _, out _, out _);
            }

            return QrTokenService.VerifyGuestAccessToken(providedToken, booking.AccessTokenHash);
        }

        return false;
    }

    [HttpGet("/api/bookings/{code}/qr.svg")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetBookingQrSvg(
        string code,
        [FromQuery] string? accessToken = null,
        [FromQuery] int size = 512,
        [FromQuery] bool logo = false)
    {
        var booking = await BookingService.FindBookingAsync(_context, code);
        if (booking == null)
        {
            return NotFound();
        }

        if (!CallerCanAccessBookingTicket(booking, accessToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        string qrToken = !string.IsNullOrWhiteSpace(booking.CheckInToken)
            ? booking.CheckInToken
            : _qrTokenService.GenerateCheckInToken(booking.BookingCode, booking.BookingDate, DateTime.UtcNow.AddDays(30));

        int ppm = Math.Clamp(size / 45, 6, 20);
        string svg = _qrTokenService.GenerateQrSvg(qrToken, ppm, includeLogo: logo);
        return Content(svg, "image/svg+xml; charset=utf-8");
    }

    [HttpGet("/api/bookings/{code}/qr.png")]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public async Task<IActionResult> GetBookingQrPng(
        string code,
        [FromQuery] string? accessToken = null,
        [FromQuery] int size = 512,
        [FromQuery] bool logo = false)
    {
        var booking = await BookingService.FindBookingAsync(_context, code);
        if (booking == null)
        {
            return NotFound();
        }

        if (!CallerCanAccessBookingTicket(booking, accessToken))
        {
            return StatusCode(StatusCodes.Status403Forbidden);
        }

        string qrToken = !string.IsNullOrWhiteSpace(booking.CheckInToken)
            ? booking.CheckInToken
            : _qrTokenService.GenerateCheckInToken(booking.BookingCode, booking.BookingDate, DateTime.UtcNow.AddDays(30));

        int clampedSize = Math.Clamp(size, 256, 1024);
        int ppm = Math.Clamp(clampedSize / 45, 8, 22);
        byte[] pngBytes = _qrTokenService.GenerateQrPngBytes(qrToken, ppm, includeLogo: logo, targetSizePx: clampedSize);
        return File(pngBytes, "image/png", $"{booking.BookingCode}-QR.png");
    }

    [Authorize]
    [HttpGet("/api/bookings/me")]
    public async Task<IActionResult> GetMyBookings()
    {
        int accountId = Function._AccountId;
        var myBookings = await _context.TbBookings
            .AsNoTracking()
            .Include(b => b.Court)
            .Where(b => b.AccountId == accountId)
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();

        return Ok(new
        {
            ok = true,
            bookings = myBookings.Select(b => MapOwnerOrStaffBookingDto(b, embedQrSvg: true)).ToList()
        });
    }

    /// <summary>
    /// P0-07 & P1-12: Secure Booking Lookup.
    /// - Staff/Admin: search by code or phone across operational bookings.
    /// - Authenticated Customer: sees bookings strictly linked to AccountId == currentAccountId (NEVER phone!).
    /// - Guest (or looking up a guest booking): MUST supply BookingCode + secret BookingAccessToken (or signed SPK1 QR token).
    ///   Self-declared phone number NEVER grants lookup access!
    /// </summary>
    [HttpGet("/api/bookings/lookup")]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> LookupBookings(
        [FromQuery] string? code,
        [FromQuery] string? accessToken,
        [FromQuery] string? phone)
    {
        bool isStaffOrAdmin = Function.IsStaffOrAdmin();
        string cleanCode = (code ?? string.Empty).Trim();
        string? effectiveAccessToken = !string.IsNullOrWhiteSpace(accessToken)
            ? accessToken.Trim()
            : Request.Headers["X-Booking-Access-Token"].FirstOrDefault()?.Trim();

        // Signed QR token pasted into code or accessToken field
        string? candidateQr = cleanCode.StartsWith("SPK1.", StringComparison.Ordinal) ? cleanCode
            : (effectiveAccessToken != null && effectiveAccessToken.StartsWith("SPK1.", StringComparison.Ordinal) ? effectiveAccessToken : null);

        if (candidateQr != null)
        {
            if (!_qrTokenService.TryValidateCheckInToken(candidateQr, out string qrBookingCode, out _, out string qrErr))
            {
                return BadRequest(new { ok = false, error = qrErr });
            }

            var qrBooking = await _context.TbBookings
                .AsNoTracking()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.BookingCode == qrBookingCode);

            if (qrBooking == null || !string.Equals(qrBooking.CheckInToken, candidateQr, StringComparison.Ordinal))
            {
                return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, error = "Mã QR không hợp lệ hoặc đã bị thu hồi." });
            }

            return Ok(new
            {
                ok = true,
                bookings = new[] { MapOwnerOrStaffBookingDto(qrBooking, candidateQr, embedQrSvg: true) }
            });
        }

        if (isStaffOrAdmin)
        {
            var query = _context.TbBookings.AsNoTracking().Include(b => b.Court).AsQueryable();
            if (!string.IsNullOrEmpty(cleanCode))
            {
                string upper = cleanCode.ToUpperInvariant();
                query = query.Where(b => b.BookingCode.ToUpper().Contains(upper));
            }
            if (!string.IsNullOrWhiteSpace(phone))
            {
                string cleanPhone = BookingService.NormalizePhone(phone);
                query = query.Where(b => b.Phone != null && b.Phone.Contains(cleanPhone));
            }

            var staffResults = await query
                .OrderByDescending(b => b.BookingDate)
                .ThenBy(b => b.StartTime)
                .Take(100)
                .ToListAsync();

            return Ok(new
            {
                ok = true,
                bookings = staffResults.Select(b => MapOwnerOrStaffBookingDto(b, embedQrSvg: true)).ToList()
            });
        }

        // Logged-in member viewing their own bookings when no guest token is supplied
        if (Function.IsLogin() && string.IsNullOrWhiteSpace(effectiveAccessToken))
        {
            int accId = Function._AccountId;
            if (string.IsNullOrEmpty(cleanCode))
            {
                return await GetMyBookings();
            }

            string upperCode = cleanCode.ToUpperInvariant();
            var target = await _context.TbBookings
                .AsNoTracking()
                .Include(b => b.Court)
                .FirstOrDefaultAsync(b => b.BookingCode == upperCode);

            if (target == null)
            {
                return NotFound(new { ok = false, error = "Không tìm thấy mã đặt sân này." });
            }

            if (target.AccountId != accId)
            {
                return StatusCode(StatusCodes.Status403Forbidden, new
                {
                    ok = false,
                    error = "Vé này không thuộc tài khoản đang đăng nhập. Nếu đây là vé đặt vãng lai, vui lòng nhập kèm Mã bảo mật vé (bk_...)."
                });
            }

            return Ok(new
            {
                ok = true,
                bookings = new[] { MapOwnerOrStaffBookingDto(target, embedQrSvg: true) }
            });
        }

        // Guest lookup MUST provide both BookingCode AND cryptographic BookingAccessToken (NEVER phone!)
        if (string.IsNullOrEmpty(cleanCode) || string.IsNullOrWhiteSpace(effectiveAccessToken))
        {
            return BadRequest(new
            {
                ok = false,
                error = "Khách vãng lai cần nhập đồng thời Mã đặt sân (PB-XXXXXX) VÀ Mã bảo mật vé (bk_...) được cấp khi đặt sân. (Số điện thoại không được dùng để xác thực quyền sở hữu vé)."
            });
        }

        string lookupUpper = cleanCode.ToUpperInvariant();
        var guestBooking = await _context.TbBookings
            .AsNoTracking()
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingCode == lookupUpper);

        if (guestBooking == null || !QrTokenService.VerifyGuestAccessToken(effectiveAccessToken, guestBooking.AccessTokenHash))
        {
            return StatusCode(StatusCodes.Status403Forbidden, new
            {
                ok = false,
                error = "Mã đặt sân hoặc Mã bảo mật vé không chính xác!"
            });
        }

        return Ok(new
        {
            ok = true,
            bookings = new[] { MapOwnerOrStaffBookingDto(guestBooking, effectiveAccessToken, embedQrSvg: true) }
        });
    }

    [ValidateAntiForgeryToken]
    [HttpPost("/api/bookings/{code}/cancel")]
    public async Task<IActionResult> CancelBooking(string code, [FromBody] JsonElement payload)
    {
        string? accessToken = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("accessToken", out var atEl)
            ? atEl.GetString()
            : Request.Headers["X-Booking-Access-Token"].FirstOrDefault();

        string? reason = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("reason", out var rEl)
            ? rEl.GetString()
            : null;

        string? idempotencyKey = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("idempotencyKey", out var ikEl)
            ? ikEl.GetString()
            : null;

        int? callerAccountId = Function.IsLogin() ? Function._AccountId : null;
        int? callerRoleId = Function.IsLogin() ? Function._RoleId : null;

        var result = await _bookingService.CancelBookingAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            callerAccountId,
            callerRoleId,
            accessToken,
            reason,
            ClientIp,
            idempotencyKey: idempotencyKey);

        if (!result.Success || result.Booking == null)
        {
            return StatusCode(result.StatusCode, new { ok = false, code = result.ErrorCode, error = result.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            deduplicated = result.IsIdempotentReplay,
            booking = MapOwnerOrStaffBookingDto(result.Booking, accessToken)
        });
    }

    // ================================================================
    // ADMIN / STAFF OPERATIONAL, QR DECODE, CHECK-IN & PAYMENT LEDGER
    // ================================================================

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/approve")]
    public async Task<IActionResult> ApproveBookingApi(string code)
    {
        var res = await _bookingService.ApproveBookingAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new { ok = true, booking = MapOwnerOrStaffBookingDto(res.Booking) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/reject")]
    public async Task<IActionResult> RejectBookingApi(string code, [FromBody] JsonElement payload)
    {
        string? reason = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("reason", out var rEl)
            ? rEl.GetString()
            : "rejected";

        string? idempotencyKey = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("idempotencyKey", out var ikEl)
            ? ikEl.GetString()
            : null;

        var res = await _bookingService.CancelBookingAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            Function._AccountId,
            Function._RoleId,
            guestAccessToken: null,
            reason: string.IsNullOrWhiteSpace(reason) ? "rejected" : reason,
            ipAddress: ClientIp,
            idempotencyKey: idempotencyKey);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new { ok = true, deduplicated = res.IsIdempotentReplay, booking = MapOwnerOrStaffBookingDto(res.Booking) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("CheckInLimiter")]
    [HttpPost("/api/checkin/verify")]
    [HttpPost("/api/admin/bookings/checkin")]
    public async Task<IActionResult> CheckInVerifyApi([FromBody] JsonElement payload)
    {
        string tokenOrCode = payload.TryGetProperty("tokenOrCode", out var tEl)
            ? (tEl.GetString() ?? string.Empty)
            : (payload.TryGetProperty("code", out var cEl) ? (cEl.GetString() ?? string.Empty) : string.Empty);

        var res = await _bookingService.CheckInBookingAsync(
            _context,
            tokenOrCode,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        if (res.Booking.Court == null)
        {
            res.Booking.Court = await _context.TbCourts.AsNoTracking().FirstOrDefaultAsync(c => c.CourtId == res.Booking.CourtId);
        }

        return Ok(new
        {
            ok = true,
            booking = MapOwnerOrStaffBookingDto(res.Booking)
        });
    }

    /// <summary>
    /// P1-01 & P1-07: Real independent server-side QR decoder (ZXing.Net + ImageSharp) for uploaded ticket images or camera frames.
    /// </summary>
    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("CheckInLimiter")]
    [HttpPost("/api/checkin/decode-qr")]
    public async Task<IActionResult> DecodeQrImageApi([FromForm] IFormFile? file, [FromForm] bool autoVerify = true)
    {
        byte[]? imageBytes = null;

        if (file != null && file.Length > 0)
        {
            if (file.Length > 8 * 1024 * 1024)
            {
                return BadRequest(new { ok = false, error = "Tệp ảnh tải lên vượt quá 8MB." });
            }

            using var ms = new MemoryStream();
            await file.CopyToAsync(ms);
            imageBytes = ms.ToArray();
        }
        else if (Request.ContentType != null && Request.ContentType.Contains("application/json", StringComparison.OrdinalIgnoreCase))
        {
            using var doc = await JsonDocument.ParseAsync(Request.Body);
            if (doc.RootElement.TryGetProperty("autoVerify", out var avEl) &&
                (avEl.ValueKind == JsonValueKind.True || avEl.ValueKind == JsonValueKind.False))
            {
                autoVerify = avEl.GetBoolean();
            }

            if (doc.RootElement.TryGetProperty("imageBase64", out var b64El))
            {
                string rawB64 = b64El.GetString() ?? string.Empty;
                int commaIdx = rawB64.IndexOf(',');
                if (commaIdx >= 0)
                {
                    rawB64 = rawB64[(commaIdx + 1)..];
                }
                try
                {
                    imageBytes = Convert.FromBase64String(rawB64);
                }
                catch
                {
                    return BadRequest(new { ok = false, error = "Dữ liệu ảnh Base64 không hợp lệ." });
                }
            }
        }

        if (imageBytes == null || imageBytes.Length == 0)
        {
            return BadRequest(new { ok = false, error = "Vui lòng chọn tệp ảnh mã QR (PNG, JPG, WebP) hoặc chụp khung hình từ Camera." });
        }

        if (!QrTokenService.TryDecodeQrImage(imageBytes, out string decodedText, out string decodeErr))
        {
            return UnprocessableEntity(new { ok = false, error = decodeErr });
        }

        if (!autoVerify)
        {
            return Ok(new { ok = true, decodedText });
        }

        var res = await _bookingService.CheckInBookingAsync(
            _context,
            decodedText,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new
            {
                ok = false,
                code = res.ErrorCode,
                decodedText,
                error = res.ErrorMessage
            });
        }

        if (res.Booking.Court == null)
        {
            res.Booking.Court = await _context.TbCourts.AsNoTracking().FirstOrDefaultAsync(c => c.CourtId == res.Booking.CourtId);
        }

        return Ok(new
        {
            ok = true,
            decodedText,
            booking = MapOwnerOrStaffBookingDto(res.Booking)
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/extend")]
    public async Task<IActionResult> ExtendBookingApi(string code, [FromBody] JsonElement payload)
    {
        int addMinutes = payload.TryGetProperty("addMinutes", out var mEl) && mEl.TryGetInt32(out int m) ? m : 30;
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikEl) ? ikEl.GetString() : null;

        var res = await _bookingService.ExtendBookingDurationAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            addMinutes,
            Function._AccountId,
            Function._Username,
            idempotencyKey);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new { ok = true, deduplicated = res.IsIdempotentReplay, booking = MapOwnerOrStaffBookingDto(res.Booking) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/move-court")]
    public async Task<IActionResult> MoveBookingCourtApi(string code, [FromBody] JsonElement payload)
    {
        int targetCourtId = payload.TryGetProperty("targetCourtId", out var cEl) && cEl.TryGetInt32(out int cid) ? cid : 0;
        var res = await _bookingService.MoveBookingCourtAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            targetCourtId,
            Function._AccountId,
            Function._Username);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new { ok = true, booking = MapOwnerOrStaffBookingDto(res.Booking) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/pos-item")]
    public async Task<IActionResult> UpdatePosItemApi(string code, [FromBody] JsonElement payload)
    {
        int productId = payload.TryGetProperty("productId", out var pEl) && pEl.TryGetInt32(out int pid) ? pid : 0;
        int deltaQty = payload.TryGetProperty("deltaQty", out var qEl) && qEl.TryGetInt32(out int dq) ? dq : 1;

        var res = await _bookingService.AddPosItemToBookingAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            productId,
            deltaQty,
            Function._AccountId,
            Function._Username);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new { ok = true, booking = MapOwnerOrStaffBookingDto(res.Booking) });
    }

    /// <summary>
    /// P1-04 & P1-05: Finishes a playing court session ONLY. PaidAmount & ledger remain completely untouched!
    /// </summary>
    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/finish")]
    public async Task<IActionResult> FinishBookingSessionApi(string code)
    {
        var res = await _bookingService.FinishBookingSessionAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new { ok = true, booking = MapOwnerOrStaffBookingDto(res.Booking) });
    }

    /// <summary>
    /// P1-04 & P1-05: Records an explicit, idempotent cash/transfer payment in TbPaymentTransaction.
    /// </summary>
    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/payments")]
    public async Task<IActionResult> RecordBookingPaymentApi(string code, [FromBody] JsonElement payload)
    {
        int amount = payload.TryGetProperty("amount", out var aEl) && aEl.TryGetInt32(out int a) ? a : 0;
        string method = payload.TryGetProperty("paymentMethod", out var mEl) ? (mEl.GetString() ?? "cash") : "cash";
        string? referenceCode = payload.TryGetProperty("referenceCode", out var refEl) ? refEl.GetString() : null;
        string? note = payload.TryGetProperty("note", out var nEl) ? nEl.GetString() : null;
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikEl) ? ikEl.GetString() : null;

        var res = await _bookingService.RecordBookingPaymentAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            amount,
            method,
            referenceCode,
            note,
            idempotencyKey,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        // Optional convenience flag: ONLY AFTER payment succeeds, staff may also close an active playing session
        if (payload.TryGetProperty("finishSessionAfterPayment", out var fsEl) &&
            fsEl.ValueKind == JsonValueKind.True &&
            res.Booking.Status == "playing")
        {
            await _bookingService.FinishBookingSessionAsync(
                _context,
                res.Booking.BookingCode,
                Function._AccountId,
                Function._Username,
                ClientIp);
        }

        return Ok(new
        {
            ok = true,
            deduplicated = res.IsIdempotentReplay,
            transactionId = res.Transaction?.TransactionId,
            referenceCode = res.Transaction?.ReferenceCode,
            booking = MapOwnerOrStaffBookingDto(res.Booking)
        });
    }

    /// <summary>
    /// P1-04 & P1-05: Creates an immutable counter-entry refund transaction in TbPaymentTransaction.
    /// </summary>
    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/bookings/{code}/refunds")]
    public async Task<IActionResult> RefundBookingPaymentApi(string code, [FromBody] JsonElement payload)
    {
        int amount = payload.TryGetProperty("amount", out var aEl) && aEl.TryGetInt32(out int a) ? a : 0;
        string method = payload.TryGetProperty("paymentMethod", out var mEl) ? (mEl.GetString() ?? "cash") : "cash";
        string? referenceCode = payload.TryGetProperty("referenceCode", out var refEl) ? refEl.GetString() : null;
        string? reason = payload.TryGetProperty("reason", out var rEl) ? rEl.GetString() : null;
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikEl) ? ikEl.GetString() : null;

        var res = await _bookingService.RefundBookingPaymentAsync(
            _context,
            code.Trim().ToUpperInvariant(),
            amount,
            method,
            referenceCode,
            reason,
            idempotencyKey,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Booking == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            deduplicated = res.IsIdempotentReplay,
            transactionId = res.Transaction?.TransactionId,
            booking = MapOwnerOrStaffBookingDto(res.Booking)
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("/api/admin/bookings/{code}/transactions")]
    public async Task<IActionResult> GetBookingTransactionsApi(string code)
    {
        var booking = await BookingService.FindBookingAsync(_context, code);
        if (booking == null)
        {
            return NotFound(new { ok = false, error = "Không tìm thấy đơn đặt sân." });
        }

        var rows = await _context.TbPaymentTransactions
            .AsNoTracking()
            .Where(t => t.BookingId == booking.BookingId)
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new
            {
                transactionId = t.TransactionId,
                type = t.TransactionType,
                amount = t.Amount,
                method = t.PaymentMethod,
                status = t.Status,
                referenceCode = t.ReferenceCode,
                note = t.Note,
                recordedBy = t.RecordedBy,
                createdAt = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
            })
            .ToListAsync();

        return Ok(new { ok = true, transactions = rows });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/court-locks")]
    public async Task<IActionResult> CreateCourtLockApi([FromBody] JsonElement payload)
    {
        int courtId = payload.TryGetProperty("courtId", out var cEl) && cEl.TryGetInt32(out int cid) ? cid : 1;
        string date = payload.TryGetProperty("date", out var dEl) ? (dEl.GetString() ?? string.Empty).Trim() : string.Empty;
        string startTime = payload.TryGetProperty("startTime", out var stEl) ? (stEl.GetString() ?? "08:00").Trim() : "08:00";
        int duration = payload.TryGetProperty("duration", out var durEl) && durEl.TryGetInt32(out int dur) && dur > 0 ? dur : 60;
        string? reason = payload.TryGetProperty("reason", out var rEl) ? rEl.GetString() : "Bảo trì định kỳ";

        var res = await _bookingService.CreateCourtLockAsync(
            _context,
            courtId,
            date,
            startTime,
            duration,
            reason,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Lock == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            courtLock = new
            {
                id = res.Lock.LockCode,
                courtId = res.Lock.CourtId,
                date = res.Lock.LockDate,
                startTime = res.Lock.StartTime,
                duration = res.Lock.Duration,
                reason = res.Lock.Reason
            }
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpDelete("/api/admin/court-locks/{lockCode}")]
    public async Task<IActionResult> DeleteCourtLockApi(string lockCode)
    {
        var lockEntry = await _context.TbCourtLocks.FirstOrDefaultAsync(l => l.LockCode == lockCode);
        if (lockEntry == null)
        {
            return NotFound(new { ok = false, error = "Không tìm thấy lệnh khóa sân." });
        }

        _context.TbCourtLocks.Remove(lockEntry);
        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "COURT_LOCK_DELETED",
            EntityType = "CourtLock",
            EntityId = lockCode,
            ActorAccountId = Function._AccountId,
            ActorUsername = Function._Username,
            IpAddress = ClientIp,
            CreatedAt = BookingService.GetVietnamNow()
        });
        await _context.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================================================================
    // AUTHENTICATION JSON ENDPOINTS (P0-01, P0-02, P0-09, P1-17, V4-B3)
    // ================================================================

    [ValidateAntiForgeryToken]
    [HttpPost("/api/auth/login")]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> LoginApi([FromBody] JsonElement payload)
    {
        string identifier = payload.TryGetProperty("identifier", out var idProp) ? (idProp.GetString() ?? string.Empty).Trim() : string.Empty;
        string password = payload.TryGetProperty("password", out var pwProp) ? (pwProp.GetString() ?? string.Empty) : string.Empty;

        if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
        {
            return BadRequest(new { ok = false, error = "Vui lòng nhập Email/Số điện thoại và mật khẩu." });
        }

        string lowerId = identifier.ToLowerInvariant();
        string normPhone = BookingService.NormalizePhone(identifier);

        var account = await _context.TbAccounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a =>
                (a.Email != null && a.Email.ToLower() == lowerId) ||
                (a.Phone != null && (a.Phone == identifier || a.Phone == normPhone)) ||
                (a.Username != null && a.Username.ToLower() == lowerId));

        if (account == null)
        {
            return Unauthorized(new { ok = false, error = "Tài khoản hoặc mật khẩu không chính xác." });
        }

        var verifyResult = _passwordService.VerifyPassword(account, account.Password, password, out string? upgradedHash);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { ok = false, error = "Tài khoản hoặc mật khẩu không chính xác." });
        }

        if (!account.IsActive)
        {
            return StatusCode(StatusCodes.Status403Forbidden, new { ok = false, error = "Tài khoản của bạn hiện đang bị tạm khóa." });
        }

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded && !string.IsNullOrEmpty(upgradedHash))
        {
            account.Password = upgradedHash;
        }

        if (string.IsNullOrEmpty(account.SecurityStamp))
        {
            account.SecurityStamp = Function.GenerateSecurityStamp();
        }

        account.LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, account);
        var csrfTokens = _antiforgery.GetAndStoreTokens(HttpContext);

        bool isAdmin = account.RoleId == 1;
        return Ok(new
        {
            ok = true,
            csrfToken = csrfTokens.RequestToken,
            mustChangePassword = account.MustChangePassword,
            user = new
            {
                id = account.AccountId,
                name = account.FullName ?? account.Username ?? "Hội viên",
                email = account.Email ?? string.Empty,
                phone = account.Phone ?? string.Empty,
                skillLevel = account.SkillLevel ?? string.Empty,
                role = isAdmin ? "admin" : account.RoleId == 3 ? "staff" : "user",
                roleLabel = isAdmin ? "Ban Quản Trị CLB" : account.RoleId == 3 ? "Nhân viên CLB" : "Hội viên CLB",
                avatar = account.Avatar ?? string.Empty
            }
        });
    }

    [Authorize]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/auth/change-password")]
    public async Task<IActionResult> ChangePasswordApi([FromBody] JsonElement payload)
    {
        string currentPassword = payload.TryGetProperty("currentPassword", out var curEl) ? (curEl.GetString() ?? string.Empty) : string.Empty;
        string newPassword = payload.TryGetProperty("newPassword", out var newEl) ? (newEl.GetString() ?? string.Empty) : string.Empty;

        if (string.IsNullOrEmpty(currentPassword) || string.IsNullOrEmpty(newPassword))
        {
            return BadRequest(new { ok = false, error = "Vui lòng nhập đầy đủ mật khẩu hiện tại và mật khẩu mới." });
        }

        if (newPassword.Length < 8 || string.Equals(currentPassword, newPassword, StringComparison.Ordinal))
        {
            return BadRequest(new { ok = false, error = "Mật khẩu mới phải từ 8 ký tự trở lên và khác mật khẩu hiện tại." });
        }

        int accountId = Function._AccountId;
        var account = await _context.TbAccounts.FirstOrDefaultAsync(a => a.AccountId == accountId);
        if (account == null)
        {
            return Unauthorized(new { ok = false, error = "Tài khoản không tồn tại." });
        }

        var verify = _passwordService.VerifyPassword(account, account.Password, currentPassword, out _);
        if (verify == PasswordVerificationResult.Failed)
        {
            return BadRequest(new { ok = false, error = "Mật khẩu hiện tại không chính xác." });
        }

        account.Password = _passwordService.HashPassword(account, newPassword);
        account.MustChangePassword = false;
        account.SecurityStamp = Function.GenerateSecurityStamp();
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, account);
        var csrfTokens = _antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new
        {
            ok = true,
            mustChangePassword = false,
            csrfToken = csrfTokens.RequestToken
        });
    }

    [Authorize]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/auth/logout")]
    public async Task<IActionResult> LogoutApi()
    {
        await Microsoft.AspNetCore.Authentication.AuthenticationHttpContextExtensions.SignOutAsync(
            HttpContext,
            Microsoft.AspNetCore.Authentication.Cookies.CookieAuthenticationDefaults.AuthenticationScheme);
        return Ok(new { ok = true });
    }

    [ValidateAntiForgeryToken]
    [HttpPost("/api/auth/register")]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> RegisterApi([FromBody] JsonElement payload)
    {
        string name = payload.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? string.Empty).Trim() : string.Empty;
        string phone = payload.TryGetProperty("phone", out var pProp) ? (pProp.GetString() ?? string.Empty).Trim() : string.Empty;
        string email = payload.TryGetProperty("email", out var eProp) ? (eProp.GetString() ?? string.Empty).Trim().ToLowerInvariant() : string.Empty;
        string password = payload.TryGetProperty("password", out var pwProp) ? (pwProp.GetString() ?? string.Empty) : string.Empty;

        if (string.IsNullOrEmpty(name) || name.Length < 2 || name.Length > 100)
        {
            return BadRequest(new { ok = false, error = "Họ và tên phải từ 2 đến 100 ký tự." });
        }

        if (!BookingService.IsValidPhone(phone))
        {
            return BadRequest(new { ok = false, error = "Số điện thoại không hợp lệ (10 chữ số Việt Nam)." });
        }

        if (string.IsNullOrEmpty(password) || password.Length < 6)
        {
            return BadRequest(new { ok = false, error = "Mật khẩu phải có tối thiểu 6 ký tự." });
        }

        string normalizedPhone = BookingService.NormalizePhone(phone);
        string desiredUsername = !string.IsNullOrEmpty(email) ? email.Split('@')[0] : normalizedPhone;

        bool exists = await _context.TbAccounts.AnyAsync(a =>
            (a.Phone != null && a.Phone == normalizedPhone) ||
            (!string.IsNullOrEmpty(email) && a.Email != null && a.Email.ToLower() == email));

        if (exists)
        {
            return Conflict(new { ok = false, error = "Số điện thoại hoặc Email này đã được đăng ký." });
        }

        if (await _context.TbAccounts.AnyAsync(a => a.Username == desiredUsername))
        {
            desiredUsername = $"{desiredUsername}_{RandomNumberGenerator.GetInt32(100, 999)}";
        }

        var newAccount = new TbAccount
        {
            Username = desiredUsername,
            FullName = name,
            Phone = normalizedPhone,
            Email = string.IsNullOrEmpty(email) ? null : email,
            SkillLevel = "Giao lưu (DUPR 3.0)",
            MemberTier = "Hội viên Tiêu chuẩn",
            DominantHand = "Tay phải",
            RoleId = 2, // Strictly Customer (P0-03)
            IsActive = true,
            MustChangePassword = false,
            SecurityStamp = Function.GenerateSecurityStamp(),
            LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd")
        };

        newAccount.Password = _passwordService.HashPassword(newAccount, password);

        _context.TbAccounts.Add(newAccount);
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, newAccount);
        var csrfTokens = _antiforgery.GetAndStoreTokens(HttpContext);

        return Ok(new
        {
            ok = true,
            csrfToken = csrfTokens.RequestToken,
            user = new
            {
                id = newAccount.AccountId,
                name = newAccount.FullName,
                email = newAccount.Email ?? string.Empty,
                phone = newAccount.Phone,
                role = "user",
                roleLabel = "Hội viên CLB",
                avatar = string.Empty
            }
        });
    }

    // ================================================================
    // PRO SHOP ORDERS, ATOMIC STOCK & STANDALONE SETTLEMENT (P0-10, P1-14, P1-15, V4-A4)
    // ================================================================

    [ValidateAntiForgeryToken]
    [HttpPost("/api/orders/create")]
    public async Task<IActionResult> CreateOrderApi([FromBody] JsonElement payload)
    {
        string customerName = payload.TryGetProperty("customerName", out var nProp) ? (nProp.GetString() ?? string.Empty) : string.Empty;
        string phone = payload.TryGetProperty("phone", out var pProp) ? (pProp.GetString() ?? string.Empty) : string.Empty;
        string? address = payload.TryGetProperty("address", out var aProp) ? aProp.GetString() : null;
        string? bookingCode = payload.TryGetProperty("bookingCode", out var bProp) ? bProp.GetString() : null;
        string? bookingAccessToken = payload.TryGetProperty("bookingAccessToken", out var batProp)
            ? batProp.GetString()
            : Request.Headers["X-Booking-Access-Token"].FirstOrDefault();
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikProp) ? ikProp.GetString() : null;

        var items = new List<OrderItemInput>();
        if (payload.TryGetProperty("items", out var itemsEl) && itemsEl.ValueKind == JsonValueKind.Array)
        {
            foreach (var el in itemsEl.EnumerateArray())
            {
                int prodId = el.TryGetProperty("productId", out var p1) && p1.TryGetInt32(out int v1) ? v1
                           : (el.TryGetProperty("id", out var p2) && p2.TryGetInt32(out int v2) ? v2 : 0);
                int qty = el.TryGetProperty("qty", out var qEl) && qEl.TryGetInt32(out int q) ? q : 0;
                items.Add(new OrderItemInput { ProductId = prodId, Qty = qty });
            }
        }

        var res = await _orderService.CreateOrderAsync(_context, new CreateShopOrderInput
        {
            CustomerName = customerName,
            Phone = phone,
            NoteOrAddress = address,
            BookingCode = bookingCode,
            BookingAccessToken = bookingAccessToken,
            IdempotencyKey = idempotencyKey,
            ActorAccountId = Function.IsLogin() ? Function._AccountId : null,
            ActorRoleId = Function.IsLogin() ? Function._RoleId : null,
            ActorUsername = Function.IsLogin() ? Function._Username : null,
            IpAddress = ClientIp,
            Items = items
        });

        if (!res.Success || res.Order == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            deduplicated = res.IsIdempotentReplay,
            orderCode = res.Order.Code,
            totalAmount = res.Order.TotalAmount ?? 0,
            bookingCode = res.Order.BookingCode ?? string.Empty,
            order = MapOrderDto(res.Order)
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("/api/admin/orders")]
    public async Task<IActionResult> GetAdminOrdersApi([FromQuery] bool standaloneOnly = false)
    {
        var query = _context.TbOrders
            .AsNoTracking()
            .Include(o => o.TbOrderDetails)
            .AsQueryable();

        if (standaloneOnly)
        {
            query = query.Where(o => o.BookingId == null);
        }

        var orders = await query
            .OrderByDescending(o => o.CreatedDate)
            .Take(200)
            .ToListAsync();

        return Ok(new
        {
            ok = true,
            orders = orders.Select(MapOrderDto).ToList()
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/orders/{code}/payments")]
    public async Task<IActionResult> RecordStandaloneOrderPaymentApi(string code, [FromBody] JsonElement payload)
    {
        int amount = payload.TryGetProperty("amount", out var aEl) && aEl.TryGetInt32(out int a) ? a : 0;
        string method = payload.TryGetProperty("paymentMethod", out var mEl) ? (mEl.GetString() ?? "cash") : "cash";
        string? referenceCode = payload.TryGetProperty("referenceCode", out var refEl) ? refEl.GetString() : null;
        string? note = payload.TryGetProperty("note", out var nEl) ? nEl.GetString() : null;
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikEl) ? ikEl.GetString() : null;

        var res = await _orderService.RecordStandaloneOrderPaymentAsync(
            _context,
            code,
            amount,
            method,
            referenceCode,
            note,
            idempotencyKey,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Order == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            deduplicated = res.IsIdempotentReplay,
            transactionId = res.Transaction?.TransactionId,
            order = MapOrderDto(res.Order)
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/orders/{code}/refunds")]
    public async Task<IActionResult> RefundStandaloneOrderApi(string code, [FromBody] JsonElement payload)
    {
        int amount = payload.TryGetProperty("amount", out var aEl) && aEl.TryGetInt32(out int a) ? a : 0;
        string method = payload.TryGetProperty("paymentMethod", out var mEl) ? (mEl.GetString() ?? "cash") : "cash";
        string? referenceCode = payload.TryGetProperty("referenceCode", out var refEl) ? refEl.GetString() : null;
        string? reason = payload.TryGetProperty("reason", out var rEl) ? rEl.GetString() : null;
        string? idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikEl) ? ikEl.GetString() : null;

        var res = await _orderService.RefundStandaloneOrderAsync(
            _context,
            code,
            amount,
            method,
            referenceCode,
            reason,
            idempotencyKey,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Order == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            deduplicated = res.IsIdempotentReplay,
            transactionId = res.Transaction?.TransactionId,
            order = MapOrderDto(res.Order)
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/orders/{code}/cancel")]
    public async Task<IActionResult> CancelStandaloneOrderApi(string code, [FromBody] JsonElement payload)
    {
        string? reason = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("reason", out var rEl)
            ? rEl.GetString()
            : null;

        string? idempotencyKey = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("idempotencyKey", out var ikEl)
            ? ikEl.GetString()
            : null;

        var res = await _orderService.CancelOrderAndRestockAsync(
            _context,
            code,
            reason,
            Function._AccountId,
            Function._Username,
            ClientIp,
            idempotencyKey);

        if (!res.Success || res.Order == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            deduplicated = res.IsIdempotentReplay,
            order = MapOrderDto(res.Order)
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [ValidateAntiForgeryToken]
    [HttpPost("/api/admin/orders/{code}/fulfill")]
    public async Task<IActionResult> FulfillOrderApi(string code)
    {
        var res = await _orderService.FulfillOrderAsync(
            _context,
            code,
            Function._AccountId,
            Function._Username,
            ClientIp);

        if (!res.Success || res.Order == null)
        {
            return StatusCode(res.StatusCode, new { ok = false, code = res.ErrorCode, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            order = MapOrderDto(res.Order)
        });
    }

    // ================================================================
    // LEDGER-DRIVEN REVENUE & FINANCIAL REPORTS API (P1-06)
    // ================================================================

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("/api/admin/reports/summary")]
    public async Task<IActionResult> GetFinancialReportSummary([FromQuery] string? dateFrom, [FromQuery] string? dateTo)
    {
        var vnNow = BookingService.GetVietnamNow();
        DateOnly startDay = DateOnly.FromDateTime(vnNow.AddDays(-30));
        DateOnly endDay = DateOnly.FromDateTime(vnNow);

        if (!string.IsNullOrWhiteSpace(dateFrom) &&
            DateOnly.TryParseExact(dateFrom.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedFrom))
        {
            startDay = parsedFrom;
        }

        if (!string.IsNullOrWhiteSpace(dateTo) &&
            DateOnly.TryParseExact(dateTo.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedTo))
        {
            endDay = parsedTo;
        }

        if (endDay < startDay)
        {
            (startDay, endDay) = (endDay, startDay);
        }

        DateTime fromDt = startDay.ToDateTime(TimeOnly.MinValue);
        DateTime toExclusiveDt = endDay.AddDays(1).ToDateTime(TimeOnly.MinValue);
        string fromIso = startDay.ToString("yyyy-MM-dd");
        string toIso = endDay.ToString("yyyy-MM-dd");

        // P1-06: Filter revenue strictly by actual payment transaction timestamp (CreatedAt) in Vietnam time!
        var ledgerRows = await _context.TbPaymentTransactions
            .AsNoTracking()
            .Include(t => t.Booking)
                .ThenInclude(b => b!.Court)
            .Include(t => t.Order)
            .Where(t => t.Status == "completed" && t.CreatedAt >= fromDt && t.CreatedAt < toExclusiveDt)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        int bookingPaymentsGross = ledgerRows
            .Where(t => t.BookingId != null && !string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
            .Sum(t => Math.Max(0, t.Amount));

        int bookingRefunds = ledgerRows
            .Where(t => t.BookingId != null && string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
            .Sum(t => Math.Abs(t.Amount));

        int standalonePaymentsGross = ledgerRows
            .Where(t => t.OrderId != null && t.BookingId == null && !string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
            .Sum(t => Math.Max(0, t.Amount));

        int standaloneRefunds = ledgerRows
            .Where(t => t.OrderId != null && t.BookingId == null && string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
            .Sum(t => Math.Abs(t.Amount));

        int collectedBookingRevenue = bookingPaymentsGross - bookingRefunds;
        int standaloneRetailRevenue = standalonePaymentsGross - standaloneRefunds;
        int totalRefunds = bookingRefunds + standaloneRefunds;
        int totalCollectedRevenue = collectedBookingRevenue + standaloneRetailRevenue;

        int cashNet = ledgerRows
            .Where(t => t.PaymentMethod == "cash")
            .Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : Math.Max(0, t.Amount));

        int transferNet = ledgerRows
            .Where(t => t.PaymentMethod == "transfer")
            .Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : Math.Max(0, t.Amount));

        // Operational bookings within [dateFrom..dateTo] for occupancy & receivables visibility
        var bookingsInRange = await _context.TbBookings
            .AsNoTracking()
            .Include(b => b.Court)
            .Where(b => string.Compare(b.BookingDate, fromIso) >= 0 && string.Compare(b.BookingDate, toIso) <= 0)
            .ToListAsync();

        var billableStatuses = new[] { "approved", "confirmed", "playing", "completed" };
        int unpaidBookingReceivables = bookingsInRange
            .Where(b => billableStatuses.Contains(b.Status))
            .Sum(b => Math.Max(0, (b.AmountDue > 0 ? b.AmountDue : (b.CourtFee + b.PosTotal - b.DiscountAmount)) - b.PaidAmount));

        int unpaidShopReceivables = await _context.TbOrders
            .AsNoTracking()
            .Where(o => o.BookingId == null && o.Status != "cancelled" &&
                        o.CreatedDate >= fromDt && o.CreatedDate < toExclusiveDt &&
                        (o.TotalAmount ?? 0) > o.PaidAmount)
            .SumAsync(o => (int?)((o.TotalAmount ?? 0) - o.PaidAmount)) ?? 0;

        int unpaidReceivables = unpaidBookingReceivables + unpaidShopReceivables;

        // Daily ledger breakdown by actual payment date
        var dailyBreakdown = ledgerRows
            .GroupBy(t => t.CreatedAt.ToString("yyyy-MM-dd"))
            .OrderBy(g => g.Key)
            .Select(g => new
            {
                date = g.Key,
                bookingNet = g.Where(t => t.BookingId != null)
                              .Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : t.Amount),
                shopNet = g.Where(t => t.OrderId != null && t.BookingId == null)
                           .Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : t.Amount),
                refunds = g.Where(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
                           .Sum(t => Math.Abs(t.Amount)),
                netTotal = g.Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : t.Amount),
                txCount = g.Count()
            })
            .ToList();

        // Court performance: net collected from ledger + hours booked
        var allCourts = await _context.TbCourts.AsNoTracking().Where(c => c.IsActive).OrderBy(c => c.CourtId).ToListAsync();
        var courtBreakdown = allCourts.Select(c =>
        {
            var cBookings = bookingsInRange.Where(b => b.CourtId == c.CourtId && billableStatuses.Contains(b.Status)).ToList();
            int cCollected = ledgerRows
                .Where(t => t.Booking != null && t.Booking.CourtId == c.CourtId)
                .Sum(t => string.Equals(t.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) ? -Math.Abs(t.Amount) : t.Amount);
            int cReceivable = cBookings.Sum(b => Math.Max(0, (b.AmountDue > 0 ? b.AmountDue : b.CourtFee) - b.PaidAmount));
            double hours = Math.Round(cBookings.Sum(b => (b.Duration > 0 ? b.Duration : 60) / 60.0), 1);

            return new
            {
                courtId = c.CourtId,
                courtName = c.CourtName,
                type = c.CourtType,
                sessionsCount = cBookings.Count,
                hoursBooked = hours,
                collectedRevenue = cCollected,
                unpaidReceivable = cReceivable
            };
        }).ToList();

        // Top verified member accounts by net collected ledger spend in range
        var memberSpendDict = new Dictionary<int, int>();
        foreach (var tx in ledgerRows)
        {
            int? ownerAccId = tx.Booking?.AccountId ?? tx.Order?.AccountId;
            if (!ownerAccId.HasValue || ownerAccId.Value <= 0) continue;
            int delta = string.Equals(tx.TransactionType, "refund", StringComparison.OrdinalIgnoreCase)
                ? -Math.Abs(tx.Amount)
                : Math.Max(0, tx.Amount);
            memberSpendDict[ownerAccId.Value] = memberSpendDict.GetValueOrDefault(ownerAccId.Value) + delta;
        }

        var topAccIds = memberSpendDict.Where(kv => kv.Value > 0).OrderByDescending(kv => kv.Value).Take(8).Select(kv => kv.Key).ToList();
        var topAccounts = await _context.TbAccounts.AsNoTracking()
            .Where(a => topAccIds.Contains(a.AccountId))
            .ToDictionaryAsync(a => a.AccountId);

        var topMembers = topAccIds
            .Where(id => topAccounts.ContainsKey(id))
            .Select(id =>
            {
                var a = topAccounts[id];
                int spend = memberSpendDict[id];
                var (tier, _) = PricingService.ResolveMemberTier(spend, a.MemberTier);
                return new
                {
                    accountId = a.AccountId,
                    name = a.FullName ?? a.Username ?? "Hội viên",
                    phone = a.Phone ?? string.Empty,
                    memberTier = tier,
                    netPaidInRange = spend
                };
            })
            .ToList();

        var recentTransactions = ledgerRows.Take(50).Select(t => new
        {
            transactionId = t.TransactionId,
            type = t.TransactionType,
            source = t.BookingId != null ? "booking" : "shop_order",
            targetCode = t.Booking?.BookingCode ?? t.Order?.Code ?? t.BookingCode ?? "-",
            customerName = t.Booking?.CustomerName ?? t.Order?.CustomerName ?? "-",
            amount = t.Amount,
            method = t.PaymentMethod,
            referenceCode = t.ReferenceCode ?? string.Empty,
            note = t.Note ?? string.Empty,
            recordedBy = t.RecordedBy ?? "System",
            createdAt = t.CreatedAt.ToString("yyyy-MM-dd HH:mm:ss")
        }).ToList();

        return Ok(new
        {
            ok = true,
            dateFrom = fromIso,
            dateTo = toIso,
            bookingPaymentsGross,
            bookingRefunds,
            collectedBookingRevenue,
            standalonePaymentsGross,
            standaloneRefunds,
            standaloneRetailRevenue,
            totalRefunds,
            totalCollectedRevenue,
            cashNetCollected = cashNet,
            transferNetCollected = transferNet,
            unpaidBookingReceivables,
            unpaidShopReceivables,
            unpaidReceivables,
            totalBookings = bookingsInRange.Count,
            completedBookings = bookingsInRange.Count(b => b.Status == "completed"),
            playingBookings = bookingsInRange.Count(b => b.Status == "playing"),
            approvedBookings = bookingsInRange.Count(b => b.Status == "approved"),
            pendingBookings = bookingsInRange.Count(b => b.Status == "pending"),
            cancelledBookings = bookingsInRange.Count(b => b.Status is "cancelled" or "rejected" or "expired" or "no_show"),
            dailyBreakdown,
            courtBreakdown,
            topMembers,
            recentTransactions
        });
    }
}
