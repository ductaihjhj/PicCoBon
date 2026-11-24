using System;

namespace PickleballClub.Models;

public partial class TbSocialInvite
{
    public int InviteId { get; set; }

    public int SenderAccountId { get; set; }

    public int ReceiverAccountId { get; set; }

    public DateOnly ProposedDate { get; set; }

    public string ProposedStartTime { get; set; } = string.Empty;

    public int DurationMinutes { get; set; } = 60;

    public string? Note { get; set; }

    public string Status { get; set; } = "pending";

    public DateOnly? CounterDate { get; set; }

    public string? CounterStartTime { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime? RespondedAtUtc { get; set; }

    public virtual TbAccount SenderAccount { get; set; } = null!;

    public virtual TbAccount ReceiverAccount { get; set; } = null!;
}
