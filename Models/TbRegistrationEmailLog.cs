using System;

namespace PickleballClub.Models;

public class TbRegistrationEmailLog
{
    public int LogId { get; set; }

    public int? VerificationId { get; set; }

    public string SessionBindingHash { get; set; } = string.Empty;

    public string EmailHash { get; set; } = string.Empty;

    public string? ClientIpHash { get; set; }

    public string Outcome { get; set; } = "Sent";

    public DateTime CreatedAtUtc { get; set; }
}
