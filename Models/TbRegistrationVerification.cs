using System;

namespace PickleballClub.Models;

public class TbRegistrationVerification
{
    public int VerificationId { get; set; }

    public string RequestToken { get; set; } = string.Empty;

    public string SessionBindingHash { get; set; } = string.Empty;

    public string FullName { get; set; } = string.Empty;

    public string Username { get; set; } = string.Empty;

    public string Phone { get; set; } = string.Empty;

    public string Email { get; set; } = string.Empty;

    public string EmailHash { get; set; } = string.Empty;

    public string? PasswordHash { get; set; }

    public string? OtpHmac { get; set; }

    public int OtpVersion { get; set; } = 1;

    public int AttemptCount { get; set; } = 0;

    public int SendCount { get; set; } = 0;

    public string Status { get; set; } = "Pending";

    public string LastDeliveryStatus { get; set; } = "PendingSend";

    public string? ClientIpHash { get; set; }

    public DateTime CreatedAtUtc { get; set; }

    public DateTime UpdatedAtUtc { get; set; }

    public DateTime? LastSentAtUtc { get; set; }

    public DateTime OtpExpiresAtUtc { get; set; }

    public DateTime RequestExpiresAtUtc { get; set; }

    public DateTime? ConsumedAtUtc { get; set; }

    public int? CreatedAccountId { get; set; }

    public byte[]? RowVersion { get; set; }

    public virtual TbAccount? CreatedAccount { get; set; }
}
