using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbOrderStatus
{
    public int OrderStatusId { get; set; }

    public string Name { get; set; } = null!;

    public string? Description { get; set; }

    public virtual ICollection<TbBooking> TbBookings { get; set; } = new List<TbBooking>();

    public virtual ICollection<TbOrder> TbOrders { get; set; } = new List<TbOrder>();
}
