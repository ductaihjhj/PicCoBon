using System;

namespace PickleballClub.Models;

public partial class TbSocialProfile
{
    public int AccountId { get; set; }

    public bool IsVisibleOnCourt { get; set; }

    public bool IsReadyToPlay { get; set; }

    public bool AllowMatchSuggestion { get; set; }

    public string? SpriteId { get; set; }

    public bool SkillConfirmed { get; set; }

    public string? ConfirmedSkillLevel { get; set; }

    public string PreferredFreeSlotsJson { get; set; } = "[]";

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;

    public virtual TbAccount Account { get; set; } = null!;
}
