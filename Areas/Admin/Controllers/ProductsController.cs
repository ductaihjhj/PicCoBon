using System;
using System.Linq;
using System.Security.Claims;
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
    private readonly ProductImageService _imageService;

    public ProductsController(PickleballContext context, ProductImageService imageService)
    {
        _context = context;
        _imageService = imageService;
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
        PopulateCategoriesSelectList();
        return View(new ProductFormViewModel
        {
            IsActive = true,
            Star = 5,
            Quantity = 100,
            UnitInStock = 100,
            Icon = "mdi:cup-water"
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Create(ProductFormViewModel model)
    {
        ValidateProductBusinessRules(model);

        string? uploadedRelativePath = null;
        if (ModelState.IsValid && model.ImageFile != null && model.ImageFile.Length > 0)
        {
            var uploadResult = await _imageService.SaveValidatedProductImageAsync(model.ImageFile);
            if (!uploadResult.Success)
            {
                ModelState.AddModelError(nameof(ProductFormViewModel.ImageFile), uploadResult.ErrorMessage ?? "Ảnh tải lên không hợp lệ.");
            }
            else
            {
                uploadedRelativePath = uploadResult.RelativePath;
            }
        }

        if (ModelState.IsValid)
        {
            var tbProduct = new TbProduct();
            model.ApplyToEntity(tbProduct);
            tbProduct.Alias = Function.TitleSlugGenerationAlias(tbProduct.Title!);
            tbProduct.Icon = IconSanitizer.ExtractSafeIconName(tbProduct.Icon, "mdi:cup-water");
            tbProduct.UnitInStock ??= tbProduct.Quantity ?? 100;
            tbProduct.Quantity = tbProduct.UnitInStock;
            tbProduct.Image = uploadedRelativePath;
            tbProduct.CreatedDate = BookingService.GetVietnamNow();
            tbProduct.CreatedBy = GetCurrentActorUsername();

            try
            {
                _context.Add(tbProduct);
                await _context.SaveChangesAsync();
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(uploadedRelativePath))
                {
                    _imageService.DeletePhysicalUploadedFile(uploadedRelativePath);
                }
                throw;
            }

            if (TempData != null)
            {
                TempData["SuccessMessage"] = $"Đã thêm sản phẩm \"{tbProduct.Title}\" thành công.";
            }
            return RedirectToAction(nameof(Index));
        }

        PopulateCategoriesSelectList(model.CategoryProductId);
        return View(model);
    }

    public async Task<IActionResult> Edit(int? id)
    {
        if (id == null) return NotFound();
        var tbProduct = await _context.TbProducts
            .AsNoTracking()
            .Include(p => p.CategoryProduct)
            .FirstOrDefaultAsync(p => p.ProductId == id);
        if (tbProduct == null) return NotFound();

        PopulateCategoriesSelectList(tbProduct.CategoryProductId);
        var vm = ProductFormViewModel.FromEntity(tbProduct);
        ViewData["CategoryAlias"] = tbProduct.CategoryProduct?.Alias ?? "other";
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [RequestSizeLimit(6 * 1024 * 1024)]
    public async Task<IActionResult> Edit(int id, ProductFormViewModel model)
    {
        if (id != model.ProductId) return NotFound();

        var existing = await _context.TbProducts
            .Include(p => p.CategoryProduct)
            .FirstOrDefaultAsync(p => p.ProductId == id);
        if (existing == null) return NotFound();

        model.ExistingImage = existing.Image;
        ViewData["CategoryAlias"] = existing.CategoryProduct?.Alias ?? "other";

        ValidateProductBusinessRules(model);

        string? previousImage = existing.Image;
        string? newlyUploadedPath = null;
        bool shouldCleanupPrevious = false;

        if (ModelState.IsValid && model.ImageFile != null && model.ImageFile.Length > 0)
        {
            var uploadResult = await _imageService.SaveValidatedProductImageAsync(model.ImageFile);
            if (!uploadResult.Success)
            {
                ModelState.AddModelError(nameof(ProductFormViewModel.ImageFile), uploadResult.ErrorMessage ?? "Ảnh tải lên không hợp lệ.");
            }
            else
            {
                newlyUploadedPath = uploadResult.RelativePath;
            }
        }

        if (ModelState.IsValid)
        {
            int oldStock = existing.UnitInStock ?? existing.Quantity ?? 0;
            int newStock = model.UnitInStock ?? model.Quantity ?? 0;

            model.ApplyToEntity(existing);
            existing.Alias = Function.TitleSlugGenerationAlias(existing.Title!);
            existing.Icon = IconSanitizer.ExtractSafeIconName(existing.Icon, "mdi:cup-water");
            existing.UnitInStock = newStock;
            existing.Quantity = newStock;
            existing.ModifiedDate = BookingService.GetVietnamNow();
            existing.ModifiedBy = GetCurrentActorUsername();

            if (!string.IsNullOrWhiteSpace(newlyUploadedPath))
            {
                existing.Image = newlyUploadedPath;
                shouldCleanupPrevious = !string.IsNullOrWhiteSpace(previousImage) &&
                                        !string.Equals(previousImage, newlyUploadedPath, StringComparison.OrdinalIgnoreCase);
            }
            else if (model.RemoveImage)
            {
                existing.Image = null;
                shouldCleanupPrevious = !string.IsNullOrWhiteSpace(previousImage);
            }

            if (newStock != oldStock)
            {
                _context.TbInventoryMovements.Add(new TbInventoryMovement
                {
                    ProductId = existing.ProductId,
                    ChangeQty = newStock - oldStock,
                    BalanceAfter = newStock,
                    Reason = "Admin manual stock adjustment",
                    ActorAccountId = GetCurrentActorAccountId(),
                    CreatedAt = BookingService.GetVietnamNow()
                });
            }

            try
            {
                await _context.SaveChangesAsync();
            }
            catch
            {
                if (!string.IsNullOrWhiteSpace(newlyUploadedPath))
                {
                    _imageService.DeletePhysicalUploadedFile(newlyUploadedPath);
                }
                throw;
            }

            if (shouldCleanupPrevious)
            {
                await _imageService.DeleteOldUploadIfUnreferencedAsync(_context, previousImage, existing.ProductId);
            }

            if (TempData != null)
            {
                TempData["SuccessMessage"] = $"Đã cập nhật sản phẩm \"{existing.Title}\" thành công.";
            }
            return RedirectToAction(nameof(Index));
        }

        PopulateCategoriesSelectList(model.CategoryProductId);
        return View(model);
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
            var oldImage = tbProduct.Image;
            bool physicallyRemoved = false;

            if (tbProduct.TbOrderDetails.Any() || tbProduct.TbInventoryMovements.Any())
            {
                tbProduct.IsActive = false;
                tbProduct.ModifiedDate = BookingService.GetVietnamNow();
                tbProduct.ModifiedBy = GetCurrentActorUsername();
            }
            else
            {
                _context.TbProducts.Remove(tbProduct);
                physicallyRemoved = true;
            }

            await _context.SaveChangesAsync();

            if (physicallyRemoved && !string.IsNullOrWhiteSpace(oldImage))
            {
                await _imageService.DeleteOldUploadIfUnreferencedAsync(_context, oldImage, id);
            }
        }
        return RedirectToAction(nameof(Index));
    }

    private void ValidateProductBusinessRules(ProductFormViewModel model)
    {
        if (string.IsNullOrWhiteSpace(model.Title))
        {
            ModelState.AddModelError(nameof(ProductFormViewModel.Title), "Tên sản phẩm không được để trống.");
        }
        if ((model.Price ?? 0) <= 0)
        {
            ModelState.AddModelError(nameof(ProductFormViewModel.Price), "Giá bán phải lớn hơn 0đ.");
        }
        if ((model.UnitInStock ?? 0) < 0)
        {
            ModelState.AddModelError(nameof(ProductFormViewModel.UnitInStock), "Số lượng tồn kho không được âm.");
        }
    }

    private void PopulateCategoriesSelectList(int? selectedId = null)
    {
        ViewData["CategoryProductId"] = new SelectList(
            _context.TbProductCategories.AsNoTracking().OrderBy(c => c.Position ?? c.CategoryProductId),
            "CategoryProductId",
            "Title",
            selectedId);
    }

    private string GetCurrentActorUsername()
    {
        var claimName = User?.Identity?.Name;
        if (!string.IsNullOrWhiteSpace(claimName))
            return claimName;
        return !string.IsNullOrWhiteSpace(Function._Username) ? Function._Username : "admin";
    }

    private int? GetCurrentActorAccountId()
    {
        var idStr = User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(idStr, out int id) && id > 0)
            return id;
        return Function._AccountId > 0 ? Function._AccountId : null;
    }
}
