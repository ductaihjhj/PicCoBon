using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin,Staff")]
public class HomeController : Controller
{
    private readonly PickleballContext _context;

    public HomeController(PickleballContext context)
    {
        _context = context;
    }

    public async Task<IActionResult> Index()
    {
        ViewBag.TotalCourts = await _context.TbCourts.CountAsync(c => c.IsActive);
        ViewBag.TotalBookings = await _context.TbBookings.CountAsync();
        ViewBag.PendingBookings = await _context.TbBookings.CountAsync(b => b.Status == "pending");
        ViewBag.TotalProducts = await _context.TbProducts.CountAsync(p => p.IsActive);

        // P1-06: Net collected revenue strictly from completed payment ledger minus refunds
        var ledgerRows = await _context.TbPaymentTransactions
            .AsNoTracking()
            .Where(t => t.Status == "completed")
            .Select(t => new { t.TransactionType, t.Amount })
            .ToListAsync();

        int netCollected = 0;
        foreach (var r in ledgerRows)
        {
            if (string.Equals(r.TransactionType, "refund", StringComparison.OrdinalIgnoreCase))
                netCollected -= Math.Abs(r.Amount);
            else
                netCollected += Math.Max(0, r.Amount);
        }

        int unpaidBookingReceivables = await _context.TbBookings
            .AsNoTracking()
            .Where(b => (b.Status == "approved" || b.Status == "playing" || b.Status == "completed") && b.AmountDue > b.PaidAmount)
            .SumAsync(b => (int?)(b.AmountDue - b.PaidAmount)) ?? 0;

        int unpaidShopReceivables = await _context.TbOrders
            .AsNoTracking()
            .Where(o => o.BookingId == null && o.Status != "cancelled" && (o.TotalAmount ?? 0) > o.PaidAmount)
            .SumAsync(o => (int?)((o.TotalAmount ?? 0) - o.PaidAmount)) ?? 0;

        ViewBag.TotalRevenue = Math.Max(0, netCollected);
        ViewBag.EstimatedReceivables = unpaidBookingReceivables + unpaidShopReceivables;

        var recentBookings = await _context.TbBookings
            .AsNoTracking()
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
