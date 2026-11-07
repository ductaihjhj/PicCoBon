using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbAccount
{
    public int AccountId { get; set; }

    public string? Username { get; set; }

    public string? Password { get; set; }

    public string? FullName { get; set; }

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Avatar { get; set; }

    public string? Gender { get; set; }

    public string? BirthDate { get; set; }

    public string? Address { get; set; }

    public string? SkillLevel { get; set; }

    public string? DominantHand { get; set; }

    public string? PaddleBrand { get; set; }

    public string? PlayStyle { get; set; }

    public string? EmergencyContact { get; set; }

    public string? Bio { get; set; }

    public string? MemberTier { get; set; }

    public int? RoleId { get; set; }

    public string? LastLogin { get; set; }

    public bool IsActive { get; set; } = true;

    public virtual TbRole? Role { get; set; }

    public virtual ICollection<TbBooking> TbBookings { get; set; } = new List<TbBooking>();
}
