using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.ViewComponents
{
    public class ProductViewComponent : ViewComponent
    {
        private readonly PickleballContext _context;

        public ProductViewComponent(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync(int? categoryId = null)
        {
            var query = _context.TbProducts
                .Include(p => p.CategoryProduct)
                .Where(p => p.IsActive);

            if (categoryId.HasValue && categoryId.Value > 0)
            {
                query = query.Where(p => p.CategoryProductId == categoryId.Value);
            }

            var items = await query.OrderBy(p => p.ProductId).ToListAsync();
            return View(items);
        }
    }
}
