using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using PickleballClub.Models;
using PickleballClub.Utilities;

namespace PickleballClub.Controllers;

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
        // P0-06 & P0-07: Never load all customer bookings into an anonymous Lookup view model!
        if (Function.IsLogin() && Function._AccountId > 0)
        {
            int accId = Function._AccountId;
            var ownBookings = await _context.TbBookings
                .AsNoTracking()
                .Include(b => b.Court)
                .Where(b => b.AccountId == accId)
                .OrderByDescending(b => b.BookingDate)
                .ThenByDescending(b => b.StartTime)
                .ToListAsync();
            return View(ownBookings);
        }

        return View(new List<TbBooking>());
    }
}
