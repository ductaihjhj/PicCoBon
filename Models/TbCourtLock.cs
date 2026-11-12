using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbCourtLock
{
    public int LockId { get; set; }

    public string LockCode { get; set; } = null!;

    public int CourtId { get; set; }

    public string LockDate { get; set; } = null!;

    public DateOnly? LockDateValue { get; set; }

    public string StartTime { get; set; } = null!;

    public TimeOnly? StartTimeValue { get; set; }

    public int Duration { get; set; } = 60;

    public string? Reason { get; set; } = "Bảo trì định kỳ";

    public DateTime? CreatedDate { get; set; } = DateTime.Now;

    public byte[]? RowVersion { get; set; }

    public virtual TbCourt? Court { get; set; }
}
