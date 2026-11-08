using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PickleballClub.Models;

namespace PickleballClub.Services;

public class BookingService
{
    private static readonly Regex PhoneRegex = new(@"^(0|\+84)[0-9]{8,10}$", RegexOptions.Compiled);
    private static readonly string[] BlockingStatuses = ["pending", "approved", "playing"];

    private readonly PickleballContext _context;
    private readonly PricingService _pricingService;
    private readonly QrTokenService _qrTokenService;

    public BookingService(
        PickleballContext context,
        PricingService pricingService,
        QrTokenService qrTokenService)
    {
        _context = context;
        _pricingService = pricingService;
        _qrTokenService = qrTokenService;
    }

    public static DateTime GetVietnamNow()
    {
        try
        {
            var tz = TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            return TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, tz);
        }
        catch
        {
            return DateTime.UtcNow.AddHours(7);
        }
    }

    public static bool IsValidPhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return false;
        string cleaned = Regex.Replace(phone.Trim(), @"[\s\.\-]", "");
        return PhoneRegex.IsMatch(cleaned);
    }

    public static string NormalizePhone(string? phone)
    {
        if (string.IsNullOrWhiteSpace(phone)) return string.Empty;
        string cleaned = Regex.Replace(phone.Trim(), @"[\s\.\-]", "");
        if (cleaned.StartsWith("+84"))
        {
            cleaned = "0" + cleaned[3..];
        }
        return cleaned;
    }

    public static int TimeToMinutes(string? timeStr)
    {
        if (string.IsNullOrWhiteSpace(timeStr)) return 17 * 60;
        if (TimeOnly.TryParse(timeStr.Trim(), out var t))
        {
            return t.Hour * 60 + t.Minute;
        }
        var parts = timeStr.Split(':');
        if (parts.Length >= 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
        {
            return h * 60 + m;
        }
        return 17 * 60;
    }

    public static string MinutesToTimeString(int totalMinutes)
    {
        int clamped = Math.Clamp(totalMinutes, 0, 23 * 60 + 59);
        return $"{clamped / 60:D2}:{clamped % 60:D2}";
    }

    /// <summary>
    /// Normalizes duration input (handles either minutes like 60, 90, 120 OR hours like 1, 1.5, 2).
    /// Returns duration in minutes (multiple of 30).
    /// </summary>
    public static int NormalizeDurationMinutes(double rawDuration)
    {
        if (rawDuration <= 0) return 60;
        if (rawDuration <= 12)
        {
            return (int)Math.Round(rawDuration * 60);
        }
        return (int)Math.Round(rawDuration);
    }

    public static bool IsOverlapping(int startA, int endA, int startB, int endB)
    {
        return startA < endB && startB < endA;
    }

    /// <summary>
    /// Strict State Machine Reconciliation (P1-07):
    /// - NEVER auto-transitions 'approved' -> 'playing' just because wall clock reached StartTime!
    ///   Check-in (/CheckIn or Staff "Nhận sân") is required to transition 'approved' -> 'playing'.
    /// - If a booking in the future was mistakenly set to 'playing', revert it to 'approved'.
    /// - Auto-transitions 'playing' -> 'completed' when now >= EndTime.
    /// - Auto-transitions 'pending'/'approved' -> 'expired' when EndTime has passed without check-in.
    /// </summary>
    public async Task<bool> ReconcileBookingStatusesAsync()
    {
        var now = GetVietnamNow();
        string todayIso = now.ToString("yyyy-MM-dd");
        int nowMins = now.Hour * 60 + now.Minute;

        var candidates = await _context.TbBookings
            .Where(b => b.Status == "playing" || b.Status == "approved" || b.Status == "pending")
            .ToListAsync();

        bool changed = false;

        foreach (var b in candidates)
        {
            string bDate = string.IsNullOrWhiteSpace(b.BookingDate) ? todayIso : b.BookingDate.Trim();
            int startMins = TimeToMinutes(b.StartTime);
            int durMins = b.Duration > 0 ? b.Duration : 60;
            int endMins = startMins + durMins;

            int dateCmp = string.CompareOrdinal(bDate, todayIso);

            if (dateCmp < 0)
            {
                if (b.Status == "playing")
                {
                    b.Status = "completed";
                    b.OrderStatusId = 3;
                    b.CompletedAt ??= now;
                    changed = true;
                }
                else if (b.Status == "pending")
                {
                    b.Status = "expired";
                    b.OrderStatusId = 4;
                    b.CancelReason ??= "Quá hạn ngày đặt sân chưa duyệt.";
                    changed = true;
                }
            }
            else if (dateCmp > 0)
            {
                // Future date cannot be 'playing'
                if (b.Status == "playing")
                {
                    b.Status = "approved";
                    changed = true;
                }
            }
            else
            {
                // Today
                if (nowMins >= endMins)
                {
                    if (b.Status == "playing")
                    {
                        b.Status = "completed";
                        b.OrderStatusId = 3;
                        b.CompletedAt ??= now;
                        changed = true;
                    }
                }
                else if (nowMins < startMins - 15 && b.Status == "playing" && b.CheckedInAt == null)
                {
                    // Legacy booking marked playing before its time without explicit check-in
                    b.Status = "approved";
                    changed = true;
                }
            }

            // Ensure CheckInToken and AmountDue are populated
            if (string.IsNullOrEmpty(b.CheckInToken))
            {
                b.CheckInToken = _qrTokenService.GenerateToken(
                    b.BookingCode, b.CourtId, bDate, b.StartTime ?? "17:00", durMins);
                changed = true;
            }

            if (b.AmountDue <= 0 && b.CourtFee > 0)
            {
                RecalculateBookingTotals(b);
                changed = true;
            }
        }

        if (changed)
        {
            await _context.SaveChangesAsync();
        }

        return changed;
    }

    public async Task<ConflictCheckResult> CheckSlotConflictAsync(
        int courtId,
        string dateIso,
        string startTimeStr,
        int durationMinutes,
        string? excludeBookingCode = null,
        string? excludeLockCode = null)
    {
        int reqStart = TimeToMinutes(startTimeStr);
        int reqEnd = reqStart + durationMinutes;

        if (reqStart < 5 * 60 || reqEnd > 23 * 60 + 30)
        {
            return ConflictCheckResult.Conflict("Khung giờ hoạt động của CLB là từ 05:00 đến 23:30.");
        }

        string cleanDate = dateIso.Trim();

        // 1. Check active bookings on the same court & date
        var existingBookings = await _context.TbBookings
            .Where(b => b.CourtId == courtId &&
                        b.BookingDate == cleanDate &&
                        BlockingStatuses.Contains(b.Status))
            .ToListAsync();

        foreach (var b in existingBookings)
        {
            if (!string.IsNullOrEmpty(excludeBookingCode) &&
                string.Equals(b.BookingCode, excludeBookingCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int bStart = TimeToMinutes(b.StartTime);
            int bEnd = bStart + (b.Duration > 0 ? b.Duration : 60);

            if (IsOverlapping(reqStart, reqEnd, bStart, bEnd))
            {
                return ConflictCheckResult.Conflict(
                    $"Khung giờ bị trùng với ca đặt sân ({b.StartTime} - {MinutesToTimeString(bEnd)}).");
            }
        }

        // 2. Check maintenance court locks on the same court & date
        var locks = await _context.TbCourtLocks
            .Where(l => l.CourtId == courtId && l.LockDate == cleanDate)
            .ToListAsync();

        foreach (var l in locks)
        {
            if (!string.IsNullOrEmpty(excludeLockCode) &&
                string.Equals(l.LockCode, excludeLockCode, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            int lStart = TimeToMinutes(l.StartTime);
            int lEnd = lStart + (l.Duration > 0 ? l.Duration : 60);

            if (IsOverlapping(reqStart, reqEnd, lStart, lEnd))
            {
                return ConflictCheckResult.Conflict(
                    $"Sân đang khóa bảo trì/giải đấu từ {l.StartTime} đến {MinutesToTimeString(lEnd)} ({l.Reason ?? "Bảo trì"}).");
            }
        }

        return ConflictCheckResult.Ok();
    }

    public async Task<ServiceResult<TbBooking>> CreateBookingAsync(
        CreateBookingRequest request,
        int? currentAccountId,
        int? currentRoleId,
        bool isStaffOrAdmin)
    {
        if (string.IsNullOrWhiteSpace(request.CustomerName) || request.CustomerName.Trim().Length < 2)
        {
            return ServiceResult<TbBooking>.Fail("Vui lòng nhập họ tên hợp lệ (tối thiểu 2 ký tự).", 400);
        }

        if (!IsValidPhone(request.CustomerPhone))
        {
            return ServiceResult<TbBooking>.Fail("Số điện thoại không hợp lệ (định dạng Việt Nam 10 số).", 400);
        }

        if (!DateOnly.TryParseExact(request.Date?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return ServiceResult<TbBooking>.Fail("Ngày đặt sân không hợp lệ (yyyy-MM-dd).", 400);
        }

        if (!TimeOnly.TryParse(request.Time?.Trim(), out var parsedStartTime))
        {
            return ServiceResult<TbBooking>.Fail("Giờ bắt đầu không hợp lệ (HH:mm).", 400);
        }

        int durationMinutes = NormalizeDurationMinutes(request.Duration);
        if (durationMinutes < 30 || durationMinutes > 480 || durationMinutes % 30 != 0)
        {
            return ServiceResult<TbBooking>.Fail("Thời lượng đặt sân phải từ 30 đến 480 phút (bội số 30 phút).", 400);
        }

        var now = GetVietnamNow();
        var bookingStartDateTime = parsedDate.ToDateTime(parsedStartTime);
        if (!isStaffOrAdmin && bookingStartDateTime < now.AddMinutes(-15))
        {
            return ServiceResult<TbBooking>.Fail("Không thể đặt sân cho khung giờ đã qua.", 400);
        }

        var court = await _context.TbCourts.FirstOrDefaultAsync(c => c.CourtId == request.CourtId);
        if (court == null || !court.IsActive)
        {
            return ServiceResult<TbBooking>.Fail("Sân bạn chọn không tồn tại hoặc đang tạm ngưng hoạt động.", 404);
        }

        string dateIso = parsedDate.ToString("yyyy-MM-dd");
        string timeStr = parsedStartTime.ToString("HH:mm");
        string normalizedPhone = NormalizePhone(request.CustomerPhone);

        using var tx = await BeginSerializableTransactionIfPossibleAsync();
        try
        {
            var conflict = await CheckSlotConflictAsync(court.CourtId, dateIso, timeStr, durationMinutes);
            if (conflict.HasConflict)
            {
                return ServiceResult<TbBooking>.Fail(conflict.Message!, 409);
            }

            string? explicitTier = null;
            if (currentAccountId is > 0)
            {
                var acc = await _context.TbAccounts.FindAsync(currentAccountId.Value);
                explicitTier = acc?.MemberTier;
            }

            int verifiedSpend = await _pricingService.GetVerifiedCustomerPaidSpendAsync(_context, currentAccountId, normalizedPhone);
            var quote = _pricingService.CalculateQuote(court, timeStr, durationMinutes, verifiedSpend, currentRoleId, explicitTier);

            string bookingCode = await GenerateUniqueBookingCodeAsync();
            string qrToken = _qrTokenService.GenerateToken(bookingCode, court.CourtId, dateIso, timeStr, durationMinutes);
            string paymentMethod = NormalizePaymentMethod(request.PaymentMethod);
            bool autoApprove = isStaffOrAdmin && request.AutoApprove;

            // P1-05: Never mark PaidAmount = CourtFee on creation! PaidAmount = 0 ("unpaid") until collected.
            var booking = new TbBooking
            {
                BookingCode = bookingCode,
                CourtId = court.CourtId,
                AccountId = currentAccountId,
                CustomerName = request.CustomerName.Trim(),
                Phone = normalizedPhone,
                BookingDate = dateIso,
                StartTime = timeStr,
                Duration = durationMinutes,
                Players = request.Players is >= 1 and <= 16 ? request.Players : 4,
                SkillLevel = string.IsNullOrWhiteSpace(request.SkillLevel) ? "Phong trào" : request.SkillLevel.Trim(),
                PaymentMethod = paymentMethod,
                CourtFee = quote.NetCourtFee,
                DiscountAmount = quote.DiscountAmount,
                PosTotal = 0,
                AmountDue = quote.NetCourtFee,
                PaidAmount = 0,
                TotalPaid = 0,
                PaymentStatus = "unpaid",
                PosItemsJson = "[]",
                PricingBreakdownJson = quote.ToJson(),
                CheckInToken = qrToken,
                Status = autoApprove ? "approved" : "pending",
                OrderStatusId = autoApprove ? 2 : 1,
                CreatedDate = now
            };

            _context.TbBookings.Add(booking);
            await _context.SaveChangesAsync();

            if (tx != null)
            {
                await tx.CommitAsync();
            }

            booking.Court = court;
            return ServiceResult<TbBooking>.Ok(booking);
        }
        catch (Exception ex)
        {
            if (tx != null) await tx.RollbackAsync();
            return ServiceResult<TbBooking>.Fail($"Lỗi tạo lịch đặt sân: {ex.Message}", 500);
        }
    }

    public async Task<ServiceResult<TbBooking>> ApproveBookingAsync(string bookingCode, string actorUsername)
    {
        using var tx = await BeginSerializableTransactionIfPossibleAsync();
        var booking = await _context.TbBookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);

        if (booking == null)
        {
            return ServiceResult<TbBooking>.Fail("Không tìm thấy mã đặt sân.", 404);
        }

        if (booking.Status != "pending")
        {
            return ServiceResult<TbBooking>.Fail($"Không thể duyệt đơn đang ở trạng thái '{booking.Status}'.", 400);
        }

        var conflict = await CheckSlotConflictAsync(
            booking.CourtId,
            booking.BookingDate,
            booking.StartTime,
            booking.Duration,
            booking.BookingCode);

        if (conflict.HasConflict)
        {
            return ServiceResult<TbBooking>.Fail(conflict.Message!, 409);
        }

        booking.Status = "approved";
        booking.OrderStatusId = 2;
        if (string.IsNullOrEmpty(booking.CheckInToken))
        {
            booking.CheckInToken = _qrTokenService.GenerateToken(
                booking.BookingCode, booking.CourtId, booking.BookingDate, booking.StartTime, booking.Duration);
        }

        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_APPROVED",
            EntityType = "TbBooking",
            EntityId = booking.BookingCode,
            ActorUsername = actorUsername,
            Details = $"Duyệt lịch đặt sân {booking.BookingCode}",
            CreatedAt = GetVietnamNow()
        });

        await _context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();

        return ServiceResult<TbBooking>.Ok(booking);
    }

    public async Task<ServiceResult<TbBooking>> CheckInBookingAsync(string rawCodeOrToken, string actorUsername, bool allowManualCode)
    {
        string lookupCode;
        var qrResult = _qrTokenService.ValidateToken(rawCodeOrToken);

        if (qrResult.IsSignedToken)
        {
            if (!qrResult.IsValid)
            {
                return ServiceResult<TbBooking>.Fail(qrResult.ErrorMessage ?? "Mã QR không hợp lệ.", 400);
            }
            lookupCode = qrResult.BookingCode;
        }
        else
        {
            if (!allowManualCode)
            {
                return ServiceResult<TbBooking>.Fail("Vui lòng quét mã QR hợp lệ trên vé đặt sân.", 400);
            }
            lookupCode = qrResult.BookingCode;
        }

        var booking = await _context.TbBookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingCode == lookupCode);

        if (booking == null)
        {
            return ServiceResult<TbBooking>.Fail($"Không tìm thấy lịch đặt sân với mã '{lookupCode}'.", 404);
        }

        if (booking.Status == "playing")
        {
            return ServiceResult<TbBooking>.Fail($"Ca đặt sân {booking.BookingCode} đã được check-in trước đó và đang thi đấu!", 409);
        }

        if (booking.Status == "completed")
        {
            return ServiceResult<TbBooking>.Fail($"Ca đặt sân {booking.BookingCode} đã hoàn thành.", 400);
        }

        if (booking.Status == "cancelled" || booking.Status == "rejected" || booking.Status == "expired")
        {
            return ServiceResult<TbBooking>.Fail($"Ca đặt sân {booking.BookingCode} đã bị hủy hoặc hết hạn ({booking.Status}).", 400);
        }

        var now = GetVietnamNow();
        string todayIso = now.ToString("yyyy-MM-dd");
        if (booking.BookingDate != todayIso)
        {
            return ServiceResult<TbBooking>.Fail(
                $"Lịch đặt sân {booking.BookingCode} được đặt cho ngày {booking.BookingDate} (hôm nay là {todayIso}).", 400);
        }

        // Check conflict if checking in a pending booking directly
        if (booking.Status == "pending")
        {
            var conflict = await CheckSlotConflictAsync(
                booking.CourtId,
                booking.BookingDate,
                booking.StartTime,
                booking.Duration,
                booking.BookingCode);

            if (conflict.HasConflict)
            {
                return ServiceResult<TbBooking>.Fail(conflict.Message!, 409);
            }
        }

        booking.Status = "playing";
        booking.OrderStatusId = 2;
        booking.CheckedInAt = now;

        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = qrResult.IsSignedToken ? "BOOKING_CHECKIN_QR" : "BOOKING_CHECKIN_MANUAL",
            EntityType = "TbBooking",
            EntityId = booking.BookingCode,
            ActorUsername = actorUsername,
            Details = $"Check-in mở sân {booking.BookingCode}",
            CreatedAt = now
        });

        await _context.SaveChangesAsync();
        return ServiceResult<TbBooking>.Ok(booking);
    }

    public async Task<ServiceResult<TbBooking>> CancelBookingAsync(
        string bookingCode,
        int? callerAccountId,
        string? callerPhone,
        bool isStaffOrAdmin,
        string? cancelReason,
        string actorUsername)
    {
        var booking = await _context.TbBookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);

        if (booking == null)
        {
            return ServiceResult<TbBooking>.Fail("Không tìm thấy lịch đặt sân.", 404);
        }

        if (booking.Status == "cancelled" || booking.Status == "rejected")
        {
            return ServiceResult<TbBooking>.Fail("Lịch đặt sân này đã được hủy trước đó.", 400);
        }

        if (booking.Status == "completed")
        {
            return ServiceResult<TbBooking>.Fail("Không thể hủy ca đặt sân đã hoàn thành.", 400);
        }

        if (!isStaffOrAdmin)
        {
            bool ownsByAccount = callerAccountId != null && booking.AccountId == callerAccountId;
            bool ownsByPhone = !string.IsNullOrWhiteSpace(callerPhone) &&
                               string.Equals(NormalizePhone(booking.Phone), NormalizePhone(callerPhone), StringComparison.OrdinalIgnoreCase);

            if (!ownsByAccount && !ownsByPhone)
            {
                return ServiceResult<TbBooking>.Fail("Bạn không có quyền hủy lịch đặt sân của người khác (yêu cầu đúng SĐT đặt sân).", 403);
            }

            if (booking.Status == "playing")
            {
                return ServiceResult<TbBooking>.Fail("Ca đặt sân đang thi đấu, vui lòng liên hệ quầy thu ngân.", 400);
            }

            // Cutoff policy (P1-12): Approved bookings can only be self-cancelled >= 2 hours before StartTime
            if (booking.Status == "approved" &&
                DateOnly.TryParse(booking.BookingDate, out var bDate) &&
                TimeOnly.TryParse(booking.StartTime, out var bTime))
            {
                var now = GetVietnamNow();
                var startDateTime = bDate.ToDateTime(bTime);
                if (startDateTime <= now.AddHours(2))
                {
                    return ServiceResult<TbBooking>.Fail(
                        "Lịch đã duyệt chỉ có thể tự hủy trước giờ bóng lăn tối thiểu 2 tiếng. Vui lòng liên hệ Hotline CLB để được hỗ trợ.", 400);
                }
            }
        }

        booking.Status = isStaffOrAdmin && cancelReason == "REJECT" ? "rejected" : "cancelled";
        booking.OrderStatusId = 4;
        booking.CancelledAt = GetVietnamNow();
        booking.CancelReason = string.IsNullOrWhiteSpace(cancelReason) || cancelReason == "REJECT"
            ? (isStaffOrAdmin ? "Hủy bởi quản trị viên / thu ngân" : "Khách hàng tự hủy")
            : cancelReason.Trim();

        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_CANCELLED",
            EntityType = "TbBooking",
            EntityId = booking.BookingCode,
            ActorAccountId = callerAccountId,
            ActorUsername = actorUsername,
            Details = booking.CancelReason,
            CreatedAt = GetVietnamNow()
        });

        await _context.SaveChangesAsync();
        return ServiceResult<TbBooking>.Ok(booking);
    }

    public async Task<ServiceResult<TbBooking>> ExtendBookingAsync(string bookingCode, int addMinutes, string actorUsername)
    {
        if (addMinutes <= 0 || addMinutes > 240 || addMinutes % 30 != 0)
        {
            return ServiceResult<TbBooking>.Fail("Thời lượng gia hạn phải là bội số 30 phút (ví dụ 30 hoặc 60 phút).", 400);
        }

        using var tx = await BeginSerializableTransactionIfPossibleAsync();
        var booking = await _context.TbBookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);

        if (booking == null)
        {
            return ServiceResult<TbBooking>.Fail("Không tìm thấy lịch đặt sân.", 404);
        }

        if (booking.Status != "playing" && booking.Status != "approved")
        {
            return ServiceResult<TbBooking>.Fail("Chỉ có thể gia hạn cho ca đang thi đấu hoặc đã duyệt.", 400);
        }

        int newDuration = (booking.Duration > 0 ? booking.Duration : 60) + addMinutes;
        var conflict = await CheckSlotConflictAsync(
            booking.CourtId,
            booking.BookingDate,
            booking.StartTime,
            newDuration,
            booking.BookingCode);

        if (conflict.HasConflict)
        {
            return ServiceResult<TbBooking>.Fail($"Không thể gia hạn thêm {addMinutes} phút: {conflict.Message}", 409);
        }

        int verifiedSpend = await _pricingService.GetVerifiedCustomerPaidSpendAsync(_context, booking.AccountId, booking.Phone);
        var newQuote = _pricingService.CalculateQuote(booking.Court, booking.StartTime, newDuration, verifiedSpend);

        booking.Duration = newDuration;
        booking.CourtFee = newQuote.NetCourtFee;
        booking.DiscountAmount = newQuote.DiscountAmount;
        booking.PricingBreakdownJson = newQuote.ToJson();
        RecalculateBookingTotals(booking);

        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_EXTENDED",
            EntityType = "TbBooking",
            EntityId = booking.BookingCode,
            ActorUsername = actorUsername,
            Details = $"Gia hạn +{addMinutes}p (tổng {newDuration}p), phí sân mới: {booking.CourtFee}đ",
            CreatedAt = GetVietnamNow()
        });

        await _context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();

        return ServiceResult<TbBooking>.Ok(booking);
    }

    public async Task<ServiceResult<TbBooking>> AddPosItemToBookingAsync(
        string bookingCode,
        int productId,
        int deltaQty,
        string actorUsername)
    {
        using var tx = await BeginSerializableTransactionIfPossibleAsync();
        var booking = await _context.TbBookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);

        if (booking == null)
        {
            return ServiceResult<TbBooking>.Fail("Không tìm thấy ca đặt sân.", 404);
        }

        var product = await _context.TbProducts.FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive);
        if (product == null)
        {
            return ServiceResult<TbBooking>.Fail("Sản phẩm không tồn tại hoặc đã ngừng kinh doanh.", 404);
        }

        var currentPos = ParsePosItems(booking.PosItemsJson);
        var existingItem = currentPos.FirstOrDefault(x => x.Id == product.ProductId);

        if (deltaQty > 0)
        {
            int stock = product.UnitInStock ?? product.Quantity ?? 0;
            if (stock < deltaQty)
            {
                return ServiceResult<TbBooking>.Fail($"Sản phẩm '{product.Title}' chỉ còn {stock} món trong kho.", 400);
            }

            product.UnitInStock = stock - deltaQty;
            product.Quantity = product.UnitInStock;

            int unitPrice = product.Price ?? 0;
            if (existingItem != null)
            {
                existingItem.Qty += deltaQty;
                existingItem.Price = unitPrice;
            }
            else
            {
                currentPos.Add(new PosItemEntry
                {
                    Id = product.ProductId,
                    Name = product.Title ?? "Sản phẩm POS",
                    Price = unitPrice,
                    Qty = deltaQty
                });
            }
        }
        else if (deltaQty < 0 && existingItem != null)
        {
            int removeQty = Math.Min(existingItem.Qty, Math.Abs(deltaQty));
            existingItem.Qty -= removeQty;
            product.UnitInStock = (product.UnitInStock ?? 0) + removeQty;
            product.Quantity = product.UnitInStock;

            if (existingItem.Qty <= 0)
            {
                currentPos.Remove(existingItem);
            }
        }

        booking.PosItemsJson = JsonSerializer.Serialize(currentPos.Select(i => new
        {
            id = i.Id,
            name = i.Name,
            price = i.Price,
            qty = i.Qty
        }));

        RecalculateBookingTotals(booking);

        await _context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();

        return ServiceResult<TbBooking>.Ok(booking);
    }

    public async Task<ServiceResult<TbBooking>> RecordBookingPaymentAsync(
        string bookingCode,
        int amount,
        string paymentMethod,
        bool markCompleted,
        string? note,
        string actorUsername)
    {
        var booking = await _context.TbBookings
            .Include(b => b.Court)
            .FirstOrDefaultAsync(b => b.BookingCode == bookingCode);

        if (booking == null)
        {
            return ServiceResult<TbBooking>.Fail("Không tìm thấy lịch đặt sân.", 404);
        }

        RecalculateBookingTotals(booking);
        int remaining = Math.Max(0, booking.AmountDue - booking.PaidAmount);
        int collectAmount = amount > 0 ? amount : remaining;

        var now = GetVietnamNow();
        if (collectAmount > 0)
        {
            string method = NormalizePaymentMethod(paymentMethod);
            booking.PaymentMethod = method;
            booking.PaidAmount += collectAmount;
            booking.TotalPaid = booking.PaidAmount;

            _context.TbPaymentTransactions.Add(new TbPaymentTransaction
            {
                BookingId = booking.BookingId,
                BookingCode = booking.BookingCode,
                Amount = collectAmount,
                PaymentMethod = method,
                Status = "completed",
                Note = note ?? (markCompleted ? "Thu ngân tất toán & đóng ca" : "Thu tiền tại quầy"),
                RecordedBy = actorUsername,
                CreatedAt = now
            });
        }

        RecalculateBookingTotals(booking);

        if (markCompleted)
        {
            booking.Status = "completed";
            booking.OrderStatusId = 3;
            booking.CompletedAt = now;
        }

        _context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = markCompleted ? "BOOKING_CHECKOUT_COMPLETED" : "BOOKING_PAYMENT_RECORDED",
            EntityType = "TbBooking",
            EntityId = booking.BookingCode,
            ActorUsername = actorUsername,
            Details = $"Thu {collectAmount}đ ({booking.PaymentMethod}), Paid={booking.PaidAmount}/{booking.AmountDue}",
            CreatedAt = now
        });

        await _context.SaveChangesAsync();
        return ServiceResult<TbBooking>.Ok(booking);
    }

    public static List<PosItemEntry> ParsePosItems(string? posJson)
    {
        var result = new List<PosItemEntry>();
        if (string.IsNullOrWhiteSpace(posJson)) return result;

        try
        {
            using var doc = JsonDocument.Parse(posJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return result;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                int id = el.TryGetProperty("id", out var idEl) && idEl.TryGetInt32(out int xi) ? xi : 0;
                string name = el.TryGetProperty("name", out var nmEl) ? (nmEl.GetString() ?? "") : "";
                int price = el.TryGetProperty("price", out var prEl) && prEl.TryGetInt32(out int xp) ? xp : 0;
                int qty = el.TryGetProperty("qty", out var qEl) && qEl.TryGetInt32(out int xq) ? xq : 1;
                if (qty > 0)
                {
                    result.Add(new PosItemEntry { Id = id, Name = name, Price = price, Qty = qty });
                }
            }
        }
        catch
        {
            // Ignore malformed JSON
        }

        return result;
    }

    public static void RecalculateBookingTotals(TbBooking booking)
    {
        var posItems = ParsePosItems(booking.PosItemsJson);
        int posSum = posItems.Sum(i => i.Price * i.Qty);
        booking.PosTotal = posSum;
        booking.AmountDue = Math.Max(0, booking.CourtFee) + posSum;
        booking.TotalPaid = booking.PaidAmount;

        if (booking.PaidAmount <= 0)
        {
            booking.PaymentStatus = "unpaid";
        }
        else if (booking.PaidAmount < booking.AmountDue)
        {
            booking.PaymentStatus = "partial";
        }
        else
        {
            booking.PaymentStatus = "paid";
        }
    }

    public static string NormalizePaymentMethod(string? method)
    {
        return (method ?? "cash").Trim().ToLowerInvariant() switch
        {
            "transfer" or "bank" or "qr" => "transfer",
            "vnpay" => "vnpay",
            "card" => "card",
            _ => "cash"
        };
    }

    private async Task<string> GenerateUniqueBookingCodeAsync()
    {
        const string chars = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        for (int attempt = 0; attempt < 10; attempt++)
        {
            var codeChars = new char[6];
            for (int i = 0; i < 6; i++)
            {
                codeChars[i] = chars[Random.Shared.Next(chars.Length)];
            }
            string candidate = "PB-" + new string(codeChars);
            bool exists = await _context.TbBookings.AnyAsync(b => b.BookingCode == candidate);
            if (!exists) return candidate;
        }
        return "PB-" + Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
    }

    private async Task<IDbContextTransaction?> BeginSerializableTransactionIfPossibleAsync()
    {
        if (!_context.Database.IsRelational())
        {
            return null;
        }
        try
        {
            return await _context.Database.BeginTransactionAsync(IsolationLevel.Serializable);
        }
        catch
        {
            return null;
        }
    }
}

public class PosItemEntry
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int Qty { get; set; }
}

public class ConflictCheckResult
{
    public bool HasConflict { get; set; }
    public string? Message { get; set; }

    public static ConflictCheckResult Ok() => new() { HasConflict = false };
    public static ConflictCheckResult Conflict(string message) => new() { HasConflict = true, Message = message };
}

public class ServiceResult<T>
{
    public bool Success { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? ErrorMessage { get; set; }
    public T? Data { get; set; }

    public static ServiceResult<T> Ok(T data) => new() { Success = true, StatusCode = 200, Data = data };
    public static ServiceResult<T> Fail(string message, int statusCode = 400) => new()
    {
        Success = false,
        StatusCode = statusCode,
        ErrorMessage = message
    };
}

public class CreateBookingRequest
{
    public int CourtId { get; set; } = 1;
    public string? CustomerName { get; set; }
    public string? CustomerPhone { get; set; }
    public string? Date { get; set; }
    public string? Time { get; set; }
    public double Duration { get; set; } = 60;
    public int Players { get; set; } = 4;
    public string? SkillLevel { get; set; } = "Phong trào";
    public string? PaymentMethod { get; set; } = "cash";
    public bool AutoApprove { get; set; }
}
