using Microsoft.AspNetCore.Mvc;
using PickleballClub.Models;

namespace PickleballClub.ViewComponents
{
    public class CourtViewComponent : ViewComponent
    {
        private readonly PickleballContext _context;

        public CourtViewComponent(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IViewComponentResult> InvokeAsync()
        {
            var items = _context.TbCourts.Where(c => c.IsActive).OrderBy(c => c.CourtId).ToList();
            return await Task.FromResult<IViewComponentResult>(View(items));
        }
    }
}
