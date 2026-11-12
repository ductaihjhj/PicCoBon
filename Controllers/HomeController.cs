using System.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;

namespace PickleballClub.Controllers
{
    public class HomeController : Controller
    {
        private readonly PickleballContext _context;

        public HomeController(PickleballContext context)
        {
            _context = context;
        }

        [HttpGet("/")]
        [HttpGet("/Home")]
        [HttpGet("/Home/Index")]
        [HttpGet("/Dashboard")]
        public async Task<IActionResult> Index()
        {
            ViewBag.Courts = await _context.TbCourts.Where(c => c.IsActive).OrderBy(c => c.CourtId).ToListAsync();
            ViewBag.TodayBookingsCount = await _context.TbBookings.CountAsync();
            return View();
        }

        [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
