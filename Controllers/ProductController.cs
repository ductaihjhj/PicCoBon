using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Controllers
{
    public class ProductController : Controller
    {
        private readonly PickleballContext _context;

        public ProductController(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _context.TbProducts
                .Include(p => p.CategoryProduct)
                .Where(p => p.IsActive)
                .OrderBy(p => p.ProductId)
                .ToListAsync();
            return View(products);
        }
    }
}
