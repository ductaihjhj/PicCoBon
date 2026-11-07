using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class ProductsController : Controller
    {
        private readonly PickleballContext _context;

        public ProductsController(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });
            var items = _context.TbProducts.Include(t => t.CategoryProduct);
            return View(await items.ToListAsync());
        }

        public IActionResult Create()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });
            ViewData["CategoryProductId"] = new SelectList(_context.TbProductCategories, "CategoryProductId", "Title");
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("ProductId,Title,Alias,CategoryProductId,Description,Image,Icon,Price,PriceSale,Quantity,UnitInStock,IsNew,IsBestSeller,IsActive,Star")] TbProduct tbProduct)
        {
            if (ModelState.IsValid)
            {
                tbProduct.Alias = Function.TitleSlugGenerationAlias(tbProduct.Title ?? string.Empty);
                tbProduct.CreatedDate = DateTime.Now;
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
            if (ModelState.IsValid)
            {
                tbProduct.Alias = Function.TitleSlugGenerationAlias(tbProduct.Title ?? string.Empty);
                tbProduct.ModifiedDate = DateTime.Now;
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
            var tbProduct = await _context.TbProducts.FindAsync(id);
            if (tbProduct != null) _context.TbProducts.Remove(tbProduct);
            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
        }
    }
}
