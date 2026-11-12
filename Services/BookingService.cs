using System;
using System.Collections.Generic;
using System.Data;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Services;

public class BookingCreateInput
{
    public int CourtId { get; set; }
    public string CustomerName { get; set; } = string.Empty;
    public string Phone { get; set; } = string.Empty;
    public string BookingDate { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public int Duration { get; set; } = 60;
    public int Players { get; set; } = 4;
    public string? SkillLevel { get; set; }
    public string? PaymentMethod { get; set; } = "cash";
    public int? AccountId { get; set; }
    public int? ActorRoleId { get; set; }
    public bool AutoApproveWalkIn { get; set; }
    public string? IdempotencyKey { get; set; }
    public string? ExpectedPricingVersion { get; set; }
    public int? ExpectedAmountDue { get; set; }
    public string? IpAddress { get; set; }
    public DateTime? ReferenceNow { get; set; }
}

public class BookingOperationResult
{
    public bool Success { get; set; }
    public int StatusCode { get; set; } = 200;
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
    public TbBooking? Booking { get; set; }
    public BookingQuoteBreakdown? Quote { get; set; }
    public string? GuestAccessToken { get; set; }
    public TbPaymentTransaction? Transaction { get; set; }
    public bool IsIdempotentReplay { get; set; }

    public static BookingOperationResult Fail(
        string message,
        int statusCode = 400,
        BookingQuoteBreakdown? latestQuote = null,
        string? errorCode = null) => new()
    {
        Success = false,
        StatusCode = statusCode,
        ErrorCode = errorCode,
        ErrorMessage = message,
        Quote = latestQuote
    };

    public static BookingOperationResult Ok(
        TbBooking booking,
        BookingQuoteBreakdown? quote = null,
        string? guestAccessToken = null,
        TbPaymentTransaction? tx = null,
        bool replay = false) => new()
    {
        Success = true,
        StatusCode = 200,
        Booking = booking,
        Quote = quote,
        GuestAccessToken = guestAccessToken,
        Transaction = tx,
        IsIdempotentReplay = replay
    };
}

public class PosCartItemDto
{
    public int ProductId { get; set; }
    public string Name { get; set; } = string.Empty;
    public int Price { get; set; }
    public int Qty { get; set; }
    public int? OrderId { get; set; }
    public string? OrderCode { get; set; }
}

public class BookingService
{
    private static readonly int[] AllowedDurations = { 60, 90, 120, 150, 180, 240 };
    private static readonly Regex PhoneRegex = new(@"^(0|\+84)[0-9]{8,10}$", RegexOptions.Compiled);

    private readonly PricingService _pricingService;
    private readonly QrTokenService _qrTokenService;
    private readonly ClubSettingsService _settingsService;

    public BookingService(
        PricingService pricingService,
        QrTokenService qrTokenService,
        ClubSettingsService settingsService)
    {
        _pricingService = pricingService;
        _qrTokenService = qrTokenService;
        _settingsService = settingsService;
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

    public static string NormalizePhone(string? rawPhone)
    {
        if (string.IsNullOrWhiteSpace(rawPhone)) return string.Empty;
        string cleaned = Regex.Replace(rawPhone.Trim(), @"[\s\.\-]", "");
        if (cleaned.StartsWith("+84"))
        {
            cleaned = "0" + cleaned[3..];
        }
        return cleaned;
    }

    public static bool IsValidPhone(string? rawPhone)
    {
        string normalized = NormalizePhone(rawPhone);
        return PhoneRegex.IsMatch(normalized) && normalized.Length >= 9 && normalized.Length <= 11;
    }

    public static bool RangesOverlap(int startA, int endA, int startB, int endB) =>
        startA < endB && endA > startB;

    public static DateTime ToUtcFromVietnamNow(DateTime? referenceVietnamNow = null)
    {
        var vn = referenceVietnamNow ?? GetVietnamNow();
        if (vn.Kind == DateTimeKind.Utc) return vn;
        return DateTime.SpecifyKind(vn.AddHours(-7), DateTimeKind.Utc);
    }

    public static bool IsPendingHoldActive(TbBooking b, DateTime utcNow)
    {
        if (!string.Equals(b.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return false;

        if (b.HoldExpiresAtUtc.HasValue)
        {
            var holdUtc = b.HoldExpiresAtUtc.Value.Kind == DateTimeKind.Utc
                ? b.HoldExpiresAtUtc.Value
                : DateTime.SpecifyKind(b.HoldExpiresAtUtc.Value, DateTimeKind.Utc);
            return holdUtc > utcNow;
        }

        if (b.CreatedDate.HasValue)
        {
            var fallbackExpiryUtc = ToUtcFromVietnamNow(b.CreatedDate.Value).AddMinutes(30);
            return fallbackExpiryUtc > utcNow;
        }

        return false;
    }

    public static bool IsBookingHoldingSlot(TbBooking b, DateTime utcNow, bool includePendingConflicts = true)
    {
        if (b.Status is "approved" or "confirmed" or "playing" or "active")
            return true;

        if (includePendingConflicts && string.Equals(b.Status, "pending", StringComparison.OrdinalIgnoreCase))
            return IsPendingHoldActive(b, utcNow);

        return false;
    }

    public static string? ComputeClientIpHash(string? ipAddress)
    {
        if (string.IsNullOrWhiteSpace(ipAddress)) return null;
        var bytes = System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes($"SPK_IP|{ipAddress.Trim().ToLowerInvariant()}"));
        return Convert.ToHexString(bytes)[..32];
    }

    private static async Task LockCourtForUpdateAsync(PickleballContext context, int courtId)
    {
        if (context.Database.IsRelational())
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT CourtId FROM tb_Court WITH (UPDLOCK, HOLDLOCK) WHERE CourtId = {0}",
                courtId);
        }
    }

    public async Task<(bool HasConflict, string Reason)> CheckSlotConflictAsync(
        PickleballContext context,
        int courtId,
        string dateIso,
        int startMins,
        int endMins,
        int? excludeBookingId = null,
        bool includePendingConflicts = true,
        DateTime? referenceNow = null)
    {
        var locks = await context.TbCourtLocks
            .AsNoTracking()
            .Where(l => l.CourtId == courtId && l.LockDate == dateIso)
            .ToListAsync();

        foreach (var l in locks)
        {
            if (!PricingService.TryParseTimeMinutes(l.StartTime, out int lockStart)) continue;
            int lockEnd = lockStart + Math.Max(15, l.Duration);
            if (RangesOverlap(startMins, endMins, lockStart, lockEnd))
            {
                return (true, $"Sân đang tạm khóa bảo trì ({l.StartTime}–{PricingService.MinutesToTime(lockEnd)}: {l.Reason}).");
            }
        }

        var blockingStatuses = includePendingConflicts
            ? new[] { "pending", "approved", "confirmed", "playing", "active" }
            : new[] { "approved", "confirmed", "playing", "active" };

        var existingBookings = await context.TbBookings
            .AsNoTracking()
            .Where(b => b.CourtId == courtId &&
                        b.BookingDate == dateIso &&
                        blockingStatuses.Contains(b.Status) &&
                        (!excludeBookingId.HasValue || b.BookingId != excludeBookingId.Value))
            .ToListAsync();

        var utcNow = ToUtcFromVietnamNow(referenceNow);

        foreach (var b in existingBookings)
        {
            if (!IsBookingHoldingSlot(b, utcNow, includePendingConflicts))
                continue;

            if (!PricingService.TryParseTimeMinutes(b.StartTime, out int bStart)) continue;
            int bEnd = bStart + Math.Max(30, b.Duration);
            if (RangesOverlap(startMins, endMins, bStart, bEnd))
            {
                return (true, $"Khung giờ này bị trùng với lịch đặt sân hiện có ({b.StartTime}–{PricingService.MinutesToTime(bEnd)}).");
            }
        }

        return (false, string.Empty);
    }

    public async Task<BookingOperationResult> CreateBookingAsync(
        PickleballContext context,
        BookingCreateInput input)
    {
        string cleanName = (input.CustomerName ?? string.Empty).Trim();
        if (cleanName.Length < 2 || cleanName.Length > 120)
        {
            return BookingOperationResult.Fail("Vui lòng nhập họ và tên hợp lệ (2–120 ký tự).");
        }

        if (!IsValidPhone(input.Phone))
        {
            return BookingOperationResult.Fail("Số điện thoại không hợp lệ (gồm 9–11 chữ số, bắt đầu bằng 0 hoặc +84).");
        }
        string cleanPhone = NormalizePhone(input.Phone);

        if (!DateOnly.TryParseExact(input.BookingDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return BookingOperationResult.Fail("Ngày đặt sân không đúng định dạng YYYY-MM-DD.");
        }

        if (!PricingService.TryParseTimeMinutes(input.StartTime, out int startMins))
        {
            return BookingOperationResult.Fail("Giờ bắt đầu không hợp lệ (HH:mm).");
        }

        if (startMins % 30 != 0)
        {
            return BookingOperationResult.Fail("Giờ bắt đầu phải theo mốc 30 phút (ví dụ: 06:00, 06:30, 17:00).");
        }

        if (!AllowedDurations.Contains(input.Duration))
        {
            return BookingOperationResult.Fail("Thời lượng thuê sân không hợp lệ (chỉ chấp nhận 60, 90, 120, 150, 180 hoặc 240 phút).");
        }

        string cleanIdemKey = IdempotencyService.NormalizeKey(input.IdempotencyKey) ?? string.Empty;
        string dateIso = parsedDate.ToString("yyyy-MM-dd");
        string normalizedStartTime = PricingService.MinutesToTime(startMins);
        int clampedPlayers = Math.Clamp(input.Players, 1, 16);

        string requestedMethod = (input.PaymentMethod ?? "cash").Trim().ToLowerInvariant();
        if (requestedMethod == "vnpay" || requestedMethod == "online")
        {
            return BookingOperationResult.Fail(
                "Thanh toán trực tuyến chưa được kích hoạt tài khoản Merchant. Vui lòng chọn Thanh toán tại quầy.", 422);
        }
        string paymentMethod = requestedMethod == "transfer" ? "transfer" : "cash";

        string idemScope = "booking.create";
        string idemActor = IdempotencyService.BuildActorKey(input.AccountId, cleanPhone);
        string idemTarget = $"COURT:{input.CourtId}:{dateIso}:{normalizedStartTime}";
        string idemPayload = $"court={input.CourtId}|date={dateIso}|start={normalizedStartTime}|dur={input.Duration}|players={clampedPlayers}|phone={cleanPhone}|name={cleanName.ToLowerInvariant()}|method={paymentMethod}|walkin={input.AutoApproveWalkIn}";

        if (!string.IsNullOrEmpty(cleanIdemKey))
        {
            var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdemKey, idemScope, idemActor, idemTarget, idemPayload);
            if (idemCheck.Status == IdempotencyStatus.Conflict)
            {
                return BookingOperationResult.Fail(idemCheck.ConflictMessage!, 409, errorCode: IdempotencyService.ConflictErrorCode);
            }
            if (idemCheck.Status == IdempotencyStatus.Replay && idemCheck.ExistingRecord?.ResultEntityId != null)
            {
                var replayed = await context.TbBookings.AsNoTracking()
                    .FirstOrDefaultAsync(b => b.BookingId == idemCheck.ExistingRecord.ResultEntityId.Value);
                if (replayed != null)
                {
                    return BookingOperationResult.Ok(replayed, replay: true);
                }
            }

            var existingByIdem = await context.TbBookings
                .AsNoTracking()
                .FirstOrDefaultAsync(b => b.IdempotencyKey == cleanIdemKey);
            if (existingByIdem != null)
            {
                bool matchesExisting =
                    existingByIdem.CourtId == input.CourtId &&
                    string.Equals(existingByIdem.BookingDate, dateIso, StringComparison.Ordinal) &&
                    string.Equals(existingByIdem.StartTime, normalizedStartTime, StringComparison.Ordinal) &&
                    existingByIdem.Duration == input.Duration &&
                    existingByIdem.AccountId == (input.AccountId.HasValue && input.AccountId.Value > 0 && !input.AutoApproveWalkIn ? input.AccountId : null) &&
                    string.Equals(NormalizePhone(existingByIdem.Phone), cleanPhone, StringComparison.Ordinal);

                if (!matchesExisting)
                {
                    return BookingOperationResult.Fail(
                        "Khóa IdempotencyKey đã được sử dụng cho một đơn đặt sân khác.",
                        409,
                        errorCode: IdempotencyService.ConflictErrorCode);
                }
                return BookingOperationResult.Ok(existingByIdem, replay: true);
            }
        }

        var settings = await _settingsService.GetSettingsAsync(context);
        int endMins = startMins + input.Duration;
        if (startMins < settings.OpenMinutes || endMins > settings.CloseMinutes)
        {
            return BookingOperationResult.Fail(
                $"Khung giờ đặt sân phải nằm trong giờ hoạt động của CLB ({settings.OpenTime} – {settings.CloseTime}).", 422);
        }

        var vnNow = input.ReferenceNow ?? GetVietnamNow();
        var utcNow = ToUtcFromVietnamNow(vnNow);
        var today = DateOnly.FromDateTime(vnNow);
        if (parsedDate < today)
        {
            return BookingOperationResult.Fail("Không thể đặt sân cho ngày trong quá khứ.", 422);
        }

        if (parsedDate > today.AddDays(60))
        {
            return BookingOperationResult.Fail("Chỉ hỗ trợ đặt lịch trước tối đa 60 ngày.", 422);
        }

        int nowMins = vnNow.Hour * 60 + vnNow.Minute;
        int pastGraceMins = input.AutoApproveWalkIn ? 15 : 0;
        if (parsedDate == today && startMins + pastGraceMins < nowMins)
        {
            return BookingOperationResult.Fail("Khung giờ bắt đầu đã qua so với thời điểm hiện tại. Vui lòng chọn khung giờ khác.", 422);
        }

        bool isRelational = context.Database.IsRelational();
        await using var transaction = isRelational
            ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable)
            : null;

        try
        {
            await LockCourtForUpdateAsync(context, input.CourtId);

            if (!string.IsNullOrEmpty(cleanIdemKey))
            {
                var txIdem = await IdempotencyService.CheckAsync(context, cleanIdemKey, idemScope, idemActor, idemTarget, idemPayload);
                if (txIdem.Status == IdempotencyStatus.Conflict)
                {
                    if (transaction != null) await transaction.RollbackAsync();
                    return BookingOperationResult.Fail(txIdem.ConflictMessage!, 409, errorCode: IdempotencyService.ConflictErrorCode);
                }
                if (txIdem.Status == IdempotencyStatus.Replay && txIdem.ExistingRecord?.ResultEntityId != null)
                {
                    var existingReplay = await context.TbBookings.FirstOrDefaultAsync(b => b.BookingId == txIdem.ExistingRecord.ResultEntityId.Value);
                    if (existingReplay != null)
                    {
                        if (transaction != null) await transaction.CommitAsync();
                        return BookingOperationResult.Ok(existingReplay, replay: true);
                    }
                }
            }

            var court = await context.TbCourts.FirstOrDefaultAsync(c => c.CourtId == input.CourtId && c.IsActive);
            if (court == null)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return BookingOperationResult.Fail("Sân thi đấu không tồn tại hoặc đang ngừng phục vụ.", 404);
            }

            // V4-A1: Immediately transition any expired pending holds on this court+date so history is accurate
            var stalePendingOnCourt = await context.TbBookings
                .Where(b => b.CourtId == court.CourtId && b.BookingDate == dateIso && b.Status == "pending")
                .ToListAsync();
            foreach (var stale in stalePendingOnCourt)
            {
                if (!IsPendingHoldActive(stale, utcNow))
                {
                    stale.Status = "expired";
                    stale.OrderStatusId = 5;
                    stale.CancelledAt = vnNow;
                    stale.CancelReason = "Hết thời gian giữ chỗ chờ duyệt (TTL)";
                }
            }

            var (hasConflict, conflictReason) = await CheckSlotConflictAsync(
                context, court.CourtId, dateIso, startMins, endMins, referenceNow: vnNow);

            if (hasConflict)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return BookingOperationResult.Fail(conflictReason, 409, errorCode: "SLOT_CONFLICT");
            }

            // V4-A1: Anti-abuse active pending cap per account / phone / IP
            string? clientIpHash = ComputeClientIpHash(input.IpAddress);
            if (!input.AutoApproveWalkIn)
            {
                var activePendingCandidates = await context.TbBookings
                    .AsNoTracking()
                    .Where(b => b.Status == "pending" &&
                                ((input.AccountId.HasValue && input.AccountId.Value > 0 && b.AccountId == input.AccountId.Value) ||
                                 b.Phone == cleanPhone))
                    .ToListAsync();

                int activePendingCount = activePendingCandidates.Count(b => IsPendingHoldActive(b, utcNow));
                if (activePendingCount >= settings.MaxActivePendingPerActor)
                {
                    if (transaction != null) await transaction.RollbackAsync();
                    return BookingOperationResult.Fail(
                        $"Bạn đang có {activePendingCount} đơn chờ duyệt giữ chỗ tạm thời (tối đa {settings.MaxActivePendingPerActor} đơn cùng lúc). Vui lòng chờ duyệt hoặc hủy đơn cũ trước khi đặt thêm.",
                        429,
                        errorCode: "PENDING_HOLD_LIMIT_EXCEEDED");
                }
            }

            // P1-16 & P1-18: Member discount ONLY for verified logged-in accounts (NEVER self-declared phone!)
            string? explicitTier = null;
            int verifiedSpend = 0;
            bool isAuthenticatedMember = input.AccountId.HasValue && input.AccountId.Value > 0;
            if (isAuthenticatedMember)
            {
                var acc = await context.TbAccounts.AsNoTracking()
                    .FirstOrDefaultAsync(a => a.AccountId == input.AccountId!.Value && a.IsActive);
                if (acc != null)
                {
                    explicitTier = acc.MemberTier;
                    verifiedSpend = await _pricingService.GetVerifiedAccountNetPaidSpendAsync(context, acc.AccountId);
                }
                else
                {
                    isAuthenticatedMember = false;
                }
            }

            var quote = _pricingService.CalculateQuote(
                court,
                normalizedStartTime,
                input.Duration,
                posTotal: 0,
                verifiedPaidSpend: verifiedSpend,
                explicitMemberTier: explicitTier,
                settings: settings,
                isAuthenticatedMember: isAuthenticatedMember);

            if (!string.IsNullOrWhiteSpace(input.ExpectedPricingVersion) &&
                !string.Equals(input.ExpectedPricingVersion.Trim(), quote.PricingVersion, StringComparison.OrdinalIgnoreCase))
            {
                if (transaction != null) await transaction.RollbackAsync();
                return BookingOperationResult.Fail(
                    "Bảng giá hoặc khung giờ của CLB vừa thay đổi. Hệ thống đã cập nhật lại báo giá mới, vui lòng xác nhận lại.",
                    409,
                    quote);
            }

            if (input.ExpectedAmountDue.HasValue && input.ExpectedAmountDue.Value != quote.AmountDue)
            {
                if (transaction != null) await transaction.RollbackAsync();
                return BookingOperationResult.Fail(
                    $"Giá niêm yết của ca đặt sân là {quote.AmountDue:N0} đ (khác với giá tạm tính {input.ExpectedAmountDue.Value:N0} đ trên màn hình). Vui lòng kiểm tra lại báo giá mới.",
                    409,
                    quote);
            }

            string bookingCode = await GenerateUniqueBookingCodeAsync(context);
            var slotStartLocal = parsedDate.ToDateTime(new TimeOnly(startMins / 60, startMins % 60));
            var slotStartUtc = ToUtcFromVietnamNow(slotStartLocal);
            var endOfSlotLocal = parsedDate.ToDateTime(new TimeOnly(Math.Min(23, endMins / 60), endMins % 60)).AddHours(2);
            var expiresUtc = DateTime.SpecifyKind(endOfSlotLocal.AddHours(-7), DateTimeKind.Utc);
            string qrToken = _qrTokenService.GenerateCheckInToken(bookingCode, dateIso, expiresUtc);

            string? guestRawToken = null;
            string? guestTokenHash = null;
            if (!isAuthenticatedMember || input.AutoApproveWalkIn)
            {
                (guestRawToken, guestTokenHash) = QrTokenService.GenerateGuestAccessToken();
            }

            string initialStatus = input.AutoApproveWalkIn ? "approved" : "pending";
            int initialOrderStatusId = input.AutoApproveWalkIn ? 2 : 1;

            DateTime? holdExpiresAtUtc = null;
            if (initialStatus == "pending")
            {
                var holdByMinutesUtc = utcNow.AddMinutes(settings.PendingHoldMinutes);
                holdExpiresAtUtc = slotStartUtc > utcNow && slotStartUtc < holdByMinutesUtc
                    ? slotStartUtc
                    : holdByMinutesUtc;
            }

            var booking = new TbBooking
            {
                BookingCode = bookingCode,
                CourtId = court.CourtId,
                AccountId = isAuthenticatedMember && !input.AutoApproveWalkIn ? input.AccountId : null,
                CustomerName = cleanName,
                Phone = cleanPhone,
                BookingDate = dateIso,
                StartTime = normalizedStartTime,
                Duration = input.Duration,
                Players = clampedPlayers,
                SkillLevel = string.IsNullOrWhiteSpace(input.SkillLevel) ? "Giao lưu (DUPR 3.0)" : input.SkillLevel.Trim()[..Math.Min(input.SkillLevel.Trim().Length, 100)],
                PaymentMethod = paymentMethod,
                CourtFee = quote.CourtFeeBeforeDiscount,
                DiscountAmount = quote.DiscountAmount,
                PosTotal = 0,
                AmountDue = quote.AmountDue,
                PaidAmount = 0,
                TotalPaid = 0,
                PaymentStatus = "unpaid",
                PosItemsJson = "[]",
                PricingBreakdownJson = quote.ToJson(),
                CheckInToken = qrToken,
                AccessTokenHash = guestTokenHash,
                IdempotencyKey = string.IsNullOrEmpty(cleanIdemKey) ? null : cleanIdemKey,
                HoldExpiresAtUtc = holdExpiresAtUtc,
                ClientIpHash = clientIpHash,
                Status = initialStatus,
                OrderStatusId = initialOrderStatusId,
                CreatedDate = vnNow
            };

            context.TbBookings.Add(booking);
            await context.SaveChangesAsync();

            if (!string.IsNullOrEmpty(cleanIdemKey))
            {
                string reqHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
                context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
                    cleanIdemKey, idemScope, idemActor, idemTarget, reqHash, booking.BookingCode, booking.BookingId));
            }

            context.TbAuditLogs.Add(new TbAuditLog
            {
                Action = input.AutoApproveWalkIn ? "WALKIN_BOOKING_CREATED" : "BOOKING_CREATED",
                EntityType = "Booking",
                EntityId = bookingCode,
                ActorAccountId = input.AccountId,
                Details = $"CourtId={court.CourtId}, Date={dateIso}, Start={booking.StartTime}, Dur={booking.Duration}, Due={booking.AmountDue}, HoldUntilUtc={holdExpiresAtUtc:O}",
                IpAddress = input.IpAddress,
                CreatedAt = vnNow
            });

            await context.SaveChangesAsync();
            if (transaction != null)
            {
                await transaction.CommitAsync();
            }

            return BookingOperationResult.Ok(booking, quote, guestRawToken);
        }
        catch (DbUpdateException)
        {
            if (transaction != null) await transaction.RollbackAsync();

            if (!string.IsNullOrEmpty(cleanIdemKey))
            {
                var afterConflict = await IdempotencyService.CheckAsync(context, cleanIdemKey, idemScope, idemActor, idemTarget, idemPayload);
                if (afterConflict.Status == IdempotencyStatus.Conflict)
                {
                    return BookingOperationResult.Fail(afterConflict.ConflictMessage!, 409, errorCode: IdempotencyService.ConflictErrorCode);
                }
                if (afterConflict.Status == IdempotencyStatus.Replay && afterConflict.ExistingRecord?.ResultEntityId != null)
                {
                    var replayed = await context.TbBookings.AsNoTracking()
                        .FirstOrDefaultAsync(b => b.BookingId == afterConflict.ExistingRecord.ResultEntityId.Value);
                    if (replayed != null)
                        return BookingOperationResult.Ok(replayed, replay: true);
                }
            }

            return BookingOperationResult.Fail("Khung giờ này vừa được người chơi khác đặt thành công. Vui lòng chọn giờ khác!", 409);
        }
    }

    public async Task<(bool Success, int StatusCode, string? ErrorMessage, TbCourtLock? Lock)> CreateCourtLockAsync(
        PickleballContext context,
        int courtId,
        string lockDate,
        string startTime,
        int duration,
        string? reason,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null,
        DateTime? referenceNow = null)
    {
        if (!DateOnly.TryParseExact(lockDate?.Trim(), "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsedDate))
        {
            return (false, 400, "Ngày áp dụng khóa sân không hợp lệ.", null);
        }

        if (!PricingService.TryParseTimeMinutes(startTime, out int startMins))
        {
            return (false, 400, "Giờ bắt đầu khóa sân không hợp lệ.", null);
        }

        int validDuration = Math.Clamp(duration > 0 ? duration : 60, 30, 1440);
        int endMins = Math.Min(24 * 60, startMins + validDuration);
        string dateIso = parsedDate.ToString("yyyy-MM-dd");

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        await LockCourtForUpdateAsync(context, courtId);

        var court = await context.TbCourts.FirstOrDefaultAsync(c => c.CourtId == courtId && c.IsActive);
        if (court == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return (false, 404, "Sân thi đấu không tồn tại.", null);
        }

        var (hasConflict, conflictReason) = await CheckSlotConflictAsync(
            context, courtId, dateIso, startMins, endMins, includePendingConflicts: true, referenceNow: referenceNow);

        if (hasConflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return (false, 409, $"Không thể khóa sân do xung đột lịch: {conflictReason}", null);
        }

        var vnNow = GetVietnamNow();
        string lockCode = $"LCK-{Guid.NewGuid().ToString("N")[..6].ToUpperInvariant()}";
        var entity = new TbCourtLock
        {
            LockCode = lockCode,
            CourtId = courtId,
            LockDate = dateIso,
            StartTime = PricingService.MinutesToTime(startMins),
            Duration = endMins - startMins,
            Reason = string.IsNullOrWhiteSpace(reason) ? "Bảo trì định kỳ" : reason.Trim()[..Math.Min(reason.Trim().Length, 250)],
            CreatedDate = vnNow
        };

        context.TbCourtLocks.Add(entity);
        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "COURT_LOCK_CREATED",
            EntityType = "CourtLock",
            EntityId = lockCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"CourtId={courtId}, Date={dateIso}, Start={entity.StartTime}, Dur={entity.Duration}",
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return (true, 200, null, entity);
    }

    public async Task<BookingOperationResult> ApproveBookingAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null,
        DateTime? referenceNow = null)
    {
        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy đơn đặt sân.", 404);
        }

        await LockCourtForUpdateAsync(context, booking.CourtId);
        if (isRelational)
        {
            await context.Entry(booking).ReloadAsync();
        }

        if (booking.Status == "approved")
        {
            if (tx != null) await tx.CommitAsync();
            return BookingOperationResult.Ok(booking, replay: true);
        }

        if (booking.Status != "pending")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Không thể duyệt đơn đang ở trạng thái '{booking.Status}'.", 409);
        }

        var vnNow = referenceNow ?? GetVietnamNow();
        var utcNow = ToUtcFromVietnamNow(vnNow);

        // V4-A1: Reject approving a pending booking whose HoldExpiresAtUtc has already passed!
        if (!IsPendingHoldActive(booking, utcNow))
        {
            booking.Status = "expired";
            booking.OrderStatusId = 5;
            booking.CancelledAt = vnNow;
            booking.CancelReason = "Hết thời gian giữ chỗ chờ duyệt (TTL)";
            context.TbAuditLogs.Add(new TbAuditLog
            {
                Action = "BOOKING_EXPIRED_ON_APPROVE",
                EntityType = "Booking",
                EntityId = booking.BookingCode,
                ActorAccountId = actorAccountId,
                ActorUsername = actorUsername,
                Details = $"HoldExpiresAtUtc={booking.HoldExpiresAtUtc:O}, UtcNow={utcNow:O}",
                IpAddress = ipAddress,
                CreatedAt = vnNow
            });
            await context.SaveChangesAsync();
            if (tx != null) await tx.CommitAsync();
            return BookingOperationResult.Fail(
                "Đơn đặt sân đã hết thời gian giữ chỗ tạm (TTL) nên không thể duyệt.",
                409,
                errorCode: "PENDING_HOLD_EXPIRED");
        }

        PricingService.TryParseTimeMinutes(booking.StartTime, out int startMins);
        var (hasConflict, reason) = await CheckSlotConflictAsync(
            context, booking.CourtId, booking.BookingDate, startMins, startMins + booking.Duration,
            excludeBookingId: booking.BookingId, includePendingConflicts: true, referenceNow: vnNow);

        if (hasConflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail(reason, 409, errorCode: "SLOT_CONFLICT");
        }

        booking.Status = "approved";
        booking.OrderStatusId = 2;
        if (string.IsNullOrWhiteSpace(booking.CheckInToken))
        {
            booking.CheckInToken = _qrTokenService.GenerateCheckInToken(
                booking.BookingCode, booking.BookingDate, DateTime.UtcNow.AddDays(30));
        }

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_APPROVED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking);
    }

    public async Task<BookingOperationResult> CheckInBookingAsync(
        PickleballContext context,
        string codeOrQrToken,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null,
        DateTime? referenceNow = null)
    {
        if (string.IsNullOrWhiteSpace(codeOrQrToken))
        {
            return BookingOperationResult.Fail("Vui lòng nhập mã vé hoặc quét mã QR.", 400);
        }

        string trimmed = codeOrQrToken.Trim();
        string lookupCode = trimmed;

        if (trimmed.StartsWith("SPK1.", StringComparison.Ordinal))
        {
            if (!_qrTokenService.TryValidateCheckInToken(trimmed, out string tokenCode, out _, out string tokenErr))
            {
                return BookingOperationResult.Fail(tokenErr, 400);
            }
            lookupCode = tokenCode;
        }

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var booking = await FindBookingAsync(context, lookupCode);
        if (booking == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy đơn đặt sân khớp với mã này.", 404);
        }

        await LockCourtForUpdateAsync(context, booking.CourtId);

        if (trimmed.StartsWith("SPK1.", StringComparison.Ordinal) &&
            !string.IsNullOrEmpty(booking.CheckInToken) &&
            !string.Equals(booking.CheckInToken, trimmed, StringComparison.Ordinal))
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Mã QR đã bị thu hồi hoặc không khớp với vé trên hệ thống.", 400);
        }

        // P1-07: Replay protection
        if (booking.Status == "playing")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Vé {booking.BookingCode} đã được Check-in nhận sân trước đó (từ chối quét lặp lại).", 409);
        }

        // P1-07: Strictly reject pending bookings!
        if (booking.Status == "pending")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Vé {booking.BookingCode} đang ở trạng thái Chờ duyệt (pending). Nhân viên cần duyệt vé trước khi Check-in!", 409);
        }

        if (booking.Status is "rejected" or "cancelled")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Đơn đặt sân này đã bị hủy, không thể Check-in.", 409);
        }

        if (booking.Status is "expired" or "no_show")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Đơn đặt sân này đã quá hạn ({booking.Status}), không thể Check-in.", 409);
        }

        if (booking.Status == "completed")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Ca thi đấu của mã vé này đã kết thúc.", 409);
        }

        if (booking.Status != "approved" && booking.Status != "confirmed")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Trạng thái vé '{booking.Status}' không hợp lệ để Check-in.", 409);
        }

        // Validate check-in date & window
        var settings = await _settingsService.GetSettingsAsync(context);
        var vnNow = referenceNow ?? GetVietnamNow();
        string todayIso = vnNow.ToString("yyyy-MM-dd");

        if (!string.Equals(booking.BookingDate, todayIso, StringComparison.Ordinal))
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail(
                $"Vé này có ngày thi đấu là {booking.BookingDate} (hôm nay là {todayIso}), chưa đến hoặc đã qua ngày nhận sân.", 422);
        }

        if (PricingService.TryParseTimeMinutes(booking.StartTime, out int startMins))
        {
            int endMins = startMins + Math.Max(30, booking.Duration);
            int nowMins = vnNow.Hour * 60 + vnNow.Minute;
            int earliestCheckIn = startMins - settings.CheckInEarlyMinutes;

            if (nowMins < earliestCheckIn)
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail(
                    $"Chưa tới khung giờ nhận sân. CLB hỗ trợ Check-in tối đa {settings.CheckInEarlyMinutes} phút trước giờ bắt đầu ({booking.StartTime}).", 422);
            }

            if (nowMins >= endMins)
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail(
                    $"Khung giờ của vé ({booking.StartTime}–{PricingService.MinutesToTime(endMins)}) đã kết thúc, không thể Check-in.", 422);
            }
        }

        booking.Status = "playing";
        booking.OrderStatusId = 3;
        booking.CheckedInAt = vnNow;

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_CHECKED_IN",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking);
    }

    public async Task<BookingOperationResult> CancelBookingAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int? actorAccountId,
        int? actorRoleId,
        string? guestAccessToken,
        string? reason,
        string? ipAddress = null,
        DateTime? referenceNow = null,
        string? idempotencyKey = null,
        string? actorUsername = null)
    {
        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            return BookingOperationResult.Fail("Không tìm thấy đơn đặt sân.", 404);
        }

        string cleanIdem = IdempotencyService.NormalizeKey(idempotencyKey) ?? string.Empty;
        string idemScope = "booking.cancel";
        string idemActor = IdempotencyService.BuildActorKey(actorAccountId, guestAccessToken);
        string idemTarget = $"BOOKING:{booking.BookingCode}";
        string idemPayload = $"code={booking.BookingCode}|reason={(reason ?? "").Trim().ToLowerInvariant()}";

        if (!string.IsNullOrEmpty(cleanIdem))
        {
            var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
            if (idemCheck.Status == IdempotencyStatus.Conflict)
            {
                return BookingOperationResult.Fail(idemCheck.ConflictMessage!, 409, errorCode: IdempotencyService.ConflictErrorCode);
            }
            if (idemCheck.Status == IdempotencyStatus.Replay)
            {
                return BookingOperationResult.Ok(booking, replay: true);
            }
        }

        bool isStaffOrAdmin = actorRoleId == 1 || actorRoleId == 3;
        if (!isStaffOrAdmin)
        {
            bool ownsByAccount = actorAccountId.HasValue && booking.AccountId.HasValue && booking.AccountId.Value == actorAccountId.Value;
            bool ownsByGuestToken = !string.IsNullOrWhiteSpace(guestAccessToken) &&
                                    QrTokenService.VerifyGuestAccessToken(guestAccessToken, booking.AccessTokenHash);

            if (!ownsByAccount && !ownsByGuestToken)
            {
                return BookingOperationResult.Fail("Bạn không có quyền hủy đơn đặt sân này (yêu cầu đăng nhập đúng tài khoản chủ vé hoặc cung cấp Mã bảo mật vé hợp lệ).", 403);
            }

            if (booking.Status is "cancelled" or "rejected")
            {
                return BookingOperationResult.Ok(booking, replay: true);
            }

            if (booking.Status != "pending" && booking.Status != "approved")
            {
                return BookingOperationResult.Fail("Chỉ có thể hủy lịch khi đơn đang Chờ duyệt hoặc Đã duyệt (trước giờ nhận sân).", 409);
            }

            if (booking.PaidAmount > 0)
            {
                return BookingOperationResult.Fail("Đơn đặt sân đã phát sinh thanh toán. Vui lòng liên hệ Quầy lễ tân để hoàn tiền và hủy lịch.", 409, errorCode: "BOOKING_HAS_PAYMENT");
            }

            var settings = await _settingsService.GetSettingsAsync(context);
            if (booking.Status == "approved" &&
                DateOnly.TryParseExact(booking.BookingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bDate) &&
                PricingService.TryParseTimeMinutes(booking.StartTime, out int sMins))
            {
                var startDateTime = bDate.ToDateTime(new TimeOnly(sMins / 60, sMins % 60));
                var vnNow = referenceNow ?? GetVietnamNow();
                if ((startDateTime - vnNow).TotalMinutes < settings.CancelCutoffMinutes)
                {
                    return BookingOperationResult.Fail(
                        $"Lịch đã duyệt chỉ được tự hủy trực tuyến tối thiểu {settings.CancelCutoffMinutes / 60.0:0.#} tiếng trước giờ thi đấu. Vui lòng liên hệ Hotline CLB.", 409);
                }
            }
        }
        else
        {
            if (booking.Status is "cancelled" or "rejected")
            {
                return BookingOperationResult.Ok(booking, replay: true);
            }
            if (booking.Status == "completed")
            {
                return BookingOperationResult.Fail("Không thể hủy ca thi đấu đã hoàn tất.", 409);
            }
            if (booking.PaidAmount > 0)
            {
                return BookingOperationResult.Fail("Đơn đã thu tiền. Vui lòng thực hiện giao dịch Hoàn tiền (Refund) trong sổ quỹ trước khi hủy đơn.", 409, errorCode: "BOOKING_HAS_PAYMENT");
            }
        }

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        if (isRelational)
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT BookingId FROM tb_Booking WITH (UPDLOCK, HOLDLOCK) WHERE BookingId = {0}",
                booking.BookingId);
            await context.Entry(booking).ReloadAsync();
        }

        if (booking.Status is "cancelled" or "rejected")
        {
            if (tx != null) await tx.CommitAsync();
            return BookingOperationResult.Ok(booking, replay: true);
        }

        // V4-A4: Load all linked Pro Shop orders (TbOrder where BookingId == booking.BookingId)
        var linkedOrders = await context.TbOrders
            .Include(o => o.TbOrderDetails)
            .Where(o => o.BookingId == booking.BookingId && o.Status != "cancelled" && o.Status != "rejected")
            .ToListAsync();

        foreach (var lo in linkedOrders)
        {
            if (lo.PaidAmount > 0 || lo.PaymentStatus is "paid" or "partial")
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail(
                    $"Đơn Pro Shop đính kèm ({lo.Code}) đã thu tiền ({lo.PaidAmount:N0} đ). Vui lòng hoàn tiền đơn Pro Shop trước khi hủy đặt sân.",
                    409,
                    errorCode: "LINKED_ORDER_PAID");
            }

            if (string.Equals(lo.FulfillmentStatus, "delivered", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lo.Status, "completed", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(lo.Status, "delivered", StringComparison.OrdinalIgnoreCase))
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail(
                    $"Đơn Pro Shop đính kèm ({lo.Code}) đã giao hàng thực tế cho khách. Vui lòng thu hồi hàng hóa / xử lý đơn Pro Shop trước khi hủy sân.",
                    409,
                    errorCode: "LINKED_ORDER_DELIVERED");
            }
        }

        // V4-A4: Compute exact restock per ProductId without double-restocking between TbOrderDetails and PosItemsJson!
        var linkedOrderQtyByProduct = new Dictionary<int, int>();
        var untaggedLinkedOrderQtyByProduct = new Dictionary<int, int>();
        var posItems = ParsePosItems(booking.PosItemsJson);
        var taggedOrderIdsInPos = posItems.Where(i => i.OrderId.HasValue).Select(i => i.OrderId!.Value).ToHashSet();

        foreach (var lo in linkedOrders)
        {
            bool isTaggedInPos = taggedOrderIdsInPos.Contains(lo.OrderId);
            foreach (var detail in lo.TbOrderDetails)
            {
                if (!detail.ProductId.HasValue || (detail.Quantity ?? 0) <= 0) continue;
                int pid = detail.ProductId.Value;
                int qty = detail.Quantity!.Value;
                linkedOrderQtyByProduct[pid] = linkedOrderQtyByProduct.GetValueOrDefault(pid) + qty;
                if (!isTaggedInPos)
                {
                    untaggedLinkedOrderQtyByProduct[pid] = untaggedLinkedOrderQtyByProduct.GetValueOrDefault(pid) + qty;
                }
            }
        }

        var directPosQtyByProduct = new Dictionary<int, int>();
        foreach (var item in posItems.Where(i => i.Qty > 0 && !i.OrderId.HasValue))
        {
            directPosQtyByProduct[item.ProductId] = directPosQtyByProduct.GetValueOrDefault(item.ProductId) + item.Qty;
        }

        // Subtract any legacy untagged linked-order quantities from directPosQtyByProduct so legacy rows never double-restock
        foreach (var kv in untaggedLinkedOrderQtyByProduct)
        {
            if (directPosQtyByProduct.TryGetValue(kv.Key, out int curDirect))
            {
                directPosQtyByProduct[kv.Key] = Math.Max(0, curDirect - kv.Value);
            }
        }

        var allAffectedProductIds = linkedOrderQtyByProduct.Keys
            .Union(directPosQtyByProduct.Where(kv => kv.Value > 0).Select(kv => kv.Key))
            .OrderBy(id => id)
            .ToList();

        var vnNowCancel = referenceNow ?? GetVietnamNow();

        // Lock products in deterministic ascending ProductId order
        foreach (var pid in allAffectedProductIds)
        {
            if (isRelational)
            {
                await context.Database.ExecuteSqlRawAsync(
                    "SELECT ProductId FROM tb_Product WITH (UPDLOCK, HOLDLOCK) WHERE ProductId = {0}",
                    pid);
            }
        }

        // 1. Cancel all linked TbOrders and restock their TbOrderDetails
        foreach (var lo in linkedOrders)
        {
            foreach (var detail in lo.TbOrderDetails)
            {
                if (!detail.ProductId.HasValue || (detail.Quantity ?? 0) <= 0) continue;
                int pid = detail.ProductId.Value;
                int qty = detail.Quantity!.Value;
                var prod = await context.TbProducts.FirstOrDefaultAsync(p => p.ProductId == pid);
                if (prod != null)
                {
                    int newStock = (prod.UnitInStock ?? prod.Quantity ?? 0) + qty;
                    prod.UnitInStock = newStock;
                    prod.Quantity = newStock;
                    context.TbInventoryMovements.Add(new TbInventoryMovement
                    {
                        ProductId = prod.ProductId,
                        OrderId = lo.OrderId,
                        BookingId = booking.BookingId,
                        ChangeQty = qty,
                        BalanceAfter = newStock,
                        Reason = $"Restock linked order {lo.Code} on cancel booking {booking.BookingCode}",
                        ActorAccountId = actorAccountId,
                        CreatedAt = vnNowCancel
                    });
                }
            }

            lo.Status = "cancelled";
            lo.FulfillmentStatus = "cancelled";
            lo.OrderStatusId = 5;
            lo.ModifiedDate = vnNowCancel;
            lo.ModifiedBy = actorUsername ?? (isStaffOrAdmin ? "staff" : "customer");

            context.TbAuditLogs.Add(new TbAuditLog
            {
                Action = "ORDER_CANCELLED_WITH_BOOKING",
                EntityType = "Order",
                EntityId = lo.Code ?? lo.OrderId.ToString(),
                ActorAccountId = actorAccountId,
                ActorUsername = actorUsername,
                Details = $"Cancelled atomically with booking {booking.BookingCode}",
                IpAddress = ipAddress,
                CreatedAt = vnNowCancel
            });
        }

        // 2. Restock any standalone direct POS items (OrderId == null)
        foreach (var kv in directPosQtyByProduct.Where(k => k.Value > 0))
        {
            var prod = await context.TbProducts.FirstOrDefaultAsync(p => p.ProductId == kv.Key);
            if (prod != null)
            {
                int newStock = (prod.UnitInStock ?? prod.Quantity ?? 0) + kv.Value;
                prod.UnitInStock = newStock;
                prod.Quantity = newStock;
                context.TbInventoryMovements.Add(new TbInventoryMovement
                {
                    ProductId = prod.ProductId,
                    BookingId = booking.BookingId,
                    ChangeQty = kv.Value,
                    BalanceAfter = newStock,
                    Reason = $"Restock direct POS on cancel booking {booking.BookingCode}",
                    ActorAccountId = actorAccountId,
                    CreatedAt = vnNowCancel
                });
            }
        }

        booking.PosItemsJson = "[]";
        booking.PosTotal = 0;
        booking.AmountDue = 0;
        booking.AmountDueDec = 0m;

        booking.Status = isStaffOrAdmin && string.Equals(reason, "rejected", StringComparison.OrdinalIgnoreCase) ? "rejected" : "cancelled";
        booking.OrderStatusId = 5;
        booking.CancelledAt = vnNowCancel;
        booking.CancelReason = string.IsNullOrWhiteSpace(reason)
            ? (isStaffOrAdmin ? "Quản trị viên hủy đơn" : "Khách hàng tự hủy lịch")
            : reason.Trim()[..Math.Min(reason.Trim().Length, 300)];

        if (!string.IsNullOrEmpty(cleanIdem))
        {
            string reqHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
            context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
                cleanIdem, idemScope, idemActor, idemTarget, reqHash, booking.BookingCode, booking.BookingId));
        }

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_CANCELLED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"{booking.CancelReason} (LinkedOrdersCancelled={linkedOrders.Count})",
            IpAddress = ipAddress,
            CreatedAt = vnNowCancel
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking);
    }

    public async Task<BookingOperationResult> ExtendBookingDurationAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int deltaMinutes,
        int? actorAccountId,
        string? actorUsername,
        string? idempotencyKey = null,
        DateTime? referenceNow = null)
    {
        if (deltaMinutes == 0 || deltaMinutes % 15 != 0)
        {
            return BookingOperationResult.Fail("Thời gian điều chỉnh phải là bội số của 15 phút.");
        }

        var settings = await _settingsService.GetSettingsAsync(context);
        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy ca đặt sân.", 404);
        }

        await LockCourtForUpdateAsync(context, booking.CourtId);

        string cleanIdem = IdempotencyService.NormalizeKey(idempotencyKey) ?? string.Empty;
        string idemScope = "booking.extend";
        string idemActor = IdempotencyService.BuildActorKey(actorAccountId, actorUsername);
        string idemTarget = $"BOOKING:{booking.BookingCode}";
        string idemPayload = $"code={booking.BookingCode}|delta={deltaMinutes}";

        if (!string.IsNullOrEmpty(cleanIdem))
        {
            var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
            if (idemCheck.Status == IdempotencyStatus.Conflict)
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail(idemCheck.ConflictMessage!, 409, errorCode: IdempotencyService.ConflictErrorCode);
            }
            if (idemCheck.Status == IdempotencyStatus.Replay)
            {
                if (tx != null) await tx.CommitAsync();
                return BookingOperationResult.Ok(booking, replay: true);
            }
        }

        if (booking.Status != "playing" && booking.Status != "approved")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Chỉ có thể gia hạn thời lượng cho ca Đã duyệt hoặc Đang thi đấu.", 409);
        }

        int newDuration = booking.Duration + deltaMinutes;
        if (newDuration < 30 || newDuration > 360)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Thời lượng thuê sân phải nằm trong khoảng 30 – 360 phút.", 422);
        }

        PricingService.TryParseTimeMinutes(booking.StartTime, out int startMins);
        int newEndMins = startMins + newDuration;
        if (newEndMins > settings.CloseMinutes)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Gia hạn vượt quá giờ đóng cửa của CLB ({settings.CloseTime}).", 422);
        }

        if (deltaMinutes > 0)
        {
            var (hasConflict, reason) = await CheckSlotConflictAsync(
                context, booking.CourtId, booking.BookingDate, startMins, newEndMins, excludeBookingId: booking.BookingId, referenceNow: referenceNow);
            if (hasConflict)
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail($"Không thể gia hạn thêm giờ: {reason}", 409);
            }
        }

        var court = await context.TbCourts.FindAsync(booking.CourtId);
        if (court == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy thông tin sân.", 404);
        }

        bool isAuthMember = booking.AccountId.HasValue && booking.AccountId.Value > 0;
        int verifiedSpend = isAuthMember
            ? await _pricingService.GetVerifiedAccountNetPaidSpendAsync(context, booking.AccountId)
            : 0;
        string? explicitTier = null;
        if (isAuthMember)
        {
            var acc = await context.TbAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == booking.AccountId!.Value);
            explicitTier = acc?.MemberTier;
        }

        var quote = _pricingService.CalculateQuote(
            court, booking.StartTime, newDuration, booking.PosTotal, verifiedSpend, explicitTier, settings, isAuthMember);

        if (quote.AmountDue < booking.PaidAmount)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không thể rút ngắn thời lượng làm tổng tiền nhỏ hơn số tiền đã thu.", 409);
        }

        booking.Duration = newDuration;
        booking.CourtFee = quote.CourtFeeBeforeDiscount;
        booking.DiscountAmount = quote.DiscountAmount;
        booking.AmountDue = quote.AmountDue;
        booking.PricingBreakdownJson = quote.ToJson();
        booking.PaymentStatus = ComputePaymentStatus(booking.PaidAmount, booking.AmountDue);

        if (!string.IsNullOrEmpty(cleanIdem))
        {
            string reqHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
            context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
                cleanIdem, idemScope, idemActor, idemTarget, reqHash, booking.BookingCode, booking.BookingId));
        }

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_DURATION_UPDATED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"NewDuration={newDuration}, NewAmountDue={booking.AmountDue}",
            CreatedAt = GetVietnamNow()
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking, quote);
    }

    public async Task<BookingOperationResult> MoveBookingCourtAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int targetCourtId,
        int? actorAccountId,
        string? actorUsername)
    {
        var settings = await _settingsService.GetSettingsAsync(context);
        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy đơn đặt sân.", 404);
        }

        int firstLock = Math.Min(booking.CourtId, targetCourtId);
        int secondLock = Math.Max(booking.CourtId, targetCourtId);
        await LockCourtForUpdateAsync(context, firstLock);
        if (secondLock != firstLock)
        {
            await LockCourtForUpdateAsync(context, secondLock);
        }

        var targetCourt = await context.TbCourts.FirstOrDefaultAsync(c => c.CourtId == targetCourtId && c.IsActive);
        if (targetCourt == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Sân chuyển tới không tồn tại hoặc đang ngừng phục vụ.", 404);
        }

        PricingService.TryParseTimeMinutes(booking.StartTime, out int startMins);
        var (hasConflict, reason) = await CheckSlotConflictAsync(
            context, targetCourtId, booking.BookingDate, startMins, startMins + booking.Duration, excludeBookingId: booking.BookingId);
        if (hasConflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Sân chuyển tới đang bận: {reason}", 409);
        }

        bool isAuthMember = booking.AccountId.HasValue && booking.AccountId.Value > 0;
        int verifiedSpend = isAuthMember
            ? await _pricingService.GetVerifiedAccountNetPaidSpendAsync(context, booking.AccountId)
            : 0;
        string? explicitTier = null;
        if (isAuthMember)
        {
            var acc = await context.TbAccounts.AsNoTracking().FirstOrDefaultAsync(a => a.AccountId == booking.AccountId!.Value);
            explicitTier = acc?.MemberTier;
        }

        var quote = _pricingService.CalculateQuote(
            targetCourt, booking.StartTime, booking.Duration, booking.PosTotal, verifiedSpend, explicitTier, settings, isAuthMember);

        booking.CourtId = targetCourtId;
        booking.CourtFee = quote.CourtFeeBeforeDiscount;
        booking.DiscountAmount = quote.DiscountAmount;
        booking.AmountDue = quote.AmountDue;
        booking.PricingBreakdownJson = quote.ToJson();
        booking.PaymentStatus = ComputePaymentStatus(booking.PaidAmount, booking.AmountDue);

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_COURT_MOVED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"MovedToCourtId={targetCourtId}, NewAmountDue={booking.AmountDue}",
            CreatedAt = GetVietnamNow()
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking, quote);
    }

    public async Task<BookingOperationResult> AddPosItemToBookingAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int productId,
        int quantityDelta,
        int? actorAccountId,
        string? actorUsername)
    {
        if (quantityDelta == 0)
        {
            return BookingOperationResult.Fail("Số lượng thay đổi không hợp lệ.");
        }

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted) : null;

        if (isRelational)
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM tb_Booking WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE BookingCode = {0}",
                bookingCodeOrId.Trim().ToUpperInvariant());
            await context.Database.ExecuteSqlRawAsync(
                "SELECT 1 FROM tb_Product WITH (UPDLOCK, ROWLOCK, HOLDLOCK) WHERE ProductId = {0}",
                productId);
        }

        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy ca thi đấu.", 404);
        }

        if (booking.Status != "playing" && booking.Status != "approved")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Chỉ có thể cập nhật món POS cho ca đang thi đấu hoặc đã duyệt.", 409);
        }

        var product = await context.TbProducts.FirstOrDefaultAsync(p => p.ProductId == productId && p.IsActive);
        if (product == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Mặt hàng không tồn tại hoặc đã ngừng kinh doanh.", 404);
        }

        var items = ParsePosItems(booking.PosItemsJson);
        var existing = items.FirstOrDefault(i => i.ProductId == productId && !i.OrderId.HasValue);
        int currentCartQty = existing?.Qty ?? 0;
        int newCartQty = currentCartQty + quantityDelta;

        if (newCartQty < 0)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Số lượng món trong hóa đơn không thể nhỏ hơn 0.");
        }

        int balanceAfter;
        if (quantityDelta > 0)
        {
            if (isRelational)
            {
                int rows = await context.Database.ExecuteSqlRawAsync(@"
UPDATE tb_Product
SET UnitInStock = COALESCE(UnitInStock, Quantity, 0) - {1},
    Quantity = COALESCE(UnitInStock, Quantity, 0) - {1}
WHERE ProductId = {0} AND IsActive = 1 AND COALESCE(UnitInStock, Quantity, 0) >= {1}",
                    productId, quantityDelta);

                if (rows == 0)
                {
                    if (tx != null) await tx.RollbackAsync();
                    return BookingOperationResult.Fail($"Mặt hàng '{product.Title}' không đủ tồn kho.", 409);
                }

                await context.Entry(product).ReloadAsync();
                balanceAfter = product.UnitInStock ?? product.Quantity ?? 0;
            }
            else
            {
                int currentStock = product.UnitInStock ?? product.Quantity ?? 0;
                if (currentStock < quantityDelta)
                {
                    return BookingOperationResult.Fail($"Mặt hàng '{product.Title}' chỉ còn {currentStock} sản phẩm trong kho.", 409);
                }
                balanceAfter = currentStock - quantityDelta;
                product.UnitInStock = balanceAfter;
                product.Quantity = balanceAfter;
            }
        }
        else
        {
            int restockQty = -quantityDelta;
            if (isRelational)
            {
                await context.Database.ExecuteSqlRawAsync(@"
UPDATE tb_Product
SET UnitInStock = COALESCE(UnitInStock, Quantity, 0) + {1},
    Quantity = COALESCE(UnitInStock, Quantity, 0) + {1}
WHERE ProductId = {0}",
                    productId, restockQty);
                await context.Entry(product).ReloadAsync();
                balanceAfter = product.UnitInStock ?? product.Quantity ?? 0;
            }
            else
            {
                balanceAfter = (product.UnitInStock ?? product.Quantity ?? 0) + restockQty;
                product.UnitInStock = balanceAfter;
                product.Quantity = balanceAfter;
            }
        }

        int serverUnitPrice = product.Price ?? 0;
        if (newCartQty == 0)
        {
            if (existing != null) items.Remove(existing);
        }
        else if (existing != null)
        {
            existing.Qty = newCartQty;
            existing.Price = serverUnitPrice;
            existing.Name = product.Title ?? existing.Name;
        }
        else
        {
            items.Add(new PosCartItemDto
            {
                ProductId = product.ProductId,
                Name = product.Title ?? "Dịch vụ POS",
                Price = serverUnitPrice,
                Qty = newCartQty
            });
        }

        int posTotal = items.Sum(i => i.Price * i.Qty);
        int newAmountDue = Math.Max(0, booking.CourtFee - booking.DiscountAmount + posTotal);
        if (newAmountDue < booking.PaidAmount)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không thể bớt món làm tổng phải thu thấp hơn số tiền đã thanh toán.", 409);
        }

        booking.PosItemsJson = JsonSerializer.Serialize(items);
        booking.PosTotal = posTotal;
        booking.AmountDue = newAmountDue;
        booking.PaymentStatus = ComputePaymentStatus(booking.PaidAmount, booking.AmountDue);

        var vnNow = GetVietnamNow();
        context.TbInventoryMovements.Add(new TbInventoryMovement
        {
            ProductId = product.ProductId,
            BookingId = booking.BookingId,
            ChangeQty = -quantityDelta,
            BalanceAfter = balanceAfter,
            Reason = quantityDelta > 0 ? $"POS add to {booking.BookingCode}" : $"POS return from {booking.BookingCode}",
            ActorAccountId = actorAccountId,
            CreatedAt = vnNow
        });

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "POS_ITEM_UPDATED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"ProductId={productId}, Delta={quantityDelta}, PosTotal={posTotal}",
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking);
    }

    /// <summary>
    /// P1-04 & P1-05: Closes an active court session ONLY. NEVER records payment or modifies PaidAmount!
    /// </summary>
    public async Task<BookingOperationResult> FinishBookingSessionAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null)
    {
        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            return BookingOperationResult.Fail("Không tìm thấy đơn đặt sân.", 404);
        }

        if (booking.Status == "completed")
        {
            return BookingOperationResult.Ok(booking, replay: true);
        }

        if (booking.Status != "playing" && booking.Status != "active")
        {
            return BookingOperationResult.Fail(
                $"Chỉ có thể đóng ca/kết thúc cho sân đang thi đấu (trạng thái hiện tại: '{booking.Status}').", 409);
        }

        var vnNow = GetVietnamNow();
        booking.Status = "completed";
        booking.OrderStatusId = 4;
        booking.CompletedAt = vnNow;
        // PaidAmount & PaymentStatus remain strictly untouched by finishing the session!

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_SESSION_FINISHED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"AmountDue={booking.AmountDue}, PaidAmount={booking.PaidAmount}, PaymentStatus={booking.PaymentStatus}",
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        return BookingOperationResult.Ok(booking);
    }

    /// <summary>
    /// P1-04 & P1-05: Records an explicit, idempotent staff-confirmed payment in TbPaymentTransaction and recomputes PaidAmount from ledger.
    /// </summary>
    public async Task<BookingOperationResult> RecordBookingPaymentAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int amount,
        string paymentMethod,
        string? referenceCode,
        string? note,
        string? idempotencyKey,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null)
    {
        if (amount <= 0)
        {
            return BookingOperationResult.Fail("Số tiền thu thực tế phải lớn hơn 0 VNĐ.", 400);
        }

        string cleanMethod = (paymentMethod ?? string.Empty).Trim().ToLowerInvariant();
        if (cleanMethod is "vnpay" or "online")
        {
            return BookingOperationResult.Fail("Thanh toán trực tuyến chưa kích hoạt Merchant. Chỉ ghi nhận Tiền mặt (cash) hoặc Chuyển khoản (transfer) tại quầy.", 422);
        }
        if (cleanMethod != "cash" && cleanMethod != "transfer")
        {
            return BookingOperationResult.Fail("Phương thức thanh toán không hợp lệ (chỉ hỗ trợ 'cash' hoặc 'transfer').", 400);
        }

        string? cleanRef = string.IsNullOrWhiteSpace(referenceCode)
            ? null
            : referenceCode.Trim()[..Math.Min(referenceCode.Trim().Length, 100)];

        if (cleanMethod == "transfer" && (cleanRef == null || cleanRef.Length < 3))
        {
            return BookingOperationResult.Fail("Vui lòng nhập mã giao dịch / mã tham chiếu ngân hàng (tối thiểu 3 ký tự) khi xác nhận Chuyển khoản.", 400);
        }

        string cleanIdem = string.IsNullOrWhiteSpace(idempotencyKey)
            ? string.Empty
            : idempotencyKey.Trim()[..Math.Min(idempotencyKey.Trim().Length, 100)];

        if (string.IsNullOrEmpty(cleanIdem))
        {
            return BookingOperationResult.Fail("Thiếu khóa chống trùng giao dịch (IdempotencyKey).", 400);
        }

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy đơn đặt sân.", 404);
        }

        string idemScope = "booking.payment";
        string idemActor = IdempotencyService.BuildActorKey(actorAccountId, actorUsername);
        string idemTarget = $"BOOKING:{booking.BookingCode}";
        string idemPayload = $"code={booking.BookingCode}|amount={amount}|method={cleanMethod}|ref={(cleanRef ?? "").ToUpperInvariant()}";

        var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
        if (idemCheck.Status == IdempotencyStatus.Conflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail(idemCheck.ConflictMessage!, 409, errorCode: IdempotencyService.ConflictErrorCode);
        }

        var existingTx = await context.TbPaymentTransactions
            .FirstOrDefaultAsync(t => t.IdempotencyKey == cleanIdem);
        if (existingTx != null)
        {
            bool matchesTx =
                existingTx.BookingId == booking.BookingId &&
                string.Equals(existingTx.TransactionType, "payment", StringComparison.OrdinalIgnoreCase) &&
                existingTx.Amount == amount &&
                string.Equals(existingTx.PaymentMethod, cleanMethod, StringComparison.OrdinalIgnoreCase) &&
                (!actorAccountId.HasValue || !existingTx.ActorAccountId.HasValue || existingTx.ActorAccountId == actorAccountId);

            if (!matchesTx)
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail(
                    "Khóa IdempotencyKey đã được sử dụng cho một giao dịch thu tiền khác (khác đơn, số tiền hoặc phương thức).",
                    409,
                    errorCode: IdempotencyService.ConflictErrorCode);
            }

            if (tx != null) await tx.CommitAsync();
            return BookingOperationResult.Ok(booking, tx: existingTx, replay: true);
        }

        if (idemCheck.Status == IdempotencyStatus.Replay && idemCheck.ExistingRecord?.ResultEntityId != null)
        {
            var replayedTx = await context.TbPaymentTransactions.FirstOrDefaultAsync(t => t.TransactionId == idemCheck.ExistingRecord.ResultEntityId.Value);
            if (tx != null) await tx.CommitAsync();
            return BookingOperationResult.Ok(booking, tx: replayedTx, replay: true);
        }

        if (isRelational)
        {
            await context.Database.ExecuteSqlRawAsync(
                "SELECT BookingId FROM tb_Booking WITH (UPDLOCK, HOLDLOCK) WHERE BookingId = {0}",
                booking.BookingId);
        }

        if (booking.Status is "cancelled" or "rejected" or "expired")
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Không thể thu tiền cho đơn đã hủy/hết hạn ({booking.Status}).", 409);
        }

        int amountDue = booking.AmountDue > 0
            ? booking.AmountDue
            : Math.Max(0, booking.CourtFee - booking.DiscountAmount + booking.PosTotal);
        booking.AmountDue = amountDue;

        int currentLedgerNet = await ComputeBookingLedgerNetPaidAsync(context, booking.BookingId);
        int remaining = Math.Max(0, amountDue - currentLedgerNet);

        if (remaining == 0)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Đơn đặt sân này đã thanh toán đủ 100%, không thể thu vượt số tiền phải thu.", 409);
        }

        if (amount > remaining)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail(
                $"Số tiền nhập ({amount:N0} đ) vượt quá số tiền còn phải thu ({remaining:N0} đ). Vui lòng nhập đúng số tiền thu vào sổ quỹ.", 422);
        }

        var vnNow = GetVietnamNow();
        var paymentRow = new TbPaymentTransaction
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode,
            Amount = amount,
            Currency = "VND",
            TransactionType = "payment",
            PaymentMethod = cleanMethod,
            Status = "completed",
            IdempotencyKey = cleanIdem,
            ReferenceCode = cleanRef ?? $"CSH-{vnNow:yyyyMMddHHmmss}",
            Note = string.IsNullOrWhiteSpace(note) ? "Thu tiền đặt sân & POS tại quầy" : note.Trim()[..Math.Min(note.Trim().Length, 300)],
            ActorAccountId = actorAccountId,
            RecordedBy = actorUsername,
            CreatedAt = vnNow
        };

        context.TbPaymentTransactions.Add(paymentRow);
        await context.SaveChangesAsync();

        string reqHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
        context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
            cleanIdem, idemScope, idemActor, idemTarget, reqHash, booking.BookingCode, paymentRow.TransactionId));

        int updatedNetPaid = await ComputeBookingLedgerNetPaidAsync(context, booking.BookingId);
        booking.PaidAmount = updatedNetPaid;
        booking.TotalPaid = updatedNetPaid;
        booking.PaymentMethod = cleanMethod;
        booking.PaymentStatus = ComputePaymentStatus(updatedNetPaid, amountDue);

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_PAYMENT_RECORDED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"Amount={amount}, Method={cleanMethod}, Ref={paymentRow.ReferenceCode}, NetPaid={updatedNetPaid}/{amountDue}",
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking, tx: paymentRow);
    }

    /// <summary>
    /// P1-04 & P1-05: Creates a counter-entry refund transaction in TbPaymentTransaction without deleting historical rows.
    /// </summary>
    public async Task<BookingOperationResult> RefundBookingPaymentAsync(
        PickleballContext context,
        string bookingCodeOrId,
        int refundAmount,
        string paymentMethod,
        string? referenceCode,
        string? reason,
        string? idempotencyKey,
        int? actorAccountId,
        string? actorUsername,
        string? ipAddress = null)
    {
        if (refundAmount <= 0)
        {
            return BookingOperationResult.Fail("Số tiền hoàn trả phải lớn hơn 0 VNĐ.", 400);
        }

        string cleanIdem = string.IsNullOrWhiteSpace(idempotencyKey)
            ? string.Empty
            : idempotencyKey.Trim()[..Math.Min(idempotencyKey.Trim().Length, 100)];
        if (string.IsNullOrEmpty(cleanIdem))
        {
            return BookingOperationResult.Fail("Thiếu khóa IdempotencyKey cho giao dịch hoàn tiền.", 400);
        }

        string cleanMethod = (paymentMethod ?? "cash").Trim().ToLowerInvariant() == "transfer" ? "transfer" : "cash";

        bool isRelational = context.Database.IsRelational();
        await using var tx = isRelational ? await context.Database.BeginTransactionAsync(IsolationLevel.Serializable) : null;

        var booking = await FindBookingAsync(context, bookingCodeOrId);
        if (booking == null)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail("Không tìm thấy đơn đặt sân.", 404);
        }

        string idemScope = "booking.refund";
        string idemActor = IdempotencyService.BuildActorKey(actorAccountId, actorUsername);
        string idemTarget = $"BOOKING:{booking.BookingCode}";
        string idemPayload = $"code={booking.BookingCode}|refund={refundAmount}|method={cleanMethod}";

        var idemCheck = await IdempotencyService.CheckAsync(context, cleanIdem, idemScope, idemActor, idemTarget, idemPayload);
        if (idemCheck.Status == IdempotencyStatus.Conflict)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail(idemCheck.ConflictMessage!, 409, errorCode: IdempotencyService.ConflictErrorCode);
        }

        var existingTx = await context.TbPaymentTransactions.FirstOrDefaultAsync(t => t.IdempotencyKey == cleanIdem);
        if (existingTx != null)
        {
            bool matchesRefund =
                existingTx.BookingId == booking.BookingId &&
                string.Equals(existingTx.TransactionType, "refund", StringComparison.OrdinalIgnoreCase) &&
                existingTx.Amount == refundAmount &&
                string.Equals(existingTx.PaymentMethod, cleanMethod, StringComparison.OrdinalIgnoreCase);

            if (!matchesRefund)
            {
                if (tx != null) await tx.RollbackAsync();
                return BookingOperationResult.Fail(
                    "Khóa IdempotencyKey đã được sử dụng cho một giao dịch hoàn tiền khác.",
                    409,
                    errorCode: IdempotencyService.ConflictErrorCode);
            }

            if (tx != null) await tx.CommitAsync();
            return BookingOperationResult.Ok(booking, tx: existingTx, replay: true);
        }

        int currentNetPaid = await ComputeBookingLedgerNetPaidAsync(context, booking.BookingId);
        if (refundAmount > currentNetPaid)
        {
            if (tx != null) await tx.RollbackAsync();
            return BookingOperationResult.Fail($"Số tiền hoàn ({refundAmount:N0} đ) vượt quá số tiền thực thu hiện có ({currentNetPaid:N0} đ).", 422);
        }

        var vnNow = GetVietnamNow();
        var refundRow = new TbPaymentTransaction
        {
            BookingId = booking.BookingId,
            BookingCode = booking.BookingCode,
            Amount = refundAmount,
            Currency = "VND",
            TransactionType = "refund",
            PaymentMethod = cleanMethod,
            Status = "completed",
            IdempotencyKey = cleanIdem,
            ReferenceCode = string.IsNullOrWhiteSpace(referenceCode) ? $"RFD-{vnNow:yyyyMMddHHmmss}" : referenceCode.Trim()[..Math.Min(referenceCode.Trim().Length, 100)],
            Note = string.IsNullOrWhiteSpace(reason) ? "Hoàn tiền cho khách tại quầy" : reason.Trim()[..Math.Min(reason.Trim().Length, 300)],
            ActorAccountId = actorAccountId,
            RecordedBy = actorUsername,
            CreatedAt = vnNow
        };

        context.TbPaymentTransactions.Add(refundRow);
        await context.SaveChangesAsync();

        string refundHash = IdempotencyService.ComputeCanonicalHash(idemScope, idemActor, idemTarget, idemPayload);
        context.TbIdempotencyRecords.Add(IdempotencyService.CreateRecord(
            cleanIdem, idemScope, idemActor, idemTarget, refundHash, booking.BookingCode, refundRow.TransactionId));

        int updatedNetPaid = await ComputeBookingLedgerNetPaidAsync(context, booking.BookingId);
        booking.PaidAmount = updatedNetPaid;
        booking.TotalPaid = updatedNetPaid;
        booking.PaymentStatus = updatedNetPaid == 0 ? "refunded" : ComputePaymentStatus(updatedNetPaid, booking.AmountDue);

        context.TbAuditLogs.Add(new TbAuditLog
        {
            Action = "BOOKING_PAYMENT_REFUNDED",
            EntityType = "Booking",
            EntityId = booking.BookingCode,
            ActorAccountId = actorAccountId,
            ActorUsername = actorUsername,
            Details = $"RefundAmount={refundAmount}, NetPaidAfter={updatedNetPaid}, Reason={refundRow.Note}",
            IpAddress = ipAddress,
            CreatedAt = vnNow
        });

        await context.SaveChangesAsync();
        if (tx != null) await tx.CommitAsync();
        return BookingOperationResult.Ok(booking, tx: refundRow);
    }

    public static async Task<int> ComputeBookingLedgerNetPaidAsync(PickleballContext context, int bookingId)
    {
        var rows = await context.TbPaymentTransactions
            .AsNoTracking()
            .Where(t => t.BookingId == bookingId && t.Status == "completed")
            .Select(t => new { t.TransactionType, t.Amount })
            .ToListAsync();

        int net = 0;
        foreach (var r in rows)
        {
            if (string.Equals(r.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
                net -= Math.Abs(r.Amount);
            else
                net += Math.Max(0, r.Amount);
        }

        return Math.Max(0, net);
    }

    public static string ComputePaymentStatus(int netPaid, int amountDue)
    {
        if (netPaid <= 0) return "unpaid";
        if (amountDue > 0 && netPaid >= amountDue) return "paid";
        return "partial";
    }

    /// <summary>
    /// Idempotent background lifecycle reconciliation:
    /// - Unapproved pending bookings past HoldExpiresAtUtc or slot end -> 'expired'
    /// - Approved bookings not checked in past grace/slot end -> 'no_show'
    /// Never auto-starts 'approved' into 'playing' and never touches PaidAmount or auto-completes 'playing'.
    /// </summary>
    public async Task<int> ReconcileBookingLifecycleAsync(PickleballContext context, DateTime? referenceNow = null)
    {
        var vnNow = referenceNow ?? GetVietnamNow();
        var utcNow = ToUtcFromVietnamNow(vnNow);
        string todayIso = vnNow.ToString("yyyy-MM-dd");

        var candidates = await context.TbBookings
            .Where(b => b.Status == "pending" ||
                        (b.Status == "approved" && string.Compare(b.BookingDate, todayIso) <= 0))
            .ToListAsync();

        int updated = 0;
        foreach (var b in candidates)
        {
            bool hasParsedDate = DateOnly.TryParseExact(b.BookingDate, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var bDate);
            bool hasParsedTime = PricingService.TryParseTimeMinutes(b.StartTime, out int sMins);
            DateTime? endDt = (hasParsedDate && hasParsedTime)
                ? bDate.ToDateTime(new TimeOnly(sMins / 60, sMins % 60)).AddMinutes(Math.Max(30, b.Duration))
                : null;

            if (b.Status == "pending" && (!IsPendingHoldActive(b, utcNow) || (endDt.HasValue && vnNow >= endDt.Value)))
            {
                b.Status = "expired";
                b.OrderStatusId = 5;
                b.CancelledAt = vnNow;
                b.CancelReason = "Hết thời gian giữ chỗ chờ duyệt (TTL)";
                context.TbAuditLogs.Add(new TbAuditLog
                {
                    Action = "BOOKING_EXPIRED_AUTO",
                    EntityType = "Booking",
                    EntityId = b.BookingCode,
                    Details = $"Date={b.BookingDate}, Start={b.StartTime}, HoldExpiresAtUtc={b.HoldExpiresAtUtc:O}",
                    CreatedAt = vnNow
                });
                updated++;
            }
            else if (b.Status == "approved" && b.CheckedInAt == null && endDt.HasValue && vnNow >= endDt.Value)
            {
                b.Status = "no_show";
                b.OrderStatusId = 5;
                b.CancelledAt = vnNow;
                b.CancelReason = "Khách không đến Check-in nhận sân (No-show)";
                context.TbAuditLogs.Add(new TbAuditLog
                {
                    Action = "BOOKING_NOSHOW_AUTO",
                    EntityType = "Booking",
                    EntityId = b.BookingCode,
                    Details = $"Date={b.BookingDate}, Start={b.StartTime}",
                    CreatedAt = vnNow
                });
                updated++;
            }
        }

        if (updated > 0)
        {
            await context.SaveChangesAsync();
        }

        return updated;
    }

    public static List<PosCartItemDto> ParsePosItems(string? json)
    {
        if (string.IsNullOrWhiteSpace(json)) return new List<PosCartItemDto>();
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return new List<PosCartItemDto>();

            var list = new List<PosCartItemDto>();
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                int prodId = el.TryGetProperty("productId", out var p1) && p1.TryGetInt32(out int v1) ? v1
                           : el.TryGetProperty("ProductId", out var p1b) && p1b.TryGetInt32(out int v1b) ? v1b
                           : el.TryGetProperty("id", out var p2) && p2.TryGetInt32(out int v2) ? v2 : 0;
                string name = el.TryGetProperty("name", out var nEl) ? nEl.GetString() ?? ""
                            : el.TryGetProperty("Name", out var nEl2) ? nEl2.GetString() ?? "" : "";
                int price = el.TryGetProperty("price", out var prEl) && prEl.TryGetInt32(out int vp) ? vp
                          : el.TryGetProperty("Price", out var prEl2) && prEl2.TryGetInt32(out int vp2) ? vp2 : 0;
                int qty = el.TryGetProperty("qty", out var qEl) && qEl.TryGetInt32(out int vq) ? vq
                        : el.TryGetProperty("Qty", out var qEl2) && qEl2.TryGetInt32(out int vq2) ? vq2 : 0;
                int? orderId = el.TryGetProperty("orderId", out var oEl) && oEl.ValueKind == JsonValueKind.Number && oEl.TryGetInt32(out int vo) ? vo
                             : el.TryGetProperty("OrderId", out var oEl2) && oEl2.ValueKind == JsonValueKind.Number && oEl2.TryGetInt32(out int vo2) ? vo2 : null;
                string? orderCode = el.TryGetProperty("orderCode", out var ocEl) && ocEl.ValueKind == JsonValueKind.String ? ocEl.GetString()
                                  : el.TryGetProperty("OrderCode", out var ocEl2) && ocEl2.ValueKind == JsonValueKind.String ? ocEl2.GetString() : null;
                if (qty > 0)
                {
                    list.Add(new PosCartItemDto
                    {
                        ProductId = prodId,
                        Name = name,
                        Price = price,
                        Qty = qty,
                        OrderId = orderId,
                        OrderCode = orderCode
                    });
                }
            }
            return list;
        }
        catch
        {
            return new List<PosCartItemDto>();
        }
    }

    public static async Task<TbBooking?> FindBookingAsync(PickleballContext context, string codeOrId)
    {
        if (string.IsNullOrWhiteSpace(codeOrId)) return null;
        string clean = codeOrId.Trim();
        if (int.TryParse(clean, out int numericId))
        {
            return await context.TbBookings.FirstOrDefaultAsync(b => b.BookingId == numericId);
        }

        string upper = clean.ToUpperInvariant();
        return await context.TbBookings.FirstOrDefaultAsync(b => b.BookingCode == upper);
    }

    private static async Task<string> GenerateUniqueBookingCodeAsync(PickleballContext context)
    {
        for (int attempt = 0; attempt < 10; attempt++)
        {
            string suffix = Guid.NewGuid().ToString("N")[..6].ToUpperInvariant();
            string candidate = $"PB-{suffix}";
            bool exists = await context.TbBookings.AnyAsync(b => b.BookingCode == candidate);
            if (!exists) return candidate;
        }
        return $"PB-{DateTime.UtcNow:HHmmssfff}";
    }
}
