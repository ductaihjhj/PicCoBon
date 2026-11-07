using System;

namespace PickleballClub.Models;

public partial class TbCmsConfig
{
    public string ConfigKey { get; set; } = null!;

    public string ConfigValue { get; set; } = null!;

    public string? Description { get; set; }
}
