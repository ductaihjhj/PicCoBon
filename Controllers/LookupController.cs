using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Controllers
{
    public class LookupController : Controller
    {
        private readonly PickleballContext _context;

        public LookupController(PickleballContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index(string? q)
        {
            ViewBag.Query = q;
            var bookings = await _context.TbBookings
                .Include(b => b.Court)
                .OrderByDescending(b => b.CreatedDate)
                .ToListAsync();
            return View(bookings);
        }
    }
}
