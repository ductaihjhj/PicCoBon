using System;

namespace PickleballClub.Models;

public partial class TbPaymentTransaction
{
    public int TransactionId { get; set; }

    public int? BookingId { get; set; }

    public int? OrderId { get; set; }

    public string? BookingCode { get; set; }

    public int Amount { get; set; }

    /// <summary>
    /// cash, transfer, vnpay, refund
    /// </summary>
    public string PaymentMethod { get; set; } = "cash";

    /// <summary>
    /// completed, pending, failed, refunded
    /// </summary>
    public string Status { get; set; } = "completed";

    public string? ReferenceCode { get; set; }

    public string? Note { get; set; }

    public string? RecordedBy { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;

    public virtual TbBooking? Booking { get; set; }

    public virtual TbOrder? Order { get; set; }
}
