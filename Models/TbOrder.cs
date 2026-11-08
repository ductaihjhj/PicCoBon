using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbOrder
{
    public int OrderId { get; set; }

    public string? Code { get; set; }

    public int? BookingId { get; set; }

    public int? AccountId { get; set; }

    public string? BookingCode { get; set; }

    public string? CustomerName { get; set; }

    public string? Phone { get; set; }

    public string? Address { get; set; }

    public int? TotalAmount { get; set; }

    public decimal TotalAmountDec { get; set; }

    public int PaidAmount { get; set; }

    public decimal PaidAmountDec { get; set; }

    public string PaymentStatus { get; set; } = "unpaid";

    public string Status { get; set; } = "confirmed";

    public string? PaymentMethod { get; set; } = "cash";

    public string? IdempotencyKey { get; set; }

    public byte[]? RowVersion { get; set; }

    public int? Quanlity { get; set; }

    public int? OrderStatusId { get; set; } = 1;

    public DateTime? CreatedDate { get; set; } = DateTime.Now;

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedDate { get; set; } = DateTime.Now;

    public string? ModifiedBy { get; set; }

    public virtual TbBooking? Booking { get; set; }

    public virtual TbAccount? Account { get; set; }

    public virtual TbOrderStatus? OrderStatus { get; set; }

    public virtual ICollection<TbOrderDetail> TbOrderDetails { get; set; } = new List<TbOrderDetail>();

    public virtual ICollection<TbPaymentTransaction> TbPaymentTransactions { get; set; } = new List<TbPaymentTransaction>();

    public virtual ICollection<TbInventoryMovement> TbInventoryMovements { get; set; } = new List<TbInventoryMovement>();
}
