using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Services;

public class BookingPriceSegment
{
    public string Label { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int Minutes { get; set; }
    public int HourlyRate { get; set; }
    public int Subtotal { get; set; }
}

public class BookingQuoteBreakdown
{
    public int CourtId { get; set; }
    public string CourtName { get; set; } = string.Empty;
    public string StartTime { get; set; } = string.Empty;
    public string EndTime { get; set; } = string.Empty;
    public int DurationMinutes { get; set; }
    public int StandardMinutes { get; set; }
    public int PeakMinutes { get; set; }
    public int StandardHourlyRate { get; set; }
    public int PeakHourlyRate { get; set; }
    public int CourtFeeBeforeDiscount { get; set; }
    public string MemberTier { get; set; } = "Khách tiêu chuẩn";
    public int DiscountPercent { get; set; }
    public int DiscountAmount { get; set; }
    public int PosTotal { get; set; }
    public int AmountDue { get; set; }
    public string PricingVersion { get; set; } = "v1";
    public string OpenTime { get; set; } = "06:00";
    public string CloseTime { get; set; } = "22:00";
    public List<BookingPriceSegment> Segments { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this);
}

public class PricingService
{
    public const int DefaultPeakStartMinutes = 17 * 60; // 17:00
    public const int DefaultPeakSurchargePerHour = 30_000;

    public static bool TryParseTimeMinutes(string? timeStr, out int totalMinutes)
    {
        totalMinutes = 0;
        if (string.IsNullOrWhiteSpace(timeStr)) return false;
        var parts = timeStr.Trim().Split(':');
        if (parts.Length < 2) return false;
        if (!int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m)) return false;
        if (h < 0 || h > 23 || m < 0 || m > 59) return false;
        totalMinutes = h * 60 + m;
        return true;
    }

    public static string MinutesToTime(int minutes)
    {
        int clamped = Math.Clamp(minutes, 0, 24 * 60);
        if (clamped == 24 * 60) return "24:00";
        int h = clamped / 60;
        int m = clamped % 60;
        return $"{h:D2}:{m:D2}";
    }

    /// <summary>
    /// Computes exact minute-by-minute segmented pricing across standard vs peak boundaries using unified server ClubSettings.
    /// </summary>
    public BookingQuoteBreakdown CalculateQuote(
        TbCourt court,
        string startTime,
        int durationMinutes,
        int posTotal = 0,
        int verifiedPaidSpend = 0,
        string? explicitMemberTier = null,
        ClubSettingsSnapshot? settings = null,
        bool isAuthenticatedMember = false)
    {
        if (!TryParseTimeMinutes(startTime, out int startMins))
        {
            startMins = 17 * 60;
        }

        int validDuration = Math.Clamp(durationMinutes, 30, 480);
        int endMins = startMins + validDuration;

        int peakStartMins = settings?.PeakStartMinutes ?? DefaultPeakStartMinutes;
        int peakSurcharge = settings?.PeakSurchargePerHour ?? DefaultPeakSurchargePerHour;
        int configuredBase = settings?.BaseCourtPrice ?? 120_000;

        int basePrice = court.BasePrice > 0 ? court.BasePrice : configuredBase;
        // If CMS updated base court price and court was using standard club base price, reflect server CMS base price + court surcharge
        if (settings != null && (court.BasePrice == 120_000 || court.BasePrice <= 0))
        {
            basePrice = settings.BaseCourtPrice;
        }

        int surcharge = Math.Max(0, court.Surcharge);
        int standardHourly = basePrice + surcharge;
        int peakHourly = standardHourly + peakSurcharge;

        int standardMins = 0;
        int peakMins = 0;

        for (int minute = startMins; minute < endMins; minute++)
        {
            if (minute >= peakStartMins)
                peakMins++;
            else
                standardMins++;
        }

        int standardFee = (int)Math.Round(standardMins * (standardHourly / 60.0), MidpointRounding.AwayFromZero);
        int peakFee = (int)Math.Round(peakMins * (peakHourly / 60.0), MidpointRounding.AwayFromZero);
        int courtFeeBeforeDiscount = standardFee + peakFee;

        var segments = new List<BookingPriceSegment>();
        if (standardMins > 0)
        {
            int segEnd = Math.Min(endMins, peakStartMins);
            segments.Add(new BookingPriceSegment
            {
                Label = $"Giờ tiêu chuẩn ({MinutesToTime(startMins)}–{MinutesToTime(segEnd)})",
                StartTime = MinutesToTime(startMins),
                EndTime = MinutesToTime(segEnd),
                Minutes = standardMins,
                HourlyRate = standardHourly,
                Subtotal = standardFee
            });
        }

        if (peakMins > 0)
        {
            int segStart = Math.Max(startMins, peakStartMins);
            segments.Add(new BookingPriceSegment
            {
                Label = $"Giờ vàng ({MinutesToTime(segStart)}–{MinutesToTime(endMins)})",
                StartTime = MinutesToTime(segStart),
                EndTime = MinutesToTime(endMins),
                Minutes = peakMins,
                HourlyRate = peakHourly,
                Subtotal = peakFee
            });
        }

        // P1-16 & P1-18: Only authenticated accounts can receive member/VIP discounts. Unverified phone numbers NEVER grant discounts.
        var (tierName, discountPct) = isAuthenticatedMember
            ? ResolveMemberTier(verifiedPaidSpend, explicitMemberTier)
            : ("Khách vãng lai", 0);

        int discountAmount = discountPct > 0
            ? (int)Math.Round(courtFeeBeforeDiscount * (discountPct / 100.0), MidpointRounding.AwayFromZero)
            : 0;

        int safePosTotal = Math.Max(0, posTotal);
        int amountDue = Math.Max(0, courtFeeBeforeDiscount - discountAmount + safePosTotal);

        return new BookingQuoteBreakdown
        {
            CourtId = court.CourtId,
            CourtName = court.CourtName,
            StartTime = MinutesToTime(startMins),
            EndTime = MinutesToTime(endMins),
            DurationMinutes = validDuration,
            StandardMinutes = standardMins,
            PeakMinutes = peakMins,
            StandardHourlyRate = standardHourly,
            PeakHourlyRate = peakHourly,
            CourtFeeBeforeDiscount = courtFeeBeforeDiscount,
            MemberTier = tierName,
            DiscountPercent = discountPct,
            DiscountAmount = discountAmount,
            PosTotal = safePosTotal,
            AmountDue = amountDue,
            PricingVersion = settings?.PricingVersion ?? "v1",
            OpenTime = settings?.OpenTime ?? "06:00",
            CloseTime = settings?.CloseTime ?? "22:00",
            Segments = segments
        };
    }

    /// <summary>
    /// Resolves member tier strictly from verified net ledger paid spend or admin-assigned tier on a verified account.
    /// Never grants VIP automatically by RoleId.
    /// </summary>
    public static (string TierName, int DiscountPercent) ResolveMemberTier(
        int verifiedPaidSpend,
        string? explicitMemberTier = null)
    {
        if (!string.IsNullOrWhiteSpace(explicitMemberTier))
        {
            if (explicitMemberTier.Contains("VIP", StringComparison.OrdinalIgnoreCase) ||
                explicitMemberTier.Contains("Vàng", StringComparison.OrdinalIgnoreCase))
            {
                return ("Hội viên Vàng VIP", 15);
            }

            if (explicitMemberTier.Contains("Bạc", StringComparison.OrdinalIgnoreCase) ||
                explicitMemberTier.Contains("Silver", StringComparison.OrdinalIgnoreCase))
            {
                return ("Hội viên Bạc", 5);
            }
        }

        if (verifiedPaidSpend >= 5_000_000)
        {
            return ("Hội viên Vàng VIP", 15);
        }

        if (verifiedPaidSpend >= 2_000_000)
        {
            return ("Hội viên Bạc", 5);
        }

        return ("Hội viên Tiêu chuẩn", 0);
    }

    /// <summary>
    /// Computes net verified paid spend strictly from completed payment ledger transactions minus refunds
    /// linked to a verified AccountId (NEVER by self-declared phone number).
    /// </summary>
    public async Task<int> GetVerifiedAccountNetPaidSpendAsync(PickleballContext context, int? verifiedAccountId)
    {
        if (!verifiedAccountId.HasValue || verifiedAccountId.Value <= 0)
        {
            return 0;
        }

        int accId = verifiedAccountId.Value;

        // Net ledger payments on bookings owned by AccountId
        var bookingLedger = await (
            from pt in context.TbPaymentTransactions.AsNoTracking()
            join b in context.TbBookings.AsNoTracking() on pt.BookingId equals b.BookingId
            where b.AccountId == accId && pt.Status == "completed"
            select new { pt.TransactionType, pt.Amount }
        ).ToListAsync();

        // Net ledger payments on standalone orders owned by AccountId
        var orderLedger = await (
            from pt in context.TbPaymentTransactions.AsNoTracking()
            join o in context.TbOrders.AsNoTracking() on pt.OrderId equals o.OrderId
            where o.BookingId == null && o.AccountId == accId && pt.Status == "completed"
            select new { pt.TransactionType, pt.Amount }
        ).ToListAsync();

        int ledgerSum = 0;
        foreach (var tx in bookingLedger.Concat(orderLedger))
        {
            if (string.Equals(tx.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
                ledgerSum -= Math.Abs(tx.Amount);
            else
                ledgerSum += Math.Max(0, tx.Amount);
        }

        // Also account for any historical migrated bookings owned by AccountId that had PaidAmount > 0 prior to ledger rows
        var bookingsWithoutTx = await context.TbBookings.AsNoTracking()
            .Where(b => b.AccountId == accId && b.PaidAmount > 0 && !b.TbPaymentTransactions.Any())
            .SumAsync(b => (int?)b.PaidAmount) ?? 0;

        return Math.Max(0, ledgerSum + bookingsWithoutTx);
    }
}
