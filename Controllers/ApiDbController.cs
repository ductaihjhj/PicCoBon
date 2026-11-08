using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
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
    private static readonly Regex SafeIconNameRegex = new(@"^[a-zA-Z0-9\-_]+:[a-zA-Z0-9\-_]+$", RegexOptions.Compiled);

    private readonly PickleballContext _context;
    private readonly BookingService _bookingService;
    private readonly PricingService _pricingService;
    private readonly QrTokenService _qrTokenService;
    private readonly PasswordService _passwordService;

    public ApiDbController(
        PickleballContext context,
        BookingService bookingService,
        PricingService pricingService,
        QrTokenService qrTokenService,
        PasswordService passwordService)
    {
        _context = context;
        _bookingService = bookingService;
        _pricingService = pricingService;
        _qrTokenService = qrTokenService;
        _passwordService = passwordService;
    }

    public static string FormatIconHtml(string? rawIcon)
    {
        if (string.IsNullOrWhiteSpace(rawIcon))
        {
            return "<iconify-icon icon=\"mdi:cup-water\" width=\"26\"></iconify-icon>";
        }

        string trimmed = rawIcon.Trim();
        // Extract icon="xxx:yyy" if stored as HTML
        if (trimmed.StartsWith("<", StringComparison.Ordinal))
        {
            var match = Regex.Match(trimmed, @"icon=[""']([a-zA-Z0-9\-_]+:[a-zA-Z0-9\-_]+)[""']");
            if (match.Success)
            {
                return $"<iconify-icon icon=\"{match.Groups[1].Value}\" width=\"26\"></iconify-icon>";
            }
            return "<iconify-icon icon=\"mdi:cup-water\" width=\"26\"></iconify-icon>";
        }

        if (!SafeIconNameRegex.IsMatch(trimmed))
        {
            return "<iconify-icon icon=\"mdi:cup-water\" width=\"26\"></iconify-icon>";
        }

        return $"<iconify-icon icon=\"{trimmed}\" width=\"26\"></iconify-icon>";
    }

    private object MapBookingDto(TbBooking b, bool includePiiAndQr)
    {
        var posItemsObj = BookingService.ParsePosItems(b.PosItemsJson)
            .Select(i => new { id = i.Id, name = i.Name, price = i.Price, qty = i.Qty })
            .ToList();

        object? pricingBreakdown = null;
        if (!string.IsNullOrWhiteSpace(b.PricingBreakdownJson))
        {
            try
            {
                pricingBreakdown = JsonSerializer.Deserialize<object>(b.PricingBreakdownJson);
            }
            catch { }
        }

        int amountDue = b.AmountDue > 0 ? b.AmountDue : Math.Max(0, b.CourtFee + b.PosTotal - b.DiscountAmount);
        int paidAmount = b.PaidAmount;

        if (!includePiiAndQr)
        {
            // P0-06: Strip PII (CustomerName, Phone, real BookingCode, CheckInToken) for non-owners / anonymous callers
            return new
            {
                id = $"SLOT-{b.BookingId}",
                courtId = b.CourtId,
                userId = (int?)null,
                date = b.BookingDate,
                name = "Đã đặt sân",
                phone = "",
                time = b.StartTime,
                duration = b.Duration,
                status = b.Status,
                players = b.Players,
                skillLevel = b.SkillLevel ?? "Phong trào",
                courtFee = 0,
                discountAmount = 0,
                posTotal = 0,
                amountDue = 0,
                paidAmount = 0,
                totalPaid = 0,
                paymentStatus = b.PaymentStatus,
                paymentMethod = "cash",
                posItems = Array.Empty<object>(),
                checkInToken = "",
                isAnonymized = true
            };
        }

        string qrToken = b.CheckInToken ?? _qrTokenService.GenerateToken(
            b.BookingCode, b.CourtId, b.BookingDate, b.StartTime, b.Duration);

        return new
        {
            id = b.BookingCode,
            bookingId = b.BookingId,
            courtId = b.CourtId,
            courtName = b.Court?.CourtName ?? $"Sân #{b.CourtId}",
            userId = b.AccountId,
            date = b.BookingDate,
            name = b.CustomerName ?? "Khách hàng",
            phone = b.Phone ?? "",
            time = b.StartTime,
            duration = b.Duration,
            status = b.Status,
            players = b.Players,
            skillLevel = b.SkillLevel ?? "Phong trào",
            courtFee = b.CourtFee,
            discountAmount = b.DiscountAmount,
            posTotal = b.PosTotal,
            amountDue,
            paidAmount,
            totalPaid = paidAmount,
            paymentStatus = b.PaymentStatus ?? (paidAmount >= amountDue && amountDue > 0 ? "paid" : "unpaid"),
            paymentMethod = b.PaymentMethod ?? "cash",
            posItems = posItemsObj,
            pricingBreakdown,
            checkInToken = qrToken,
            checkedInAt = b.CheckedInAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            completedAt = b.CompletedAt?.ToString("yyyy-MM-dd HH:mm:ss"),
            isAnonymized = false
        };
    }

    [HttpGet("/api/db/state")]
    public async Task<IActionResult> GetState()
    {
        await _bookingService.ReconcileBookingStatusesAsync();

        bool isStaffOrAdmin = Function.IsStaffOrAdmin();
        int? currentAccountId = Function.IsLogin() ? Function._AccountId : null;
        string currentPhone = Function.IsLogin() ? BookingService.NormalizePhone(Function._Phone) : string.Empty;

        var courts = await _context.TbCourts
            .Where(c => c.IsActive)
            .OrderBy(c => c.CourtId)
            .Select(c => new
            {
                id = c.CourtId,
                name = c.CourtName,
                type = c.CourtType,
                typeLabel = c.TypeLabel,
                surface = c.Surface,
                surcharge = c.Surcharge,
                basePrice = c.BasePrice,
                imageUrl = c.Image,
                description = c.Description,
                isActive = c.IsActive
            })
            .ToListAsync();

        var rawBookings = await _context.TbBookings
            .Include(b => b.Court)
            .OrderByDescending(b => b.BookingDate)
            .ThenBy(b => b.StartTime)
            .ToListAsync();

        var bookings = rawBookings.Select(b =>
        {
            bool isOwner = currentAccountId != null &&
                           (b.AccountId == currentAccountId ||
                            (!string.IsNullOrEmpty(currentPhone) && BookingService.NormalizePhone(b.Phone) == currentPhone));
            return MapBookingDto(b, includePiiAndQr: isStaffOrAdmin || isOwner);
        }).ToList();

        var myBookings = currentAccountId != null
            ? rawBookings
                .Where(b => b.AccountId == currentAccountId ||
                            (!string.IsNullOrEmpty(currentPhone) && BookingService.NormalizePhone(b.Phone) == currentPhone))
                .Select(b => MapBookingDto(b, includePiiAndQr: true))
                .ToList()
            : new List<object>();

        var courtLocks = await _context.TbCourtLocks
            .OrderByDescending(l => l.LockDate)
            .Select(l => new
            {
                id = l.LockCode,
                courtId = l.CourtId,
                date = l.LockDate,
                startTime = l.StartTime,
                duration = l.Duration,
                reason = l.Reason
            })
            .ToListAsync();

        var rawProducts = await _context.TbProducts
            .Include(p => p.CategoryProduct)
            .Where(p => p.IsActive)
            .OrderBy(p => p.ProductId)
            .ToListAsync();

        var menu = rawProducts.Select(p => new
        {
            id = p.ProductId,
            name = p.Title,
            price = p.Price ?? 0,
            stock = p.UnitInStock ?? p.Quantity ?? 0,
            type = p.CategoryProduct != null && !string.IsNullOrEmpty(p.CategoryProduct.Alias) ? p.CategoryProduct.Alias : "drink",
            icon = FormatIconHtml(p.Icon)
        }).ToList();

        var cmsRows = await _context.TbCmsConfigs.ToListAsync();
        var cmsConfig = new Dictionary<string, object>();
        foreach (var row in cmsRows)
        {
            if (int.TryParse(row.ConfigValue, out int numVal))
                cmsConfig[row.ConfigKey] = numVal;
            else
                cmsConfig[row.ConfigKey] = row.ConfigValue;
        }

        // Current user info & tier discount
        object? currentUserDto = null;
        if (Function.IsLogin() && currentAccountId != null)
        {
            var acc = await _context.TbAccounts.FindAsync(currentAccountId.Value);
            if (acc != null)
            {
                int paidSpend = await _pricingService.GetVerifiedCustomerPaidSpendAsync(_context, acc.AccountId, acc.Phone);
                var (tierName, discountPct) = PricingService.ResolveMemberTier(paidSpend, acc.RoleId, acc.MemberTier);
                currentUserDto = new
                {
                    id = acc.AccountId,
                    name = acc.FullName ?? acc.Username ?? "Hội viên",
                    email = acc.Email ?? "",
                    phone = acc.Phone ?? "",
                    skillLevel = acc.SkillLevel ?? "Giao lưu (DUPR 3.0)",
                    memberTier = tierName,
                    discountPercent = discountPct,
                    paidSpend,
                    role = acc.RoleId == 1 ? "admin" : acc.RoleId == 3 ? "staff" : "user",
                    roleLabel = acc.RoleId == 1 ? "Ban Quản Trị CLB" : acc.RoleId == 3 ? "Nhân viên Vận hành" : "Hội viên CLB",
                    avatar = acc.Avatar ?? ""
                };
            }
        }

        return Ok(new
        {
            ok = true,
            stateVersion = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            serverTime = BookingService.GetVietnamNow().ToString("yyyy-MM-dd HH:mm:ss"),
            isStaffOrAdmin,
            currentUser = currentUserDto,
            courts,
            bookings,
            myBookings,
            courtLocks,
            menu,
            cmsConfig
        });
    }

    /// <summary>
    /// P0-05: Block unauthenticated snapshot overwrite & clearAll wipe on /api/db/sync.
    /// Only Admin/Staff may sync CMS config or court locks through this legacy endpoint; clearAll is permanently forbidden.
    /// </summary>
    [HttpPost("/api/db/sync")]
    public async Task<IActionResult> SyncState([FromBody] JsonElement payload)
    {
        if (payload.TryGetProperty("clearAll", out JsonElement clearEl) && clearEl.ValueKind == JsonValueKind.True)
        {
            return StatusCode(403, new { ok = false, error = "Thao tác xóa toàn bộ dữ liệu (clearAll) đã bị khóa để bảo vệ toàn vẹn dữ liệu." });
        }

        if (!Function.IsStaffOrAdmin())
        {
            return StatusCode(403, new { ok = false, error = "Chỉ Quản trị viên / Thu ngân mới có quyền đồng bộ cấu hình quản trị." });
        }

        // Only allow Admin to update cmsConfig if provided
        if (Function.IsAdmin() && payload.TryGetProperty("cmsConfig", out JsonElement cmsEl) && cmsEl.ValueKind == JsonValueKind.Object)
        {
            foreach (var prop in cmsEl.EnumerateObject())
            {
                string valStr = prop.Value.ValueKind == JsonValueKind.String ? (prop.Value.GetString() ?? "") : prop.Value.GetRawText();
                var existingCfg = await _context.TbCmsConfigs.FindAsync(prop.Name);
                if (existingCfg != null)
                {
                    existingCfg.ConfigValue = valStr;
                }
                else
                {
                    _context.TbCmsConfigs.Add(new TbCmsConfig
                    {
                        ConfigKey = prop.Name,
                        ConfigValue = valStr
                    });
                }
            }
            await _context.SaveChangesAsync();
        }

        return await GetState();
    }

    // ================================================================
    // GRANULAR BOOKING & PRICING ENDPOINTS (P0-08, P1-08, P1-09)
    // ================================================================

    [HttpPost("/api/bookings/quote")]
    public async Task<IActionResult> GetBookingQuote([FromBody] JsonElement payload)
    {
        int courtId = payload.TryGetProperty("courtId", out var cEl) && cEl.TryGetInt32(out int cid) ? cid : 1;
        string time = payload.TryGetProperty("time", out var tEl) ? (tEl.GetString() ?? "17:00") : "17:00";
        double rawDur = payload.TryGetProperty("duration", out var dEl) && dEl.TryGetDouble(out double d) ? d : 60;
        string phone = payload.TryGetProperty("phone", out var pEl) ? (pEl.GetString() ?? "") : "";

        int durationMinutes = BookingService.NormalizeDurationMinutes(rawDur);
        if (durationMinutes < 30 || durationMinutes > 480 || durationMinutes % 30 != 0)
        {
            return BadRequest(new { ok = false, error = "Thời lượng đặt sân phải từ 30 đến 480 phút (bội số 30 phút)." });
        }

        var court = await _context.TbCourts.FirstOrDefaultAsync(c => c.CourtId == courtId);
        if (court == null)
        {
            return NotFound(new { ok = false, error = "Không tìm thấy sân thi đấu." });
        }

        int? currentAccountId = Function.IsLogin() ? Function._AccountId : null;
        int? currentRoleId = Function.IsLogin() ? Function._RoleId : null;
        string? explicitTier = null;

        if (currentAccountId != null)
        {
            var acc = await _context.TbAccounts.FindAsync(currentAccountId.Value);
            explicitTier = acc?.MemberTier;
            if (string.IsNullOrEmpty(phone)) phone = acc?.Phone ?? "";
        }

        int verifiedSpend = await _pricingService.GetVerifiedCustomerPaidSpendAsync(_context, currentAccountId, BookingService.NormalizePhone(phone));
        var quote = _pricingService.CalculateQuote(court, time, durationMinutes, verifiedSpend, currentRoleId, explicitTier);

        return Ok(new
        {
            ok = true,
            quote
        });
    }

    [HttpPost("/api/bookings")]
    public async Task<IActionResult> CreateBooking([FromBody] CreateBookingRequest request)
    {
        int? currentAccountId = Function.IsLogin() ? Function._AccountId : null;
        int? currentRoleId = Function.IsLogin() ? Function._RoleId : null;
        bool isStaffOrAdmin = Function.IsStaffOrAdmin();

        var result = await _bookingService.CreateBookingAsync(request, currentAccountId, currentRoleId, isStaffOrAdmin);
        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { ok = false, error = result.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            booking = MapBookingDto(result.Data!, includePiiAndQr: true)
        });
    }

    [Authorize]
    [HttpGet("/api/bookings/me")]
    public async Task<IActionResult> GetMyBookings()
    {
        await _bookingService.ReconcileBookingStatusesAsync();
        int accountId = Function._AccountId;
        string phone = BookingService.NormalizePhone(Function._Phone);

        var myBookings = await _context.TbBookings
            .Include(b => b.Court)
            .Where(b => b.AccountId == accountId || (!string.IsNullOrEmpty(phone) && b.Phone == phone))
            .OrderByDescending(b => b.BookingDate)
            .ThenByDescending(b => b.StartTime)
            .ToListAsync();

        return Ok(new
        {
            ok = true,
            bookings = myBookings.Select(b => MapBookingDto(b, includePiiAndQr: true)).ToList()
        });
    }

    /// <summary>
    /// P0-07: Secure Lookup.
    /// - Admin/Staff can search by code OR phone OR view all.
    /// - Authenticated Customer sees their own bookings automatically, or can look up a guest booking only with matching Code + Phone.
    /// - Anonymous Guest MUST supply BOTH BookingCode AND matching Phone (or a valid signed QR token).
    /// </summary>
    [HttpGet("/api/bookings/lookup")]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> LookupBookings([FromQuery] string? code, [FromQuery] string? phone)
    {
        await _bookingService.ReconcileBookingStatusesAsync();

        bool isStaffOrAdmin = Function.IsStaffOrAdmin();
        string cleanCode = (code ?? "").Trim().ToUpperInvariant();
        string cleanPhone = BookingService.NormalizePhone(phone);

        // If user pasted a signed QR token into the code box
        if (cleanCode.StartsWith("SPK1.", StringComparison.OrdinalIgnoreCase))
        {
            var qrCheck = _qrTokenService.ValidateToken(cleanCode);
            if (qrCheck.IsValid)
            {
                var qrBooking = await _context.TbBookings
                    .Include(b => b.Court)
                    .FirstOrDefaultAsync(b => b.BookingCode == qrCheck.BookingCode);
                if (qrBooking != null)
                {
                    return Ok(new { ok = true, bookings = new[] { MapBookingDto(qrBooking, includePiiAndQr: true) } });
                }
            }
        }

        if (isStaffOrAdmin)
        {
            var query = _context.TbBookings.Include(b => b.Court).AsQueryable();
            if (!string.IsNullOrEmpty(cleanCode))
            {
                query = query.Where(b => b.BookingCode.ToUpper().Contains(cleanCode));
            }
            if (!string.IsNullOrEmpty(cleanPhone))
            {
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
                bookings = staffResults.Select(b => MapBookingDto(b, includePiiAndQr: true)).ToList()
            });
        }

        // Non-admin caller:
        if (string.IsNullOrEmpty(cleanCode) && string.IsNullOrEmpty(cleanPhone))
        {
            if (Function.IsLogin())
            {
                return await GetMyBookings();
            }
            return BadRequest(new { ok = false, error = "Vui lòng nhập đồng thời Mã đặt sân (PB-XXXXXX) và Số điện thoại đặt sân để tra cứu bảo mật." });
        }

        // If logged-in customer searches within their own bookings by code or phone
        if (Function.IsLogin())
        {
            int accId = Function._AccountId;
            string myPhone = BookingService.NormalizePhone(Function._Phone);

            var ownMatches = await _context.TbBookings
                .Include(b => b.Court)
                .Where(b => (b.AccountId == accId || (!string.IsNullOrEmpty(myPhone) && b.Phone == myPhone)) &&
                            (string.IsNullOrEmpty(cleanCode) || b.BookingCode.ToUpper() == cleanCode) &&
                            (string.IsNullOrEmpty(cleanPhone) || b.Phone == cleanPhone))
                .OrderByDescending(b => b.BookingDate)
                .ToListAsync();

            if (ownMatches.Count > 0)
            {
                return Ok(new
                {
                    ok = true,
                    bookings = ownMatches.Select(b => MapBookingDto(b, includePiiAndQr: true)).ToList()
                });
            }
        }

        // Guest lookup requires BOTH bookingCode AND matching phone (P0-07)
        if (string.IsNullOrEmpty(cleanCode) || string.IsNullOrEmpty(cleanPhone))
        {
            return BadRequest(new
            {
                ok = false,
                error = "Để bảo mật thông tin khách hàng, vui lòng nhập đầy đủ cả Mã đặt sân (VD: PB-XXXXXX) VÀ Số điện thoại đã dùng khi đặt sân."
            });
        }

        var exactMatch = await _context.TbBookings
            .Include(b => b.Court)
            .Where(b => b.BookingCode.ToUpper() == cleanCode && b.Phone == cleanPhone)
            .ToListAsync();

        return Ok(new
        {
            ok = true,
            bookings = exactMatch.Select(b => MapBookingDto(b, includePiiAndQr: true)).ToList()
        });
    }

    [HttpPost("/api/bookings/{code}/cancel")]
    public async Task<IActionResult> CancelBooking(string code, [FromBody] JsonElement payload)
    {
        string? phone = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("phone", out var pEl)
            ? pEl.GetString()
            : null;
        string? reason = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("reason", out var rEl)
            ? rEl.GetString()
            : null;

        int? callerAccountId = Function.IsLogin() ? Function._AccountId : null;
        string? callerPhone = !string.IsNullOrWhiteSpace(phone)
            ? phone
            : (Function.IsLogin() ? Function._Phone : null);
        bool isStaffOrAdmin = Function.IsStaffOrAdmin();
        string actor = Function.IsLogin() ? Function._Username : (callerPhone ?? "Guest");

        var result = await _bookingService.CancelBookingAsync(
            code.Trim().ToUpperInvariant(),
            callerAccountId,
            callerPhone,
            isStaffOrAdmin,
            reason,
            actor);

        if (!result.Success)
        {
            return StatusCode(result.StatusCode, new { ok = false, error = result.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            booking = MapBookingDto(result.Data!, includePiiAndQr: true)
        });
    }

    // ================================================================
    // ADMIN / STAFF OPERATIONAL ENDPOINTS (Management, CheckIn, POS)
    // ================================================================

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("/api/admin/bookings/{code}/approve")]
    public async Task<IActionResult> ApproveBookingApi(string code)
    {
        var res = await _bookingService.ApproveBookingAsync(code.Trim().ToUpperInvariant(), Function._Username);
        if (!res.Success)
        {
            return StatusCode(res.StatusCode, new { ok = false, error = res.ErrorMessage });
        }
        return Ok(new { ok = true, booking = MapBookingDto(res.Data!, includePiiAndQr: true) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("/api/admin/bookings/{code}/reject")]
    public async Task<IActionResult> RejectBookingApi(string code, [FromBody] JsonElement payload)
    {
        string? reason = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("reason", out var rEl)
            ? rEl.GetString()
            : "REJECT";

        var res = await _bookingService.CancelBookingAsync(
            code.Trim().ToUpperInvariant(),
            Function._AccountId,
            null,
            isStaffOrAdmin: true,
            cancelReason: string.IsNullOrWhiteSpace(reason) ? "REJECT" : reason,
            actorUsername: Function._Username);

        if (!res.Success)
        {
            return StatusCode(res.StatusCode, new { ok = false, error = res.ErrorMessage });
        }
        return Ok(new { ok = true, booking = MapBookingDto(res.Data!, includePiiAndQr: true) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("/api/checkin/verify")]
    [HttpPost("/api/admin/bookings/checkin")]
    public async Task<IActionResult> CheckInVerifyApi([FromBody] JsonElement payload)
    {
        string tokenOrCode = payload.TryGetProperty("tokenOrCode", out var tEl)
            ? (tEl.GetString() ?? "")
            : (payload.TryGetProperty("code", out var cEl) ? (cEl.GetString() ?? "") : "");

        bool allowManual = !payload.TryGetProperty("requireSignedQr", out var reqEl) || reqEl.ValueKind != JsonValueKind.True;

        var res = await _bookingService.CheckInBookingAsync(tokenOrCode, Function._Username, allowManual);
        if (!res.Success)
        {
            return StatusCode(res.StatusCode, new { ok = false, error = res.ErrorMessage });
        }

        return Ok(new
        {
            ok = true,
            booking = MapBookingDto(res.Data!, includePiiAndQr: true)
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("/api/admin/bookings/{code}/extend")]
    public async Task<IActionResult> ExtendBookingApi(string code, [FromBody] JsonElement payload)
    {
        int addMinutes = payload.TryGetProperty("addMinutes", out var mEl) && mEl.TryGetInt32(out int m) ? m : 30;
        var res = await _bookingService.ExtendBookingAsync(code.Trim().ToUpperInvariant(), addMinutes, Function._Username);
        if (!res.Success)
        {
            return StatusCode(res.StatusCode, new { ok = false, error = res.ErrorMessage });
        }
        return Ok(new { ok = true, booking = MapBookingDto(res.Data!, includePiiAndQr: true) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("/api/admin/bookings/{code}/pos-item")]
    public async Task<IActionResult> UpdatePosItemApi(string code, [FromBody] JsonElement payload)
    {
        int productId = payload.TryGetProperty("productId", out var pEl) && pEl.TryGetInt32(out int pid) ? pid : 0;
        int deltaQty = payload.TryGetProperty("deltaQty", out var qEl) && qEl.TryGetInt32(out int dq) ? dq : 1;

        var res = await _bookingService.AddPosItemToBookingAsync(code.Trim().ToUpperInvariant(), productId, deltaQty, Function._Username);
        if (!res.Success)
        {
            return StatusCode(res.StatusCode, new { ok = false, error = res.ErrorMessage });
        }
        return Ok(new { ok = true, booking = MapBookingDto(res.Data!, includePiiAndQr: true) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("/api/admin/bookings/{code}/checkout")]
    public async Task<IActionResult> CheckoutBookingApi(string code, [FromBody] JsonElement payload)
    {
        int amount = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("amount", out var aEl) && aEl.TryGetInt32(out int a) ? a : 0;
        string method = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("paymentMethod", out var mEl)
            ? (mEl.GetString() ?? "cash")
            : "cash";
        bool markCompleted = !payload.TryGetProperty("markCompleted", out var mcEl) || mcEl.ValueKind != JsonValueKind.False;
        string? note = payload.ValueKind == JsonValueKind.Object && payload.TryGetProperty("note", out var nEl)
            ? nEl.GetString()
            : null;

        var res = await _bookingService.RecordBookingPaymentAsync(
            code.Trim().ToUpperInvariant(),
            amount,
            method,
            markCompleted,
            note,
            Function._Username);

        if (!res.Success)
        {
            return StatusCode(res.StatusCode, new { ok = false, error = res.ErrorMessage });
        }
        return Ok(new { ok = true, booking = MapBookingDto(res.Data!, includePiiAndQr: true) });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpPost("/api/admin/court-locks")]
    public async Task<IActionResult> CreateCourtLockApi([FromBody] JsonElement payload)
    {
        int courtId = payload.TryGetProperty("courtId", out var cEl) && cEl.TryGetInt32(out int cid) ? cid : 1;
        string date = payload.TryGetProperty("date", out var dEl) ? (dEl.GetString() ?? "").Trim() : "";
        string startTime = payload.TryGetProperty("startTime", out var stEl) ? (stEl.GetString() ?? "08:00").Trim() : "08:00";
        int duration = payload.TryGetProperty("duration", out var durEl) && durEl.TryGetInt32(out int dur) && dur > 0 ? dur : 60;
        string reason = payload.TryGetProperty("reason", out var rEl) ? (rEl.GetString() ?? "Bảo trì định kỳ").Trim() : "Bảo trì định kỳ";

        if (string.IsNullOrEmpty(date))
        {
            return BadRequest(new { ok = false, error = "Vui lòng chọn ngày khóa sân." });
        }

        // P1-11: Verify no active booking conflicts with the requested maintenance lock
        var conflict = await _bookingService.CheckSlotConflictAsync(courtId, date, startTime, duration);
        if (conflict.HasConflict)
        {
            return Conflict(new { ok = false, error = $"Không thể khóa sân: {conflict.Message}" });
        }

        string lockCode = "LK-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
        var lockEntry = new TbCourtLock
        {
            LockCode = lockCode,
            CourtId = courtId,
            LockDate = date,
            StartTime = startTime,
            Duration = duration,
            Reason = reason,
            CreatedDate = BookingService.GetVietnamNow()
        };

        _context.TbCourtLocks.Add(lockEntry);
        await _context.SaveChangesAsync();

        return Ok(new
        {
            ok = true,
            courtLock = new
            {
                id = lockEntry.LockCode,
                courtId = lockEntry.CourtId,
                date = lockEntry.LockDate,
                startTime = lockEntry.StartTime,
                duration = lockEntry.Duration,
                reason = lockEntry.Reason
            }
        });
    }

    [Authorize(Roles = "Admin,Staff")]
    [HttpDelete("/api/admin/court-locks/{lockCode}")]
    public async Task<IActionResult> DeleteCourtLockApi(string lockCode)
    {
        var lockEntry = await _context.TbCourtLocks.FirstOrDefaultAsync(l => l.LockCode == lockCode);
        if (lockEntry == null)
        {
            return NotFound(new { ok = false, error = "Không tìm thấy lệnh khóa sân." });
        }

        _context.TbCourtLocks.Remove(lockEntry);
        await _context.SaveChangesAsync();
        return Ok(new { ok = true });
    }

    // ================================================================
    // AUTHENTICATION JSON ENDPOINTS (P0-01, P0-02, P0-09)
    // ================================================================

    [HttpPost("/api/auth/login")]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> LoginApi([FromBody] JsonElement payload)
    {
        string identifier = payload.TryGetProperty("identifier", out var idProp) ? (idProp.GetString() ?? "").Trim() : "";
        string password = payload.TryGetProperty("password", out var pwProp) ? (pwProp.GetString() ?? "") : "";

        if (string.IsNullOrEmpty(identifier) || string.IsNullOrEmpty(password))
        {
            return BadRequest(new { ok = false, error = "Vui lòng nhập Email/Số điện thoại và mật khẩu." });
        }

        string lowerId = identifier.ToLowerInvariant();
        var account = await _context.TbAccounts
            .Include(a => a.Role)
            .FirstOrDefaultAsync(a =>
                a.IsActive &&
                ((a.Email != null && a.Email.ToLower() == lowerId) ||
                 (a.Phone != null && a.Phone == identifier) ||
                 (a.Username != null && a.Username.ToLower() == lowerId)));

        if (account == null)
        {
            return Unauthorized(new { ok = false, error = "Tài khoản hoặc mật khẩu không chính xác." });
        }

        var verifyResult = _passwordService.VerifyPassword(account, account.Password, password, out string? upgradedHash);
        if (verifyResult == PasswordVerificationResult.Failed)
        {
            return Unauthorized(new { ok = false, error = "Tài khoản hoặc mật khẩu không chính xác." });
        }

        if (verifyResult == PasswordVerificationResult.SuccessRehashNeeded && !string.IsNullOrEmpty(upgradedHash))
        {
            account.Password = upgradedHash;
        }

        account.LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd");
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, account);

        bool isAdmin = account.RoleId == 1;
        string defaultAvatar = isAdmin
            ? "https://images.unsplash.com/photo-1472099645785-5658abf4ff4e?auto=format&fit=crop&w=120&q=80"
            : "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=120&q=80";

        return Ok(new
        {
            ok = true,
            user = new
            {
                id = account.AccountId,
                name = account.FullName ?? account.Username ?? "Hội viên",
                email = account.Email ?? "",
                phone = account.Phone ?? "",
                skillLevel = account.SkillLevel ?? "",
                role = isAdmin ? "admin" : account.RoleId == 3 ? "staff" : "user",
                roleLabel = isAdmin ? "Ban Quản Trị CLB" : account.RoleId == 3 ? "Nhân viên CLB" : "Hội viên CLB",
                avatar = !string.IsNullOrEmpty(account.Avatar) ? account.Avatar : defaultAvatar
            }
        });
    }

    [HttpPost("/api/auth/register")]
    [EnableRateLimiting("AuthAndLookup")]
    public async Task<IActionResult> RegisterApi([FromBody] JsonElement payload)
    {
        string name = payload.TryGetProperty("name", out var nProp) ? (nProp.GetString() ?? "").Trim() : "";
        string phone = payload.TryGetProperty("phone", out var pProp) ? (pProp.GetString() ?? "").Trim() : "";
        string email = payload.TryGetProperty("email", out var eProp) ? (eProp.GetString() ?? "").Trim().ToLowerInvariant() : "";
        string password = payload.TryGetProperty("password", out var pwProp) ? (pwProp.GetString() ?? "") : "";

        if (string.IsNullOrEmpty(name) || name.Length < 2)
        {
            return BadRequest(new { ok = false, error = "Họ và tên phải có ít nhất 2 ký tự." });
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

        bool exists = await _context.TbAccounts.AnyAsync(a =>
            (a.Phone != null && a.Phone == normalizedPhone) ||
            (!string.IsNullOrEmpty(email) && a.Email != null && a.Email.ToLower() == email));

        if (exists)
        {
            return Conflict(new { ok = false, error = "Số điện thoại hoặc Email này đã được đăng ký." });
        }

        var newAccount = new TbAccount
        {
            Username = !string.IsNullOrEmpty(email) ? email.Split('@')[0] : normalizedPhone,
            FullName = name,
            Phone = normalizedPhone,
            Email = string.IsNullOrEmpty(email) ? null : email,
            SkillLevel = "Giao lưu (DUPR 3.0)",
            MemberTier = "Hội viên Tiêu chuẩn",
            DominantHand = "Tay phải",
            RoleId = 2, // Always Customer (P0-03)
            IsActive = true,
            LastLogin = BookingService.GetVietnamNow().ToString("yyyy-MM-dd")
        };

        newAccount.Password = _passwordService.HashPassword(newAccount, password);

        _context.TbAccounts.Add(newAccount);
        await _context.SaveChangesAsync();

        await Function.SignInAccountAsync(HttpContext, newAccount);

        return Ok(new
        {
            ok = true,
            user = new
            {
                id = newAccount.AccountId,
                name = newAccount.FullName,
                email = newAccount.Email ?? "",
                phone = newAccount.Phone,
                role = "user",
                roleLabel = "Hội viên CLB",
                avatar = "https://images.unsplash.com/photo-1535713875002-d1d0cf377fde?auto=format&fit=crop&w=120&q=80"
            }
        });
    }

    // ================================================================
    // SERVER-AUTHORITATIVE ORDERS & STOCK MANAGEMENT (P0-10, P1-14, P1-15)
    // ================================================================

    [HttpPost("/api/orders/create")]
    public async Task<IActionResult> CreateOrderApi([FromBody] JsonElement payload)
    {
        string customerName = payload.TryGetProperty("customerName", out var nProp) ? (nProp.GetString() ?? "").Trim() : "";
        string phone = payload.TryGetProperty("phone", out var pProp) ? (pProp.GetString() ?? "").Trim() : "";
        string address = payload.TryGetProperty("address", out var aProp) ? (aProp.GetString() ?? "").Trim() : "Nhận tại quầy Pro Shop";
        string bookingCode = payload.TryGetProperty("bookingCode", out var bProp) ? (bProp.GetString() ?? "").Trim().ToUpperInvariant() : "";
        string idempotencyKey = payload.TryGetProperty("idempotencyKey", out var ikProp) ? (ikProp.GetString() ?? "").Trim() : "";
        string paymentMethod = payload.TryGetProperty("paymentMethod", out var pmProp) ? (pmProp.GetString() ?? "cash").Trim() : "cash";

        if (string.IsNullOrEmpty(customerName) || customerName.Length < 2)
        {
            return BadRequest(new { ok = false, error = "Vui lòng nhập Họ tên người nhận hợp lệ." });
        }

        if (!BookingService.IsValidPhone(phone))
        {
            return BadRequest(new { ok = false, error = "Vui lòng nhập Số điện thoại hợp lệ (10 chữ số)." });
        }

        string normalizedPhone = BookingService.NormalizePhone(phone);

        if (!payload.TryGetProperty("items", out var itemsEl) || itemsEl.ValueKind != JsonValueKind.Array || itemsEl.GetArrayLength() == 0)
        {
            return BadRequest(new { ok = false, error = "Giỏ hàng đang trống." });
        }

        // Idempotency check
        if (!string.IsNullOrEmpty(idempotencyKey))
        {
            var duplicateOrder = await _context.TbOrders.FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey);
            if (duplicateOrder != null)
            {
                return Ok(new
                {
                    ok = true,
                    orderCode = duplicateOrder.Code,
                    totalAmount = duplicateOrder.TotalAmount ?? 0,
                    bookingCode = duplicateOrder.BookingCode ?? "",
                    deduplicated = true
                });
            }
        }

        // Parse & validate requested items (reject qty <= 0 or missing product ID)
        var requestedItems = new List<(int ProductId, int Qty)>();
        foreach (var item in itemsEl.EnumerateArray())
        {
            int prodId = item.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int pid) ? pid : 0;
            int qty = item.TryGetProperty("qty", out var qEl) && qEl.TryGetInt32(out int q) ? q : 0;

            if (prodId <= 0)
            {
                return BadRequest(new { ok = false, error = "Mã sản phẩm không hợp lệ." });
            }
            if (qty <= 0 || qty > 100)
            {
                return BadRequest(new { ok = false, error = "Số lượng đặt mua phải lớn hơn 0 và không vượt quá 100." });
            }

            requestedItems.Add((prodId, qty));
        }

        // Group by ProductId in case duplicate lines were submitted
        var groupedItems = requestedItems
            .GroupBy(x => x.ProductId)
            .Select(g => (ProductId: g.Key, Qty: g.Sum(x => x.Qty)))
            .ToList();

        var prodIds = groupedItems.Select(x => x.ProductId).ToList();
        var dbProducts = await _context.TbProducts
            .Where(p => prodIds.Contains(p.ProductId) && p.IsActive)
            .ToDictionaryAsync(p => p.ProductId);

        if (dbProducts.Count != groupedItems.Count)
        {
            return BadRequest(new { ok = false, error = "Một hoặc nhiều sản phẩm trong giỏ hàng không tồn tại hoặc đã ngừng kinh doanh." });
        }

        // Check stock availability & compute authoritative server total (P0-10, P1-14)
        int totalQty = 0;
        int totalAmount = 0;
        var verifiedDetails = new List<(TbProduct Product, int UnitPrice, int Qty)>();

        foreach (var reqItem in groupedItems)
        {
            var prod = dbProducts[reqItem.ProductId];
            int availableStock = prod.UnitInStock ?? prod.Quantity ?? 0;
            if (availableStock < reqItem.Qty)
            {
                return BadRequest(new
                {
                    ok = false,
                    error = $"Sản phẩm '{prod.Title}' hiện chỉ còn {availableStock} món trong kho (bạn đang chọn {reqItem.Qty})."
                });
            }

            int unitPrice = (prod.PriceSale is > 0 && prod.PriceSale < prod.Price)
                ? prod.PriceSale.Value
                : (prod.Price ?? 0);

            if (unitPrice <= 0)
            {
                return BadRequest(new { ok = false, error = $"Sản phẩm '{prod.Title}' có cấu hình giá không hợp lệ." });
            }

            totalQty += reqItem.Qty;
            totalAmount += unitPrice * reqItem.Qty;
            verifiedDetails.Add((prod, unitPrice, reqItem.Qty));
        }

        // P1-15: If attaching to a court booking, verify booking exists, is active today, and caller owns it (or is Staff/Admin)
        TbBooking? targetBooking = null;
        if (!string.IsNullOrEmpty(bookingCode))
        {
            targetBooking = await _context.TbBookings.FirstOrDefaultAsync(b => b.BookingCode == bookingCode);
            if (targetBooking == null)
            {
                return BadRequest(new { ok = false, error = $"Không tìm thấy mã đặt sân '{bookingCode}'." });
            }

            if (targetBooking.Status != "approved" && targetBooking.Status != "playing")
            {
                return BadRequest(new { ok = false, error = $"Chỉ có thể gộp hóa đơn F&B vào ca đặt sân đang hoạt động (Đã duyệt / Đang thi đấu)." });
            }

            bool isStaffOrAdmin = Function.IsStaffOrAdmin();
            bool ownsByAccount = Function.IsLogin() && targetBooking.AccountId == Function._AccountId;
            bool ownsByPhone = BookingService.NormalizePhone(targetBooking.Phone) == normalizedPhone;

            if (!isStaffOrAdmin && !ownsByAccount && !ownsByPhone)
            {
                return StatusCode(403, new
                {
                    ok = false,
                    error = "Số điện thoại người nhận không khớp với số điện thoại của chủ ca đặt sân này."
                });
            }
        }

        // Decrement stock & persist order
        foreach (var (prod, _, qty) in verifiedDetails)
        {
            int currentStock = prod.UnitInStock ?? prod.Quantity ?? 0;
            prod.UnitInStock = Math.Max(0, currentStock - qty);
            prod.Quantity = prod.UnitInStock;
        }

        string orderCode = "ORD-" + BookingService.GetVietnamNow().ToString("MMdd") + "-" + Guid.NewGuid().ToString("N")[..4].ToUpperInvariant();
        var newOrder = new TbOrder
        {
            Code = orderCode,
            BookingId = targetBooking?.BookingId,
            BookingCode = targetBooking?.BookingCode,
            CustomerName = customerName,
            Phone = normalizedPhone,
            Address = targetBooking != null ? $"Giao tận sân (Đơn {targetBooking.BookingCode}) - {address}" : address,
            TotalAmount = totalAmount,
            PaidAmount = 0,
            PaymentStatus = "unpaid",
            PaymentMethod = BookingService.NormalizePaymentMethod(paymentMethod),
            IdempotencyKey = string.IsNullOrEmpty(idempotencyKey) ? null : idempotencyKey,
            Quanlity = totalQty,
            OrderStatusId = 2,
            CreatedDate = BookingService.GetVietnamNow(),
            CreatedBy = customerName
        };

        _context.TbOrders.Add(newOrder);
        await _context.SaveChangesAsync();

        foreach (var (prod, unitPrice, qty) in verifiedDetails)
        {
            _context.TbOrderDetails.Add(new TbOrderDetail
            {
                OrderId = newOrder.OrderId,
                ProductId = prod.ProductId,
                Price = unitPrice,
                Quantity = qty
            });
        }

        if (targetBooking != null)
        {
            var currentPos = BookingService.ParsePosItems(targetBooking.PosItemsJson);
            foreach (var (prod, unitPrice, qty) in verifiedDetails)
            {
                var existingItem = currentPos.FirstOrDefault(x => x.Id == prod.ProductId);
                if (existingItem != null)
                {
                    existingItem.Qty += qty;
                    existingItem.Price = unitPrice;
                }
                else
                {
                    currentPos.Add(new PosItemEntry
                    {
                        Id = prod.ProductId,
                        Name = prod.Title ?? "Sản phẩm POS",
                        Price = unitPrice,
                        Qty = qty
                    });
                }
            }

            targetBooking.PosItemsJson = JsonSerializer.Serialize(currentPos.Select(i => new
            {
                id = i.Id,
                name = i.Name,
                price = i.Price,
                qty = i.Qty
            }));

            BookingService.RecalculateBookingTotals(targetBooking);
        }

        await _context.SaveChangesAsync();

        return Ok(new
        {
            ok = true,
            orderCode,
            totalAmount,
            bookingCode = targetBooking?.BookingCode ?? ""
        });
    }

    // ================================================================
    // REAL REVENUE & FINANCIAL REPORTS API (P1-06)
    // ================================================================

    [Authorize(Roles = "Admin,Staff")]
    [HttpGet("/api/admin/reports/summary")]
    public async Task<IActionResult> GetFinancialReportSummary([FromQuery] string? dateFrom, [FromQuery] string? dateTo)
    {
        await _bookingService.ReconcileBookingStatusesAsync();

        var bookingsQuery = _context.TbBookings
            .Include(b => b.Court)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(dateFrom))
        {
            bookingsQuery = bookingsQuery.Where(b => string.Compare(b.BookingDate, dateFrom) >= 0);
        }
        if (!string.IsNullOrWhiteSpace(dateTo))
        {
            bookingsQuery = bookingsQuery.Where(b => string.Compare(b.BookingDate, dateTo) <= 0);
        }

        var bookings = await bookingsQuery.OrderByDescending(b => b.BookingDate).ToListAsync();
        var validBookings = bookings.Where(b => b.Status != "cancelled" && b.Status != "rejected" && b.Status != "expired").ToList();

        int collectedRevenue = validBookings.Sum(b => b.PaidAmount);
        int totalAmountDue = validBookings.Sum(b => b.AmountDue > 0 ? b.AmountDue : (b.CourtFee + b.PosTotal));
        int unpaidReceivables = Math.Max(0, totalAmountDue - collectedRevenue);

        int standaloneOrdersPaid = await _context.TbOrders
            .Where(o => o.BookingId == null && o.PaidAmount > 0)
            .SumAsync(o => (int?)o.PaidAmount) ?? 0;

        return Ok(new
        {
            ok = true,
            collectedBookingRevenue = collectedRevenue,
            standaloneRetailRevenue = standaloneOrdersPaid,
            totalCollectedRevenue = collectedRevenue + standaloneOrdersPaid,
            unpaidReceivables,
            totalEstimatedContractValue = totalAmountDue + standaloneOrdersPaid,
            totalBookings = bookings.Count,
            completedBookings = bookings.Count(b => b.Status == "completed"),
            playingBookings = bookings.Count(b => b.Status == "playing"),
            approvedBookings = bookings.Count(b => b.Status == "approved"),
            pendingBookings = bookings.Count(b => b.Status == "pending"),
            cancelledBookings = bookings.Count(b => b.Status == "cancelled" || b.Status == "rejected" || b.Status == "expired")
        });
    }
}
