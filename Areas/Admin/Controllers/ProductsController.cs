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
            tbProduct.UnitInStock ??= tbProduct.Quantity ?? 100;
            tbProduct.Quantity = tbProduct.UnitInStock;
            tbProduct.CreatedDate = BookingService.GetVietnamNow();
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
            tbProduct.Title = tbProduct.Title!.Trim();
            tbProduct.Alias = Function.TitleSlugGenerationAlias(tbProduct.Title);
            tbProduct.UnitInStock ??= tbProduct.Quantity ?? 0;
            tbProduct.Quantity = tbProduct.UnitInStock;
            tbProduct.ModifiedDate = BookingService.GetVietnamNow();
            _context.Update(tbProduct);
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
            .FirstOrDefaultAsync(p => p.ProductId == id);

        if (tbProduct != null)
        {
            // P1-17: Soft-deactivate if referenced by historical orders to prevent FK crash
            if (tbProduct.TbOrderDetails.Any())
            {
                tbProduct.IsActive = false;
                tbProduct.ModifiedDate = BookingService.GetVietnamNow();
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
