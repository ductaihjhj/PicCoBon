using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbCourt
{
    public int CourtId { get; set; }

    public string CourtName { get; set; } = null!;

    public string? Alias { get; set; }

    public string CourtType { get; set; } = "Indoor";

    public string? TypeLabel { get; set; }

    public string? Surface { get; set; }

    public string? Image { get; set; }

    public int BasePrice { get; set; } = 120000;

    public int Surcharge { get; set; } = 0;

    public string? Description { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual ICollection<TbBooking> TbBookings { get; set; } = new List<TbBooking>();

    public virtual ICollection<TbCourtLock> TbCourtLocks { get; set; } = new List<TbCourtLock>();
}
