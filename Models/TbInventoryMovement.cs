using System;

namespace PickleballClub.Models;

public partial class TbInventoryMovement
{
    public int MovementId { get; set; }

    public int ProductId { get; set; }

    public int? OrderId { get; set; }

    public int? BookingId { get; set; }

    /// <summary>
    /// Negative for stock deduction (sale/POS), positive for restock/cancellation/adjustment.
    /// </summary>
    public int ChangeQty { get; set; }

    public int BalanceAfter { get; set; }

    public string Reason { get; set; } = null!;

    public int? ActorAccountId { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public virtual TbProduct? Product { get; set; }

    public virtual TbOrder? Order { get; set; }

    public virtual TbBooking? Booking { get; set; }
}
