using System;

namespace PickleballClub.Models;

public partial class TbAuditLog
{
    public int AuditId { get; set; }

    public string Action { get; set; } = null!;

    public string? EntityType { get; set; }

    public string? EntityId { get; set; }

    public int? ActorAccountId { get; set; }

    public string? ActorUsername { get; set; }

    public string? Details { get; set; }

    public string? IpAddress { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.Now;
}
