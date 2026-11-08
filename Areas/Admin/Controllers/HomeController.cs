using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Services;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Staff")]
public class HomeController : Controller
{
    private readonly PickleballContext _context;
    private readonly BookingService _bookingService;

    public HomeController(PickleballContext context, BookingService bookingService)
    {
        _context = context;
        _bookingService = bookingService;
    }

    public async Task<IActionResult> Index()
    {
        await _bookingService.ReconcileBookingStatusesAsync();

        ViewBag.TotalCourts = await _context.TbCourts.CountAsync(c => c.IsActive);
        ViewBag.TotalBookings = await _context.TbBookings.CountAsync();
        ViewBag.PendingBookings = await _context.TbBookings.CountAsync(b => b.Status == "pending");
        ViewBag.TotalProducts = await _context.TbProducts.CountAsync(p => p.IsActive);

        // P1-06: Only count ACTUAL collected revenue (PaidAmount + standalone paid retail orders)
        int collectedBookingRev = await _context.TbBookings
            .Where(b => b.Status != "cancelled" && b.Status != "rejected" && b.PaidAmount > 0)
            .SumAsync(b => (int?)b.PaidAmount) ?? 0;

        int standaloneRetailRev = await _context.TbOrders
            .Where(o => o.BookingId == null && o.PaidAmount > 0)
            .SumAsync(o => (int?)o.PaidAmount) ?? 0;

        int estimatedReceivables = await _context.TbBookings
            .Where(b => (b.Status == "approved" || b.Status == "playing" || b.Status == "completed") && b.AmountDue > b.PaidAmount)
            .SumAsync(b => (int?)(b.AmountDue - b.PaidAmount)) ?? 0;

        ViewBag.TotalRevenue = collectedBookingRev + standaloneRetailRev;
        ViewBag.EstimatedReceivables = estimatedReceivables;

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
        return View();
    }

    [HttpGet("/Checkin")]
    [HttpGet("/checkin.html")]
    [HttpGet("/Admin/Home/Checkin")]
    public IActionResult Checkin()
    {
        return View();
    }

    [HttpGet("/Reports")]
    [HttpGet("/reports.html")]
    [HttpGet("/Admin/Home/Reports")]
    public IActionResult Reports()
    {
        return View();
    }

    [Authorize(Roles = "Admin")]
    [HttpGet("/Cms")]
    [HttpGet("/admin-cms.html")]
    [HttpGet("/Admin/Home/Cms")]
    public IActionResult Cms()
    {
        return View();
    }

    public async Task<IActionResult> Logout()
    {
        await Function.SignOutAccountAsync(HttpContext);
        return RedirectToAction("Index", "Home", new { area = "" });
    }
}
