using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Services;

public class PricingService
{
    public const int DefaultStandardBaseRate = 120000;
    public const int PeakSurchargePerHour = 30000;
    public static readonly TimeOnly PeakStartTime = new(17, 0);

    /// <summary>
    /// Calculates segmented pricing in 30-minute slices across normal (< 17:00) and peak (>= 17:00) hours.
    /// Rate per hour:
    /// - Standard (< 17:00): court.BasePrice + court.Surcharge (e.g. 120k + 20k = 140k/h for VIP, 120k/h for Standard)
    /// - Peak (>= 17:00): StandardRate + PeakSurcharge (30,000đ/h) (e.g. 170k/h for VIP, 150k/h for Standard)
    /// </summary>
    public PricingQuote CalculateQuote(
        TbCourt? court,
        string startTimeStr,
        int durationMinutes,
        int memberPaidSpend = 0,
        int? overrideRoleId = null,
        string? explicitMemberTier = null,
        int peakSurchargeOverride = PeakSurchargePerHour)
    {
        if (durationMinutes < 30 || durationMinutes > 480 || durationMinutes % 30 != 0)
        {
            throw new ArgumentException("Thời lượng đặt sân phải từ 30 đến 480 phút (bội số 30 phút).");
        }

        if (!TimeOnly.TryParse(startTimeStr?.Trim(), out var startTime))
        {
            startTime = new TimeOnly(17, 0);
        }

        int basePrice = court is { BasePrice: > 0 } ? court.BasePrice : DefaultStandardBaseRate;
        int courtSurcharge = court?.Surcharge ?? 0;
        int peakSurcharge = peakSurchargeOverride >= 0 ? peakSurchargeOverride : PeakSurchargePerHour;

        int standardHourlyRate = basePrice + courtSurcharge;
        int peakHourlyRate = standardHourlyRate + peakSurcharge;

        int halfHourSlots = durationMinutes / 30;
        int normalMinutes = 0;
        int peakMinutes = 0;
        int normalSubtotal = 0;
        int peakSubtotal = 0;

        var segments = new List<PricingSegmentDto>();

        for (int i = 0; i < halfHourSlots; i++)
        {
            var segStart = startTime.AddMinutes(i * 30);
            var segEnd = segStart.AddMinutes(30);
            bool isPeak = segStart >= PeakStartTime;
            int ratePerHour = isPeak ? peakHourlyRate : standardHourlyRate;
            int segmentFee = ratePerHour / 2;

            if (isPeak)
            {
                peakMinutes += 30;
                peakSubtotal += segmentFee;
            }
            else
            {
                normalMinutes += 30;
                normalSubtotal += segmentFee;
            }

            segments.Add(new PricingSegmentDto
            {
                Start = segStart.ToString("HH:mm"),
                End = segEnd.ToString("HH:mm"),
                IsPeak = isPeak,
                RatePerHour = ratePerHour,
                Fee = segmentFee
            });
        }

        int grossCourtFee = normalSubtotal + peakSubtotal;

        var (tierName, discountPercent) = ResolveMemberTier(memberPaidSpend, overrideRoleId, explicitMemberTier);
        int discountAmount = discountPercent > 0
            ? (int)Math.Round(grossCourtFee * (discountPercent / 100.0) / 1000.0) * 1000
            : 0;
        int netCourtFee = Math.Max(0, grossCourtFee - discountAmount);

        return new PricingQuote
        {
            BasePricePerHour = basePrice,
            CourtSurchargePerHour = courtSurcharge,
            PeakSurchargePerHour = peakSurcharge,
            StandardRatePerHour = standardHourlyRate,
            PeakRatePerHour = peakHourlyRate,
            DurationMinutes = durationMinutes,
            NormalMinutes = normalMinutes,
            PeakMinutes = peakMinutes,
            NormalSubtotal = normalSubtotal,
            PeakSubtotal = peakSubtotal,
            GrossCourtFee = grossCourtFee,
            MemberTier = tierName,
            DiscountPercent = discountPercent,
            DiscountAmount = discountAmount,
            NetCourtFee = netCourtFee,
            Segments = segments
        };
    }

    public static (string TierName, int DiscountPercent) ResolveMemberTier(
        int totalPaidSpend,
        int? roleId = null,
        string? explicitMemberTier = null)
    {
        if (roleId == 1)
        {
            return ("Hội viên Vàng VIP", 15);
        }

        if (!string.IsNullOrWhiteSpace(explicitMemberTier))
        {
            if (explicitMemberTier.Contains("Vàng", StringComparison.OrdinalIgnoreCase) ||
                explicitMemberTier.Contains("VIP", StringComparison.OrdinalIgnoreCase))
            {
                return ("Hội viên Vàng VIP", 15);
            }
            if (explicitMemberTier.Contains("Bạc", StringComparison.OrdinalIgnoreCase))
            {
                return ("Hội viên Bạc", 5);
            }
        }

        if (totalPaidSpend >= 5_000_000)
        {
            return ("Hội viên Vàng VIP", 15);
        }

        if (totalPaidSpend >= 2_000_000)
        {
            return ("Hội viên Bạc", 5);
        }

        return ("Hội viên Tiêu chuẩn", 0);
    }

    public async Task<int> GetVerifiedCustomerPaidSpendAsync(PickleballContext context, int? accountId, string? phone)
    {
        if (accountId == null && string.IsNullOrWhiteSpace(phone))
        {
            return 0;
        }

        string normalizedPhone = (phone ?? string.Empty).Trim();

        int paidBookingSpend = 0;
        var bookings = await context.TbBookings
            .Where(b => b.Status != "cancelled" && b.Status != "rejected" && b.Status != "expired" &&
                        ((accountId != null && b.AccountId == accountId) ||
                         (!string.IsNullOrEmpty(normalizedPhone) && b.Phone == normalizedPhone)))
            .Select(b => new { b.PaidAmount, b.TotalPaid, b.Status })
            .ToListAsync();

        foreach (var b in bookings)
        {
            if (b.PaidAmount > 0)
            {
                paidBookingSpend += b.PaidAmount;
            }
            else if (b.Status == "completed" && b.TotalPaid > 0)
            {
                paidBookingSpend += b.TotalPaid;
            }
        }

        int paidStandaloneOrders = 0;
        if (!string.IsNullOrEmpty(normalizedPhone))
        {
            paidStandaloneOrders = await context.TbOrders
                .Where(o => o.BookingId == null && o.PaidAmount > 0 && o.Phone == normalizedPhone)
                .SumAsync(o => (int?)o.PaidAmount) ?? 0;
        }

        return paidBookingSpend + paidStandaloneOrders;
    }
}

public class PricingQuote
{
    public int BasePricePerHour { get; set; }
    public int CourtSurchargePerHour { get; set; }
    public int PeakSurchargePerHour { get; set; }
    public int StandardRatePerHour { get; set; }
    public int PeakRatePerHour { get; set; }
    public int DurationMinutes { get; set; }
    public int NormalMinutes { get; set; }
    public int PeakMinutes { get; set; }
    public int NormalSubtotal { get; set; }
    public int PeakSubtotal { get; set; }
    public int GrossCourtFee { get; set; }
    public string MemberTier { get; set; } = "Hội viên Tiêu chuẩn";
    public int DiscountPercent { get; set; }
    public int DiscountAmount { get; set; }
    public int NetCourtFee { get; set; }
    public List<PricingSegmentDto> Segments { get; set; } = new();

    public string ToJson() => JsonSerializer.Serialize(this);
}

public class PricingSegmentDto
{
    public string Start { get; set; } = string.Empty;
    public string End { get; set; } = string.Empty;
    public bool IsPeak { get; set; }
    public int RatePerHour { get; set; }
    public int Fee { get; set; }
}
