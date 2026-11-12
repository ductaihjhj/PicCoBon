using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Services;

public enum IdempotencyStatus
{
    None = 0,
    New = 1,
    Replay = 2,
    Conflict = 3
}

public sealed record IdempotencyCheckResult(
    IdempotencyStatus Status,
    string? CleanKey,
    string? RequestHash,
    TbIdempotencyRecord? ExistingRecord,
    string? ConflictMessage)
{
    public static readonly IdempotencyCheckResult None = new(IdempotencyStatus.None, null, null, null, null);
}

public static class IdempotencyService
{
    public const string ConflictErrorCode = "IDEMPOTENCY_KEY_CONFLICT";

    public static string? NormalizeKey(string? rawKey)
    {
        if (string.IsNullOrWhiteSpace(rawKey)) return null;
        var trimmed = rawKey.Trim();
        return trimmed.Length <= 100 ? trimmed : trimmed[..100];
    }

    public static string BuildActorKey(int? accountId, string? fallbackIdentity = null)
    {
        if (accountId.HasValue && accountId.Value > 0)
            return $"acc:{accountId.Value}";

        var cleanFallback = (fallbackIdentity ?? "anon").Trim().ToLowerInvariant();
        return $"guest:{cleanFallback}";
    }

    public static string ComputeCanonicalHash(string scope, string actorKey, string targetEntity, string canonicalPayload)
    {
        var raw = $"{scope.Trim().ToLowerInvariant()}|{actorKey.Trim().ToLowerInvariant()}|{targetEntity.Trim().ToUpperInvariant()}|{canonicalPayload.Trim()}";
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }

    public static async Task<IdempotencyCheckResult> CheckAsync(
        PickleballContext db,
        string? rawKey,
        string scope,
        string actorKey,
        string targetEntity,
        string canonicalPayload,
        CancellationToken ct = default)
    {
        var cleanKey = NormalizeKey(rawKey);
        if (cleanKey == null)
            return IdempotencyCheckResult.None;

        var normScope = scope.Trim().ToLowerInvariant();
        var normActor = actorKey.Trim().ToLowerInvariant();
        var normTarget = targetEntity.Trim().ToUpperInvariant();
        var hash = ComputeCanonicalHash(normScope, normActor, normTarget, canonicalPayload);

        var existing = await db.TbIdempotencyRecords
            .AsNoTracking()
            .FirstOrDefaultAsync(r => r.IdempotencyKey == cleanKey, ct);

        if (existing == null)
        {
            return new IdempotencyCheckResult(IdempotencyStatus.New, cleanKey, hash, null, null);
        }

        bool isMatch =
            string.Equals(existing.Scope, normScope, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.ActorKey, normActor, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.TargetEntity, normTarget, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(existing.RequestHash, hash, StringComparison.OrdinalIgnoreCase);

        if (isMatch)
        {
            return new IdempotencyCheckResult(IdempotencyStatus.Replay, cleanKey, hash, existing, null);
        }

        return new IdempotencyCheckResult(
            IdempotencyStatus.Conflict,
            cleanKey,
            hash,
            existing,
            "Khóa IdempotencyKey đã được sử dụng cho một tác vụ, người dùng, đối tượng hoặc số tiền khác.");
    }

    public static TbIdempotencyRecord CreateRecord(
        string cleanKey,
        string scope,
        string actorKey,
        string targetEntity,
        string requestHash,
        string? resultEntityCode = null,
        int? resultEntityId = null,
        string? responseJson = null)
    {
        return new TbIdempotencyRecord
        {
            IdempotencyKey = cleanKey,
            Scope = scope.Trim().ToLowerInvariant(),
            ActorKey = actorKey.Trim().ToLowerInvariant(),
            TargetEntity = targetEntity.Trim().ToUpperInvariant(),
            RequestHash = requestHash,
            ResultEntityCode = resultEntityCode,
            ResultEntityId = resultEntityId,
            ResponseJson = responseJson,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(24)
        };
    }
}
