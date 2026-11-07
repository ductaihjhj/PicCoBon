using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Controllers
{
    public class BookingController : Controller
    {
        private readonly PickleballContext _context;

        public BookingController(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var courts = await _context.TbCourts.Where(c => c.IsActive).OrderBy(c => c.CourtId).ToListAsync();
            return View(courts);
        }
    }
}
