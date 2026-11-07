using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers
{
    [Area("Admin")]
    public class HomeController : Controller
    {
        private readonly PickleballContext _context;

        public HomeController(PickleballContext context)
        {
            _context = context;
        }

        private async Task ReconcileBookingsByRealTimeAsync()
        {
            var now = DateTime.Now;
            string todayIso = now.ToString("yyyy-MM-dd");
            int nowMins = now.Hour * 60 + now.Minute;

            var activeBookings = await _context.TbBookings
                .Where(b => b.Status == "approved" || b.Status == "playing")
                .ToListAsync();

            bool changed = false;
            foreach (var b in activeBookings)
            {
                string bDate = string.IsNullOrWhiteSpace(b.BookingDate) ? todayIso : b.BookingDate.Trim();
                int startMins = 17 * 60;
                if (!string.IsNullOrWhiteSpace(b.StartTime) && b.StartTime.Contains(':'))
                {
                    var parts = b.StartTime.Split(':');
                    if (parts.Length >= 2 && int.TryParse(parts[0], out int h) && int.TryParse(parts[1], out int m))
                    {
                        startMins = h * 60 + m;
                    }
                }
                int dur = b.Duration > 0 ? b.Duration : 60;
                int endMins = startMins + dur;

                int dateCmp = string.CompareOrdinal(bDate, todayIso);
                if (dateCmp < 0)
                {
                    if (b.Status == "playing")
                    {
                        b.Status = "completed";
                        if (b.TotalPaid <= 0) b.TotalPaid = b.CourtFee;
                        changed = true;
                    }
                }
                else if (dateCmp > 0)
                {
                    if (b.Status == "playing")
                    {
                        b.Status = "approved";
                        changed = true;
                    }
                }
                else
                {
                    if (nowMins >= endMins)
                    {
                        if (b.Status == "playing")
                        {
                            b.Status = "completed";
                            if (b.TotalPaid <= 0) b.TotalPaid = b.CourtFee;
                            changed = true;
                        }
                    }
                    else if (nowMins >= startMins && nowMins < endMins)
                    {
                        if (b.Status == "approved")
                        {
                            b.Status = "playing";
                            changed = true;
                        }
                    }
                    else if (nowMins < startMins - 15)
                    {
                        if (b.Status == "playing")
                        {
                            b.Status = "approved";
                            changed = true;
                        }
                    }
                }
            }

            if (changed)
            {
                await _context.SaveChangesAsync();
            }
        }

        public async Task<IActionResult> Index()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });

            await ReconcileBookingsByRealTimeAsync();

            ViewBag.TotalCourts = await _context.TbCourts.CountAsync();
            ViewBag.TotalBookings = await _context.TbBookings.CountAsync();
            ViewBag.PendingBookings = await _context.TbBookings.CountAsync(b => b.Status == "pending");
            ViewBag.TotalProducts = await _context.TbProducts.CountAsync();
            ViewBag.TotalRevenue = await _context.TbBookings
                .Where(b => b.Status == "completed" || b.Status == "playing" || b.Status == "approved")
                .SumAsync(b => (int?)(b.TotalPaid > 0 ? b.TotalPaid : b.CourtFee)) ?? 0;

            var recentBookings = await _context.TbBookings
                .Include(b => b.Court)
                .OrderByDescending(b => b.CreatedDate)
                .Take(15)
                .ToListAsync();

            return View(recentBookings);
        }

        [HttpGet("/Management")]
        [HttpGet("/management.html")]
        [HttpGet("/Admin/Home/Management")]
        public IActionResult Management()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });
            return View();
        }

        [HttpGet("/Checkin")]
        [HttpGet("/checkin.html")]
        [HttpGet("/Admin/Home/Checkin")]
        public IActionResult Checkin()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });
            return View();
        }

        [HttpGet("/Reports")]
        [HttpGet("/reports.html")]
        [HttpGet("/Admin/Home/Reports")]
        public IActionResult Reports()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });
            return View();
        }

        [HttpGet("/Cms")]
        [HttpGet("/admin-cms.html")]
        [HttpGet("/Admin/Home/Cms")]
        public IActionResult Cms()
        {
            if (!Function.IsLogin())
                return RedirectToAction("Index", "Login", new { area = "Admin" });
            return View();
        }

        public IActionResult Logout()
        {
            Function._AccountId = 0;
            Function._Username = string.Empty;
            Function._FullName = string.Empty;
            Function._Email = string.Empty;
            Function._Phone = string.Empty;
            Function._RoleId = 0;
            Function._Message = string.Empty;
            Response.Cookies.Delete("pb_admin_uid");
            return Redirect("/Account/Logout");
        }
    }
}
