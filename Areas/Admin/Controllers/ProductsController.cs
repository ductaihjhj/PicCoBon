using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public class ProductsController : Controller
{
    private readonly PickleballContext _context;

    public ProductsController(PickleballContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        var items = _context.TbProducts
            .AsNoTracking()
            .Include(t => t.CategoryProduct)
            .OrderBy(p => p.ProductId);
        return View(await items.ToListAsync());
    }

    public IActionResult Create()
    {
        ViewData["CategoryProductId"] = new SelectList(_context.TbProductCategories, "CategoryProductId", "Title");
        return View();
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Create([Bind("ProductId,Title,Alias,CategoryProductId,Description,Image,Icon,Price,PriceSale,Quantity,UnitInStock,IsNew,IsBestSeller,IsActive,Star")] TbProduct tbProduct)
    {
        if (string.IsNullOrWhiteSpace(tbProduct.Title))
        {
            ModelState.AddModelError("Title", "Tên sản phẩm không được để trống.");
        }
        if ((tbProduct.Price ?? 0) <= 0)
        {
            ModelState.AddModelError("Price", "Giá bán phải lớn hơn 0đ.");
        }
        if ((tbProduct.UnitInStock ?? 0) < 0)
        {
            ModelState.AddModelError("UnitInStock", "Số lượng tồn kho không được âm.");
        }

        if (ModelState.IsValid)
        {
            tbProduct.Title = tbProduct.Title!.Trim();
            tbProduct.Alias = Function.TitleSlugGenerationAlias(tbProduct.Title);
            tbProduct.Icon = IconSanitizer.ExtractSafeIconName(tbProduct.Icon, "mdi:cup-water");
            tbProduct.UnitInStock ??= tbProduct.Quantity ?? 100;
            tbProduct.Quantity = tbProduct.UnitInStock;
            tbProduct.CreatedDate = BookingService.GetVietnamNow();
            tbProduct.CreatedBy = Function._Username;
            _context.Add(tbProduct);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        ViewData["CategoryProductId"] = new SelectList(_context.TbProductCategories, "CategoryProductId", "Title", tbProduct.CategoryProductId);
        return View(tbProduct);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var tbProduct = await _context.TbProducts.FindAsync(id);
        if (tbProduct == null) return NotFound();
        ViewData["CategoryProductId"] = new SelectList(_context.TbProductCategories, "CategoryProductId", "Title", tbProduct.CategoryProductId);
        return View(tbProduct);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(int id, [Bind("ProductId,Title,Alias,CategoryProductId,Description,Image,Icon,Price,PriceSale,Quantity,UnitInStock,IsNew,IsBestSeller,IsActive,Star")] TbProduct tbProduct)
    {
        if (id != tbProduct.ProductId) return NotFound();

        if (string.IsNullOrWhiteSpace(tbProduct.Title))
        {
            ModelState.AddModelError("Title", "Tên sản phẩm không được để trống.");
        }
        if ((tbProduct.Price ?? 0) <= 0)
        {
            ModelState.AddModelError("Price", "Giá bán phải lớn hơn 0đ.");
        }
        if ((tbProduct.UnitInStock ?? 0) < 0)
        {
            ModelState.AddModelError("UnitInStock", "Số lượng tồn kho không được âm.");
        }

        if (ModelState.IsValid)
        {
            var existing = await _context.TbProducts.FindAsync(id);
            if (existing == null) return NotFound();

            int oldStock = existing.UnitInStock ?? existing.Quantity ?? 0;
            int newStock = tbProduct.UnitInStock ?? tbProduct.Quantity ?? 0;

            existing.Title = tbProduct.Title!.Trim();
            existing.Alias = Function.TitleSlugGenerationAlias(existing.Title);
            existing.CategoryProductId = tbProduct.CategoryProductId;
            existing.Description = tbProduct.Description;
            existing.Image = tbProduct.Image;
            existing.Icon = IconSanitizer.ExtractSafeIconName(tbProduct.Icon, "mdi:cup-water");
            existing.Price = tbProduct.Price;
            existing.PriceSale = tbProduct.PriceSale;
            existing.UnitInStock = newStock;
            existing.Quantity = newStock;
            existing.IsNew = tbProduct.IsNew;
            existing.IsBestSeller = tbProduct.IsBestSeller;
            existing.IsActive = tbProduct.IsActive;
            existing.Star = tbProduct.Star;
            existing.ModifiedDate = BookingService.GetVietnamNow();
            existing.ModifiedBy = Function._Username;

            if (newStock != oldStock)
            {
                _context.TbInventoryMovements.Add(new TbInventoryMovement
                {
                    ProductId = existing.ProductId,
                    ChangeQty = newStock - oldStock,
                    BalanceAfter = newStock,
                    Reason = "Admin manual stock adjustment",
                    ActorAccountId = Function._AccountId,
                    CreatedAt = BookingService.GetVietnamNow()
                });
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
        ViewData["CategoryProductId"] = new SelectList(_context.TbProductCategories, "CategoryProductId", "Title", tbProduct.CategoryProductId);
        return View(tbProduct);
    }

    public async Task<IActionResult> Delete(int? id)
    {
        if (id == null) return NotFound();
        var tbProduct = await _context.TbProducts
            .AsNoTracking()
            .Include(t => t.CategoryProduct)
            .FirstOrDefaultAsync(m => m.ProductId == id);
        if (tbProduct == null) return NotFound();
        return View(tbProduct);
    }

    [HttpPost, ActionName("Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteConfirmed(int id)
    {
        var tbProduct = await _context.TbProducts
            .Include(p => p.TbOrderDetails)
            .Include(p => p.TbInventoryMovements)
            .FirstOrDefaultAsync(p => p.ProductId == id);

        if (tbProduct != null)
        {
            if (tbProduct.TbOrderDetails.Any() || tbProduct.TbInventoryMovements.Any())
            {
                tbProduct.IsActive = false;
                tbProduct.ModifiedDate = BookingService.GetVietnamNow();
                tbProduct.ModifiedBy = Function._Username;
            }
            else
            {
                _context.TbProducts.Remove(tbProduct);
            }
            await _context.SaveChangesAsync();
        }
        return RedirectToAction(nameof(Index));
    }
}
