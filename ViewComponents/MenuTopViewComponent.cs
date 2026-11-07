using Microsoft.AspNetCore.Mvc;
using PickleballClub.Models;

namespace PickleballClub.ViewComponents
{
    public class MenuTopViewComponent : ViewComponent
    {
        private readonly PickleballContext _context;

        public MenuTopViewComponent(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var items = _context.TbMenus.Where(m => m.IsActive).OrderBy(m => m.Position).ToList();
            return await Task.FromResult<IViewComponentResult>(View(items));
        }
    }
}
