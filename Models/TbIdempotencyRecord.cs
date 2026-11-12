using System;

namespace PickleballClub.Models;

public partial class TbIdempotencyRecord
{
    public int IdempotencyId { get; set; }

    public string IdempotencyKey { get; set; } = null!;

    /// <summary>
    /// e.g. booking.create, booking.payment, booking.refund, booking.extend, booking.cancel,
    /// order.create, order.payment, order.refund, order.cancel
    /// </summary>
    public string Scope { get; set; } = null!;

    /// <summary>
    /// Normalized actor identity (e.g. acc:12, guest:0901234567)
    /// </summary>
    public string ActorKey { get; set; } = null!;

    /// <summary>
    /// Normalized target entity identifier (e.g. court:1:2026-11-10:18:00, booking:PB-ABC, order:ORD-123)
    /// </summary>
    public string TargetEntity { get; set; } = null!;

    /// <summary>
    /// SHA-256 hex digest of canonical request payload (including amount, method, items, duration)
    /// </summary>
    public string RequestHash { get; set; } = null!;

    public string? ResultEntityCode { get; set; }

    public int? ResultEntityId { get; set; }

    public string? ResponseJson { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public DateTime ExpiresAtUtc { get; set; } = DateTime.UtcNow.AddHours(24);
}
