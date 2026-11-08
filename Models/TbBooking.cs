using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbBooking
{
    public int BookingId { get; set; }

    public string BookingCode { get; set; } = null!;

    public int CourtId { get; set; }

    public int? AccountId { get; set; }

    public string? CustomerName { get; set; }

    public string? Phone { get; set; }

    /// <summary>
    /// Format: yyyy-MM-dd
    /// </summary>
    public string BookingDate { get; set; } = null!;

    /// <summary>
    /// Format: HH:mm
    /// </summary>
    public string StartTime { get; set; } = null!;

    /// <summary>
    /// Duration in minutes (e.g. 60, 90, 120)
    /// </summary>
    public int Duration { get; set; } = 60;

    public int Players { get; set; } = 4;

    public string? SkillLevel { get; set; } = "Phong trào";

    public string? PaymentMethod { get; set; } = "cash";

    public int CourtFee { get; set; }

    public int DiscountAmount { get; set; }

    public int PosTotal { get; set; }

    public int AmountDue { get; set; }

    public int PaidAmount { get; set; }

    public int TotalPaid { get; set; }

    /// <summary>
    /// unpaid, partial, paid, refunded
    /// </summary>
    public string PaymentStatus { get; set; } = "unpaid";

    public string? PosItemsJson { get; set; } = "[]";

    public string? PricingBreakdownJson { get; set; }

    public string? CheckInToken { get; set; }

    /// <summary>
    /// SHA-256 hex hash of the opaque guest BookingAccessToken issued once at booking creation.
    /// </summary>
    public string? AccessTokenHash { get; set; }

    public string? IdempotencyKey { get; set; }

    public DateOnly? BookingDateValue { get; set; }

    public TimeOnly? StartTimeValue { get; set; }

    public decimal AmountDueDec { get; set; }

    public decimal PaidAmountDec { get; set; }

    public byte[]? RowVersion { get; set; }

    public DateTime? CheckedInAt { get; set; }

    public DateTime? CompletedAt { get; set; }

    public DateTime? CancelledAt { get; set; }

    public string? CancelReason { get; set; }

    public string Status { get; set; } = "pending";

    public int? OrderStatusId { get; set; } = 1;

    public DateTime? CreatedDate { get; set; } = DateTime.Now;

    public virtual TbAccount? Account { get; set; }

    public virtual TbCourt? Court { get; set; }

    public virtual TbOrderStatus? OrderStatus { get; set; }

    public virtual ICollection<TbOrder> TbOrders { get; set; } = new List<TbOrder>();

    public virtual ICollection<TbPaymentTransaction> TbPaymentTransactions { get; set; } = new List<TbPaymentTransaction>();

    public virtual ICollection<TbInventoryMovement> TbInventoryMovements { get; set; } = new List<TbInventoryMovement>();
}
