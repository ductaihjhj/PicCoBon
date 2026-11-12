using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Http;

namespace PickleballClub.Models;

public class ProductFormViewModel
{
    public int ProductId { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập tên sản phẩm.")]
    [StringLength(250, ErrorMessage = "Tên sản phẩm không vượt quá 250 ký tự.")]
    public string Title { get; set; } = string.Empty;

    [StringLength(250)]
    public string? Alias { get; set; }

    [Required(ErrorMessage = "Vui lòng chọn danh mục sản phẩm.")]
    public int? CategoryProductId { get; set; }

    [StringLength(500, ErrorMessage = "Mô tả ngắn không vượt quá 500 ký tự.")]
    public string? Description { get; set; }

    public string? Detail { get; set; }

    [StringLength(500)]
    public string? ExistingImage { get; set; }

    [StringLength(250)]
    public string? Icon { get; set; }

    [Required(ErrorMessage = "Vui lòng nhập giá bán.")]
    [Range(0, 100_000_000, ErrorMessage = "Giá bán phải từ 0đ đến 100.000.000đ.")]
    public int? Price { get; set; }

    [Range(0, 100_000_000, ErrorMessage = "Giá khuyến mãi phải từ 0đ đến 100.000.000đ.")]
    public int? PriceSale { get; set; }

    [Range(0, 1_000_000, ErrorMessage = "Số lượng nhập phải từ 0 trở lên.")]
    public int? Quantity { get; set; } = 0;

    [Range(0, 1_000_000, ErrorMessage = "Tồn kho phải từ 0 trở lên.")]
    public int? UnitInStock { get; set; } = 0;

    public bool IsNew { get; set; }

    public bool IsBestSeller { get; set; }

    public bool IsActive { get; set; } = true;

    [Range(1, 5, ErrorMessage = "Số sao đánh giá từ 1 đến 5.")]
    public int? Star { get; set; } = 5;

    /// <summary>
    /// Uploaded product image file (PNG, JPG/JPEG, WebP up to 5 MB).
    /// </summary>
    public IFormFile? ImageFile { get; set; }

    /// <summary>
    /// When true on Edit, removes the current custom uploaded product image and reverts to category placeholder.
    /// </summary>
    public bool RemoveImage { get; set; }

    public static ProductFormViewModel FromEntity(TbProduct entity)
    {
        return new ProductFormViewModel
        {
            ProductId = entity.ProductId,
            Title = entity.Title ?? string.Empty,
            Alias = entity.Alias,
            CategoryProductId = entity.CategoryProductId,
            Description = entity.Description,
            Detail = entity.Detail,
            ExistingImage = entity.Image,
            Icon = PickleballClub.Services.IconSanitizer.ExtractSafeIconName(entity.Icon, "mdi:cup-water"),
            Price = entity.Price,
            PriceSale = entity.PriceSale,
            Quantity = entity.Quantity ?? 0,
            UnitInStock = entity.UnitInStock ?? entity.Quantity ?? 0,
            IsNew = entity.IsNew,
            IsBestSeller = entity.IsBestSeller,
            IsActive = entity.IsActive,
            Star = entity.Star ?? 5,
            RemoveImage = false
        };
    }

    public void ApplyToEntity(TbProduct target)
    {
        target.Title = Title.Trim();
        target.CategoryProductId = CategoryProductId;
        target.Description = string.IsNullOrWhiteSpace(Description) ? null : Description.Trim();
        target.Detail = string.IsNullOrWhiteSpace(Detail) ? null : Detail.Trim();
        target.Icon = string.IsNullOrWhiteSpace(Icon) ? "lucide:package" : Icon.Trim();
        target.Price = Price ?? 0;
        target.PriceSale = PriceSale;
        target.Quantity = Quantity ?? 0;
        target.UnitInStock = UnitInStock ?? Quantity ?? 0;
        target.IsNew = IsNew;
        target.IsBestSeller = IsBestSeller;
        target.IsActive = IsActive;
        target.Star = Star ?? 5;
    }
}
