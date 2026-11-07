using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbBooking
{
    public int BookingId { get; set; }

    public string BookingCode { get; set; } = null!;

    public int CourtId { get; set; }

    public int? AccountId { get; set; }

    public string CustomerName { get; set; } = null!;

    public string Phone { get; set; } = null!;

    public string BookingDate { get; set; } = null!;

    public string StartTime { get; set; } = null!;

    public int Duration { get; set; } = 60;

    public int Players { get; set; } = 4;

    public string? SkillLevel { get; set; } = "Phong trào";

    public int CourtFee { get; set; } = 0;

    public string PosItemsJson { get; set; } = "[]";

    public int TotalPaid { get; set; } = 0;

    public string Status { get; set; } = "pending";

    public int? OrderStatusId { get; set; }

    public DateTime? CreatedDate { get; set; } = DateTime.Now;

    public virtual TbCourt? Court { get; set; }

    public virtual TbAccount? Account { get; set; }

    public virtual TbOrderStatus? OrderStatus { get; set; }
}
