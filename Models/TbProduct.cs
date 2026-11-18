using System;
using System.Collections.Generic;

namespace PickleballClub.Models;

public partial class TbProduct
{
    public int ProductId { get; set; }

    public string? Title { get; set; }

    public string? Alias { get; set; }

    public int? CategoryProductId { get; set; }

    public string? Description { get; set; }

    public string? Detail { get; set; }

    public string? Image { get; set; }

    public string? Icon { get; set; }

    public int? Price { get; set; } = 0;

    public decimal PriceDec { get; set; }

    public int? PriceSale { get; set; } = 0;

    public int? Quantity { get; set; } = 100;

    public DateTime? CreatedDate { get; set; } = DateTime.Now;

    public string? CreatedBy { get; set; }

    public DateTime? ModifiedDate { get; set; } = DateTime.Now;

    public string? ModifiedBy { get; set; }

    public bool IsNew { get; set; } = true;

    public bool IsBestSeller { get; set; } = false;

    public int? UnitInStock { get; set; } = 100;

    public bool IsActive { get; set; } = true;

    public int? Star { get; set; } = 5;

    public byte[]? RowVersion { get; set; }

    public virtual TbProductCategory? CategoryProduct { get; set; }

    public virtual ICollection<TbOrderDetail> TbOrderDetails { get; set; } = new List<TbOrderDetail>();

    public virtual ICollection<TbInventoryMovement> TbInventoryMovements { get; set; } = new List<TbInventoryMovement>();
}
