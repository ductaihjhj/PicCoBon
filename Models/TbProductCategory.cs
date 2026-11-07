using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbProductCategory
{
    public int CategoryProductId { get; set; }

    public string? Title { get; set; }

    public string? Alias { get; set; }

    public string? Description { get; set; }

    public string? Icon { get; set; }

    public int? Position { get; set; } = 1;

    public DateTime? CreatedDate { get; set; } = DateTime.Now;

    public bool IsActive { get; set; } = true;

    public virtual ICollection<TbProduct> TbProducts { get; set; } = new List<TbProduct>();
}
