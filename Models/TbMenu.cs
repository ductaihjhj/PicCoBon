using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbMenu
{
    public int MenuId { get; set; }

    public string? Title { get; set; }

    public string? Alias { get; set; }

    public string? Description { get; set; }

    public int? Levels { get; set; } = 1;

    public int? ParentId { get; set; } = 0;

    public int? Position { get; set; } = 1;

    public DateTime? CreatedDate { get; set; } = DateTime.Now;

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedDate { get; set; } = DateTime.Now;

    public string? ModifiedBy { get; set; }

    public bool IsActive { get; set; } = true;
}
